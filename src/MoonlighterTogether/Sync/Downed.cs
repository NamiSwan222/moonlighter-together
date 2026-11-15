using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MoonlighterTogether.Net;
using UnityEngine;

namespace MoonlighterTogether.Sync
{
    /// Falling in a dungeon while a teammate still stands on the floor makes you a ghost instead of ending
    /// your run: you float along, cannot hit or be hit, and enemies ignore you. A standing teammate revives
    /// you by pressing R next to you (half health), or you come back on the next floor. When nobody is left
    /// standing, the game's own death happens for everyone (results screen, bag lost, home).
    public static class Downed
    {
        public const Msg DownMsg = (Msg)31;
        public const Msg ReviveMsg = (Msg)32;
        public const Msg WipeMsg = (Msg)33;

        public static readonly HashSet<int> Ghosts = new HashSet<int>();
        public static bool IsGhost(int playerId) => Ghosts.Contains(playerId);
        /// I am a ghost (down, waiting for a revive). Not true while the game's real death runs.
        public static bool Me => Ghosts.Contains(Session.MyId) && !RunningRealDeath;
        /// Set while a trip home that concerns only this player runs (the whole group was wiped out).
        public static bool AllowSoloTripHome;
        public static bool RunningRealDeath;
        public static int TimesDowned, TimesRevived, RevivesGiven, Wipes;
        public const float ReviveRange = 48f;

        public static void Init()
        {
            Session.On(DownMsg, Relay.Others, OnDown);
            Session.On(ReviveMsg, Relay.Others, OnRevive);
            Session.On(WipeMsg, Relay.None, OnWipe);
            // A link that drops for a moment does not end anyone's run: only someone who left for good stops counting.
            Session.PlayerLeft += (p, temporary) => { if (!temporary) { Ghosts.Remove(p.Id); CheckWipe(); } };
            Session.Ended += why => { if (Me) StandUp(1f); Ghosts.Clear(); };
            HeroMerchant.OnFloorChange += (c, l) => { if (Me) StandUp(0.5f); Ghosts.Clear(); };
        }

        // ---------------------------------------------------------------- falling

        /// Teammates standing (connected, alive, not a ghost) on my dungeon floor.
        public static int StandingTeammates()
        {
            var dm = DungeonManager.Instance;
            if (dm == null) return 0;
            string floor = $"dungeon:{dm.culture}:{dm.level}:";
            return Presence.Remotes.Values.Count(r => r.Zone.StartsWith(floor) && !r.Ghost && !Ghosts.Contains(r.Id)
                && Session.Players.ContainsKey(r.Id));   // still in the session, even while reconnecting
        }

        /// Called at the end of Will's death animation. True = we handled it (became a ghost).
        internal static bool TryBecomeGhost()
        {
            if (!Session.Active || RunningRealDeath || GameManager.Instance == null || !GameManager.Instance.IsDungeonSceneLoaded()) return false;
            if (StandingTeammates() == 0)
            {
                Plugin.Log.LogInfo("[downed] nobody left standing: the real death happens");
                RunningRealDeath = true; AllowSoloTripHome = true;
                Ghosts.Add(Session.MyId);
                Session.Send(DownMsg, new W().U8((byte)Session.MyId).Bool(true));
                CheckWipe();
                return false;
            }
            Ghosts.Add(Session.MyId);
            TimesDowned++;
            var hero = HeroMerchant.Instance;
            var st = hero.heroMerchantStats;
            SetHealth(st, Mathf.Max(1f, (float)st.currentHealth.maxValue * 0.01f));
            st.SetInvencible();
            RestoreControl(hero);
            Session.Send(DownMsg, new W().U8((byte)Session.MyId).Bool(true));
            Plugin.Log.LogInfo("[downed] I am a ghost; waiting for a revive");
            if (Session.IsHost) CheckWipe();
            return true;
        }

        /// Will's health stat is read-only while he is invulnerable (as after death): open it, write, done.
        private static void SetHealth(HeroMerchantStats st, float hp)
        {
            st.SetVencible();
            st.isDead = false;
            st.currentHealth.Value = hp;
            st.isDead = false;
        }

        private static void RestoreControl(HeroMerchant hero)
        {
            var gm = GameManager.Instance;
            gm.UnpauseGame();
            var gui = GUIManager.Instance;
            gui.AllowPanelChange();
            gui.canOpenPause = true;
            var ctl = hero.heroMerchantController;
            try { ctl.characterAnimator.ResetTrigger("Death"); ctl.characterAnimator.Play("Idle", 0, 0f); } catch { }
            try { ctl.ShowHeroMerchantArmor(); } catch { }
            ctl.EnableMoveInput();
            ctl.AllowMovement();
        }

        private static void OnDown(int sender, R r)
        {
            int id = r.U8(); bool down = r.Bool();
            if (id != sender) return;   // only for yourself
            if (down) Ghosts.Add(id); else Ghosts.Remove(id);
            if (Session.IsHost) CheckWipe();
        }

        // ---------------------------------------------------------------- reviving

        /// The ghost a living local player can revive right now (nearest within range), or -1.
        public static int GhostInReach()
        {
            var hero = HeroMerchant.Instance;
            if (hero == null || Me || (bool)hero.heroMerchantStats.isDead) return -1;
            int best = -1; float bestD = ReviveRange * ReviveRange;
            foreach (var r in Presence.HereWithMe())
            {
                if (!r.Ghost) continue;
                float d = (r.Pos - (Vector2)hero.transform.position).sqrMagnitude;
                if (d <= bestD) { bestD = d; best = r.Id; }
            }
            return best;
        }

        public static bool ReviveNearest()
        {
            int id = GhostInReach();
            if (id < 0) return false;
            Session.Send(ReviveMsg, new W().U8((byte)id));
            RevivesGiven++;
            Plugin.Log.LogInfo($"[downed] reviving player {id}");
            return true;
        }

        private static void OnRevive(int sender, R r)
        {
            int id = r.U8();
            if (Ghosts.Contains(sender)) return;   // a ghost cannot revive
            Ghosts.Remove(id);
            if (id == Session.MyId) StandUp(0.5f);
        }

        public static void StandUp(float healthFraction)
        {
            if (!Ghosts.Remove(Session.MyId) && !Me) { }
            var hero = HeroMerchant.Instance;
            if (hero == null) return;
            var st = hero.heroMerchantStats;
            SetHealth(st, Mathf.Max((float)st.currentHealth.Value, (float)st.currentHealth.maxValue * healthFraction));
            if (hero.isGodModeEnabled) st.SetInvencible();
            SetGhostLook(hero, false);
            TimesRevived++;
            if (Session.Active) Session.Send(DownMsg, new W().U8((byte)Session.MyId).Bool(false));
            Plugin.Log.LogInfo($"[downed] back on my feet with {(float)st.currentHealth.Value:0} hp");
        }

        // ---------------------------------------------------------------- everyone down

        /// Host: if every player on a dungeon floor is down, the run is lost for all of them.
        public static void CheckWipe()
        {
            if (!Session.IsHost) return;
            var hero = HeroMerchant.Instance;
            if (GameManager.Instance == null || !GameManager.Instance.IsDungeonSceneLoaded() || hero == null) return;
            bool hostStanding = !Ghosts.Contains(0) && !(bool)hero.heroMerchantStats.isDead;
            if (hostStanding || StandingTeammates() > 0) return;
            if (Ghosts.Count == 0) return;
            Wipes++;
            Plugin.Log.LogInfo("[downed] everyone is down: the run ends for the whole group");
            Session.Send(WipeMsg, new W());
            ApplyWipe();
        }

        private static void OnWipe(int sender, R r) { if (!Session.IsHost) ApplyWipe(); }

        private static void ApplyWipe()
        {
            if (!Me) return;   // standing, or already in the game's own death
            Ghosts.Remove(Session.MyId);
            var hero = HeroMerchant.Instance;
            SetGhostLook(hero, false);
            RunningRealDeath = true;
            AllowSoloTripHome = true;
            hero.heroMerchantStats.isDead = false;
            hero.heroMerchantStats.Die();   // the game's death: animation, results, bag lost, home
        }

        // ---------------------------------------------------------------- every frame

        public static void Tick()
        {
            var hero = HeroMerchant.Instance;
            if (hero == null) return;
            if (Me)
            {
                SetGhostLook(hero, true);
                hero.heroMerchantController._canAttack = false;
            }
            if (Session.Active && !Me && Input.GetKeyDown(KeyCode.R)) ReviveNearest();
            if (GameManager.Instance != null && GameManager.Instance.IsTownSceneLoaded())
            {
                if (Me) StandUp(1f);
                RunningRealDeath = false; AllowSoloTripHome = false;
                Ghosts.Clear();
            }
        }

        private static readonly List<SpriteRenderer> _srs = new List<SpriteRenderer>();
        private static bool _looking;

        private static void SetGhostLook(HeroMerchant hero, bool ghost)
        {
            if (hero == null || (!ghost && !_looking)) return;
            _looking = ghost;
            hero.GetComponentsInChildren(true, _srs);
            foreach (var sr in _srs)
            {
                var c = sr.color;
                c.a = ghost ? 0.45f : 1f;
                sr.color = c;
            }
        }

        /// Host: tell one (re)joining player who is down right now.
        public static void SendStateTo(int player)
        {
            foreach (int id in Ghosts) Session.SendTo(player, DownMsg, new W().U8((byte)id).Bool(true), Channel.Reliable, id);
        }

        public static object Snapshot() => new
        {
            me = Me,
            ghosts = Ghosts.ToArray(),
            inReach = GhostInReach(),
            standingTeammates = StandingTeammates(),
            TimesDowned, TimesRevived, RevivesGiven, Wipes,
        };
    }

    [HarmonyPatch(typeof(HeroMerchant), nameof(HeroMerchant.OnWillDeathAnimationEnded))]
    static class GhostInsteadOfDeath
    {
        static bool Prefix()
        {
            if (MoonlighterEffectRegistry.GetEffect<AngelAmuletEffect>() != null) return true;   // the amulet's own revive
            return !Downed.TryBecomeGhost();
        }
    }

    // A ghost neither hurts nor gets hurt.
    [HarmonyPatch(typeof(HeroMerchant), nameof(HeroMerchant.DealDamageToPlayer))]
    static class GhostTakesNoDamage
    {
        static bool Prefix() => !Downed.Me;
    }

    [HarmonyPatch(typeof(Enemy), nameof(Enemy.DealDamageToEnemy))]
    static class GhostDealsNoDamage
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix() => !Downed.Me || Enemies.ApplyingRemoteHit;
    }
}
