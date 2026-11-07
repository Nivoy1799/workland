using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Social;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 012 AC1 and AC2.</summary>
    public class SocialRulesTests
    {
        [TestCase(0f, 1f, 0)]      // up
        [TestCase(1f, 1f, 1)]      // up-right
        [TestCase(1f, 0f, 2)]      // right
        [TestCase(1f, -1f, 3)]
        [TestCase(0f, -1f, 4)]     // down
        [TestCase(-1f, -1f, 5)]
        [TestCase(-1f, 0f, 6)]     // left
        [TestCase(-1f, 1f, 7)]
        public void WheelSlot_EightDirections(float x, float y, int slot)
        {
            Assert.AreEqual(slot, SocialRules.WheelSlot(new Vector2(x, y)));
        }

        [Test]
        public void WheelSlot_DeadZoneAndBadInput()
        {
            Assert.AreEqual(-1, SocialRules.WheelSlot(Vector2.zero));
            Assert.AreEqual(-1, SocialRules.WheelSlot(new Vector2(0.1f, 0.1f)));
            Assert.AreEqual(-1, SocialRules.WheelSlot(new Vector2(float.NaN, 1f)));
            Assert.AreEqual(-1, SocialRules.WheelSlot(new Vector2(float.PositiveInfinity, 0f)));
        }

        [Test]
        public void RateLimit_AllowsBurstThenRefuses_ThenRefills()
        {
            var limiter = new SocialRateLimiter();
            for (int i = 0; i < SocialRules.Burst; i++)
                Assert.IsTrue(limiter.TryConsume(10f), "burst " + i);
            Assert.IsFalse(limiter.TryConsume(10f), "spam is refused");
            Assert.IsFalse(limiter.TryConsume(10.5f), "half a second is not enough");
            Assert.IsTrue(limiter.TryConsume(11.6f), "refilled after a second");
        }

        [Test]
        public void RateLimit_TimeGoingBackwardsDoesNotRefill()
        {
            var limiter = new SocialRateLimiter();
            for (int i = 0; i < SocialRules.Burst; i++) limiter.TryConsume(5f);
            Assert.IsFalse(limiter.TryConsume(1f));
        }

        [Test]
        public void Ping_RangeAndFinite()
        {
            Vector3 sender = new Vector3(10f, 0f, 10f);
            Assert.IsTrue(SocialRules.IsValidPing(sender, sender + Vector3.forward * 39f));
            Assert.IsFalse(SocialRules.IsValidPing(sender, sender + Vector3.forward * 41f));
            Assert.IsFalse(SocialRules.IsValidPing(sender, new Vector3(float.NaN, 0f, 0f)));
            Assert.IsFalse(SocialRules.IsValidPing(sender, new Vector3(0f, float.NegativeInfinity, 0f)));
        }

        [Test]
        public void Catalogs_HaveUniqueIdsAndText()
        {
            AssertCatalog(EmoteCatalog.All, SocialRules.WheelSlots);
            AssertCatalog(QuickChatCatalog.All, 6);
        }

        [Test]
        public void UnknownIds_AreRefused()
        {
            Assert.IsFalse(EmoteCatalog.TryGet(200, out _));
            Assert.IsFalse(QuickChatCatalog.TryGet(6, out _));
            Assert.IsTrue(EmoteCatalog.TryGet(0, out SocialPhrase wave));
            Assert.AreEqual("Wave", wave.Text);
        }

        private static void AssertCatalog(IReadOnlyList<SocialPhrase> phrases, int expectedCount)
        {
            Assert.AreEqual(expectedCount, phrases.Count);
            for (int i = 0; i < phrases.Count; i++)
            {
                Assert.AreEqual(i, phrases[i].Id, "id equals index");
                Assert.IsNotEmpty(phrases[i].Text);
                Assert.IsNotEmpty(phrases[i].Bubble);
            }
        }
    }
}
