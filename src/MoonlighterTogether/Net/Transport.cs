using System;
using System.Collections.Generic;
using System.Net;
using LiteNetLib;

namespace MoonlighterTogether.Net
{
    /// How a message must travel.
    public enum Channel : byte
    {
        /// Arrives, exactly once, in the order sent. Events: hits, deaths, pick-ups, travel, chat.
        Reliable = 0,
        /// May be lost or arrive late; the newest one wins. Streams: positions, animations.
        State = 1,
    }

    /// A link to other copies of the game. Connections are numbered by the transport; the session layer
    /// maps them to players. Everything is called from, and calls back on, Unity's main thread.
    public interface ITransport
    {
        string Kind { get; }
        bool IsServer { get; }
        void Poll();
        void Send(int conn, byte[] data, int len, Channel ch);
        void Disconnect(int conn);
        void Shutdown();
        /// Round-trip time to that connection in milliseconds as the transport measures it (-1 if unknown).
        int Rtt(int conn);
        string Describe(int conn);

        event Action<int> Connected;
        event Action<int, string> Disconnected;
        event Action<int, byte[], int, int> Data;   // conn, buffer, offset, length (valid during the call only)
    }

    /// UDP with LiteNetLib (reliability, ordering, fragmentation, keep-alive). Used for LAN, VPN, a
    /// forwarded port, and every automated test.
    public sealed class UdpTransport : ITransport, INetEventListener
    {
        public const int DefaultPort = 7810;
        private const string ConnectKey = "moonlighter-together";

        private readonly NetManager _net;
        private readonly Dictionary<int, NetPeer> _peers = new Dictionary<int, NetPeer>();
        public bool IsServer { get; private set; }
        public string Kind => "udp";
        public int LocalPort => _net.LocalPort;

        public event Action<int> Connected;
        public event Action<int, string> Disconnected;
        public event Action<int, byte[], int, int> Data;

        private UdpTransport()
        {
            _net = new NetManager(this)
            {
                AutoRecycle = true,
                UpdateTime = 10,
                DisconnectTimeout = 8000,
                PingInterval = 500,
                ChannelsCount = 2,
                UnsyncedEvents = false,
                IPv6Enabled = false,
                ReuseAddress = false,
                MaxConnectAttempts = 6,
                ReconnectDelay = 500,
            };
        }

        public static UdpTransport Host(int port)
        {
            var t = new UdpTransport { IsServer = true };
            if (!t._net.Start(port)) throw new InvalidOperationException("could not open UDP port " + port + " (already in use?)");
            return t;
        }

        public static UdpTransport Join(string address, int port)
        {
            var t = new UdpTransport { IsServer = false };
            if (!t._net.Start()) throw new InvalidOperationException("could not open a UDP socket");
            t._net.Connect(address, port, ConnectKey);
            return t;
        }

        public void Poll() => _net.PollEvents();

        public void Send(int conn, byte[] data, int len, Channel ch)
        {
            if (!_peers.TryGetValue(conn, out var p)) return;
            if (ch == Channel.Reliable) p.Send(data, 0, len, 0, DeliveryMethod.ReliableOrdered);
            else
            {
                // An unreliable packet must fit in one datagram; anything bigger travels reliably instead.
                if (len <= p.GetMaxSinglePacketSize(DeliveryMethod.Unreliable)) p.Send(data, 0, len, 1, DeliveryMethod.Unreliable);
                else p.Send(data, 0, len, 0, DeliveryMethod.ReliableOrdered);
            }
        }

        public void Disconnect(int conn) { if (_peers.TryGetValue(conn, out var p)) p.Disconnect(); }
        public void Shutdown() { try { _net.Stop(true); } catch { } _peers.Clear(); }
        public int Rtt(int conn) => _peers.TryGetValue(conn, out var p) ? p.Ping * 2 : -1;
        public string Describe(int conn) => _peers.TryGetValue(conn, out var p) ? p.ToString() : "?";

        // ---- LiteNetLib callbacks (main thread, from Poll) ----
        void INetEventListener.OnConnectionRequest(ConnectionRequest request) => request.AcceptIfKey(ConnectKey);
        void INetEventListener.OnPeerConnected(NetPeer peer) { _peers[peer.Id] = peer; Connected?.Invoke(peer.Id); }

        void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
        {
            // A join that never got through also ends up here, with a peer we were never told about.
            bool known = _peers.Remove(peer.Id);
            Disconnected?.Invoke(known ? peer.Id : -1, info.Reason.ToString());
        }

        void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
            => Data?.Invoke(peer.Id, reader.RawData, reader.Position, reader.AvailableBytes);

        void INetEventListener.OnNetworkError(IPEndPoint endPoint, System.Net.Sockets.SocketError error)
            => Plugin.Log.LogWarning($"[udp] socket error {error} ({endPoint})");
        void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint ep, NetPacketReader reader, UnconnectedMessageType type) { }
        void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
    }
}
