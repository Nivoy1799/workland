using System.Collections.Generic;
using NUnit.Framework;
using Worktown.Game.Dev;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 054, part A (measure): the traffic table (sums, per client and per second, Unattributed never negative, the top objects,
    /// the field names), wire bytes, the reflected FishNet and LiteNetLib members (fail on an upgrade that renames one, naming it),
    /// when the host turns FishNet's statistics on (only with -net-stats), and the new launch flags.
    /// </summary>
    public class NetTrafficTests
    {
        private static Dictionary<string, double> AsMap(IReadOnlyList<KeyValuePair<string, double>> fields)
        {
            var map = new Dictionary<string, double>();
            foreach (KeyValuePair<string, double> f in fields) map[f.Key] = f.Value;
            return map;
        }

        [Test]
        public void TrafficTable_SumsPerType_PerClientAndPerSecond()
        {
            var table = new TrafficTable();
            table.AddGroup("Replicate", 3000);
            table.AddGroup("Replicate", 1000);
            table.AddGroup("SyncType", 800);
            table.AddSocket(10000);
            table.AddSocket(2000);
            Assert.AreEqual(12000UL, table.SocketBytes);
            Assert.AreEqual(4800UL, table.GroupBytes);
            Dictionary<string, double> f = AsMap(table.Fields(4, 8));
            Assert.AreEqual(1000d, f["pid_Replicate_bps"], 1e-9, "4000 bytes over 4 clients in one second");
            Assert.AreEqual(200d, f["pid_SyncType_bps"], 1e-9);
            Assert.AreEqual(1800d, f["pid_Unattributed_bps"], 1e-9, "(12000 - 4800) / 4");
            Assert.AreEqual(3000d, f["net_socket_bps"], 1e-9);
            Dictionary<string, double> twoSeconds = AsMap(table.Fields(4, 8, 2d));
            Assert.AreEqual(500d, twoSeconds["pid_Replicate_bps"], 1e-9, "per second");
            Assert.AreEqual(4000d, AsMap(table.Fields(0, 8))["pid_Replicate_bps"], 1e-9, "no clients counts as one");
            table.Clear();
            Assert.AreEqual(0UL, table.SocketBytes);
            Assert.AreEqual(0d, AsMap(table.Fields(1, 8))["pid_Unattributed_bps"]);
        }

        [Test]
        public void TrafficTable_Unattributed_IsNeverNegative()
        {
            var table = new TrafficTable();
            table.AddGroup("ObserversRpc", 5000); // FishNet counts it per observer, the socket may have sent less this window
            table.AddSocket(3000);
            Assert.AreEqual(0UL, table.UnattributedBytes);
            Assert.AreEqual(0d, AsMap(table.Fields(2, 8))["pid_Unattributed_bps"]);
        }

        [Test]
        public void TrafficTable_TopObjects_LargestFirst_ThenByName()
        {
            var table = new TrafficTable();
            table.AddObject("Player(Clone)", 500);
            table.AddObject("Player(Clone)", 700);
            table.AddObject("Pan 2", 300);
            table.AddObject("Pan (1)", 300);
            table.AddObject("Customer(Clone)", 900);
            table.AddObject("Knife", 900);
            table.AddObject("Food", 10);
            IReadOnlyList<KeyValuePair<string, double>> fields = table.Fields(1, 3);
            var objects = new List<string>();
            foreach (KeyValuePair<string, double> f in fields) if (f.Key.StartsWith("obj_")) objects.Add(f.Key);
            CollectionAssert.AreEqual(new[] { "obj_Player_bps", "obj_Customer_bps", "obj_Knife_bps" }, objects,
                "Player 1200, then the two 900s by name; Pan (600) and Food are cut at 3");
            Assert.AreEqual(600d, AsMap(table.Fields(1, 8))["obj_Pan_bps"], 1e-9, "both pans in one bucket");
        }

        [Test]
        public void TrafficTable_FieldNames_AreFlatWords()
        {
            Assert.AreEqual("Player", TrafficTable.ObjectKey("Player(Clone)"));
            Assert.AreEqual("Pan", TrafficTable.ObjectKey("Pan 2"));
            Assert.AreEqual("Pan", TrafficTable.ObjectKey("Pan (3)"));
            Assert.AreEqual("Unnamed", TrafficTable.ObjectKey(""));
            Assert.AreEqual("Unnamed", TrafficTable.ObjectKey(null));
            Assert.AreEqual("Drink_tap", TrafficTable.ObjectKey("Drink-tap"));
            var table = new TrafficTable();
            table.AddGroup("Replicate", 1);
            table.AddObject("Food(Clone)", 1);
            table.AddSocket(5);
            foreach (KeyValuePair<string, double> f in table.Fields(1, 8))
                Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(f.Key, "^(pid|obj|net)_\\w+_bps$"), f.Key + " is a flat word field");
        }

        [Test]
        public void WireBytes_AddsTheIpAndUdpHeaders()
        {
            Assert.AreEqual(1280L, TrafficTable.WireBytes(1000, 10));
            Assert.AreEqual(0L, TrafficTable.WireBytes(0, 0));
        }

        [Test]
        public void ReflectedFishNetMembers_StillExist()
        {
            IReadOnlyList<string> traffic = NetTrafficProbe.CheckMembers();
            Assert.IsEmpty(traffic, "FishNet changed (NetTrafficProbe): " + string.Join("; ", traffic));
            IReadOnlyList<string> wire = WireBytesProbe.CheckMembers();
            Assert.IsEmpty(wire, "LiteNetLib/Tugboat changed (WireBytesProbe): " + string.Join("; ", wire));
        }

        [Test]
        public void Statistics_AreOnlyEnabled_WithNetStats_OnAHostOrServer()
        {
            Assert.IsFalse(NetTrafficProbe.ShouldEnable(LaunchArgs.Parse(new[] { "-host" })), "a host without -net-stats");
            Assert.IsFalse(NetTrafficProbe.ShouldEnable(LaunchArgs.Parse(new[] { "-host", "-metrics", "m.jsonl" })), "the perf check's host");
            Assert.IsTrue(NetTrafficProbe.ShouldEnable(LaunchArgs.Parse(new[] { "-host", "-net-stats" })));
            Assert.IsTrue(NetTrafficProbe.ShouldEnable(LaunchArgs.Parse(new[] { "-server", "-net-stats" })));
            Assert.IsFalse(NetTrafficProbe.ShouldEnable(LaunchArgs.Parse(new[] { "-connect", "127.0.0.1", "-net-stats" })), "a client: refused");
            Assert.IsFalse(NetTrafficProbe.ShouldEnable(LaunchArgs.Parse(new string[0])), "the title screen");
            Assert.IsFalse(NetTrafficProbe.ShouldEnable(null));
        }

        [Test]
        public void LaunchArgs_NetStatsAndRemoteHz()
        {
            LaunchArgs host = LaunchArgs.Parse(new[] { "-host", "-net-stats", "-remote-hz", "30" });
            Assert.IsTrue(host.IsValid, host.Error);
            Assert.IsTrue(host.NetStats);
            Assert.AreEqual(30, host.RemoteHz);
            Assert.AreEqual(15, LaunchArgs.Parse(new[] { "-server", "-remote-hz", "15" }).RemoteHz);
            Assert.AreEqual(0, LaunchArgs.Parse(new[] { "-host" }).RemoteHz, "not given");
            Assert.IsFalse(LaunchArgs.Parse(new[] { "-host", "-remote-hz", "20" }).IsValid, "only 30 or 15");
            Assert.IsFalse(LaunchArgs.Parse(new[] { "-host", "-remote-hz" }).IsValid, "needs a value");
            Assert.IsFalse(LaunchArgs.Parse(new[] { "-connect", "127.0.0.1", "-remote-hz", "30" }).IsValid, "the host decides");
            Assert.IsFalse(LaunchArgs.Parse(new[] { "-net-stats" }).IsValid, "needs a host or server");
        }

        [Test]
        public void LaunchArgs_PlayerCollision_DefaultsSoft_TheHostDecides()
        {
            // Spec 054b (D48, replaces the 053 review's pass-through default): soft unless -player-collision hard|off (the A/B).
            LaunchArgs plain = LaunchArgs.Parse(new[] { "-host" });
            Assert.AreEqual(PlayerCollisionMode.Soft, plain.PlayerCollision, "soft by default");
            Assert.IsFalse(plain.PlayerCollisionGiven);
            LaunchArgs off = LaunchArgs.Parse(new[] { "-host", "-player-collision", "off" });
            Assert.IsTrue(off.IsValid, off.Error);
            Assert.AreEqual(PlayerCollisionMode.Off, off.PlayerCollision);
            Assert.IsTrue(off.PlayerCollisionGiven);
            Assert.AreEqual(PlayerCollisionMode.Hard, LaunchArgs.Parse(new[] { "-server", "-player-collision", "hard" }).PlayerCollision);
            Assert.AreEqual(PlayerCollisionMode.Soft, LaunchArgs.Parse(new[] { "-host", "-player-collision", "soft" }).PlayerCollision);
            Assert.IsFalse(LaunchArgs.Parse(new[] { "-host", "-player-collision", "on" }).IsValid, "054's on/off is gone: soft, hard or off");
            Assert.IsFalse(LaunchArgs.Parse(new[] { "-host", "-player-collision", "maybe" }).IsValid);
            Assert.IsFalse(LaunchArgs.Parse(new[] { "-host", "-player-collision" }).IsValid);
            Assert.IsFalse(LaunchArgs.Parse(new[] { "-connect", "127.0.0.1", "-player-collision", "off" }).IsValid,
                "a client never decides: the server and every prediction must agree");
        }
    }
}
