using System.Linq;
using MoonlighterTogether.Net;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MoonlighterTogether.Testing
{
    public static class NetCommands
    {
        public static void Register()
        {
            Bridge.Register("host", a => { Session.Host((int?)a["port"] ?? 0); return Snapshot(); });
            Bridge.Register("hoststeam", a => { Session.HostSteam(); return Snapshot(); });
            Bridge.Register("steam", a => new
            {
                available = SteamTransport.Available,
                status = (Session.Transport as SteamTransport)?.Status,
                lobby = Session.LobbyCode,
                me = SteamTransport.Available ? Steamworks.SteamUser.GetSteamID().m_SteamID.ToString().Length + " digit id" : null,
                lobbyTag = Session.LobbyCode != null ? Steamworks.SteamMatchmaking.GetLobbyData(new Steamworks.CSteamID(ulong.Parse(Session.LobbyCode)), SteamTransport.LobbyKey) : null,
                members = Session.LobbyCode != null ? Steamworks.SteamMatchmaking.GetNumLobbyMembers(new Steamworks.CSteamID(ulong.Parse(Session.LobbyCode))) : 0,
            });
            Bridge.Register("say", a => { UI.Menu.Say((string)a["text"]); return true; });
            Bridge.Register("chatcount", a => UI.Menu.ChatReceived);
            Bridge.Register("menu", a => { UI.Menu.Instance.SetOpen((bool?)a["open"] ?? true); return true; });
            Bridge.Register("join", a => { Session.Join((string)a["address"]); return Snapshot(); });
            Bridge.Register("leave", a => { Session.Leave("asked by test"); return Snapshot(); });
            Bridge.Register("session", a => Snapshot());
            Bridge.Register("presence", a => new
            {
                myZone = Sync.Presence.MyZone,
                sent = Sync.Presence.PosesSent,
                received = Sync.Presence.PosesReceived,
                spriteMisses = Sync.SpriteCache.Misses,
                worldsReceived = Sync.WorldSync.WorldsReceived,
                worldsSent = Sync.WorldSync.WorldsSent,
                inHostWorld = Sync.WorldSync.GuestInHostWorld,
                worldRefused = Sync.WorldSync.LastRefusal,
                remotes = Sync.Presence.Remotes.Values.Select(r => new
                {
                    id = r.Id, zone = r.Zone, x = r.Pos.x, y = r.Pos.y, hp = r.Hp, ghost = r.Ghost, parts = r.Parts.Count,
                    visible = r.Puppet != null && r.Puppet.gameObject.activeSelf,
                    drawn = r.Puppet == null ? 0 : r.Puppet.GetComponentsInChildren<SpriteRenderer>(false).Count(s => s.enabled && s.sprite != null),
                    px = r.Puppet == null ? 0f : r.Puppet.transform.position.x, py = r.Puppet == null ? 0f : r.Puppet.transform.position.y,
                }).ToArray(),
            });
            // Change a save slot on disk before a test (e.g. give a guest's own character some gold).
            Bridge.Register("editslot", a =>
            {
                var gm = GameManager.Instance;
                int i = (int?)a["slot"] ?? 0;
                var s = gm.gameSaveData.gameSlots[i];
                if ((bool?)a["fresh"] ?? false)
                {
                    gm.ResetSlot(i); s = gm.gameSaveData.gameSlots[i];
                    s.gameType = GameSlot.GameType.Normal; s.timeLastSave = System.DateTime.UtcNow; s.playedTutorial = 4;
                    s.townboardSaveInfo.UnlockBlacksmith(); s.townboardSaveInfo.UnlockWitch();
                }
                if (a["gold"] != null) s.willGold = (int)a["gold"];
                if (a["days"] != null) s.daysPassed = (int)a["days"];
                gm.SerializeGameSlot();
                return new { gold = s.willGold, days = s.daysPassed, items = s.willInventory.Count };
            });
            Bridge.Register("slotinfo", a =>
            {
                var gm = GameManager.Instance;
                var s = (bool?)a["current"] ?? false ? gm.currentGameSlot : gm.gameSaveData.gameSlots[(int?)a["slot"] ?? 0];
                return s == null ? null : new { gold = s.willGold, days = s.daysPassed, items = s.willInventory.Count, seed = s.seed, tutorial = s.playedTutorial, defeated = s.culturesDefeated, dayTime = s.dayTime.ToString() };
            });
            // Cut this copy's network for N seconds, as if the cable were pulled (guest or host).
            Bridge.Register("droplink", a => { Session.SimulateDrop((float?)a["seconds"] ?? 10f); return Snapshot(); });
        }

        public static object Snapshot() => new
        {
            state = Session.State.ToString(),
            me = Session.MyId,
            error = Session.LastError,
            rtt = Session.HostRttMs,
            now = Session.Now,
            reconnects = Session.Reconnects,
            drops = Session.Drops,
            bytesOut = Session.BytesOut,
            bytesIn = Session.BytesIn,
            bad = Session.BadPackets,
            players = Session.Players.Values.OrderBy(p => p.Id).Select(p => new { id = p.Id, name = p.Name, up = p.Connected, rtt = p.RttMs }).ToArray(),
        };
    }
}
