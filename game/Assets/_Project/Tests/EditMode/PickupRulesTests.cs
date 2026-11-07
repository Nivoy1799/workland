using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 003 AC4: no pickup further than 2.5 m.</summary>
    public class PickupRulesTests
    {
        [Test]
        public void ItemWithinReach_IsAllowed()
        {
            Assert.IsTrue(PickupRules.IsInReach(Vector3.zero, new Vector3(2.4f, 0f, 0f)));
        }

        [Test]
        public void ItemExactlyAtMaxReach_IsAllowed()
        {
            Assert.IsTrue(PickupRules.IsInReach(Vector3.zero, new Vector3(0f, 0f, PickupRules.MaxReach)));
        }

        [Test]
        public void ItemBeyondReach_IsRefused()
        {
            Assert.IsFalse(PickupRules.IsInReach(Vector3.zero, new Vector3(2.6f, 0f, 0f)));
        }

        [Test]
        public void ReachIsMeasuredIn3D()
        {
            // 2 m sideways and 2 m up is 2.83 m away.
            Assert.IsFalse(PickupRules.IsInReach(Vector3.zero, new Vector3(2f, 2f, 0f)));
        }

        [Test]
        public void ReachDoesNotDependOnWhereThePlayerStands()
        {
            var player = new Vector3(100f, 0f, -40f);
            Assert.IsTrue(PickupRules.IsInReach(player, player + new Vector3(0f, 0f, 2f)));
            Assert.IsFalse(PickupRules.IsInReach(player, player + new Vector3(0f, 0f, 3f)));
        }
    }
}
