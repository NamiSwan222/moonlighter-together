using System.Collections.Generic;
using System.Linq;
using MoonlighterTogether.Net;
using UnityEngine;

namespace MoonlighterTogether.Sync
{
    /// Where every player is and what their Will looks like.
    ///
    /// Each copy sends, ~20 times a second on the lossy channel, its zone (menu / town / one dungeon room),
    /// position, health and the sprites its Will is showing right now (body, armour, weapon, effects).
    /// Copying the visible sprites instead of re-running Will's animator means every animation, weapon and
    /// colour the other player has is drawn exactly, with no animation events firing on our side.
    public static class Presence
    {
        public const float SendRate = 20f;
        public static readonly Dictionary<int, Remote> Remotes = new Dictionary<int, Remote>();
        private static float _nextSend;
        private static readonly W Out = new W();
        public static int PosesSent, PosesReceived;

        public sealed class Remote
        {
            public int Id;
            public string Zone = "menu";
            public Vector2 Pos;
            public float Hp, MaxHp;
            public bool Ghost;
            public double Stamp;
            public float LastHeard;
            public readonly List<Part> Parts = new List<Part>();
            public Puppet Puppet;
            public bool SameZoneAsMe => Zone == MyZone && Zone != "menu";
        }

        public struct Part
        {
            public string Sprite;
            public Vector2 Offset;
            public bool FlipX, FlipY;
            public int Layer, Order;
            public Color32 Color;
        }

        public static void Init()
        {
            Session.On(Msg.Pose, Relay.Others, OnPose, Channel.State);
            Session.PlayerLeft += (p, temporary) =>
            {
                // A dropped player's Will stays where they were (greyed) until they come back.
                if (!Remotes.TryGetValue(p.Id, out var r)) return;
                if (temporary) { if (r.Puppet != null) r.Puppet.SetAway(true); }
                else { if (r.Puppet != null) Object.Destroy(r.Puppet.gameObject); Remotes.Remove(p.Id); }
            };
            Session.PlayerJoined += (p, back) => { if (Remotes.TryGetValue(p.Id, out var r) && r.Puppet != null) r.Puppet.SetAway(false); };
            Session.Ended += why => { foreach (var r in Remotes.Values) if (r.Puppet != null) Object.Destroy(r.Puppet.gameObject); Remotes.Clear(); };
        }

        // ---------------------------------------------------------------- zone

        /// "menu", "loading", "town", or "dungeon:Culture:level:x,y[s]" for one room of one floor.
        public static string MyZone
        {
            get
            {
                var gm = GameManager.Instance;
                if (gm == null || HeroMerchant.Instance == null) return "menu";
                if (!gm.NotLoadingScene) return "loading";
                if (gm.IsDungeonSceneLoaded())
                {
                    var dm = DungeonManager.Instance;
                    if (dm == null || dm.currentDungeonRoom == null) return "loading";
                    return RoomZone(dm, dm.currentDungeonRoom);
                }
                return gm.IsTownSceneLoaded() ? "town" : "loading";
            }
        }

        public static string RoomZone(DungeonManager dm, DungeonRoom r)
            => $"dungeon:{dm.culture}:{dm.level}:{RoomKey(r)}";

        public static string RoomKey(DungeonRoom r)
            => r == null ? "?" : $"{r.x},{r.y}{(r.isSecret ? "s" : "")}{(r is DungeonBossRoom ? "b" : "")}";

        // ---------------------------------------------------------------- send

        public static void Tick()
        {
            if (!Session.Active) return;
            float now = Time.unscaledTime;
            if (now >= _nextSend)
            {
                _nextSend = now + 1f / SendRate;
                SendPose();
            }
            foreach (var r in Remotes.Values) UpdatePuppet(r);
        }

        private static readonly List<SpriteRenderer> Renderers = new List<SpriteRenderer>();

        private static void SendPose()
        {
            var hero = HeroMerchant.Instance;
            string zone = MyZone;
            Out.Reset().F64(Session.Now).Str(zone);
            if (hero == null || zone == "menu" || zone == "loading")
            {
                Out.V2(Vector2.zero).F32(0).F32(0).Bool(false).U8(0);
            }
            else
            {
                var st = hero.heroMerchantStats;
                Out.V2(hero.transform.position).F32((float)st.currentHealth.Value).F32((float)st.currentHealth.maxValue).Bool(Downed.IsGhost(Session.MyId));
                hero.GetComponentsInChildren(false, Renderers);
                int count = 0;
                foreach (var sr in Renderers) if (Visible(sr)) count++;
                count = Mathf.Min(count, 32);
                Out.U8((byte)count);
                Vector3 root = hero.transform.position;
                int written = 0;
                foreach (var sr in Renderers)
                {
                    if (!Visible(sr) || written >= count) continue;
                    written++;
                    var c = (Color32)sr.color;
                    Out.Str(sr.sprite.name).V2((Vector2)(sr.transform.position - root))
                       .U8((byte)((sr.flipX ? 1 : 0) | (sr.flipY ? 2 : 0) | (sr.transform.lossyScale.x < 0 ? 4 : 0)))
                       .I32(sr.sortingLayerID).I16((short)Mathf.Clamp(sr.sortingOrder, short.MinValue, short.MaxValue))
                       .U8(c.r).U8(c.g).U8(c.b).U8(c.a);
                }
            }
            Session.Send(Msg.Pose, Out, Channel.State);
            PosesSent++;
        }

        private static bool Visible(SpriteRenderer sr)
            => sr != null && sr.enabled && sr.sprite != null && sr.gameObject.activeInHierarchy && sr.color.a > 0.01f;

        // ---------------------------------------------------------------- receive

        private static void OnPose(int sender, R r)
        {
            if (sender == Session.MyId || sender == 255 || !Session.Players.ContainsKey(sender)) return;
            double stamp = r.F64();
            if (!Remotes.TryGetValue(sender, out var rem)) { rem = new Remote { Id = sender }; Remotes[sender] = rem; }
            if (stamp < rem.Stamp) return;   // an older pose that arrived late
            rem.Stamp = stamp;
            rem.LastHeard = Time.unscaledTime;
            rem.Zone = r.Str() ?? "menu";
            rem.Pos = r.V2(); rem.Hp = r.F32(); rem.MaxHp = r.F32(); rem.Ghost = r.Bool();
            int n = r.U8();
            if (n > 32) throw new WireException("too many parts");
            rem.Parts.Clear();
            for (int i = 0; i < n; i++)
            {
                var p = new Part { Sprite = r.Str(), Offset = r.V2() };
                int flags = r.U8();
                p.FlipX = (flags & 1) != 0; p.FlipY = (flags & 2) != 0;
                if ((flags & 4) != 0) p.FlipX = !p.FlipX;   // mirrored by a negative scale on the sender
                p.Layer = r.I32(); p.Order = r.I16();
                p.Color = new Color32(r.U8(), r.U8(), r.U8(), r.U8());
                rem.Parts.Add(p);
            }
            PosesReceived++;
        }

        private static void UpdatePuppet(Remote r)
        {
            bool show = r.SameZoneAsMe && r.Parts.Count > 0;
            if (show && r.Puppet == null)
            {
                var go = new GameObject("MT Player " + r.Id);
                r.Puppet = go.AddComponent<Puppet>();
                r.Puppet.Init(r.Id);
                r.Puppet.Snap(r.Pos);
            }
            if (r.Puppet == null) return;
            r.Puppet.gameObject.SetActive(show);
            if (show) r.Puppet.Apply(r);
        }

        public static Remote Get(int id) => Remotes.TryGetValue(id, out var r) ? r : null;

        /// Remote players standing in the same place as us (and, if asked, alive).
        public static IEnumerable<Remote> HereWithMe(bool aliveOnly = false)
            => Remotes.Values.Where(r => r.SameZoneAsMe && Session.Players.TryGetValue(r.Id, out var p) && p.Connected && (!aliveOnly || !r.Ghost));
    }
}
