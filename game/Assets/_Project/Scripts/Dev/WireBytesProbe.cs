#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using FishNet.Transporting;
using FishNet.Transporting.Multipass;
using FishNet.Transporting.Tugboat;
using LiteNetLib;

namespace Worktown.Game.Dev
{
    /// <summary>
    /// Spec 054 (A2), development builds only: the bytes and datagrams Tugboat's LiteNetLib socket really sent (a host) or received
    /// (a bot), so the metrics line can give wire bytes (<see cref="TrafficTable.WireBytes"/>: plus the IPv4 and UDP headers) next to
    /// FishNet's payload. The socket's NetManager is internal (CommonSocket.NetManager, read by reflection, checked by
    /// <see cref="CheckMembers"/>); its public EnableStatistics is turned on once the socket exists and its public Statistics are read
    /// as deltas. A new socket (a reconnect) starts a new count. On the Steam transport Tugboat has no socket: the probe reports 0.
    /// </summary>
    public sealed class WireBytesProbe
    {
        private static readonly FieldInfo NetManagerField =
            typeof(CommonSocket).GetField("NetManager", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        private readonly Tugboat _tugboat;
        private readonly bool _asServer;
        private NetManager _manager;
        private long _lastBytes;
        private long _lastDatagrams;

        public WireBytesProbe(Transport transport, bool asServer)
        {
            _tugboat = transport as Tugboat ?? (transport is Multipass multipass ? multipass.GetTransport<Tugboat>() : null);
            _asServer = asServer;
        }

        /// <summary>AC3: the reflected LiteNetLib member exists with the expected type (empty when it does).</summary>
        public static IReadOnlyList<string> CheckMembers()
        {
            var problems = new List<string>();
            NetTrafficProbe.Expect(problems, "CommonSocket.NetManager", NetManagerField, typeof(NetManager));
            return problems;
        }

        /// <summary>
        /// Since the last call: the socket's bytes and datagrams out (a host's server socket) or in (a bot's client socket). Zero while
        /// there is no Tugboat socket.
        /// </summary>
        public void Sample(out long bytes, out long datagrams)
        {
            bytes = 0;
            datagrams = 0;
            if (_tugboat == null || NetManagerField == null) return;
            CommonSocket socket = _asServer ? (CommonSocket)_tugboat.ServerSocket : _tugboat.ClientSocket;
            var manager = socket != null ? NetManagerField.GetValue(socket) as NetManager : null;
            if (manager == null) return;
            if (!ReferenceEquals(manager, _manager))
            {
                _manager = manager;
                manager.EnableStatistics = true;
                Read(manager, out _lastBytes, out _lastDatagrams);
                return;
            }
            Read(manager, out long nowBytes, out long nowDatagrams);
            bytes = nowBytes - _lastBytes;
            datagrams = nowDatagrams - _lastDatagrams;
            _lastBytes = nowBytes;
            _lastDatagrams = nowDatagrams;
            if (bytes < 0) bytes = 0;
            if (datagrams < 0) datagrams = 0;
        }

        private void Read(NetManager manager, out long bytes, out long datagrams)
        {
            NetStatistics s = manager.Statistics;
            bytes = _asServer ? s.BytesSent : s.BytesReceived;
            datagrams = _asServer ? s.PacketsSent : s.PacketsReceived;
        }
    }
}
#endif
