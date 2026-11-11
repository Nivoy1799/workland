#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using FishNet.Editing.NetworkProfiler;
using FishNet.Managing;
using FishNet.Managing.Statistic;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Dev
{
    /// <summary>
    /// Spec 054 (A1), development builds only: the host's outbound bytes per FishNet packet type and per object, without editing
    /// FishNet. FishNet counts them already (NetworkTrafficStatistics) but its switch is a private serialized field that every manager
    /// reads once at start, so <see cref="EnableBeforeSceneLoad"/> sets it by reflection before NetworkManager.Awake, and only with
    /// <c>-net-stats</c> on a host or server (<see cref="ShouldEnable"/>; without it nothing changes, so the perf check's GC numbers
    /// stay comparable). Mode Development is enough: FishNet's statistics file defines DEVELOPMENT for UNITY_EDITOR || DEVELOPMENT_BUILD.
    /// Each tick the probe copies the numbers of the server's outbound groups into a <see cref="TrafficTable"/> (never FishNet's objects:
    /// they are reset right after the event). Every reflected member is checked by <see cref="CheckMembers"/> (NetTrafficTests).
    /// </summary>
    public sealed class NetTrafficProbe
    {
        /// <summary>Objects reported per metrics line.</summary>
        public const int TopObjects = 8;

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        /// <summary>FishNet's id of the socket bytes in the group table (NetworkTrafficStatistics.UNSPECIFIED_PACKETID, internal).</summary>
        private const PacketId SocketGroup = (PacketId)ushort.MaxValue;

        private static readonly FieldInfo StatisticsField = typeof(StatisticsManager).GetField("_networkTraffic", Private);
        private static readonly FieldInfo EnableModeField = typeof(NetworkTrafficStatistics).GetField("_enableMode", Private);
        private static readonly FieldInfo OutboundField = typeof(BidirectionalNetworkTraffic).GetField("OutboundTraffic", Any);
        private static readonly FieldInfo GroupsField = OutboundField?.FieldType.GetField("_packetGroups", Private);
        private static readonly FieldInfo BytesField = OutboundField?.FieldType.GetField("Bytes", Any);
        private static readonly FieldInfo PacketsField = typeof(PacketGroup).GetField("_packets", Private);

        private readonly TrafficTable _table = new TrafficTable();
        private readonly Dictionary<PacketId, string> _packetNames = new Dictionary<PacketId, string>();
        private readonly Dictionary<int, string> _objectNames = new Dictionary<int, string>();
        private NetworkTrafficStatistics _statistics;

        /// <summary>AC4: whether this start turns FishNet's statistics on: only <c>-net-stats</c> on a host or a server.</summary>
        public static bool ShouldEnable(LaunchArgs args) =>
            args != null && args.IsValid && args.NetStats && (args.Mode == LaunchMode.Host || args.Mode == LaunchMode.Server);

        /// <summary>
        /// AC3: every reflected FishNet member exists with the expected type. Empty when all are there; else one line per missing or
        /// changed member (a FishNet upgrade renamed it: fix the name here).
        /// </summary>
        public static IReadOnlyList<string> CheckMembers()
        {
            var problems = new List<string>();
            Expect(problems, "StatisticsManager._networkTraffic", StatisticsField, typeof(NetworkTrafficStatistics));
            Expect(problems, "NetworkTrafficStatistics._enableMode", EnableModeField, typeof(NetworkTrafficStatistics.EnabledMode));
            Expect(problems, "BidirectionalNetworkTraffic.OutboundTraffic", OutboundField, null);
            if (OutboundField != null && OutboundField.FieldType.Name != "NetworkTraffic")
                problems.Add("BidirectionalNetworkTraffic.OutboundTraffic: type " + OutboundField.FieldType.Name + ", expected NetworkTraffic");
            Expect(problems, "NetworkTraffic._packetGroups", GroupsField, typeof(Dictionary<PacketId, PacketGroup>));
            Expect(problems, "NetworkTraffic.Bytes", BytesField, typeof(ulong));
            Expect(problems, "PacketGroup._packets", PacketsField, typeof(List<Packet>));
            if (Enum.GetName(typeof(NetworkTrafficStatistics.EnabledMode), NetworkTrafficStatistics.EnabledMode.Development) != "Development")
                problems.Add("NetworkTrafficStatistics.EnabledMode.Development is gone");
            return problems;
        }

        internal static void Expect(List<string> problems, string member, FieldInfo field, Type expected)
        {
            if (field == null) problems.Add(member + ": not found");
            else if (expected != null && field.FieldType != expected)
                problems.Add(member + ": type " + field.FieldType.FullName + ", expected " + expected.FullName);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnableBeforeSceneLoad()
        {
            if (!ShouldEnable(LaunchArgs.Parse(Environment.GetCommandLineArgs()))) return;
            IReadOnlyList<string> problems = CheckMembers();
            if (problems.Count > 0)
            {
                Debug.LogError("[NetStats] FishNet members changed, statistics stay off: " + string.Join("; ", problems));
                return;
            }
            NetworkManager manager = UnityEngine.Object.FindFirstObjectByType<NetworkManager>(FindObjectsInactive.Include);
            if (manager == null)
            {
                Debug.LogError("[NetStats] no NetworkManager in the first scene: statistics stay off");
                return;
            }
            if (!manager.TryGetComponent(out StatisticsManager statisticsManager))
                statisticsManager = manager.gameObject.AddComponent<StatisticsManager>();
            var statistics = StatisticsField.GetValue(statisticsManager) as NetworkTrafficStatistics;
            if (statistics == null)
            {
                statistics = new NetworkTrafficStatistics();
                StatisticsField.SetValue(statisticsManager, statistics);
            }
            EnableModeField.SetValue(statistics, NetworkTrafficStatistics.EnabledMode.Development);
            Debug.Log("[NetStats] FishNet traffic statistics on (-net-stats)");
        }

        /// <summary>The host's probe once its NetworkManager has started; null when statistics are off (no <c>-net-stats</c>).</summary>
        public static NetTrafficProbe TryAttach(NetworkManager manager)
        {
            if (manager == null || manager.StatisticsManager == null) return null;
            if (!manager.StatisticsManager.TryGetNetworkTrafficStatistics(out NetworkTrafficStatistics statistics)) return null;
            var probe = new NetTrafficProbe { _statistics = statistics };
            statistics.OnNetworkTraffic += probe.OnTraffic;
            return probe;
        }

        public void Detach()
        {
            if (_statistics != null) _statistics.OnNetworkTraffic -= OnTraffic;
            _statistics = null;
        }

        /// <summary>The window's fields (per second, per client; <see cref="TrafficTable.Fields(int, int, double)"/>), then a new window.</summary>
        public IReadOnlyList<KeyValuePair<string, double>> TakeFields(int clients, double seconds)
        {
            IReadOnlyList<KeyValuePair<string, double>> fields = _table.Fields(clients, TopObjects, seconds);
            _table.Clear();
            return fields;
        }

        private void OnTraffic(uint tick, BidirectionalNetworkTraffic serverTraffic, BidirectionalNetworkTraffic clientTraffic)
        {
            if (serverTraffic == null) return;
            object outbound = OutboundField.GetValue(serverTraffic);
            if (outbound == null) return;
            _table.AddSocket((ulong)BytesField.GetValue(outbound));
            if (!(GroupsField.GetValue(outbound) is Dictionary<PacketId, PacketGroup> groups)) return;
            foreach (KeyValuePair<PacketId, PacketGroup> pair in groups)
            {
                PacketGroup group = pair.Value;
                if (group == null || group.PacketId == SocketGroup) continue;
                _table.AddGroup(NameOf(group.PacketId), group.Bytes);
                if (!(PacketsField.GetValue(group) is List<Packet> packets)) continue;
                foreach (Packet packet in packets)
                    if (packet.GameObject != null) _table.AddObject(NameOf(packet.GameObject), packet.Bytes);
            }
        }

        private string NameOf(PacketId id)
        {
            if (!_packetNames.TryGetValue(id, out string name))
            {
                name = id.ToString();
                _packetNames[id] = name;
            }
            return name;
        }

        /// <summary>The object's bucket: the name of its NetworkObject (a behaviour on a child counts for its object), cached.</summary>
        private string NameOf(GameObject go)
        {
            int id = go.GetInstanceID();
            if (_objectNames.TryGetValue(id, out string name)) return name;
            NetworkObject nob = go.GetComponentInParent<NetworkObject>();
            name = TrafficTable.ObjectKey(nob != null ? nob.gameObject.name : go.name);
            _objectNames[id] = name;
            return name;
        }
    }
}
#endif
