using NUnit.Framework;
using Worktown.Game.Net.Rules;

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
        public void Bot_WithConnectAndSeed()
        {
            var a = Parse("-batchmode", "-nographics", "-bot", "-connect", "127.0.0.1:7770", "-seed", "17");
            Assert.IsTrue(a.IsValid, a.Error);
            Assert.AreEqual(LaunchMode.ClientTugboat, a.Mode);
            Assert.IsTrue(a.IsBot);
            Assert.AreEqual(17, a.Seed);
        }

        [Test]
        public void Seed_AcceptsNegative()
        {
            Assert.AreEqual(-3, Parse("-bot", "-connect", "h", "-seed", "-3").Seed);
        }

        [TestCase("-bot")]
        [TestCase("-bot", "-server")]
        [TestCase("-bot", "-host")]
        [TestCase("-bot", "-steam", "123")]
        [TestCase("-bot", "-connect", "h", "-seed")]
        [TestCase("-bot", "-connect", "h", "-seed", "x")]
        public void BotWithoutTugboatOrBadSeed_IsAnError(params string[] args)
        {
            Assert.IsFalse(LaunchArgs.Parse(args).IsValid);
        }

        [Test]
        public void Metrics_OnServer()
        {
            var a = Parse("-server", "-metrics", "/tmp/m.jsonl");
            Assert.IsTrue(a.IsValid, a.Error);
            Assert.AreEqual("/tmp/m.jsonl", a.MetricsPath);
        }

        [TestCase("-metrics", "/tmp/m.jsonl")]
        [TestCase("-connect", "h", "-metrics", "/tmp/m.jsonl")]
        [TestCase("-server", "-metrics")]
        [TestCase("-server", "-metrics", "-batchmode")]
        public void MetricsOutsideServerOrWithoutPath_IsAnError(params string[] args)
        {
            Assert.IsFalse(LaunchArgs.Parse(args).IsValid);
        }

        [Test]
        public void TwoModeFlags_AreAnError()
        {
            var a = Parse("-server", "-connect", "127.0.0.1");
            Assert.AreEqual(LaunchMode.Menu, a.Mode);
            Assert.IsFalse(a.IsValid);
        }
    }
}
