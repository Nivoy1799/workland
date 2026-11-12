using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 054b option (a): the server pushes a player against the others where that player's client drew them. The position history
    /// lookup, the view-tick clamp (never ahead, at most 20 ticks back), the client's view tick, and that two players who see each other
    /// late (each by a different delay) never stay overlapping by more than 10 cm for more than half a second.
    /// </summary>
    public class LagCompensationTests
    {
        private const float Dt = 1f / 30f;

        [Test]
        public void TheHistory_FindsRecordedTicks_AndForgetsOverwrittenOnes()
        {
            var history = new PositionHistory(8);
            for (uint t = 100; t < 110; t++) history.Record(t, new Vector3(t, 0f, 0f));
            Assert.IsTrue(history.TryGet(109, out Vector3 newest));
            Assert.AreEqual(109f, newest.x);
            Assert.IsTrue(history.TryGet(102, out Vector3 oldest));
            Assert.AreEqual(102f, oldest.x, "the ring keeps the last 8 ticks");
            Assert.IsFalse(history.TryGet(101, out _), "overwritten by 109");
            Assert.IsFalse(history.TryGet(110, out _), "not recorded yet");
            history.Record(109, new Vector3(-1f, 0f, 0f));
            Assert.IsTrue(history.TryGet(109, out Vector3 again));
            Assert.AreEqual(-1f, again.x, "a later write of a tick replaces it");
            history.Clear();
            Assert.IsFalse(history.TryGet(109, out _));
            Assert.AreEqual(LagCompensation.HistoryTicks, new PositionHistory().Capacity);
            Assert.Greater(LagCompensation.HistoryTicks, LagCompensation.MaxViewTicks, "the history reaches past the longest view delay");
        }

        [Test]
        public void TheServer_ClampsTheViewTick_NeverAheadAndAtMost20TicksBack()
        {
            Assert.AreEqual(Mathf.RoundToInt(LagCompensation.MaxViewSeconds * NetRateRules.TickRate), LagCompensation.MaxViewTicks);
            Assert.AreEqual(20, LagCompensation.MaxViewTicks, "667 ms (the netcode owner, 2026-11-12)");
            Assert.AreEqual(1000u, LagCompensation.ServerViewTick(1000, LagCompensation.Now), "no claim: now");
            Assert.AreEqual(1000u, LagCompensation.ServerViewTick(1000, 1005), "a claim from the future: now");
            Assert.AreEqual(1000u, LagCompensation.ServerViewTick(1000, 1000));
            Assert.AreEqual(994u, LagCompensation.ServerViewTick(1000, 994), "a plausible delay is taken as it is");
            Assert.AreEqual(989u, LagCompensation.ServerViewTick(1000, 989), "the 15-bot rig's median, 11 ticks");
            Assert.AreEqual(980u, LagCompensation.ServerViewTick(1000, 980), "exactly 20 ticks back");
            Assert.AreEqual(980u, LagCompensation.ServerViewTick(1000, 979), "21 ticks back is clamped to 20");
            Assert.AreEqual(980u, LagCompensation.ServerViewTick(1000, 900), "a long claimed delay is clamped to 20 ticks");
            Assert.AreEqual(980u, LagCompensation.ServerViewTick(1000, 1), "however long");
            Assert.AreEqual(1u, LagCompensation.ServerViewTick(5, 1), "early in a session: 4 ticks back is within the limit");
            Assert.AreEqual(10u, LagCompensation.ServerViewTick(30, 1));
        }

        [Test]
        public void TheClient_ClaimsTheNewestServerTick_MinusTheDisplayDelay()
        {
            Assert.AreEqual(NetRateRules.RemotePlayerInterval * NetRateRules.RemoteInterpolation, LagCompensation.DisplayTicks);
            Assert.AreEqual(4, LagCompensation.DisplayTicks, "15 Hz, two updates buffered: 133 ms");
            Assert.AreEqual(496u, LagCompensation.ClientViewTick(500));
            Assert.AreEqual(LagCompensation.Now, LagCompensation.ClientViewTick(0), "nothing received yet");
            Assert.AreEqual(LagCompensation.Now, LagCompensation.ClientViewTick(3));
        }

        /// <summary>Two players on the server, one replicate after the other, each pushed against the other as its own client drew it
        /// (the history at its own delay), the floor against the real position.</summary>
        private static float LongestDeepOverlap(Vector3 a, Vector3 b, Vector3 va, Vector3 vb, int delayA, int delayB, float seconds,
            out float closest)
        {
            var historyA = new PositionHistory();
            var historyB = new PositionHistory();
            uint tick = 1000;
            historyA.Record(tick, a);
            historyB.Record(tick, b);
            float deepFor = 0f, longest = 0f;
            closest = float.MaxValue;
            int ticks = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < ticks; i++)
            {
                tick++;
                a += Step(a, va, b, historyB, tick, delayA, 1, 2);
                b += Step(b, vb, a, historyA, tick, delayB, 2, 1);
                historyA.Record(tick, a);
                historyB.Record(tick, b);
                float distance = new Vector2(a.x - b.x, a.z - b.z).magnitude;
                closest = Mathf.Min(closest, distance);
                // Overlapping bodies (0.30 m each) by more than 10 cm.
                if (2f * PlayerSeparation.BodyRadius - distance > 0.10f) deepFor += Dt;
                else deepFor = 0f;
                longest = Mathf.Max(longest, deepFor);
            }
            return longest;
        }

        private static Vector3 Step(Vector3 self, Vector3 velocity, Vector3 otherReal, PositionHistory otherHistory, uint now, int delay,
            int selfId, int otherId)
        {
            uint at = LagCompensation.ServerViewTick(now, now - (uint)delay);
            Vector3 seen = at < now && otherHistory.TryGet(at, out Vector3 past) ? past : otherReal;
            return PlayerSeparation.Step(self, velocity * Dt, seen, otherReal, selfId, otherId, Dt);
        }

        [Test]
        public void PlayersSeeingEachOtherLate_NeverStayOverlappingMoreThan10Cm_ForHalfASecond()
        {
            const float walk = 2.5f, sprint = 5f;
            var cases = new List<(string name, Vector3 a, Vector3 b, Vector3 va, Vector3 vb)>
            {
                ("walk into a standing player", new Vector3(-2f, 0f, 0.05f), Vector3.zero, new Vector3(walk, 0f, 0f), Vector3.zero),
                ("sprint into a standing player", new Vector3(-2f, 0f, 0.05f), Vector3.zero, new Vector3(sprint, 0f, 0f), Vector3.zero),
                ("walk head-on", new Vector3(-2f, 0f, 0.05f), new Vector3(2f, 0f, 0f), new Vector3(walk, 0f, 0f), new Vector3(-walk, 0f, 0f)),
                ("sprint head-on", new Vector3(-3f, 0f, 0.1f), new Vector3(3f, 0f, 0f), new Vector3(sprint, 0f, 0f), new Vector3(-sprint, 0f, 0f)),
                ("cross at a right angle", new Vector3(-2f, 0f, 0f), new Vector3(0.05f, 0f, -2f), new Vector3(walk, 0f, 0f), new Vector3(0f, 0f, walk)),
                ("sprint across", new Vector3(-2f, 0f, 0f), new Vector3(0.05f, 0f, -2f), new Vector3(sprint, 0f, 0f), new Vector3(0f, 0f, sprint)),
            };
            int[][] delays = { new[] { 0, 0 }, new[] { 6, 2 }, new[] { 9, 9 }, new[] { 9, 0 }, new[] { 20, 20 }, new[] { 20, 0 }, new[] { 30, 30 } };
            foreach ((string name, Vector3 a, Vector3 b, Vector3 va, Vector3 vb) c in cases)
            foreach (int[] d in delays)
            {
                float longest = LongestDeepOverlap(c.a, c.b, c.va, c.vb, d[0], d[1], 4f, out float closest);
                string label = c.name + ", delays " + d[0] + "/" + d[1] + " ticks";
                Assert.LessOrEqual(longest, 0.5f, label + ": overlapping more than 10 cm for " + longest + " s");
                Assert.Greater(closest, PlayerSeparation.MinDistance - 2f * sprint * Dt, label + ": the floor holds on real positions");
            }
        }

        [Test]
        public void TwoPlayersPushingDeadCentre_StayAtTheFloor_AndNeverPass()
        {
            // Both hold walk straight into each other: nothing to slide along, so they stand pressed at the floor (bodies 15 cm into
            // each other) for as long as they push. The only case the half-second bound above leaves out; they never pass.
            foreach (int delay in new[] { 0, 9 })
            {
                LongestDeepOverlap(new Vector3(-2f, 0f, 0f), new Vector3(2f, 0f, 0f), new Vector3(2.5f, 0f, 0f), new Vector3(-2.5f, 0f, 0f),
                    delay, delay, 3f, out float closest);
                Assert.GreaterOrEqual(closest, PlayerSeparation.MinDistance - 0.02f, "delay " + delay);
            }
        }
    }
}
