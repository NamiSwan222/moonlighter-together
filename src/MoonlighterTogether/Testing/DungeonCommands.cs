using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MoonlighterTogether.Sync;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MoonlighterTogether.Testing
{
    /// Test commands for the dungeon. The bot moves the way the game does (through doors, down the floor
    /// exit) and hurts enemies through the game's own damage function, i.e. the same path a sword hit takes.
    public static class DungeonCommands
    {
        public static void Register()
        {
            Bridge.Register("enterdungeon", a =>
            {
                var c = (ItemMaster.Culture)Enum.Parse(typeof(ItemMaster.Culture), (string)a["culture"] ?? "Golem", true);
                HeroMerchant.Instance.LoadDungeonScene(c);
                return c.ToString();
            });
            // Between Dimensions: what the DLC's portal in town does.
            Bridge.Register("enterwanderer", a =>
            {
                var dlc = Moonlighter.DLC.WandererDungeon.WandererDLCController.Instance;
                if (dlc == null) throw new Exception("the Between Dimensions DLC is not active in this game");
                dlc.WorldDungeonDoor_OnDungeonPortalActivate(ItemMaster.Culture.Wanderer, false);
                return true;
            });
            Bridge.Register("roomstats", a => new { enters = RoomStats.Enters, resets = RoomStats.Resets });
            Bridge.Register("dlcstate", a =>
            {
                var dlc = Moonlighter.DLC.WandererDungeon.WandererDLCController.Instance;
                return new { active = dlc != null, beaten = (GameManager.Instance.currentGameSlot.culturesDefeated & 32) != 0 };
            });
            Bridge.Register("floorsig", a => { string s = Floor.Signature(out object d); return new { sig = s, seed = Floor.CurrentSeed, rooms = (bool?)a["detail"] ?? false ? d : null }; });
            Bridge.Register("enemies", a => Enemies.Snapshot());
            Bridge.Register("boss", a =>
            {
                var dm = DungeonManager.Instance;
                var br = dm != null ? dm.bossRoom as DungeonBossRoom : null;
                if (br == null || br.Boss == null) return null;
                var e = br.Boss.enemy;
                return new
                {
                    id = Enemies.Id(e), name = e.name, hp = e.enemyStats.CurrentHealth, max = e.enemyStats.maxHealth, alive = e.enemyStats.isAlive,
                    invincible = e.enemyStats._invincible, defeated = br.IsBossDefeated, mirrored = e.GetComponent<EnemyMirror>() != null,
                    active = e.gameObject.activeInHierarchy, bossDefeatedCulture = GameManager.Instance.bossDefeatedCulture.ToString(),
                };
            });
            Bridge.Register("rooms", a => Rooms());
            Bridge.Register("door", a => Door((string)a["to"], (bool?)a["force"] ?? false));
            Bridge.Register("exit", a => Exit());
            // Step into the boss room's alert zone (what wakes the boss when Will walks in).
            Bridge.Register("bossalert", a =>
            {
                var dm = DungeonManager.Instance;
                var area = dm.currentDungeonRoom.GetComponentsInChildren<DungeonRoomBossAlertArea>(true).FirstOrDefault();
                if (area == null) throw new Exception("no boss alert area here");
                var col = area.GetComponent<Collider2D>();
                Vector3 p = col != null ? (Vector3)col.bounds.center : area.transform.position;
                var h = HeroMerchant.Instance;
                h.heroMerchantController.TeleportTo(new Vector3(p.x, p.y, h.transform.position.z));
                return new { x = p.x, y = p.y, active = area.gameObject.activeInHierarchy };
            });
            Bridge.Register("gohome", a => { HeroMerchant.Instance.LoadTownScene(); return true; });
            Bridge.Register("hit", a => Hit((string)a["enemy"], (float?)a["damage"] ?? 10f));
            // Last resort for the bot: lower an enemy's health through the stats setter (still the owner's
            // death path and the network), for enemies the damage function will not touch. Reported by tests.
            Bridge.Register("forcehit", a =>
            {
                var e = Enemies.Find((string)a["enemy"]) ?? throw new Exception("no enemy");
                e.enemyStats._invincible = false;
                e.enemyStats.CurrentHealth = e.enemyStats._currentHealth - ((float?)a["damage"] ?? 500f);
                return e.enemyStats._currentHealth;
            });
            Bridge.Register("probehit", a =>
            {
                var e = Enemies.Find((string)a["enemy"]) ?? throw new Exception("no enemy");
                float h0 = e.enemyStats._currentHealth;
                e.enemyStats._invincible = false;
                e.DealDamageToEnemy(50f);
                float h1 = e.enemyStats._currentHealth;
                e.enemyStats._invincible = false;
                e.enemyStats.CurrentHealth = h1 - 50f;
                float h2 = e.enemyStats._currentHealth;
                return new { h0, afterDamageFunction = h1, afterSetter = h2, behaviourEnabled = e.enemyBehaviour.isActiveAndEnabled, behaviourType = e.enemyBehaviour.GetType().Name,
                             enemyType = e.GetType().Name, statsType = e.enemyStats.GetType().Name, alive = e.enemyStats.isAlive, sameStats = ReferenceEquals(e.enemyStats, e.GetComponent<EnemyStats>()) };
            });
            Bridge.Register("hitall", a => HitAll((float?)a["damage"] ?? 10f));
            Bridge.Register("hitdetail", a =>
            {
                var dm = DungeonManager.Instance;
                var list = new List<object>();
                foreach (var e in Enemies.InRoom(dm, dm.currentDungeonRoom).ToList())
                {
                    float before = e.enemyStats._currentHealth; bool invBefore = e.enemyStats._invincible;
                    e.enemyStats._invincible = false;
                    e.DealDamageToEnemy((float?)a["damage"] ?? 40f);
                    float after = e.enemyStats._currentHealth;
                    list.Add(new { id = Enemies.Id(e), before, after, invBefore, invAfter = e.enemyStats._invincible, active = e.enemyBehaviour.isActiveAndEnabled, frame = Time.frameCount });
                }
                return list;
            });
            Bridge.Register("hurtme", a =>
            {
                var h = HeroMerchant.Instance;
                h.DealDamageToPlayer((float)a["damage"], "test", HeroMerchant.DamageType.Normal, null);
                return (float)h.heroMerchantStats.currentHealth.Value;
            });
            Bridge.Register("heal", a => { var st = HeroMerchant.Instance.heroMerchantStats; st.currentHealth.Value = st.currentHealth.maxValue; return (float)st.currentHealth.Value; });
        }

        private static object Rooms()
        {
            var dm = DungeonManager.Instance;
            if (dm == null || dm.dungeonRooms == null) return null;
            return new
            {
                current = Presence.RoomKey(dm.currentDungeonRoom),
                culture = dm.culture.ToString(),
                level = dm.level,
                bossRoom = dm.bossRoom != null ? Presence.RoomKey(dm.bossRoom) : null,
                rooms = dm.dungeonRooms.Where(r => r != null).Concat(dm.bossRoom != null && !dm.dungeonRooms.Contains(dm.bossRoom) ? new DungeonRoom[] { dm.bossRoom } : new DungeonRoom[0]).Select(r => new
                {
                    key = Presence.RoomKey(r),
                    boss = r is DungeonBossRoom,
                    exit = r.GetComponentsInChildren<NextLevelDoor>(true).Length > 0,
                    enemiesLeft = r.roomEnemies.Count(e => e != null && e.enemyStats != null && e.enemyStats.isAlive && !(e is InvulnerableEnemy)),
                    doors = r.roomDoors.Where(d => d != null && !d.isDisabled)
                        .Concat(r.GetComponentsInChildren<DungeonBossRoomEnterDoor>(true).Where(d => !r.roomDoors.Contains(d)))
                        .Select(d => new
                    {
                        to = d.destinationRoom != null ? Presence.RoomKey(d.destinationRoom) : (d is DungeonBossRoomEnterDoor && dm.bossRoom != null ? Presence.RoomKey(dm.bossRoom) : null),
                        kind = d.GetType().Name,
                        open = d.isOpened,
                    }).ToArray(),
                }).ToArray(),
            };
        }

        private static object Door(string to, bool force)
        {
            var dm = DungeonManager.Instance;
            var hero = HeroMerchant.Instance;
            var room = dm.currentDungeonRoom;
            var door = room.roomDoors.FirstOrDefault(d => d != null && d.destinationRoom != null && Presence.RoomKey(d.destinationRoom) == to);
            if (door == null && dm.bossRoom != null && Presence.RoomKey(dm.bossRoom) == to)
                door = room.GetComponentsInChildren<DungeonBossRoomEnterDoor>(true).FirstOrDefault();
            if (door == null) throw new Exception($"no door from {Presence.RoomKey(room)} to {to}");
            if (!door.isOpened && !force) throw new Exception("door is closed");
            door.OnPlayerEnter(hero.gameObject);
            return Presence.RoomKey(room) + " -> " + to;
        }

        private static object Exit()
        {
            var dm = DungeonManager.Instance;
            var room = dm.currentDungeonRoom;
            var exit = room.GetComponentsInChildren<NextLevelDoor>(true).FirstOrDefault();
            if (exit == null) throw new Exception("no floor exit in " + Presence.RoomKey(room));
            exit.OnPlayerEnter(HeroMerchant.Instance.gameObject);
            return true;
        }

        private static object Hit(string id, float damage)
        {
            var e = Enemies.Find(id);
            if (e == null) throw new Exception("no enemy " + id);
            e.DealDamageToEnemy(damage);
            return e.enemyStats.CurrentHealth;
        }

        private static object HitAll(float damage)
        {
            var dm = DungeonManager.Instance;
            int n = 0;
            var targets = Enemies.InRoom(dm, dm.currentDungeonRoom).ToList();
            // Plus anything the room itself still counts (enemies the network layer has no name for).
            foreach (var e in dm.currentDungeonRoom.roomEnemies)
                if (e != null && e.enemyStats != null && e.enemyStats.isAlive && e.gameObject.activeInHierarchy && !targets.Contains(e))
                {
                    targets.Add(e);
                    Plugin.Log.LogInfo($"[test] room enemy without a network id: {e.name} (registered as {Enemies.Id(e) ?? "nothing"})");
                }
            // The game does not count invulnerable fixtures when deciding a room is clear; neither does the bot.
            targets.RemoveAll(e => e is InvulnerableEnemy);
            foreach (var e in targets)
            {
                e.enemyStats._invincible = false;   // the game's short invulnerability after a hit; a bot hits every 0.2 s
                e.DealDamageToEnemy(damage);
                n++;
            }
            return n;
        }
    }
}

namespace MoonlighterTogether.Testing
{
    /// Test diagnostics: how often rooms are entered and enemies reset to full health.
    public static class RoomStats
    {
        public static readonly System.Collections.Generic.Dictionary<string, int> Enters = new System.Collections.Generic.Dictionary<string, int>();
        public static readonly System.Collections.Generic.Dictionary<string, int> Resets = new System.Collections.Generic.Dictionary<string, int>();
        public static void Count(System.Collections.Generic.Dictionary<string, int> d, string k) { d.TryGetValue(k, out int n); d[k] = n + 1; }
    }

    [HarmonyLib.HarmonyPatch(typeof(DungeonRoom), nameof(DungeonRoom.EnterRoom))]
    static class CountRoomEnter { static void Postfix(DungeonRoom __instance) => RoomStats.Count(RoomStats.Enters, Sync.Presence.RoomKey(__instance)); }

    [HarmonyLib.HarmonyPatch(typeof(EnemyBehaviour), nameof(EnemyBehaviour.EnemyRoom_OnRoomEnter))]
    static class CountEnemyReset { static void Postfix(EnemyBehaviour __instance) => RoomStats.Count(RoomStats.Resets, (Sync.Enemies.Id(__instance.enemy) ?? "?") + " " + __instance.enemy?.name); }
}
