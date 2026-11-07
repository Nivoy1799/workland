using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Tests
{
    /// <summary>Edge cases for the pure netcode rules (spec 003 tester gap fill).</summary>
    public class NetRulesEdgeCaseTests
    {
        [TestCase("a:b:c")]
        [TestCase("::1")]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("host:-1")]
        [TestCase("host:")]
        public void TrySplitEndpoint_RejectsMalformed(string value)
        {
            Assert.IsFalse(LaunchArgs.TrySplitEndpoint(value, out _, out _));
        }

        [Test]
        public void TrySplitEndpoint_HostAndPort()
        {
            Assert.IsTrue(LaunchArgs.TrySplitEndpoint("10.0.0.5:9000", out string a, out ushort p));
            Assert.AreEqual("10.0.0.5", a);
            Assert.AreEqual(9000, p);
        }

        [Test]
        public void ServerPlusConnect_IsAnError()
        {
            var r = LaunchArgs.Parse(new[] { "-server", "-connect", "1.2.3.4" });
            Assert.IsFalse(r.IsValid);
            Assert.AreEqual(LaunchMode.Menu, r.Mode);
        }

        [Test]
        public void Registry_UnknownItemAndClient_AreEmpty()
        {
            var reg = new PickupRegistry();
            Assert.AreEqual(PickupRegistry.NoHolder, reg.HolderOf(99));
            Assert.IsFalse(reg.TryGetHeldItem(5, out _));
            Assert.IsFalse(reg.ReleaseAllFor(5, out _));
            Assert.IsFalse(reg.TryRelease(99, 5));
        }

        [Test]
        public void Registry_TryGetHeldItem_ReturnsClaimedItem()
        {
            var reg = new PickupRegistry();
            Assert.IsTrue(reg.TryClaim(3, 1));
            Assert.IsTrue(reg.TryGetHeldItem(1, out int item));
            Assert.AreEqual(3, item);
            Assert.AreEqual(1, reg.HolderOf(3));
        }

        [Test]
        public void Registry_HolderRepeatingClaim_IsRefusedAndKeepsItem()
        {
            var reg = new PickupRegistry();
            Assert.IsTrue(reg.TryClaim(3, 1));
            Assert.IsFalse(reg.TryClaim(3, 1));
            Assert.AreEqual(1, reg.HolderOf(3));
        }

        [Test]
        public void Reach_NaNPosition_IsRefused()
        {
            Assert.IsFalse(PickupRules.IsInReach(Vector3.zero, new Vector3(float.NaN, 0, 0)));
        }
    }
}
