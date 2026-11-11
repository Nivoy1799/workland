using NUnit.Framework;
using Worktown.Game.Net.Rules;
using Worktown.Game.Player;

namespace Worktown.Game.Tests
{
    public class LaunchArgsTests
    {
        private static LaunchArgs Parse(params string[] args) => LaunchArgs.Parse(args);

        [Test]
        public void NoFlags_ShowsMenu()
        {
            var a = Parse("game.exe");
            Assert.AreEqual(LaunchMode.Menu, a.Mode);
            Assert.IsTrue(a.IsValid);
        }

        [Test]
        public void Server_IgnoresUnityFlags()
        {
            var a = Parse("server.x86_64", "-batchmode", "-nographics", "-server", "-logFile", "-");
            Assert.AreEqual(LaunchMode.Server, a.Mode);
            Assert.IsTrue(a.IsValid);
        }

        [Test]
        public void Host()
        {
            Assert.AreEqual(LaunchMode.Host, Parse("-host").Mode);
        }

        [Test]
        public void Connect_WithPort()
        {
            var a = Parse("-connect", "192.168.1.20:7780");
            Assert.AreEqual(LaunchMode.ClientTugboat, a.Mode);
            Assert.AreEqual("192.168.1.20", a.Address);
            Assert.AreEqual(7780, a.Port);
        }

        [Test]
        public void Connect_WithoutPort_UsesDefault()
        {
            var a = Parse("-connect", "localhost");
            Assert.AreEqual("localhost", a.Address);
            Assert.AreEqual(LaunchArgs.DefaultTugboatPort, a.Port);
        }

        [TestCase("-connect")]
        [TestCase("-connect", "-batchmode")]
        [TestCase("-connect", "host:notaport")]
        [TestCase("-connect", "host:0")]
        [TestCase("-connect", "host:70000")]
        [TestCase("-connect", ":7770")]
        [TestCase("-steam")]
        public void InvalidValues_FallBackToMenuWithError(params string[] args)
        {
            var a = LaunchArgs.Parse(args);
            Assert.AreEqual(LaunchMode.Menu, a.Mode);
            Assert.IsFalse(a.IsValid);
        }

        [Test]
        public void Steam_TakesTarget()
        {
            var a = Parse("-steam", "76561197960287930");
            Assert.AreEqual(LaunchMode.ClientSteam, a.Mode);
            Assert.AreEqual("76561197960287930", a.SteamTarget);
        }

        [Test]
        public void Latency_ForTesting()
        {
            var a = Parse("-connect", "127.0.0.1", "-latency", "150");
            Assert.IsTrue(a.IsValid, a.Error);
            Assert.AreEqual(150, a.LatencyMs);
            Assert.AreEqual(0, Parse("-connect", "h").LatencyMs);
        }

        [TestCase("-latency")]
        [TestCase("-latency", "-5")]
        [TestCase("-latency", "abc")]
        [TestCase("-latency", "5000")]
        public void BadLatency_IsAnError(params string[] args)
        {
            Assert.IsFalse(LaunchArgs.Parse(args).IsValid);
        }

        [TestCase("first", ViewMode.FirstPerson)]
        [TestCase("third", ViewMode.ThirdPerson)]
        public void View_Parses(string value, ViewMode expected)
        {
            var a = Parse("-connect", "127.0.0.1", "-view", value, "-screenshot", "shot.png");
            Assert.IsTrue(a.IsValid, a.Error);
            Assert.AreEqual(expected, a.View);
            Assert.AreEqual(LaunchMode.ClientTugboat, a.Mode);
        }

        [Test]
        public void NoView_LeavesItUnset()
        {
            Assert.IsNull(Parse("-connect", "127.0.0.1").View);
            Assert.IsNull(Parse("game.exe").View);
        }

        [TestCase("-view")]
        [TestCase("-view", "sideways")]
        [TestCase("-view", "-host")]
        public void BadView_IsAnError(params string[] args)
        {
            var a = LaunchArgs.Parse(args);
            Assert.IsFalse(a.IsValid);
            Assert.AreEqual(LaunchMode.Menu, a.Mode);
            Assert.IsNull(a.View);
        }

        [Test]
        public void TwoModeFlags_AreAnError()
        {
            var a = Parse("-server", "-connect", "127.0.0.1");
            Assert.AreEqual(LaunchMode.Menu, a.Mode);
            Assert.IsFalse(a.IsValid);
        }

        // ---- Spec 027: -tour, -observe, -tour-out ----

        [Test]
        public void Tour_WithConnect_Parses()
        {
            var a = Parse("Worktown.exe", "-connect", "127.0.0.1", "-tour", "walk", "-tour-out", "C:/out/visual", "-skip-whatsnew");
            Assert.IsTrue(a.IsValid, a.Error);
            Assert.AreEqual("walk", a.Tour);
            Assert.AreEqual("C:/out/visual", a.TourOut);
            Assert.AreEqual(string.Empty, a.Observe);
            Assert.AreEqual(LaunchMode.ClientTugboat, a.Mode);
        }

        [Test]
        public void NoTourFlags_LeaveThemEmpty()
        {
            var a = Parse("-connect", "127.0.0.1");
            Assert.AreEqual(string.Empty, a.Tour);
            Assert.AreEqual(string.Empty, a.Observe);
            Assert.AreEqual(string.Empty, a.TourOut);
        }

        [TestCase("-tour", "walk")]
        [TestCase("-host", "-tour", "walk")]
        [TestCase("-server", "-tour", "walk")]
        [TestCase("-steam", "76561197960287930", "-tour", "walk")]
        public void Tour_OnlyWithConnect(params string[] args)
        {
            var a = LaunchArgs.Parse(args);
            Assert.IsFalse(a.IsValid);
            Assert.AreEqual(LaunchMode.Menu, a.Mode);
            Assert.AreEqual(string.Empty, a.Tour);
        }

        [TestCase("-host")]
        [TestCase("-connect", "127.0.0.1")]
        public void Observe_WithHostOrConnect_Parses(params string[] mode)
        {
            var args = new System.Collections.Generic.List<string>(mode) { "-observe", "walk" };
            var a = LaunchArgs.Parse(args);
            Assert.IsTrue(a.IsValid, a.Error);
            Assert.AreEqual("walk", a.Observe);
        }

        [TestCase("-observe", "walk")]
        [TestCase("-server", "-observe", "walk")]
        public void Observe_NeedsAPlayer(params string[] args)
        {
            Assert.IsFalse(LaunchArgs.Parse(args).IsValid);
        }

        [TestCase("-connect", "127.0.0.1", "-tour")]
        [TestCase("-connect", "127.0.0.1", "-tour", "-view", "first")]
        [TestCase("-connect", "127.0.0.1", "-observe")]
        [TestCase("-connect", "127.0.0.1", "-tour", "walk", "-tour-out")]
        [TestCase("-connect", "127.0.0.1", "-tour", " ")]
        public void TourFlags_NeedAValue(params string[] args)
        {
            var a = LaunchArgs.Parse(args);
            Assert.IsFalse(a.IsValid);
            Assert.AreEqual(LaunchMode.Menu, a.Mode);
        }

        [Test]
        public void TourAndObserve_ExcludeEachOther()
        {
            Assert.IsFalse(Parse("-connect", "127.0.0.1", "-tour", "walk", "-observe", "walk").IsValid);
        }

        // ---- spec 045: the load test (spec 004 tooling) ----

        [Test]
        public void Bot_WithSeedAndMetrics()
        {
            var a = Parse("-batchmode", "-connect", "127.0.0.1:7770", "-bot", "-seed", "7", "-metrics", "out/bot7.jsonl");
            Assert.IsTrue(a.IsValid, a.Error);
            Assert.IsTrue(a.IsBot);
            Assert.AreEqual(7, a.Seed);
            Assert.AreEqual("out/bot7.jsonl", a.MetricsPath);
            Assert.AreEqual(LaunchMode.ClientTugboat, a.Mode);
        }

        [Test]
        public void Metrics_OnAHostOrServer()
        {
            Assert.IsTrue(Parse("-host", "-metrics", "host.jsonl").IsValid);
            Assert.IsTrue(Parse("-server", "-metrics", "server.jsonl").IsValid);
            Assert.AreEqual("host.jsonl", Parse("-host", "-metrics", "host.jsonl").MetricsPath);
            Assert.IsFalse(Parse("-host").IsBot);
            Assert.AreEqual(string.Empty, Parse("-host").MetricsPath);
        }

        [Test]
        public void Bot_NeedsConnect_AndNoTour_MetricsNeedARole_SeedNeedsAnInteger()
        {
            Assert.IsFalse(Parse("-host", "-bot").IsValid, "bots join over Tugboat");
            Assert.IsFalse(Parse("-bot").IsValid);
            Assert.IsFalse(Parse("-connect", "127.0.0.1", "-bot", "-tour", "look").IsValid);
            Assert.IsFalse(Parse("-connect", "127.0.0.1", "-metrics", "x.jsonl").IsValid, "a plain client writes no metrics");
            Assert.IsFalse(Parse("-host", "-metrics").IsValid);
            Assert.IsFalse(Parse("-connect", "127.0.0.1", "-bot", "-seed", "seven").IsValid);
            Assert.AreEqual(-3, Parse("-connect", "127.0.0.1", "-bot", "-seed", "-3").Seed);
        }

        // ---- Spec 047 ----

        [Test]
        public void MaxPlayers_OnAHostOrServer_OneTo64()
        {
            Assert.AreEqual(0, Parse("-host").MaxPlayers, "not given: D36");
            Assert.AreEqual(17, Parse("-host", "-max-players", "17").MaxPlayers);
            Assert.AreEqual(4, Parse("-server", "-max-players", "4").MaxPlayers);
            Assert.IsFalse(Parse("-host", "-max-players", "0").IsValid);
            Assert.IsFalse(Parse("-host", "-max-players", "65").IsValid);
            Assert.IsFalse(Parse("-host", "-max-players").IsValid);
            Assert.IsFalse(Parse("-connect", "127.0.0.1", "-max-players", "4").IsValid, "a client has no places");
        }

        [Test]
        public void TheMenuTour_StartsOnTheTitleScreen_WithoutAMode()
        {
            var a = Parse("Worktown.exe", "-tour", "menu", "-tour-out", "C:/out");
            Assert.IsTrue(a.IsValid, a.Error);
            Assert.AreEqual(LaunchMode.Menu, a.Mode);
            Assert.AreEqual(LaunchArgs.MenuTour, a.Tour);
            Assert.IsFalse(Parse("-connect", "127.0.0.1", "-tour", "menu").IsValid);
            Assert.IsTrue(Parse("-host", "-observe", "menu").IsValid, "its host");
            Assert.IsFalse(Parse("-tour", "look").IsValid, "other tours still need -connect");
        }
    }
}
