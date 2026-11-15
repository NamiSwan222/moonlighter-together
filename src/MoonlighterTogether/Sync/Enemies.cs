using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MoonlighterTogether.Net;
using UnityEngine;

namespace MoonlighterTogether.Sync
{
    /// Enemies, room by room.
    ///
    /// Identity: every copy builds the same floor (Floor.cs), so the n-th enemy created during generation is
    /// the same enemy everywhere ("g#n"). Enemies born later (split slimes, summons, bosses' minions) are
    /// "room/name#k": the k-th of that name in that room.
    ///
    /// Authority: each room has an owner, the player who has been in it longest (the host keeps the table).
    /// The owner runs that room's enemies as the game does; everyone else in the room sees mirrored copies
    /// (AI off, position, animation, health from the owner), sends their hits to the owner, and runs the
    /// game's own death when the owner says an enemy died (so drops are personal). Players in different
    /// rooms each own their own room: nobody is pulled through doors.
    public static class Enemies
    {
        public const Msg OwnersMsg = (Msg)25;
        public const Msg StatesMsg = (Msg)26;
        public const Msg DiedMsg = (Msg)27;
        public const Msg DeadListMsg = (Msg)28;
        public const Msg HitMsg = (Msg)29;
        public const Msg SpawnMsg = (Msg)30;

        public const float StateRate = 15f;

        // ---- identity ----
        private static readonly Dictionary<string, Enemy> ById = new Dictionary<string, Enemy>();
        private static readonly Dictionary<Enemy, string> IdOf = new Dictionary<Enemy, string>();
        private static readonly Dictionary<Enemy, DungeonRoom> RoomOf = new Dictionary<Enemy, DungeonRoom>();
        private static readonly List<KeyValuePair<string, Enemy>> GenOrder = new List<KeyValuePair<string, Enemy>>();
        /// What generation created, kept after enemies die and are destroyed: (room key, enemy name) per id.
        private static readonly List<(string room, string name)> GenRecord = new List<(string, string)>();
        private static readonly Dictionary<string, int> RuntimeCounter = new Dictionary<string, int>();
        private static int _genCounter;
        public static string ApplyingSpawnId;

        // ---- authority ----
        /// zone -> owner player id (everyone has the host's table).
        public static readonly Dictionary<string, int> Owners = new Dictionary<string, int>();
        // host only
        private static readonly Dictionary<int, string> PlayerZone = new Dictionary<int, string>();
        private static readonly Dictionary<int, int> PlayerSeed = new Dictionary<int, int>();
        private static readonly Dictionary<string, List<int>> Arrivals = new Dictionary<string, List<int>>();
        public static readonly HashSet<string> DeadThisFloor = new HashSet<string>();

        // ---- flags ----
        public static bool ApplyingDeath, ApplyingRemoteHit;

        // ---- statistics for tests ----
        public static int StatesSent, StatesApplied, HitsSent, HitsApplied, DeathsSent, DeathsApplied, SilentRemovals,
            SpawnsSent, SpawnsCreated, LocalDuplicatesRemoved, Culled, OwnerChanges, Takeovers;

        private static string _lastZoneSent = "";
        private static int _lastSeedSent;
        private static float _nextStates, _nextZoneResend, _nextTargets;
        private static readonly W Out = new W();

        public static void Init()
        {
            Session.On(Msg.Zone, Relay.None, OnZone);
            Session.On(OwnersMsg, Relay.None, OnOwners);
            Session.On(StatesMsg, Relay.Others, OnStates, Channel.State);
            Session.On(DiedMsg, Relay.Others, OnDied);
            Session.On(DeadListMsg, Relay.None, OnDeadList);
            Session.On(HitMsg, Relay.Others, OnHit);
            Session.On(SpawnMsg, Relay.Others, OnSpawn);
            Session.PlayerLeft += (p, temporary) => { if (Session.IsHost) { SetZone(p.Id, "away", 0); } };
            Session.Started += back => { _lastZoneSent = ""; if (Session.IsHost) { Owners.Clear(); PlayerZone.Clear(); Arrivals.Clear(); } };
            // Session over: every enemy gets its own brain back, single-player as usual.
            Session.Ended += why =>
            {
                foreach (var m in UnityEngine.Object.FindObjectsOfType<EnemyMirror>()) m.Release();
                TargetOf.Clear(); Owners.Clear(); PlayerZone.Clear(); Arrivals.Clear(); DeadThisFloor.Clear();
            };
            TargetPatches.Install(Plugin.Harmony);
        }

        // =====================================================================================
        // identity
        // =====================================================================================

        public static void OnFloorStart()
        {
            foreach (var m in UnityEngine.Object.FindObjectsOfType<EnemyMirror>()) UnityEngine.Object.Destroy(m);
            ById.Clear(); IdOf.Clear(); RoomOf.Clear(); GenOrder.Clear(); GenRecord.Clear(); RuntimeCounter.Clear(); _genCounter = 0;
            _seen.Clear(); _missingSince.Clear(); TargetOf.Clear();
            if (Session.IsHost) DeadThisFloor.Clear();
        }

        public static string Id(Enemy e) => e != null && IdOf.TryGetValue(e, out var id) ? id : null;
        public static Enemy Find(string id) => id != null && ById.TryGetValue(id, out var e) && e != null ? e : null;
        public static IEnumerable<Enemy> SpawnedIn(DungeonRoom r) => GenOrder.Where(kv => kv.Value != null && RoomOf.TryGetValue(kv.Value, out var rr) && rr == r).Select(kv => kv.Value);
        /// Names of the enemies generation put in a room (dead or alive).
        public static IEnumerable<string> GeneratedNamesIn(DungeonRoom r) { string k = Presence.RoomKey(r); return GenRecord.Where(x => x.room == k).Select(x => x.name); }

        public static DungeonRoom Room(Enemy e)
        {
            if (e == null) return null;
            if (e.enemyRoom != null) return e.enemyRoom;
            if (RoomOf.TryGetValue(e, out var r) && r != null) return r;
            // Some spawn paths set no room on the enemy: it is still inside its room in the scene.
            for (var t = e.transform.parent; t != null && r == null; t = t.parent) r = t.GetComponent<DungeonRoom>();   // works on inactive rooms too
            if (r != null) RoomOf[e] = r;
            return r;
        }

        public static void Register(Enemy e, string id, DungeonRoom room)
        {
            if (IdOf.TryGetValue(e, out var old)) ById.Remove(old);
            ById[id] = e; IdOf[e] = id;
            if (room != null) RoomOf[e] = room;
        }

        internal static void OnEnemyInit(Enemy e, string name)
        {
            if (e == null || IdOf.ContainsKey(e)) return;
            var dm = DungeonManager.Instance;
            if (Floor.Generating)
            {
                string gid = "g#" + _genCounter++;
                Register(e, gid, e.enemyRoom);
                GenOrder.Add(new KeyValuePair<string, Enemy>(gid, e));
                GenRecord.Add((Presence.RoomKey(e.enemyRoom), string.IsNullOrEmpty(name) ? e.name : name));
                return;
            }
            if (dm == null || GameManager.Instance == null || !GameManager.Instance.IsDungeonSceneLoaded()) return;
            var room = e.enemyRoom != null ? e.enemyRoom : dm.currentDungeonRoom;
            if (ApplyingSpawnId != null) { Register(e, ApplyingSpawnId, room); return; }
            string key = Presence.RoomKey(room) + "/" + (string.IsNullOrEmpty(name) ? e.name : name);
            RuntimeCounter.TryGetValue(key, out int k);
            RuntimeCounter[key] = k + 1;
            string id = key + "#" + k;
            Register(e, id, room);
            if (!Session.Active || room == null) return;
            string zone = Presence.RoomZone(dm, room);
            if (AmOwner(zone))
            {
                Session.Send(SpawnMsg, Out.Reset().I32(Floor.CurrentSeed).Str(id).Str(name ?? "").V2(e.transform.position));
                SpawnsSent++;
            }
            // Not the owner: a copy born from a mirrored animation here. It stays only if the owner has it too
            // (the owner's states keep it alive; otherwise it is culled in a few seconds).
        }

        internal static void RegisterBoss(DungeonBossRoom room)
        {
            var e = room != null && room.Boss != null ? room.Boss.enemy : null;
            if (e == null || IdOf.ContainsKey(e)) return;
            Register(e, "boss:" + Presence.RoomKey(room), room);
            Plugin.Log.LogInfo($"[enemy] boss {e.name} is {Id(e)}");
        }

        private static void OnSpawn(int sender, R r)
        {
            int seed = r.I32(); string id = r.Str(); string name = r.Str(); Vector2 pos = r.V2();
            if (seed != Floor.CurrentSeed || id == null || name == null || name.Length > 64 || Find(id) != null) return;
            // Only the owner of a room creates enemies in it.
            int slash = id.IndexOf('/');
            var dms = DungeonManager.Instance;
            if (slash <= 0 || dms == null || !Owners.TryGetValue($"dungeon:{dms.culture}:{dms.level}:{id.Substring(0, slash)}", out int sowner) || sowner != sender) return;
            CreateRemote(id, name, pos);
        }

        private static Enemy CreateRemote(string id, string name, Vector2 pos)
        {
            var dm = DungeonManager.Instance;
            if (dm == null || dm.currentDungeonRoom == null || string.IsNullOrEmpty(name)) return null;
            int slash = id.IndexOf('/');
            string roomKey = slash > 0 ? id.Substring(0, slash) : null;
            var room = dm.dungeonRooms.FirstOrDefault(x => x != null && Presence.RoomKey(x) == roomKey);
            if (room == null || room != dm.currentDungeonRoom) return null;   // only needed where we stand
            GameObject prefab = null;
            try { prefab = EnemiesPrefabRegister.GetEnemyByName(name); } catch { }
            if (prefab == null && PrefabRegister.Instance != null) prefab = PrefabRegister.Instance.GetPrefabByName(name);
            if (prefab == null) { Plugin.Log.LogWarning($"[enemy] no prefab {name} for {id}"); return null; }
            var go = UnityEngine.Object.Instantiate(prefab, new Vector3(pos.x, pos.y, 0), Quaternion.identity, room.transform);
            var e = go.GetComponent<Enemy>();
            if (e == null) { UnityEngine.Object.Destroy(go); return null; }
            ApplyingSpawnId = id;
            try { e.Init(name); } finally { ApplyingSpawnId = null; }
            RoomOf[e] = room;
            EnemyMirror.Take(e);
            SpawnsCreated++;
            return e;
        }

        // =====================================================================================
        // authority
        // =====================================================================================

        public static bool AmOwner(string zone)
        {
            if (!Session.Active) return true;
            // Until the host's table says otherwise, a room we are in is ours (a room nobody else is in).
            return !Owners.TryGetValue(zone, out int owner) || owner == Session.MyId;
        }

        public static bool AmOwnerOf(Enemy e)
        {
            var dm = DungeonManager.Instance;
            var room = Room(e);
            if (!Session.Active || dm == null || room == null) return true;
            return AmOwner(Presence.RoomZone(dm, room));
        }

        private static void OnZone(int sender, R r)
        {
            if (!Session.IsHost) return;
            SetZone(sender, r.Str() ?? "menu", r.I32());
        }

        private static void SetZone(int player, string zone, int seed)
        {
            PlayerZone.TryGetValue(player, out var old);
            PlayerZone[player] = zone;
            PlayerSeed.TryGetValue(player, out int oldSeed);
            PlayerSeed[player] = seed;
            if (old == zone) { MaybeSendDeadList(player, oldSeed, seed); return; }
            if (old != null && Arrivals.TryGetValue(old, out var leftList)) leftList.Remove(player);
            if (zone.StartsWith("dungeon:"))
            {
                if (!Arrivals.TryGetValue(zone, out var list)) Arrivals[zone] = list = new List<int>();
                if (!list.Contains(player)) list.Add(player);
            }
            // Owner of every room: the earliest arrival still there.
            bool changed = false;
            foreach (var z in Arrivals.Keys.ToList())
            {
                var list = Arrivals[z];
                if (list.Count == 0) { if (Owners.Remove(z)) changed = true; Arrivals.Remove(z); continue; }
                if (!Owners.TryGetValue(z, out int o) || o != list[0]) { Owners[z] = list[0]; changed = true; }
            }
            if (changed) BroadcastOwners();
            MaybeSendDeadList(player, oldSeed, seed);
        }

        private static void MaybeSendDeadList(int player, int oldSeed, int seed)
        {
            if (seed == 0 || seed == oldSeed || seed != Floor.CurrentSeed || player == 0) return;
            var w = new W().I32(seed).I32(DeadThisFloor.Count);
            foreach (var id in DeadThisFloor) w.Str(id);
            Session.SendTo(player, DeadListMsg, w);
        }

        private static void BroadcastOwners()
        {
            var w = new W().I32(Owners.Count);
            foreach (var kv in Owners) w.Str(kv.Key).U8((byte)kv.Value);
            Session.Send(OwnersMsg, w);
            ApplyOwners(new R(w.ToArray()));
        }

        private static void OnOwners(int sender, R r) { if (!Session.IsHost) ApplyOwners(r); }

        private static void ApplyOwners(R r)
        {
            string myZone = Presence.MyZone;
            bool wasOwner = AmOwner(myZone);
            Owners.Clear();
            int n = r.Count(4096);
            for (int i = 0; i < n; i++) { string z = r.Str(); Owners[z] = r.U8(); }
            OwnerChanges++;
            bool isOwner = AmOwner(myZone);
            if (isOwner && !wasOwner) { Takeovers++; Plugin.Log.LogInfo($"[enemy] this room is ours now: {myZone}"); }
        }

        // =====================================================================================
        // every frame
        // =====================================================================================

        public static void Tick()
        {
            if (!Session.Active) return;
            var gm = GameManager.Instance;
            string zone = Presence.MyZone;
            float now = Time.unscaledTime;
            int seed = zone.StartsWith("dungeon:") ? Floor.CurrentSeed : 0;
            if (zone != _lastZoneSent || seed != _lastSeedSent || now >= _nextZoneResend)
            {
                if (zone != "loading")
                {
                    _lastZoneSent = zone; _lastSeedSent = seed; _nextZoneResend = now + 2f;
                    if (Session.IsHost) SetZone(0, zone, seed);
                    else Session.SendToHost(Msg.Zone, Out.Reset().Str(zone).I32(seed));
                }
            }
            if (!zone.StartsWith("dungeon:") || gm == null || !gm.NotLoadingScene) return;
            var dm = DungeonManager.Instance;
            var room = dm.currentDungeonRoom;
            bool owner = AmOwner(zone);
            var here = InRoom(dm, room).ToList();

            foreach (var e in here)
            {
                var m = e.GetComponent<EnemyMirror>();
                if (owner) { if (m != null) m.Release(); }
                else if (m == null) EnemyMirror.Take(e);
            }

            if (owner)
            {
                if (now >= _nextTargets) { _nextTargets = now + 0.25f; ChooseTargets(here); }
                if (now >= _nextStates && Presence.HereWithMe().Any())
                {
                    _nextStates = now + 1f / StateRate;
                    SendStates(zone, here);
                }
            }
            else Cull(zone, here, now);
        }

        /// Living, networked enemies of a room, including the boss and enemies born there later.
        public static IEnumerable<Enemy> InRoom(DungeonManager dm, DungeonRoom room)
        {
            if (room == null) yield break;
            foreach (var kv in ById)
            {
                var e = kv.Value;
                if (e == null || e.enemyStats == null || !e.enemyStats.isAlive || !e.gameObject.activeInHierarchy) continue;
                if (Room(e) != room) continue;
                yield return e;
            }
        }

        // ---------------------------------------------------------------- owner: targets and states

        public static readonly Dictionary<Enemy, int> TargetOf = new Dictionary<Enemy, int>();

        private static void ChooseTargets(List<Enemy> here)
        {
            var hero = HeroMerchant.Instance;
            var remotes = Presence.HereWithMe(aliveOnly: true).Where(r => r.Puppet != null).ToList();
            bool meAlive = hero != null && !Downed.Me && !(bool)hero.heroMerchantStats.isDead;
            foreach (var e in here)
            {
                var b = e.enemyBehaviour;
                if (b == null) continue;
                Vector2 at = e.transform.position;
                int best = meAlive ? Session.MyId : -1;
                float bestD = meAlive ? ((Vector2)hero.transform.position - at).sqrMagnitude : float.MaxValue;
                foreach (var r in remotes)
                {
                    float d = ((Vector2)r.Puppet.transform.position - at).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = r.Id; }
                }
                TargetOf[e] = best;
                b.heroMerchantTransform = TargetTransform(best) ?? (hero != null ? hero.transform : null);
            }
        }

        public static Transform TargetTransform(int playerId)
        {
            if (playerId == Session.MyId) return HeroMerchant.Instance != null ? HeroMerchant.Instance.transform : null;
            var r = Presence.Get(playerId);
            return r != null && r.Puppet != null ? r.Puppet.transform : null;
        }

        private static readonly List<Animator> Anims = new List<Animator>();

        private static void SendStates(string zone, List<Enemy> here)
        {
            Out.Reset().I32(Floor.CurrentSeed).Str(zone).I32(here.Count);
            foreach (var e in here)
            {
                var t = e.transform;
                Out.Str(Id(e)).V2(t.position).F32(t.localScale.x).F32(e.enemyStats.CurrentHealth);
                Out.Bool(e.enemySprite != null && e.enemySprite.flipX);
                Out.U8((byte)(TargetOf.TryGetValue(e, out int tgt) && tgt >= 0 ? tgt : 255));
                Out.Str(e.name);
                e.GetComponentsInChildren(true, Anims);
                Out.U8((byte)Mathf.Min(Anims.Count, 8));
                for (int i = 0; i < Anims.Count && i < 8; i++) AnimState.Write(Out, Anims[i]);
            }
            Session.Send(StatesMsg, Out, Channel.State);
            StatesSent++;
        }

        // ---------------------------------------------------------------- others: apply states

        private static readonly Dictionary<string, float> _seen = new Dictionary<string, float>();
        private static readonly Dictionary<Enemy, float> _missingSince = new Dictionary<Enemy, float>();
        private static float _lastStatesForMyRoom;

        private static void OnStates(int sender, R r)
        {
            int seed = r.I32(); string zone = r.Str();
            if (seed != Floor.CurrentSeed || zone != Presence.MyZone || AmOwner(zone)) return;
            if (!Owners.TryGetValue(zone, out int owner) || owner != sender) return;
            int n = r.Count(512);
            float now = Time.unscaledTime;
            _lastStatesForMyRoom = now;
            for (int i = 0; i < n; i++)
            {
                string id = r.Str(); Vector2 pos = r.V2(); float scaleX = r.F32(); float hp = r.F32(); bool flip = r.Bool(); int target = r.U8(); string name = r.Str();
                int na = r.U8();
                var e = Find(id);
                if (e == null && id.Contains("/")) e = CreateRemote(id, name, pos);
                var m = e != null ? (e.GetComponent<EnemyMirror>() ?? EnemyMirror.Take(e)) : null;
                for (int k = 0; k < na; k++) AnimState.Read(r, m != null ? m.Animator(k) : null);
                if (e == null || m == null) continue;
                _seen[id] = now;
                m.Apply(pos, scaleX, hp, flip);
                var tt = target == 255 ? null : TargetTransform(target);
                if (tt != null && e.enemyBehaviour != null) e.enemyBehaviour.heroMerchantTransform = tt;
                StatesApplied++;
            }
        }

        /// An enemy here that the owner no longer reports (its death message was lost, or it was a copy born
        /// only on this side) goes away after a few seconds.
        private static void Cull(string zone, List<Enemy> here, float now)
        {
            if (now - _lastStatesForMyRoom > 1f) { _missingSince.Clear(); return; }
            foreach (var e in here)
            {
                string id = Id(e);
                if (id == null) continue;
                if (_seen.TryGetValue(id, out float t) && now - t < 1.5f) { _missingSince.Remove(e); continue; }
                if (!_missingSince.TryGetValue(e, out float since)) { _missingSince[e] = now; continue; }
                if (now - since < 3f) continue;
                _missingSince.Remove(e);
                Culled++;
                if (id.StartsWith("boss:")) continue;   // a boss is only ever removed by its death
                Plugin.Log.LogInfo($"[enemy] {id} ({e.name}) is not at the owner: removed");
                if (id.Contains("/")) { LocalDuplicatesRemoved++; ById.Remove(id); IdOf.Remove(e); UnityEngine.Object.Destroy(e.gameObject); }
                else RemoveSilently(e);
            }
        }

        // ---------------------------------------------------------------- hits

        internal static bool InterceptHit(Enemy e, float strength, Enemy.EnemyDamageReceivedType type)
        {
            if (!Session.Active || ApplyingRemoteHit) return false;
            string id = Id(e);
            if (id == null || AmOwnerOf(e)) return false;
            Session.Send(HitMsg, Out.Reset().I32(Floor.CurrentSeed).Str(id).F32(strength).I32((int)type));
            HitsSent++;
            var m = e.GetComponent<EnemyMirror>();
            if (m != null) m.Flash();
            return true;
        }

        private static void OnHit(int sender, R r)
        {
            int seed = r.I32(); string id = r.Str(); float strength = r.F32(); int type = r.I32();
            if (seed != Floor.CurrentSeed) return;
            var e = Find(id);
            if (e == null || !e.enemyStats.isAlive || !AmOwnerOf(e)) return;
            // A hit only counts from someone standing in that room.
            var dmh = DungeonManager.Instance; var from = Presence.Get(sender);
            if (dmh == null || from == null || from.Zone != Presence.RoomZone(dmh, Room(e))) return;
            if (strength < 0 || strength > 100000) return;
            HitsApplied++;
            ApplyingRemoteHit = true;
            try
            {
                bool wasEnabled = e.enemyBehaviour.enabled;
                e.DealDamageToEnemy(strength, null, (Enemy.EnemyDamageReceivedType)type);
            }
            finally { ApplyingRemoteHit = false; }
        }

        // ---------------------------------------------------------------- deaths

        internal static void OnLocalDeath(Enemy e)
        {
            string id = Id(e);
            if (!Session.Active || id == null || ApplyingDeath) return;
            if (!AmOwnerOf(e)) return;
            Session.Send(DiedMsg, Out.Reset().I32(Floor.CurrentSeed).Str(id));
            if (Session.IsHost) RecordDeath(id, e);
            DeathsSent++;
        }

        private static void RecordDeath(string id, Enemy e)
        {
            if (DeadThisFloor.Count < 5000) DeadThisFloor.Add(id);
            // The boss counts for the whole group even if the host was elsewhere when it fell.
            if (id.StartsWith("boss:") && DungeonManager.Instance != null && GameManager.Instance != null)
                GameManager.Instance.bossDefeatedCulture = DungeonManager.Instance.culture;
        }

        private static void OnDied(int sender, R r)
        {
            int seed = r.I32(); string id = r.Str();
            if (seed != Floor.CurrentSeed) return;
            var e = Find(id);
            // Only the owner of the enemy's room can say it died.
            var dmd = DungeonManager.Instance;
            if (e == null || dmd == null || Room(e) == null) { Plugin.Log.LogInfo($"[enemy] death of {id} from {sender} ignored: {(e == null ? "unknown here" : "no room")}"); return; }
            string dz = Presence.RoomZone(dmd, Room(e));
            if (!Owners.TryGetValue(dz, out int downer) || downer != sender) { Plugin.Log.LogInfo($"[enemy] death of {id} from {sender} ignored: {dz} is owned by {(Owners.TryGetValue(dz, out int o2) ? o2 : -1)}"); return; }
            if (Session.IsHost) RecordDeath(id, e);
            if (e == null || e.enemyStats == null || !e.enemyStats.isAlive) return;
            var dm = DungeonManager.Instance;
            if (dm != null && Room(e) == dm.currentDungeonRoom && e.gameObject.activeInHierarchy) KillHere(e);
            else RemoveSilently(e);
        }

        private static void OnDeadList(int sender, R r)
        {
            int seed = r.I32();
            int n = r.Count(100000);
            int removed = 0;
            for (int i = 0; i < n; i++)
            {
                var e = Find(r.Str());
                if (seed != Floor.CurrentSeed || e == null || e.enemyStats == null || !e.enemyStats.isAlive) continue;
                RemoveSilently(e); removed++;
            }
            Plugin.Log.LogInfo($"[enemy] catch-up: {removed} enemies already dead on this floor");
        }

        /// The game's own death, with its animation, sounds, room bookkeeping and our own drops.
        private static void KillHere(Enemy e)
        {
            var m = e.GetComponent<EnemyMirror>();
            if (m != null) m.Release();
            ApplyingDeath = true;
            try
            {
                e.enemyStats._invincible = false;
                e.enemyStats.CurrentHealth = 0f;
            }
            finally { ApplyingDeath = false; }
            DeathsApplied++;
        }

        /// Died somewhere we are not: no animation, no drops, but the room counts it as defeated.
        private static void RemoveSilently(Enemy e)
        {
            var room = Room(e);
            e.enemyStats._isAlive = false;
            e.enemyStats._currentHealth = 0f;
            if (room != null && room.roomEnemies.Contains(e))
            {
                room.roomEnemies.Remove(e);
            }
            e.gameObject.SetActive(false);
            SilentRemovals++;
        }

        // ---------------------------------------------------------------- tests

        public static object Snapshot()
        {
            var dm = DungeonManager.Instance;
            string zone = Presence.MyZone;
            var here = dm != null && zone.StartsWith("dungeon:") ? InRoom(dm, dm.currentDungeonRoom).ToList() : new List<Enemy>();
            return new
            {
                zone,
                owner = Owners.TryGetValue(zone, out int o) ? o : -1,
                amOwner = AmOwner(zone),
                owners = Owners.ToDictionary(kv => kv.Key, kv => kv.Value),
                seed = Floor.CurrentSeed,
                registered = ById.Count,
                generated = GenOrder.Count,
                deadKnown = Session.IsHost ? DeadThisFloor.Count : -1,
                here = here.Select(e => new
                {
                    id = Id(e), name = e.name, x = e.transform.position.x, y = e.transform.position.y, hp = e.enemyStats.CurrentHealth,
                    mirrored = e.GetComponent<EnemyMirror>() != null, target = TargetOf.TryGetValue(e, out int t) ? t : -1,
                    ai = e.enemyBehaviour != null && e.enemyBehaviour.isActiveAndEnabled, invincible = e.enemyStats._invincible,
                    state = e.enemyBehaviour != null ? e.enemyBehaviour.enemyState.ToString() : null,
                    room = Presence.RoomKey(Room(e)), amOwnerOf = AmOwnerOf(e), defense = e.enemyStats.defense,
                }).ToArray(),
                stats = new { StatesSent, StatesApplied, HitsSent, HitsApplied, DeathsSent, DeathsApplied, SilentRemovals, SpawnsSent, SpawnsCreated, LocalDuplicatesRemoved, Culled, OwnerChanges, Takeovers },
            };
        }
    }

    // =========================================================================================
    // mirrored enemy
    // =========================================================================================

    /// An enemy whose brain is off: it shows what the owner's copy does. Its attack hitboxes stay, so when
    /// the owner's enemy swings at us, the swing we see is the one that hurts us.
    public sealed class EnemyMirror : MonoBehaviour
    {
        private readonly List<Behaviour> _off = new List<Behaviour>();
        private readonly List<(Rigidbody2D rb, RigidbodyType2D type)> _bodies = new List<(Rigidbody2D, RigidbodyType2D)>();
        private readonly List<Animator> _anims = new List<Animator>();
        private Enemy _enemy;
        private Vector3 _target;
        private bool _hasTarget;

        public static EnemyMirror Take(Enemy e)
        {
            var m = e.gameObject.AddComponent<EnemyMirror>();
            m._enemy = e;
            foreach (var b in e.GetComponentsInChildren<EnemyBehaviour>(true)) if (b.enabled) { b.enabled = false; m._off.Add(b); }
            if (e.navAgent != null && e.navAgent.enabled) { e.navAgent.enabled = false; m._off.Add(e.navAgent); }
            foreach (var rb in e.GetComponentsInChildren<Rigidbody2D>(true)) { m._bodies.Add((rb, rb.bodyType)); rb.velocity = Vector2.zero; rb.bodyType = RigidbodyType2D.Kinematic; }
            e.GetComponentsInChildren(true, m._anims);
            return m;
        }

        public Animator Animator(int i) => i < _anims.Count ? _anims[i] : null;

        public void Apply(Vector2 pos, float scaleX, float hp, bool flip)
        {
            _target = new Vector3(pos.x, pos.y, transform.position.z);
            if (!_hasTarget || (transform.position - _target).sqrMagnitude > 150f * 150f) transform.position = _target;
            _hasTarget = true;
            var s = transform.localScale; s.x = scaleX; transform.localScale = s;
            if (_enemy.enemySprite != null) _enemy.enemySprite.flipX = flip;
            var st = _enemy.enemyStats;
            if (st != null && st.isAlive && hp > 0f)
            {
                if (hp < st._currentHealth - 0.01f) Flash();
                st._currentHealth = hp;
            }
        }

        public void Flash()
        {
            try { _enemy.enemyBehaviour?.PlayDamagedEffect(); } catch { }
        }

        public void Release()
        {
            foreach (var b in _off) if (b != null) b.enabled = true;
            foreach (var (rb, type) in _bodies) if (rb != null) rb.bodyType = type;
            _off.Clear(); _bodies.Clear();
            Destroy(this);
        }

        private void Update()
        {
            if (!_hasTarget) return;
            transform.position = Vector3.Lerp(transform.position, _target, 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime));
        }
    }

    /// Animator state on the wire: current state, time in it, and every float/int/bool parameter (facing in
    /// this game is a pair of float parameters, so the state alone would face the wrong way).
    public static class AnimState
    {
        public static void Write(W w, Animator a)
        {
            if (a == null || a.runtimeAnimatorController == null || !a.isActiveAndEnabled) { w.U8(0); return; }
            w.U8(1);
            var st = a.GetCurrentAnimatorStateInfo(0);
            w.I32(st.fullPathHash).F32(st.normalizedTime);
            var ps = a.parameters;
            w.U8((byte)Mathf.Min(ps.Length, 32));
            for (int i = 0; i < ps.Length && i < 32; i++)
            {
                var p = ps[i];
                switch (p.type)
                {
                    case AnimatorControllerParameterType.Float: w.U8(1).F32(a.GetFloat(p.nameHash)); break;
                    case AnimatorControllerParameterType.Int: w.U8(2).I32(a.GetInteger(p.nameHash)); break;
                    case AnimatorControllerParameterType.Bool: w.U8(3).Bool(a.GetBool(p.nameHash)); break;
                    default: w.U8(0); break;
                }
            }
        }

        public static void Read(R r, Animator a)
        {
            if (r.U8() == 0) return;
            int hash = r.I32(); float time = r.F32();
            int n = r.U8();
            AnimatorControllerParameter[] ps = null;
            bool usable = a != null && a.runtimeAnimatorController != null && a.isActiveAndEnabled;
            if (usable) ps = a.parameters;
            for (int i = 0; i < n; i++)
            {
                int kind = r.U8();
                switch (kind)
                {
                    case 1: { float v = r.F32(); if (usable && i < ps.Length && ps[i].type == AnimatorControllerParameterType.Float) a.SetFloat(ps[i].nameHash, v); break; }
                    case 2: { int v = r.I32(); if (usable && i < ps.Length && ps[i].type == AnimatorControllerParameterType.Int) a.SetInteger(ps[i].nameHash, v); break; }
                    case 3: { bool v = r.Bool(); if (usable && i < ps.Length && ps[i].type == AnimatorControllerParameterType.Bool) a.SetBool(ps[i].nameHash, v); break; }
                }
            }
            if (!usable) return;
            var st = a.GetCurrentAnimatorStateInfo(0);
            if (st.fullPathHash != hash) { if (a.HasState(0, hash)) a.Play(hash, 0, time - Mathf.Floor(time)); }
            else if (Mathf.Abs(st.normalizedTime - time) > 0.35f) a.Play(hash, 0, time - Mathf.Floor(time));
        }
    }

    // =========================================================================================
    // patches
    // =========================================================================================

    [HarmonyPatch(typeof(Enemy), nameof(Enemy.Init))]
    static class EnemyInitPatch
    {
        static void Postfix(Enemy __instance, string _name) => Enemies.OnEnemyInit(__instance, _name);
    }

    [HarmonyPatch(typeof(DungeonBossRoom), "Start")]
    static class BossRegisterPatch
    {
        static void Postfix(DungeonBossRoom __instance) { if (!__instance.IsBossDefeated) Enemies.RegisterBoss(__instance); }
    }

    [HarmonyPatch(typeof(Enemy), nameof(Enemy.DealDamageToEnemy))]
    static class HitGoesToOwner
    {
        static bool Prefix(Enemy __instance, float attackStrength, Enemy.EnemyDamageReceivedType enemyDamageReceivedType)
            => !Enemies.InterceptHit(__instance, attackStrength, enemyDamageReceivedType);
    }

    /// On a copy that does not own the enemy, its health only changes when the owner says so.
    [HarmonyPatch(typeof(EnemyStats), nameof(EnemyStats.CurrentHealth), MethodType.Setter)]
    static class HealthAuthority
    {
        static bool Prefix(EnemyStats __instance, float value, out bool __state)
        {
            __state = __instance._isAlive;
            if (!Session.Active || Enemies.ApplyingDeath || value >= __instance._currentHealth) return true;
            var e = __instance.GetComponent<Enemy>();
            if (e == null || Enemies.Id(e) == null || Enemies.AmOwnerOf(e)) return true;
            if (GameManager.Instance != null && !GameManager.Instance.NotLoadingScene) return true;
            return false;
        }

        static void Postfix(EnemyStats __instance, bool __state)
        {
            if (__state && !__instance._isAlive) Enemies.OnLocalDeath(__instance.GetComponent<Enemy>());
        }
    }

    /// Enemies read the hero singleton directly in hundreds of places. While an owned enemy thinks, the
    /// hero stand-in is moved onto the player it is after and put back right away, so all of those reads
    /// see its real target. (The same idea is used by elde_al_lado's Moonlighter Coop.)
    static class TargetPatches
    {
        public static int Swaps;

        public static void Install(Harmony h)
        {
            var pre = new HarmonyMethod(typeof(TargetPatches), nameof(Pre));
            var post = new HarmonyMethod(typeof(TargetPatches), nameof(Post));
            var fin = new HarmonyMethod(typeof(TargetPatches), nameof(Fin));
            int n = 0;
            foreach (var t in typeof(EnemyBehaviour).Assembly.GetTypes().Where(t => typeof(EnemyBehaviour).IsAssignableFrom(t)))
                foreach (var name in new[] { "Update", "FixedUpdate", "LateUpdate" })
                {
                    var mi = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                    if (mi == null || mi.IsAbstract || mi.ReturnType != typeof(void)) continue;
                    try { h.Patch(mi, prefix: pre, postfix: post, finalizer: fin); n++; }
                    catch (Exception e) { Plugin.Log.LogWarning($"[target] {t.Name}.{name}: {e.Message}"); }
                }
            Plugin.Log.LogInfo($"[target] {n} enemy update methods aim at their chosen player");
        }

        static void Pre(EnemyBehaviour __instance, out Vector3? __state)
        {
            __state = null;
            if (!Session.Active) return;
            var hero = HeroMerchant.Instance;
            var e = __instance.enemy;
            if (hero == null || e == null || !Enemies.TargetOf.TryGetValue(e, out int tgt) || tgt == Session.MyId || tgt < 0) return;
            var tt = Enemies.TargetTransform(tgt);
            if (tt == null) return;
            __state = hero.transform.position;
            hero.transform.position = new Vector3(tt.position.x, tt.position.y, __state.Value.z);
            Swaps++;
        }

        static void Post(Vector3? __state) { if (__state.HasValue && HeroMerchant.Instance != null) HeroMerchant.Instance.transform.position = __state.Value; }

        static Exception Fin(Exception __exception, Vector3? __state)
        {
            if (__state.HasValue && HeroMerchant.Instance != null) HeroMerchant.Instance.transform.position = __state.Value;
            return __exception;
        }
    }
}
