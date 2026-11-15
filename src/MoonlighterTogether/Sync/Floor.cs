using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using MoonlighterTogether.Net;
using UnityEngine;

namespace MoonlighterTogether.Sync
{
    /// Every copy builds the same dungeon floor.
    ///
    /// The game generates a floor over many frames from UnityEngine.Random, which everything else in the game
    /// also draws from, so the same seed alone gives different floors. Here the generator gets a private
    /// random stream: before each step of the generation coroutines the stream's state is swapped in, and
    /// after the step it is saved and the game's own state put back. The host picks the seed for each floor;
    /// a guest waits for it before generating. Result: the same rooms, enemies, champions, chests and
    /// contents on every copy, checked by a floor signature both sides compute.
    public static class Floor
    {
        public const Msg SeedMsg = (Msg)22;
        public const Msg TravelMsg = (Msg)23;
        public const Msg TravelAsk = (Msg)24;

        public enum Trip : byte { Next = 1, Town = 2, Wanderer = 3 }

        private struct Pending { public ItemMaster.Culture Culture; public int Level; public int Seed; }
        private static readonly List<Pending> Seeds = new List<Pending>();

        public static bool ApplyingRemoteTravel;
        public static int CurrentSeed;
        public static int SeedsUsed, SeedWaitsTimedOut, TripsSent, TripsApplied;
        public static string LastSignature = "";

        // isolation state
        private static UnityEngine.Random.State _stream;
        private static int _depth;
        public static bool Generating;

        public const Msg ReadyMsg = (Msg)44;

        public static void Init()
        {
            Session.On(ReadyMsg, Relay.None, OnReady);
            Session.On(SeedMsg, Relay.None, OnSeed);
            Session.On(TravelMsg, Relay.None, OnTravel);
            Session.On(TravelAsk, Relay.None, OnTravelAsk);
        }

        // ------------------------------------------------------------------ seeds

        private static void OnSeed(int sender, R r)
        {
            if (Session.IsHost) return;
            var p = new Pending { Culture = (ItemMaster.Culture)r.I32(), Level = r.I32(), Seed = r.I32() };
            Seeds.RemoveAll(x => x.Culture == p.Culture && x.Level == p.Level);
            Seeds.Add(p);
            Plugin.Log.LogInfo($"[floor] seed for {p.Culture} floor {p.Level}: {p.Seed}");
        }

        /// Wraps the floor generator's static entry point: decides the seed (host) or waits for it (guest).
        public static IEnumerator Isolate(IEnumerator inner, ItemMaster.Culture culture, int level)
        {
            if (!Session.Active)
            {
                _isolating = false;
                var plain = Steps(inner);
                while (plain.MoveNext()) yield return plain.Current;
                yield break;
            }

            int seed;
            if (Session.IsHost)
            {
                seed = new System.Random().Next(1, int.MaxValue);
                Session.Send(SeedMsg, new W().I32((int)culture).I32(level).I32(seed));
            }
            else
            {
                float until = Time.unscaledTime + 30f;
                while (!Seeds.Any(x => x.Culture == culture && x.Level == level) && Time.unscaledTime < until) yield return null;
                var found = Seeds.FirstOrDefault(x => x.Culture == culture && x.Level == level);
                if (found.Seed == 0)
                {
                    SeedWaitsTimedOut++;
                    seed = new System.Random().Next(1, int.MaxValue);
                    Plugin.Log.LogError($"[floor] no seed from the host for {culture} floor {level}: this floor will differ");
                }
                else { seed = found.Seed; Seeds.Remove(found); }
            }
            CurrentSeed = seed;
            SeedsUsed++;
            var outer = UnityEngine.Random.state;
            UnityEngine.Random.InitState(seed);
            _stream = UnityEngine.Random.state;
            UnityEngine.Random.state = outer;
            Plugin.Log.LogInfo($"[floor] generating {culture} floor {level} with seed {seed}");
            _isolating = true;
            var steps = Steps(inner);
            while (steps.MoveNext()) yield return steps.Current;
        }

        private static bool _isolating;

        /// Runs a coroutine's steps on the private stream (in a session) and marks them as "generating".
        /// Nested wrappers (a coroutine started from inside a step) just run: the stream is already in place.
        public static IEnumerator Steps(IEnumerator inner)
        {
            while (true)
            {
                bool more;
                if (_depth == 0)
                {
                    bool swap = _isolating;
                    var outer = UnityEngine.Random.state;
                    if (swap) UnityEngine.Random.state = _stream;
                    _depth++; Generating = true;
                    try { more = inner.MoveNext(); }
                    finally
                    {
                        _depth--; Generating = false;
                        if (swap) { _stream = UnityEngine.Random.state; UnityEngine.Random.state = outer; }
                    }
                }
                else more = inner.MoveNext();
                if (!more) yield break;
                yield return inner.Current;
            }
        }

        // ------------------------------------------------------------------ signature

        /// What must be equal on every copy: each room's template and place, enemy names, chest contents.
        public static string Signature(out object detail)
        {
            var dm = DungeonManager.Instance;
            detail = null;
            if (dm == null || dm.dungeonRooms == null) return "";
            var rooms = new List<string>();
            foreach (var r in dm.dungeonRooms.Where(r => r != null).OrderBy(r => r.x).ThenBy(r => r.y).ThenBy(r => r.isSecret))
            {
                var sb = new StringBuilder();
                sb.Append(Presence.RoomKey(r)).Append('=').Append(r.roomInfo != null ? r.roomInfo.name : "?");
                sb.Append(" e:").Append(string.Join(",", Enemies.GeneratedNamesIn(r)));
                var chests = r.GetComponentsInChildren<Chest>(true);
                foreach (var c in chests.OrderBy(c => c.transform.position.x).ThenBy(c => c.transform.position.y))
                {
                    sb.Append(" c:");
                    try { sb.Append(string.Join("+", c.GetComponentsInChildren<ItemStack>(true).Select(i => i.name))); } catch { sb.Append("?"); }
                }
                rooms.Add(sb.ToString());
            }
            detail = rooms;
            unchecked
            {
                int h = 17;
                foreach (var s in rooms) foreach (char ch in s) h = h * 31 + ch;
                return h.ToString("X8") + "/" + rooms.Count;
            }
        }

        // ------------------------------------------------------------------ group trips

        /// Host: tell everyone to make a trip. Guest: ask the host for one.
        public static void Announce(Trip trip, ItemMaster.Culture culture, int level)
        {
            if (!Session.IsHost) return;
            Session.Send(TravelMsg, new W().U8((byte)trip).I32((int)culture).I32(level));
            TripsSent++;
            Plugin.Log.LogInfo($"[floor] trip for everyone: {trip} {culture} {level}");
        }

        public static void Ask(Trip trip, ItemMaster.Culture culture, int level)
        {
            Session.SendToHost(TravelAsk, new W().U8((byte)trip).I32((int)culture).I32(level));
            Plugin.Log.LogInfo($"[floor] asked the host for a trip: {trip} {culture} {level}");
        }

        private static void OnTravelAsk(int sender, R r)
        {
            if (!Session.IsHost) return;
            var trip = (Trip)r.U8(); var culture = (ItemMaster.Culture)r.I32(); int level = r.I32();
            var hero = HeroMerchant.Instance; var gm = GameManager.Instance;
            if (hero == null || !gm.NotLoadingScene) return;
            // The host decides where "next" is: a guest can only ask to go one floor down from where the host is.
            if (trip == Trip.Next)
            {
                var dmNow = DungeonManager.Instance;
                if (gm.IsDungeonSceneLoaded() && dmNow != null) { culture = dmNow.culture; level = dmNow.level + 1; }
                else if (level != 0 || culture < ItemMaster.Culture.Golem || culture > ItemMaster.Culture.Unknown) return;
                if (level > 3 && culture != ItemMaster.Culture.Wanderer) return;
            }
            if (trip == Trip.Town && !gm.IsDungeonSceneLoaded()) return;
            Plugin.Log.LogInfo($"[floor] player {sender} asks for {trip} {culture} {level}");
            Run(trip, culture, level, true);
        }

        private static void OnTravel(int sender, R r)
        {
            if (Session.IsHost) return;
            var trip = (Trip)r.U8(); var culture = (ItemMaster.Culture)r.I32(); int level = r.I32();
            Plugin.Instance.StartCoroutine(Follow(trip, culture, level));
        }

        private static IEnumerator Follow(Trip trip, ItemMaster.Culture culture, int level)
        {
            var gm = GameManager.Instance;
            float until = Time.unscaledTime + 30f;
            while (Time.unscaledTime < until && (HeroMerchant.Instance == null || !gm.NotLoadingScene)) yield return null;
            ClosePanels();
            TripsApplied++;
            Run(trip, culture, level, false);
        }

        /// Make the trip here. asHost: the host itself goes (and that announces it to everyone).
        private static void Run(Trip trip, ItemMaster.Culture culture, int level, bool asHost)
        {
            var hero = HeroMerchant.Instance; var gm = GameManager.Instance;
            ClosePanels();
            ApplyingRemoteTravel = !asHost;
            try
            {
                if (trip == Trip.Town)
                {
                    if (gm.IsDungeonSceneLoaded()) hero.LoadTownScene();
                }
                else if (trip == Trip.Wanderer)
                {
                    var dlc = Moonlighter.DLC.WandererDungeon.WandererDLCController.Instance;
                    if (dlc == null) { Plugin.Log.LogWarning("[floor] the host went into Between Dimensions, but the DLC is not active here"); return; }
                    dlc.WorldDungeonDoor_OnDungeonPortalActivateToFloor(ItemMaster.Culture.Wanderer, level);
                }
                else if (level <= 0) hero.LoadDungeonScene(culture);
                else
                {
                    hero._currentDungeonLevel = level - 1;
                    hero._lastDungeonCulture = culture;
                    hero.LoadNextLevelDungeonScene(culture);
                }
            }
            finally { ApplyingRemoteTravel = false; }
        }

        /// Guest: in the host's world and in control. Host: if it is inside a dungeon, the newcomer is sent the
        /// seed of that floor and a trip there (late join, or a guest who came back to the village somehow).
        public static void SendReady()
        {
            var dm = DungeonManager.Instance; var gm = GameManager.Instance;
            bool inDungeon = gm != null && gm.IsDungeonSceneLoaded() && dm != null;
            Session.SendToHost(ReadyMsg, new W().I32(inDungeon ? (int)dm.culture : -1).I32(inDungeon ? dm.level : -1).I32(inDungeon ? CurrentSeed : 0));
        }

        public static int LateJoinTrips;

        private static void OnReady(int sender, R r)
        {
            if (!Session.IsHost) return;
            int theirCulture = r.Left >= 12 ? r.I32() : -1;
            int theirLevel = r.Left >= 8 ? r.I32() : -1;
            int theirSeed = r.Left >= 4 ? r.I32() : 0;
            Downed.SendStateTo(sender);
            var gm = GameManager.Instance; var dm = DungeonManager.Instance; var hero = HeroMerchant.Instance;
            if (gm == null || hero == null || !gm.IsDungeonSceneLoaded() || dm == null || CurrentSeed == 0) return;
            if (theirSeed == CurrentSeed && theirCulture == (int)dm.culture && theirLevel == dm.level) return;   // already there
            Session.SendTo(sender, SeedMsg, new W().I32((int)dm.culture).I32(dm.level).I32(CurrentSeed));
            var trip = dm.culture == ItemMaster.Culture.Wanderer ? Trip.Wanderer : Trip.Next;
            Session.SendTo(sender, TravelMsg, new W().U8((byte)trip).I32((int)dm.culture).I32(dm.level));
            LateJoinTrips++;
            Plugin.Log.LogInfo($"[floor] player {sender} joined while we are in {dm.culture} floor {dm.level}: sent there");
        }

        public static void ClosePanels()
        {
            var gui = GUIManager.Instance;
            if (gui == null) return;
            if (gui.currentInventory != null)
            {
                gui.AllowPanelChange();
                gui.DisableCurrentPanel();
            }
            if (GameManager.Instance != null && GameManager.Instance._isGamePaused) GameManager.Instance.UnpauseGame();
        }

        public static void OnTownReached()
        {
            Seeds.Clear();
        }
    }

    // ---------------------------------------------------------------------- patches

    [HarmonyPatch(typeof(DungeonGenerator), nameof(DungeonGenerator.GenerateDungeon), typeof(ItemMaster.Culture), typeof(int), typeof(bool))]
    static class IsolateFloorGeneration
    {
        static void Postfix(ItemMaster.Culture culture, int level, bool test, ref IEnumerator __result)
        {
            Enemies.OnFloorStart();
            if (Environment.GetCommandLineArgs().Contains("--mt-no-isolate")) return;
            __result = Floor.Isolate(__result, culture, level);
        }
    }

    [HarmonyPatch(typeof(DungeonGenerator), "GenerateDungeonCoroutine")]
    static class IsolateFloorCoroutine
    {
        static void Postfix(ref IEnumerator __result)
        {
            __result = Floor.Steps(__result);
        }
    }

    // Trips: the host leads; a guest that walks into a dungeon gate or down a hole asks the host instead.
    [HarmonyPatch(typeof(HeroMerchant), nameof(HeroMerchant.LoadNextLevelDungeonScene))]
    static class GroupNextLevel
    {
        static bool Prefix(HeroMerchant __instance, ItemMaster.Culture culture, bool istest)
        {
            if (!Session.Active || istest || Floor.ApplyingRemoteTravel || Session.IsHost) return true;
            Floor.Ask(Floor.Trip.Next, culture, __instance._currentDungeonLevel + 1);
            // LoadDungeonScene already set the level to -1 before calling us: put it back.
            if (!GameManager.Instance.IsDungeonSceneLoaded()) __instance._currentDungeonLevel = 0;
            return false;
        }

        static void Postfix(HeroMerchant __instance, ItemMaster.Culture culture, bool istest)
        {
            if (Session.IsHost && !istest) Floor.Announce(Floor.Trip.Next, culture, __instance._currentDungeonLevel);
        }
    }

    [HarmonyPatch(typeof(HeroMerchant), nameof(HeroMerchant.LoadTownScene))]
    static class GroupGoHome
    {
        static bool Prefix()
        {
            if (!Session.Active || Floor.ApplyingRemoteTravel || Session.IsHost) return true;
            if (Downed.AllowSoloTripHome) return true;
            Floor.Ask(Floor.Trip.Town, ItemMaster.Culture.Merchant, 0);
            return false;
        }

        static void Postfix()
        {
            if (Session.IsHost) Floor.Announce(Floor.Trip.Town, ItemMaster.Culture.Merchant, 0);
            Floor.OnTownReached();
        }
    }

    /// In a session nothing stops the world: a menu, a chest or the pause screen freeze only you (your Will
    /// stops taking input), never the rooms other players are fighting in.
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.PauseGame))]
    static class NoGlobalPause
    {
        static bool Prefix(GameManager __instance)
        {
            if (!Session.Active) return true;
            __instance._isGamePaused = true;
            var ev = AccessTools.Field(typeof(GameManager), "OnGamePaused")?.GetValue(null) as Delegate;
            ev?.DynamicInvoke(true);
            return false;
        }
    }
}

namespace MoonlighterTogether.Sync
{
    // Entering the DLC dungeon from its portal in town is a group trip like any other.
    [HarmonyLib.HarmonyPatch(typeof(Moonlighter.DLC.WandererDungeon.WandererDLCController), "WorldDungeonDoor_OnDungeonPortalActivate")]
    static class GroupWandererEntry
    {
        static bool Prefix(ItemMaster.Culture culture)
        {
            if (culture != ItemMaster.Culture.Wanderer || !Session.Active || Floor.ApplyingRemoteTravel) return true;
            if (!Session.IsHost) { Floor.Ask(Floor.Trip.Wanderer, culture, 0); return false; }
            Floor.Announce(Floor.Trip.Wanderer, culture, 0);
            return true;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Moonlighter.DLC.WandererDungeon.WandererDLCController), "WorldDungeonDoor_OnDungeonPortalActivateToFloor")]
    static class GroupWandererEntryToFloor
    {
        static bool Prefix(ItemMaster.Culture culture, int floor)
        {
            if (culture != ItemMaster.Culture.Wanderer || !Session.Active || Floor.ApplyingRemoteTravel) return true;
            if (!Session.IsHost) { Floor.Ask(Floor.Trip.Wanderer, culture, floor); return false; }
            Floor.Announce(Floor.Trip.Wanderer, culture, floor);
            return true;
        }
    }

    // Between Dimensions (DLC): its dungeon has its own generator; its intermediate floors too; and it adds
    // a secret room to normal floors in a coroutine that runs after the main generation. All of them get the
    // same treatment: the host's seed and the private random stream.
    [HarmonyLib.HarmonyPatch(typeof(Moonlighter.DLC.WandererDungeon.WandererDLCController), "StartDungeonGeneration", typeof(ItemMaster.Culture), typeof(int))]
    static class IsolateWandererFloor
    {
        static void Postfix(ItemMaster.Culture culture, int floor, ref System.Collections.IEnumerator __result)
        {
            Enemies.OnFloorStart();
            __result = Floor.Isolate(__result, culture, floor);
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Moonlighter.DLC.WandererDungeon.WandererDLCController), nameof(Moonlighter.DLC.WandererDungeon.WandererDLCController.StartIntermediateDungeonGeneration), typeof(ItemMaster.Culture), typeof(ItemMaster.Culture), typeof(int))]
    static class IsolateIntermediateFloor
    {
        static void Postfix(ItemMaster.Culture currentCulture, int floor, ref System.Collections.IEnumerator __result)
        {
            Enemies.OnFloorStart();
            __result = Floor.Isolate(__result, currentCulture, 100 + floor);   // its own seed slot
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Moonlighter.DungeonGeneration.DungeonGenerator), nameof(Moonlighter.DungeonGeneration.DungeonGenerator.GenerateDungeonCoroutine))]
    static class IsolateWandererGeneratorSteps
    {
        static void Postfix(ref System.Collections.IEnumerator __result) => __result = Floor.Steps(__result);
    }

    [HarmonyLib.HarmonyPatch(typeof(Moonlighter.DLC.WandererDungeon.WandererDLCController), "DungeonGenerator_PostprocessDungeonEvent")]
    static class IsolateDlcPostprocess
    {
        static void Postfix(ref System.Collections.IEnumerator __result) => __result = Floor.Steps(__result);
    }
}
