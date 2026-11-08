using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Player;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 025 AC2: the clip speed measured from the planted foot.</summary>
    public class StrideRulesTests
    {
        private const int Rate = 60;

        /// <summary>
        /// A synthetic gait over one loop: each foot is on the ground for <paramref name="stance"/> of the loop, moving
        /// back at <paramref name="speed"/>, and swings forwards in the air for the rest. The right foot is half a loop
        /// behind the left one. A stance under 0.5 leaves phases with both feet in the air (a run).
        /// </summary>
        private static void Gait(float speed, float loop, float stance, out List<float> times, out List<Vector3> left, out List<Vector3> right)
        {
            times = new List<float>();
            left = new List<Vector3>();
            right = new List<Vector3>();
            int samples = Mathf.RoundToInt(loop * Rate);
            for (int i = 0; i <= samples; i++)
            {
                float t = loop * i / samples;
                times.Add(t);
                left.Add(Foot(t / loop, speed, loop, stance, -0.15f));
                right.Add(Foot(Mathf.Repeat(t / loop + 0.5f, 1f), speed, loop, stance, 0.15f));
            }
        }

        private static Vector3 Foot(float phase, float speed, float loop, float stance, float x)
        {
            float step = speed * loop * stance; // distance the body covers while this foot is planted
            if (phase < stance)
            {
                float u = phase / stance;
                return new Vector3(x, 0.02f, step * 0.5f - step * u);
            }
            float s = (phase - stance) / (1f - stance);
            return new Vector3(x, 0.02f + 0.25f * Mathf.Sin(s * Mathf.PI), -step * 0.5f + step * s);
        }

        [TestCase(1.4f, 1.0f, 0.6f)]   // walk with double support
        [TestCase(1.2f, 1.25f, 0.5f)]  // walk, one foot down at a time
        [TestCase(4.5f, 0.7f, 0.35f)]  // run with flight phases
        public void SyntheticGait_GivesItsSpeed(float speed, float loop, float stance)
        {
            Gait(speed, loop, stance, out List<float> times, out List<Vector3> left, out List<Vector3> right);
            Assert.AreEqual(speed, StrideRules.ClipSpeed(times, left, right), speed * 0.02f);
        }

        [Test]
        public void TheSpeedDoesNotDependOnTheSampleRateOrTheStartPhase()
        {
            Gait(2f, 1f, 0.55f, out List<float> times, out List<Vector3> left, out List<Vector3> right);
            float all = StrideRules.ClipSpeed(times, left, right);
            var t2 = new List<float>();
            var l2 = new List<Vector3>();
            var r2 = new List<Vector3>();
            for (int i = 0; i < times.Count; i += 2)
            {
                t2.Add(times[i]);
                l2.Add(left[i]);
                r2.Add(right[i]);
            }
            Assert.AreEqual(all, StrideRules.ClipSpeed(t2, l2, r2), 0.04f);
            Assert.AreEqual(all, StrideRules.ClipSpeed(times, right, left), 0.04f, "feet swapped");
        }

        [Test]
        public void StandingClip_GivesZero()
        {
            var times = new List<float>();
            var left = new List<Vector3>();
            var right = new List<Vector3>();
            for (int i = 0; i <= Rate; i++)
            {
                times.Add(i / (float)Rate);
                // An idle clip: the feet stay where they are, the weight shifts a little from one to the other.
                left.Add(new Vector3(-0.15f, 0.02f + 0.005f * Mathf.Sin(i * 0.3f), 0.05f));
                right.Add(new Vector3(0.15f, 0.02f - 0.005f * Mathf.Sin(i * 0.3f), -0.02f));
            }
            Assert.AreEqual(0f, StrideRules.ClipSpeed(times, left, right));
        }

        [Test]
        public void BadInput_GivesZero()
        {
            Gait(1.4f, 1f, 0.6f, out List<float> times, out List<Vector3> left, out List<Vector3> right);
            Assert.AreEqual(0f, StrideRules.ClipSpeed(new float[0], new Vector3[0], new Vector3[0]), "no samples");
            Assert.AreEqual(0f, StrideRules.ClipSpeed(new[] { 0f }, new[] { Vector3.zero }, new[] { Vector3.zero }), "one sample");
            Assert.AreEqual(0f, StrideRules.ClipSpeed(null, left, right));
            Assert.AreEqual(0f, StrideRules.ClipSpeed(times, left, right.GetRange(0, right.Count - 1)), "lengths differ");

            var nan = new List<Vector3>(left) { [3] = new Vector3(0f, float.NaN, 0f) };
            Assert.AreEqual(0f, StrideRules.ClipSpeed(times, nan, right), "NaN foot");
            var badTime = new List<float>(times) { [5] = float.PositiveInfinity };
            Assert.AreEqual(0f, StrideRules.ClipSpeed(badTime, left, right), "infinite time");
            var backwards = new List<float>(times) { [5] = times[3] };
            Assert.AreEqual(0f, StrideRules.ClipSpeed(backwards, left, right), "time goes backwards");
        }

        [Test]
        public void FeetSlidingForwardsOnTheGround_DoNotCount()
        {
            var times = new List<float>();
            var left = new List<Vector3>();
            var right = new List<Vector3>();
            for (int i = 0; i <= Rate; i++)
            {
                times.Add(i / (float)Rate);
                left.Add(new Vector3(-0.15f, 0.02f, i * 0.01f));
                right.Add(new Vector3(0.15f, 0.02f, i * 0.02f));
            }
            Assert.AreEqual(0f, StrideRules.ClipSpeed(times, left, right));
        }
    }
}
