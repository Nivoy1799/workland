using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Dev;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 045: the pure parts of the load-test bot and metrics (BotBrain, LoadStats).</summary>
    public class LoadTestToolTests
    {
        [Test]
        public void BotBrain_SameSeedSameChoices_AndTheSpec004Ranges()
        {
            var a = new BotBrain(42);
            var b = new BotBrain(42);
            var c = new BotBrain(43);
            bool differs = false;
            for (int i = 0; i < 50; i++)
            {
                float wait = a.NextPickupWait(), carry = a.NextCarry();
                int waypoint = a.NextWaypoint(7);
                Assert.AreEqual(wait, b.NextPickupWait());
                Assert.AreEqual(carry, b.NextCarry());
                Assert.AreEqual(waypoint, b.NextWaypoint(7));
                Assert.That(wait, Is.InRange(BotBrain.MinPickupWait, BotBrain.MaxPickupWait));
                Assert.That(carry, Is.InRange(BotBrain.MinCarry, BotBrain.MaxCarry));
                Assert.That(waypoint, Is.InRange(0, 6));
                differs |= c.NextPickupWait() != wait;
                c.NextCarry();
                c.NextWaypoint(7);
            }
            Assert.IsTrue(differs, "another seed, other choices");
            Assert.AreEqual(5f, BotBrain.MinPickupWait);
            Assert.AreEqual(15f, BotBrain.MaxPickupWait);
            Assert.AreEqual(10f, BotBrain.MinCarry);
            Assert.AreEqual(30f, BotBrain.MaxCarry);
            Assert.AreEqual(-1, a.NextWaypoint(0), "no waypoints");
        }

        [Test]
        public void BotBrain_Nearest_IsTheClosestUsableWithinReach_FlatDistance()
        {
            var items = new List<Vector3> { new Vector3(5f, 0f, 0f), new Vector3(1f, 3f, 1f), new Vector3(-2f, 0f, 0f), new Vector3(50f, 0f, 0f) };
            Assert.AreEqual(1, BotBrain.Nearest(Vector3.zero, items, 12f), "height does not count");
            Assert.AreEqual(2, BotBrain.Nearest(Vector3.zero, items, 12f, i => i != 1), "a held one is skipped");
            Assert.AreEqual(-1, BotBrain.Nearest(new Vector3(30f, 0f, 0f), items, 12f), "nothing near enough");
            Assert.AreEqual(-1, BotBrain.Nearest(Vector3.zero, null, 12f));
        }

        [Test]
        public void LoadStats_AverageP95Max_AndAnEmptySecond()
        {
            var samples = new List<float>();
            for (int i = 1; i <= 100; i++) samples.Add(101 - i);
            Assert.AreEqual(50.5f, LoadStats.Average(samples), 1e-4f);
            Assert.AreEqual(95f, LoadStats.Percentile(samples, 95f));
            Assert.AreEqual(100f, LoadStats.Max(samples));
            Assert.AreEqual(1f, LoadStats.Percentile(samples, 0f));
            var none = new List<float>();
            Assert.AreEqual(0f, LoadStats.Average(none));
            Assert.AreEqual(0f, LoadStats.Percentile(none, 95f));
            Assert.AreEqual(0f, LoadStats.Max(none));
            Assert.AreEqual(7f, LoadStats.Percentile(new List<float> { 7f }, 95f));
        }

        [Test]
        public void LoadStats_Line_IsFlatJson_InvariantCulture()
        {
            var fields = new List<KeyValuePair<string, double>>
            {
                new KeyValuePair<string, double>("t", 12),
                new KeyValuePair<string, double>("tick_p95_ms", 1.23456),
                new KeyValuePair<string, double>("bad", double.NaN),
                new KeyValuePair<string, double>("in_bytes", 123456789),
            };
            Assert.AreEqual("{\"t\":12,\"tick_p95_ms\":1.235,\"bad\":0,\"in_bytes\":123456789}", LoadStats.Line(fields));
            Assert.AreEqual("{}", LoadStats.Line(null));
        }
    }
}
