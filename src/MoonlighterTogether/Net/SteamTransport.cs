using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace MoonlighterTogether.Net
{
    /// Steam: friends-only lobby + Steam P2P. Works between two homes with no ports to open (Steam relays
    /// the traffic when a direct path fails). The host makes a lobby and invites friends from the Steam
    /// overlay (or gives them the lobby code); a guest joins from the invite, "Join game" in the friends list,
    /// or the code. Needs the game started through Steam (not with --mt-profile).
    public sealed class SteamTransport : ITransport
    {
        private const int ChReliable = 0, ChState = 1;
        public const string LobbyKey = "moonlighter_together";

        public bool IsServer { get; private set; }
        public string Kind => "steam";
        public CSteamID Lobby { get; private set; }
        public CSteamID HostId { get; private set; }
        public string Status = "starting";

        public event Action<int> Connected;
        public event Action<int, string> Disconnected;
        public event Action<int, byte[], int, int> Data;

        private readonly Dictionary<ulong, int> _connOf = new Dictionary<ulong, int>();
        private readonly Dictionary<int, CSteamID> _idOf = new Dictionary<int, CSteamID>();
        private readonly Dictionary<int, float> _lastHeard = new Dictionary<int, float>();
        private int _nextConn = 1;
        private byte[] _buf = new byte[64 * 1024];
        private const float Timeout = 10f;

        private Callback<P2PSessionRequest_t> _cbRequest;
        private Callback<P2PSessionConnectFail_t> _cbFail;
        private Callback<LobbyChatUpdate_t> _cbLobbyChat;
        private CallResult<LobbyCreated_t> _crCreated;
        private CallResult<LobbyEnter_t> _crEnter;

        public static bool Available
        {
            get { try { return DigitalSunGames.Platforms.SteamManager.Instance != null && DigitalSunGames.Platforms.SteamManager.Instance.Initialized && (!Platforms.OfflineProfile.Active || Args.SteamNet); } catch { return false; } }
        }

        private SteamTransport()
        {
            SteamNetworking.AllowP2PPacketRelay(true);
            _cbRequest = Callback<P2PSessionRequest_t>.Create(OnSessionRequest);
            _cbFail = Callback<P2PSessionConnectFail_t>.Create(OnSessionFail);
            _cbLobbyChat = Callback<LobbyChatUpdate_t>.Create(OnLobbyChat);
        }

        public static SteamTransport Host(int maxPlayers)
        {
            var t = new SteamTransport { IsServer = true, Status = "creating lobby" };
            t._crCreated = CallResult<LobbyCreated_t>.Create(t.OnLobbyCreated);
            t._crCreated.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, maxPlayers));
            return t;
        }

        public static SteamTransport Join(ulong lobby)
        {
            var t = new SteamTransport { IsServer = false, Status = "joining lobby" };
            t._crEnter = CallResult<LobbyEnter_t>.Create(t.OnLobbyEntered);
            t._crEnter.Set(SteamMatchmaking.JoinLobby(new CSteamID(lobby)));
            return t;
        }

        private void OnLobbyCreated(LobbyCreated_t r, bool ioFailure)
        {
            if (ioFailure || r.m_eResult != EResult.k_EResultOK) { Status = "could not create a Steam lobby (" + r.m_eResult + ")"; Plugin.Log.LogWarning("[steam] " + Status); return; }
            Lobby = new CSteamID(r.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(Lobby, LobbyKey, Plugin.Version);
            SteamMatchmaking.SetLobbyData(Lobby, "protocol", Plugin.Protocol.ToString());
            Status = "lobby open";
            Plugin.Log.LogInfo($"[steam] lobby {Lobby.m_SteamID} open");
        }

        private void OnLobbyEntered(LobbyEnter_t r, bool ioFailure)
        {
            if (ioFailure || r.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                Status = "could not enter the lobby"; Disconnected?.Invoke(-1, Status); return;
            }
            Lobby = new CSteamID(r.m_ulSteamIDLobby);
            if (SteamMatchmaking.GetLobbyData(Lobby, LobbyKey) == "") { Status = "that lobby is not a Moonlighter Together game"; Disconnected?.Invoke(-1, Status); return; }
            HostId = SteamMatchmaking.GetLobbyOwner(Lobby);
            int conn = ConnFor(HostId);
            Status = "connected to " + SteamFriends.GetFriendPersonaName(HostId);
            Plugin.Log.LogInfo($"[steam] in lobby {Lobby.m_SteamID}, host {HostId.m_SteamID}");
            // Steam P2P opens on the first packet; the session layer's hello is that packet (reliable).
            Connected?.Invoke(conn);
        }

        private int ConnFor(CSteamID id)
        {
            if (_connOf.TryGetValue(id.m_SteamID, out int c)) return c;
            c = _nextConn++;
            _connOf[id.m_SteamID] = c; _idOf[c] = id; _lastHeard[c] = Time.unscaledTime;
            return c;
        }

        private void OnSessionRequest(P2PSessionRequest_t r)
        {
            // Only people in our lobby (host), or the host (guest).
            bool ok = IsServer ? InLobby(r.m_steamIDRemote) : r.m_steamIDRemote == HostId;
            if (ok) SteamNetworking.AcceptP2PSessionWithUser(r.m_steamIDRemote);
            else Plugin.Log.LogInfo($"[steam] refused a P2P request from {r.m_steamIDRemote.m_SteamID} (not in the lobby)");
        }

        private bool InLobby(CSteamID who)
        {
            if (!Lobby.IsValid()) return false;
            int n = SteamMatchmaking.GetNumLobbyMembers(Lobby);
            for (int i = 0; i < n; i++) if (SteamMatchmaking.GetLobbyMemberByIndex(Lobby, i) == who) return true;
            return false;
        }

        private void OnSessionFail(P2PSessionConnectFail_t r)
        {
            if (_connOf.TryGetValue(r.m_steamIDRemote.m_SteamID, out int c)) Drop(c, "Steam P2P failed (" + r.m_eP2PSessionError + ")");
        }

        private void OnLobbyChat(LobbyChatUpdate_t r)
        {
            if (r.m_ulSteamIDLobby != Lobby.m_SteamID) return;
            var who = new CSteamID(r.m_ulSteamIDUserChanged);
            bool left = (r.m_rgfChatMemberStateChange & (uint)(EChatMemberStateChange.k_EChatMemberStateChangeLeft | EChatMemberStateChange.k_EChatMemberStateChangeDisconnected)) != 0;
            if (left && _connOf.TryGetValue(who.m_SteamID, out int c)) Drop(c, "left the lobby");
        }

        private void Drop(int conn, string why)
        {
            if (!_idOf.TryGetValue(conn, out var id)) return;
            SteamNetworking.CloseP2PSessionWithUser(id);
            _idOf.Remove(conn); _connOf.Remove(id.m_SteamID); _lastHeard.Remove(conn);
            Disconnected?.Invoke(conn, why);
        }

        public void Poll()
        {
            for (int ch = 0; ch < 2; ch++)
            {
                while (SteamNetworking.IsP2PPacketAvailable(out uint size, ch))
                {
                    if (size > _buf.Length) _buf = new byte[size];
                    if (!SteamNetworking.ReadP2PPacket(_buf, (uint)_buf.Length, out uint got, out CSteamID from, ch)) break;
                    int conn;
                    if (!_connOf.TryGetValue(from.m_SteamID, out conn))
                    {
                        if (!IsServer || !InLobby(from)) continue;
                        conn = ConnFor(from);
                        Connected?.Invoke(conn);
                    }
                    _lastHeard[conn] = Time.unscaledTime;
                    Data?.Invoke(conn, _buf, 0, (int)got);
                }
            }
            // Steam does not tell us when a peer just vanishes: nothing heard for a while = gone.
            float now = Time.unscaledTime;
            foreach (var kv in new List<KeyValuePair<int, float>>(_lastHeard))
                if (now - kv.Value > Timeout) Drop(kv.Key, "nothing heard for " + Timeout + " s");
        }

        public void Send(int conn, byte[] data, int len, Channel ch)
        {
            if (!_idOf.TryGetValue(conn, out var id)) return;
            byte[] copy = data;
            if (len != data.Length) { copy = new byte[len]; Buffer.BlockCopy(data, 0, copy, 0, len); }
            if (ch == Channel.Reliable || len > 1100) SteamNetworking.SendP2PPacket(id, copy, (uint)len, EP2PSend.k_EP2PSendReliable, ChReliable);
            else SteamNetworking.SendP2PPacket(id, copy, (uint)len, EP2PSend.k_EP2PSendUnreliableNoDelay, ChState);
        }

        public void Disconnect(int conn) => Drop(conn, "disconnected");

        public void Shutdown()
        {
            foreach (var id in _idOf.Values) SteamNetworking.CloseP2PSessionWithUser(id);
            _idOf.Clear(); _connOf.Clear(); _lastHeard.Clear();
            if (Lobby.IsValid()) SteamMatchmaking.LeaveLobby(Lobby);
            _cbRequest?.Dispose(); _cbFail?.Dispose(); _cbLobbyChat?.Dispose();
            _crCreated?.Dispose(); _crEnter?.Dispose();
            Connected = null; Disconnected = null; Data = null;   // an orphaned attempt must never reach the session
        }

        public int Rtt(int conn) => -1;
        public string Describe(int conn) => _idOf.TryGetValue(conn, out var id) ? SteamFriends.GetFriendPersonaName(id) : "?";

        public void OpenInviteOverlay() { if (Lobby.IsValid()) SteamFriends.ActivateGameOverlayInviteDialog(Lobby); }
    }

    /// Steam tells a running game when the player accepts an invite or clicks "Join game".
    public static class SteamInvites
    {
        private static Callback<GameLobbyJoinRequested_t> _cb;

        public static void Init()
        {
            if (!SteamTransport.Available || _cb != null) return;
            _cb = Callback<GameLobbyJoinRequested_t>.Create(r =>
            {
                Plugin.Log.LogInfo($"[steam] invite accepted: lobby {r.m_steamIDLobby.m_SteamID}");
                Session.JoinSteam(r.m_steamIDLobby.m_SteamID);
            });
            // Started from an invite while the game was closed: "+connect_lobby <id>" on the command line.
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "+connect_lobby" && ulong.TryParse(args[i + 1], out ulong lobby)) Session.JoinSteam(lobby);
        }
    }
}
