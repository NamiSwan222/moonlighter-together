using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MoonlighterTogether.Net
{
    /// Message types. The first byte of every packet. Never renumber: add at the end and bump Plugin.Protocol.
    public enum Msg : byte
    {
        Hello = 1,          // guest -> host: protocol, name, client key (same key = same player coming back)
        Welcome = 2,        // host -> guest: your number, who is here
        Refuse = 3,         // host -> guest: why you cannot come in
        Ping = 4,           // guest -> host: my clock
        Pong = 5,           // host -> guest: your clock back + the session clock
        PlayerJoined = 6,   // host -> all: someone came in (or came back)
        PlayerLeft = 7,     // host -> all: someone's link dropped (they may return) or they left for good
        Bye = 8,            // either way: leaving on purpose
        Chat = 9,
        // 16+ : game messages, see Sync/*.cs
        Pose = 16,
        Zone = 17,
    }

    /// What the host does with a message a guest sent.
    public enum Relay : byte
    {
        /// Host handles it; nobody else hears about it.
        None,
        /// Host handles it and passes it on to every other guest, stamped with the real sender.
        Others,
    }

    public sealed class Player
    {
        public int Id;                 // 0 = host
        public string Name;
        public int Conn = -1;          // host only: transport connection, -1 while the link is down
        public bool Connected;
        public string ClientKey;       // host only: recognises a player who comes back
        public float LastHeard;        // unscaled time of the last packet (host: per guest; guest: host)
        public float DroppedAt;        // when the link went down
        public int RttMs = -1;
        public bool IsLocal => Id == Session.MyId;
    }

    public enum SessionState { Off, Hosting, Joining, Joined, Reconnecting }

    /// Who is in the game and how to reach them. Star topology: guests talk to the host only, the host
    /// passes on what the others need. Every packet is [type][sender id][payload]; the host overwrites the
    /// sender with the truth, so a guest can never speak for someone else.
    public static class Session
    {
        public const int MaxPlayers = 4;
        /// A dropped guest keeps their place (and their Will stays where he fell) for this long.
        public const float HoldSeconds = 300f;

        public static SessionState State = SessionState.Off;
        public static ITransport Transport;
        public static int MyId = -1;
        public static string MyName = "Will";
        public static readonly Dictionary<int, Player> Players = new Dictionary<int, Player>();
        public static string LastError;

        public static bool Active => State == SessionState.Hosting || State == SessionState.Joined;
        public static bool IsHost => State == SessionState.Hosting;
        public static bool IsGuest => State == SessionState.Joined || State == SessionState.Joining || State == SessionState.Reconnecting;
        /// Other players whose link is up right now.
        public static IEnumerable<Player> Others => Players.Values.Where(p => p.Id != MyId && p.Connected);
        public static int ConnectedCount => Players.Values.Count(p => p.Connected);

        // ---- events for the game-side modules ----
        /// A player is in (host: after Welcome was sent; guest: for every player in Welcome and later joins).
        public static event Action<Player, bool> PlayerJoined;    // (player, cameBack)
        /// A player's link dropped (temporary = they may come back) or they left.
        public static event Action<Player, bool> PlayerLeft;      // (player, temporary)
        /// Local session started (hosting, or welcomed as a guest; cameBack = this was a reconnect).
        public static event Action<bool> Started;
        public static event Action<string> Ended;
        /// Guest only: the link to the host dropped and we are trying to get back.
        public static event Action LinkLost;

        // ---- clock ----
        private static double _clockOffset;        // session time - local time (guest); 0 on the host
        private static float _bestRtt = float.MaxValue;
        private static float _nextPing;
        public static int HostRttMs = -1;
        /// Seconds on the session clock (the host's). The same moment has the same value on every PC,
        /// give or take a few milliseconds, so states can be stamped and replayed in step.
        public static double Now => TimeExt.Local + _clockOffset;

        // ---- handlers ----
        private struct Handler { public Relay Relay; public Action<int, R> Fn; public Channel Channel; }
        private static readonly Handler[] Handlers = new Handler[256];
        private static readonly R Reader = new R();
        private static readonly W Scratch = new W();

        // guest side
        private static string _joinAddress; private static int _joinPort;
        private static string _clientKey = Guid.NewGuid().ToString("N");
        private static float _nextReconnect, _reconnectStarted;
        private static int _hostConn = -1;
        public static int Reconnects, Drops;

        // statistics for tests
        public static long BytesOut, BytesIn, PacketsOut, PacketsIn, BadPackets;

        static Session()
        {
            On(Msg.Hello, Relay.None, OnHello);
            On(Msg.Welcome, Relay.None, OnWelcome);
            On(Msg.Refuse, Relay.None, OnRefuse);
            On(Msg.Ping, Relay.None, OnPing);
            On(Msg.Pong, Relay.None, OnPong);
            On(Msg.PlayerJoined, Relay.None, OnPlayerJoinedMsg);
            On(Msg.PlayerLeft, Relay.None, OnPlayerLeftMsg);
            On(Msg.Bye, Relay.None, OnBye);
        }

        public static void On(Msg type, Relay relay, Action<int, R> fn, Channel relayChannel = Channel.Reliable)
            => Handlers[(byte)type] = new Handler { Relay = relay, Fn = fn, Channel = relayChannel };

        // =====================================================================================
        // starting and stopping
        // =====================================================================================

        public static void Host(int port)
        {
            Leave("starting a new session");
            Transport = UdpTransport.Host(port <= 0 ? UdpTransport.DefaultPort : port);
            Wire();
            State = SessionState.Hosting;
            MyId = 0; _clockOffset = 0;
            Players.Clear();
            Players[0] = new Player { Id = 0, Name = MyName, Connected = true };
            Plugin.Log.LogInfo($"[session] hosting on UDP {((UdpTransport)Transport).LocalPort}");
            Started?.Invoke(false);
        }

        public static void Join(string address)
        {
            Leave("joining another session");
            ParseAddress(address, out _joinAddress, out _joinPort);
            _joinLobby = 0;
            State = SessionState.Joining;
            OpenLinkToHost();
        }

        /// Host through Steam: a friends-only lobby; friends join from an invite or the lobby code.
        public static void HostSteam()
        {
            Leave("starting a new session");
            Transport = SteamTransport.Host(MaxPlayers);
            Wire();
            State = SessionState.Hosting;
            MyId = 0; _clockOffset = 0;
            Players.Clear();
            Players[0] = new Player { Id = 0, Name = MyName, Connected = true };
            Plugin.Log.LogInfo("[session] hosting through Steam");
            Started?.Invoke(false);
        }

        public static void JoinSteam(ulong lobby)
        {
            Leave("joining another session");
            _joinLobby = lobby;
            State = SessionState.Joining;
            OpenLinkToHost();
        }

        private static ulong _joinLobby;
        public static string LobbyCode => Transport is SteamTransport st && st.Lobby.IsValid() ? st.Lobby.m_SteamID.ToString() : null;

        private static void ParseAddress(string address, out string host, out int port)
        {
            host = (address ?? "").Trim(); port = UdpTransport.DefaultPort;
            int colon = host.LastIndexOf(':');
            if (colon > 0 && int.TryParse(host.Substring(colon + 1), out int p)) { port = p; host = host.Substring(0, colon); }
            if (host.Length == 0) host = "127.0.0.1";
        }

        private static void OpenLinkToHost()
        {
            try { Transport?.Shutdown(); } catch { }
            _hostConn = -1;
            if (_joinLobby != 0)
            {
                Transport = SteamTransport.Join(_joinLobby);
                Wire();
                Plugin.Log.LogInfo($"[session] joining Steam lobby {_joinLobby}");
                return;
            }
            Transport = UdpTransport.Join(_joinAddress, _joinPort);
            Wire();
            Plugin.Log.LogInfo($"[session] connecting to {_joinAddress}:{_joinPort}");
        }

        private static void Wire()
        {
            Transport.Connected += OnConnected;
            Transport.Disconnected += OnDisconnected;
            Transport.Data += OnData;
        }

        /// Leave on purpose (also used internally before starting something new).
        public static void Leave(string why)
        {
            if (State == SessionState.Off) return;
            try { if (Active) Send(Msg.Bye, Scratch.Reset()); Transport?.Poll(); } catch { }
            try { Transport?.Shutdown(); } catch { }
            Transport = null;
            var was = State;
            State = SessionState.Off;
            foreach (var p in Players.Values.ToList()) if (p.Id != MyId) PlayerLeft?.Invoke(p, false);
            Players.Clear(); MyId = -1; _hostConn = -1; _clockOffset = 0; _bestRtt = float.MaxValue; HostRttMs = -1;
            Plugin.Log.LogInfo($"[session] ended ({was}): {why}");
            Ended?.Invoke(why);
        }

        /// Tests: behave as if the network vanished for a while. Nothing is sent or received; the peers find
        /// out the normal way (time-out), and the guest then has to find its way back by itself.
        private static float _blackoutUntil;
        public static bool Blackout => Time.unscaledTime < _blackoutUntil;

        public static void SimulateDrop(float seconds)
        {
            _blackoutUntil = Time.unscaledTime + seconds;
            Plugin.Log.LogInfo($"[session] simulated network loss for {seconds:0.#}s");
            if (IsGuest && State != SessionState.Reconnecting)
            {
                try { Transport?.Shutdown(); } catch { }
                Transport = null; _hostConn = -1;
                Drops++;
                State = SessionState.Reconnecting;
                _reconnectStarted = Time.unscaledTime; _nextReconnect = _blackoutUntil;
                LinkLost?.Invoke();
            }
        }

        // =====================================================================================
        // every frame
        // =====================================================================================

        public static void Tick()
        {
            if (State == SessionState.Off) return;
            Transport?.Poll();
            float now = Time.unscaledTime;

            if (State == SessionState.Joined && now >= _nextPing)
            {
                _nextPing = now + 0.5f;
                Send(Msg.Ping, Scratch.Reset().F64(TimeExt.Local), Channel.State);
            }
            if (State == SessionState.Reconnecting && now >= _nextReconnect)
            {
                if (now - _reconnectStarted > HoldSeconds) { Leave("could not get back to the host"); return; }
                _nextReconnect = now + (_joinLobby != 0 ? 8f : 2f);   // a Steam lobby join takes longer than a UDP hello
                OpenLinkToHost();
            }
            if (IsHost)
            {
                foreach (var p in Players.Values.ToList())
                {
                    if (p.Id == 0) continue;
                    if (p.Connected) p.RttMs = Transport.Rtt(p.Conn);
                    else if (now - p.DroppedAt > HoldSeconds) RemovePlayer(p, "did not come back");
                }
            }
        }

        // =====================================================================================
        // sending
        // =====================================================================================

        private static readonly W Packet = new W();

        private static void Raw(int conn, Msg type, int sender, byte[] payload, int off, int len, Channel ch)
        {
            if (Transport == null || conn < 0) return;
            Packet.Reset().U8((byte)type).U8((byte)sender);
            if (len > 0) { if (Packet.Data.Length < len + 2) Array.Resize(ref Packet.Data, len + 64); Buffer.BlockCopy(payload, off, Packet.Data, 2, len); }
            Packet.Len = len + 2;
            Transport.Send(conn, Packet.Data, Packet.Len, ch);
            BytesOut += Packet.Len; PacketsOut++;
        }

        /// To everyone else: the host sends to every guest; a guest sends to the host (who relays if the
        /// message type says so).
        public static void Send(Msg type, W w, Channel ch = Channel.Reliable)
        {
            if (IsHost) { foreach (var p in Players.Values) if (p.Connected && p.Id != 0) Raw(p.Conn, type, 0, w.Data, 0, w.Len, ch); }
            else if (_hostConn >= 0) Raw(_hostConn, type, MyId < 0 ? 255 : MyId, w.Data, 0, w.Len, ch);
        }

        /// To the host only (on the host: handled right here, as if it had arrived).
        public static void SendToHost(Msg type, W w, Channel ch = Channel.Reliable)
        {
            if (IsHost) Dispatch(type, 0, Reader.Set(w.Data, 0, w.Len), -1, ch);
            else if (_hostConn >= 0) Raw(_hostConn, type, MyId < 0 ? 255 : MyId, w.Data, 0, w.Len, ch);
        }

        /// Host only: to one player (to itself: handled right here).
        public static void SendTo(int playerId, Msg type, W w, Channel ch = Channel.Reliable, int asSender = 0)
        {
            if (!IsHost) return;
            if (playerId == 0) { Dispatch(type, asSender, Reader.Set(w.Data, 0, w.Len), -1, ch); return; }
            if (Players.TryGetValue(playerId, out var p) && p.Connected) Raw(p.Conn, type, asSender, w.Data, 0, w.Len, ch);
        }

        /// Host only: to every guest except one.
        public static void SendExcept(int exceptId, Msg type, W w, Channel ch = Channel.Reliable, int asSender = 0)
        {
            if (!IsHost) return;
            foreach (var p in Players.Values) if (p.Connected && p.Id != 0 && p.Id != exceptId) Raw(p.Conn, type, asSender, w.Data, 0, w.Len, ch);
        }

        // =====================================================================================
        // receiving
        // =====================================================================================

        private static void OnData(int conn, byte[] data, int off, int len)
        {
            BytesIn += len; PacketsIn++;
            if (len < 2) { BadPackets++; return; }
            var type = (Msg)data[off];
            int sender = data[off + 1];
            Channel ch = Channel.Reliable;
            if (IsHost)
            {
                // The truth about who is talking comes from the connection, never from the packet.
                var p = Players.Values.FirstOrDefault(x => x.Conn == conn && x.Connected);
                if (p == null) { if (type != Msg.Hello) return; sender = 255; }
                else { sender = p.Id; p.LastHeard = Time.unscaledTime; }
            }
            else if (conn != _hostConn) return;
            Dispatch(type, sender, Reader.Set(data, off + 2, len - 2), conn, ch);
        }

        private static void Dispatch(Msg type, int sender, R r, int conn, Channel ch)
        {
            var h = Handlers[(byte)type];
            if (h.Fn == null) { BadPackets++; return; }
            int start = r.Pos, end = r.End;
            _currentConn = conn;
            try { h.Fn(sender, r); }
            catch (WireException e) { BadPackets++; Plugin.Log.LogWarning($"[session] bad {type} from player {sender}: {e.Message}"); return; }
            catch (Exception e) { Plugin.Log.LogError($"[session] {type} from player {sender}: {e}"); return; }
            finally { _currentConn = -1; }
            if (IsHost && h.Relay == Relay.Others && sender != 0 && sender != 255)
                foreach (var p in Players.Values)
                    if (p.Connected && p.Id != 0 && p.Id != sender) Raw(p.Conn, type, sender, r.Data, start, end - start, h.Channel);
        }

        private static int _currentConn = -1;

        // =====================================================================================
        // link up / down
        // =====================================================================================

        private static void OnConnected(int conn)
        {
            if (IsHost) { Plugin.Log.LogInfo($"[session] link {conn} up ({Transport.Describe(conn)}), waiting for hello"); return; }
            _hostConn = conn;
            Send(Msg.Hello, Scratch.Reset().I32(Plugin.Protocol).Str(Plugin.Version).Str(MyName).Str(_clientKey).Bool(State == SessionState.Reconnecting));
        }

        private static void OnDisconnected(int conn, string reason)
        {
            if (IsHost)
            {
                var p = Players.Values.FirstOrDefault(x => x.Conn == conn && x.Connected);
                if (p == null) return;
                p.Connected = false; p.Conn = -1; p.DroppedAt = Time.unscaledTime;
                Plugin.Log.LogInfo($"[session] player {p.Id} ({p.Name}) dropped: {reason}; place kept for {HoldSeconds:0}s");
                Send(Msg.PlayerLeft, Scratch.Reset().U8((byte)p.Id).Bool(true));
                PlayerLeft?.Invoke(p, true);
                return;
            }
            if (State == SessionState.Joined)
            {
                Drops++;
                State = SessionState.Reconnecting;
                _reconnectStarted = Time.unscaledTime; _nextReconnect = Time.unscaledTime + 0.5f;
                Plugin.Log.LogInfo($"[session] link to the host dropped ({reason}); trying to get back");
                LinkLost?.Invoke();
            }
            else if (State == SessionState.Joining)
            {
                LastError = "could not reach the host (" + reason + ")";
                Leave(LastError);
            }
            // Reconnecting: the next attempt is already scheduled in Tick.
        }

        // =====================================================================================
        // handshake
        // =====================================================================================

        private static void OnHello(int sender, R r)
        {
            if (!IsHost) return;
            if (sender != 255) return;   // already in: a second hello on the same link is ignored
            int conn = _currentConn;
            int protocol = r.I32(); string version = r.Str(); string name = r.Str(); string key = r.Str(); bool back = r.Bool();
            if (protocol != Plugin.Protocol) { RefuseConn(conn, $"version mismatch: the host has Moonlighter Together {Plugin.Version}, you have {version}"); return; }
            if (string.IsNullOrEmpty(key) || key.Length > 64) { RefuseConn(conn, "bad hello"); return; }
            name = string.IsNullOrEmpty(name) ? "Will" : (name.Length > 24 ? name.Substring(0, 24) : name);

            var p = Players.Values.FirstOrDefault(x => x.Id != 0 && x.ClientKey == key);
            bool cameBack = p != null;
            if (p != null && p.Connected)
            {
                // The old link is still "up" on our side (it has not timed out yet): the new one replaces it.
                int old = p.Conn; p.Conn = -1; p.Connected = false;
                Transport.Disconnect(old);
            }
            if (p == null)
            {
                if (Players.Count >= MaxPlayers) { RefuseConn(conn, "the session is full"); return; }
                int id = 1; while (Players.ContainsKey(id)) id++;
                p = new Player { Id = id, ClientKey = key };
                Players[id] = p;
            }
            p.Name = name; p.Conn = conn; p.Connected = true; p.LastHeard = Time.unscaledTime;

            var w = Scratch.Reset().U8((byte)p.Id).F64(Now).U8((byte)Players.Count);
            foreach (var q in Players.Values) w.U8((byte)q.Id).Str(q.Name).Bool(q.Connected);
            Raw(conn, Msg.Welcome, 0, w.Data, 0, w.Len, Channel.Reliable);
            SendExcept(p.Id, Msg.PlayerJoined, Scratch.Reset().U8((byte)p.Id).Str(p.Name).Bool(cameBack));
            Plugin.Log.LogInfo($"[session] player {p.Id} ({p.Name}) {(cameBack ? "came back" : "joined")} on link {conn}");
            PlayerJoined?.Invoke(p, cameBack);
        }

        private static void RefuseConn(int conn, string why)
        {
            Plugin.Log.LogInfo($"[session] refused link {conn}: {why}");
            var w = Scratch.Reset().Str(why);
            Raw(conn, Msg.Refuse, 0, w.Data, 0, w.Len, Channel.Reliable);
            // Let the message leave before the link closes.
            Plugin.Instance.StartCoroutine(DisconnectSoon(conn));
        }

        private static System.Collections.IEnumerator DisconnectSoon(int conn)
        {
            yield return new WaitForSecondsRealtime(0.5f);
            Transport?.Disconnect(conn);
        }

        private static void OnRefuse(int sender, R r)
        {
            if (IsHost) return;
            LastError = r.Str();
            Leave("refused: " + LastError);
        }

        private static void OnWelcome(int sender, R r)
        {
            if (IsHost) return;
            bool cameBack = State == SessionState.Reconnecting;
            MyId = r.U8();
            double hostNow = r.F64();
            _clockOffset = hostNow - TimeExt.Local; _bestRtt = float.MaxValue;
            int n = r.U8();
            var seen = new HashSet<int>();
            var fresh = new List<Player>();
            for (int i = 0; i < n; i++)
            {
                int id = r.U8(); string name = r.Str(); bool up = r.Bool();
                seen.Add(id);
                if (!Players.TryGetValue(id, out var p)) { p = new Player { Id = id }; Players[id] = p; fresh.Add(p); }
                p.Name = name; p.Connected = up;
            }
            foreach (var gone in Players.Keys.Where(k => !seen.Contains(k)).ToList()) { var p = Players[gone]; Players.Remove(gone); PlayerLeft?.Invoke(p, false); }
            State = SessionState.Joined;
            if (cameBack) Reconnects++;
            _nextPing = 0;
            Plugin.Log.LogInfo($"[session] {(cameBack ? "back in" : "joined")} as player {MyId}; {Players.Count} players");
            Started?.Invoke(cameBack);
            foreach (var p in fresh) if (p.Id != MyId) PlayerJoined?.Invoke(p, false);
        }

        private static void OnPlayerJoinedMsg(int sender, R r)
        {
            if (IsHost) return;
            int id = r.U8(); string name = r.Str(); bool cameBack = r.Bool();
            if (!Players.TryGetValue(id, out var p)) { p = new Player { Id = id }; Players[id] = p; }
            p.Name = name; p.Connected = true;
            PlayerJoined?.Invoke(p, cameBack);
        }

        private static void OnPlayerLeftMsg(int sender, R r)
        {
            if (IsHost) return;
            int id = r.U8(); bool temporary = r.Bool();
            if (!Players.TryGetValue(id, out var p)) return;
            p.Connected = false;
            if (!temporary) Players.Remove(id);
            PlayerLeft?.Invoke(p, temporary);
        }

        private static void OnBye(int sender, R r)
        {
            if (IsHost)
            {
                if (Players.TryGetValue(sender, out var p) && p.Id != 0) RemovePlayer(p, "left");
            }
            else Leave("the host closed the session");
        }

        private static void RemovePlayer(Player p, string why)
        {
            int conn = p.Conn;
            Players.Remove(p.Id);
            p.Connected = false; p.Conn = -1;
            if (conn >= 0) Transport.Disconnect(conn);
            Plugin.Log.LogInfo($"[session] player {p.Id} ({p.Name}) {why}");
            Send(Msg.PlayerLeft, Scratch.Reset().U8((byte)p.Id).Bool(false));
            PlayerLeft?.Invoke(p, false);
        }

        // =====================================================================================
        // clock
        // =====================================================================================

        private static void OnPing(int sender, R r)
        {
            if (!IsHost) return;
            double t = r.F64();
            SendTo(sender, Msg.Pong, Scratch.Reset().F64(t).F64(Now), Channel.State);
        }

        private static void OnPong(int sender, R r)
        {
            if (IsHost) return;
            double sent = r.F64(), hostNow = r.F64();
            double local = TimeExt.Local;
            float rtt = (float)(local - sent);
            if (rtt < 0 || rtt > 5) return;
            HostRttMs = (int)(rtt * 1000);
            if (Players.TryGetValue(0, out var host)) host.LastHeard = Time.unscaledTime;
            // The sample with the shortest round trip has the least guesswork in it. Let the best one age
            // slowly so the clock can still follow a route that got slower for good.
            _bestRtt *= 1.01f;
            if (rtt <= _bestRtt)
            {
                _bestRtt = rtt;
                double target = hostNow + rtt * 0.5 - local;
                // Small corrections are eased in so replayed motion never jumps.
                _clockOffset = Math.Abs(target - _clockOffset) > 0.25 ? target : _clockOffset + (target - _clockOffset) * 0.2;
            }
        }
    }

    internal static class TimeExt
    {
        // Unity 2019.2 has no double-precision clock; a Stopwatch gives one that never loses precision.
        private static readonly System.Diagnostics.Stopwatch Watch = System.Diagnostics.Stopwatch.StartNew();
        public static double Local => Watch.Elapsed.TotalSeconds;
    }
}
