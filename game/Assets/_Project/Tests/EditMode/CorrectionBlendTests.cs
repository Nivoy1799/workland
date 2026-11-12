using System;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Dev;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 054b AC3: the smoothed reconciliation. A correction below CorrectionBlend.SnapMeters blends over BlendSeconds, one above it
    /// snaps; the blend only moves the drawn body, never the simulated position (which is always the server's). Also the dev numbers the
    /// load test reads (RemoteMotionStats smoothing bins) and the session's mode byte.
    /// </summary>
    public class CorrectionBlendTests
    {
        private const float Tick = 1f / 30f;

        [Test]
        public void ASmallCorrection_Blends_ALargeOneSnaps()
        {
            Vector3 small = new Vector3(0.12f, 0f, 0f);
            CorrectionBlend blended = CorrectionBlend.None.Correct(small, Tick, out float visibleSmall);
            Assert.AreEqual(-small, blended.Offset, "the drawn body stays where it was");
            Assert.AreEqual(small.magnitude / CorrectionBlend.BlendSeconds, blended.Rate, 1e-5f);
            Assert.AreEqual(small.magnitude * Tick / CorrectionBlend.BlendSeconds, visibleSmall, 1e-5f, "a quarter of it in the next tick");

            Vector3 large = new Vector3(0f, 0f, CorrectionBlend.SnapMeters);
            CorrectionBlend snapped = CorrectionBlend.None.Correct(large, Tick, out float visibleLarge);
            Assert.AreEqual(Vector3.zero, snapped.Offset, "a real teleport is never hidden");
            Assert.AreEqual(large.magnitude, visibleLarge, 1e-6f, "the whole jump is visible");
            Assert.IsTrue(CorrectionBlend.Blends(CorrectionBlend.SnapMeters - 0.001f));
            Assert.IsFalse(CorrectionBlend.Blends(CorrectionBlend.SnapMeters));
        }

        [Test]
        public void ABlend_ReachesTheSimulatedPosition_InBlendSeconds()
        {
            CorrectionBlend blend = CorrectionBlend.None.Correct(new Vector3(0.2f, 0f, -0.1f), Tick, out _);
            float t = 0f;
            float lastLength = blend.Offset.magnitude;
            while (blend.Offset != Vector3.zero && t < 1f)
            {
                blend = blend.Advance(Tick);
                t += Tick;
                Assert.LessOrEqual(blend.Offset.magnitude, lastLength, "it only ever shrinks");
                lastLength = blend.Offset.magnitude;
            }
            Assert.LessOrEqual(t, CorrectionBlend.BlendSeconds + Tick, "done in about 0.12 s");
            Assert.AreEqual(0f, blend.Rate);
        }

        [Test]
        public void TheSimulation_AlwaysEqualsTheServer_OnlyTheDrawnBodyBlends()
        {
            // The owner predicts 2.5 m/s east; the server has it a little slower (pushed by someone), and every reconcile snaps the
            // simulated position to the server's. The blend never feeds back into the simulation.
            Vector3 server = Vector3.zero, simulated = Vector3.zero, lastDrawn = Vector3.zero;
            CorrectionBlend blend = CorrectionBlend.None;
            for (int tick = 0; tick < 90; tick++)
            {
                server += new Vector3(2.4f * Tick, 0f, 0f);
                Vector3 predicted = simulated + new Vector3(2.5f * Tick, 0f, 0f);
                // Reconcile: the simulated position becomes the server's; the drawn body keeps its place and blends.
                Vector3 correction = server - predicted;
                simulated = server;
                blend = blend.Correct(correction, Tick, out float visible).Advance(Tick);
                Assert.AreEqual(server, simulated, "the simulated position is the server's, every tick");
                Vector3 drawn = blend.Drawn(simulated);
                Assert.Less((drawn - simulated).magnitude, CorrectionBlend.SnapMeters + 1e-5f);
                Assert.Less(visible, 0.05f, "a 3 mm drift is never a visible jump");
                Assert.Greater(drawn.x, lastDrawn.x - 1e-6f, "the drawn body never goes backwards");
                lastDrawn = drawn;
            }
        }

        [Test]
        public void RepeatedCorrections_InOneDirection_NeverPullTheBodyFurtherThanTheSnapDistance()
        {
            CorrectionBlend blend = CorrectionBlend.None;
            float visible = 0f;
            for (int i = 0; i < 20; i++) blend = blend.Correct(new Vector3(0.2f, 0f, 0f), Tick, out visible);
            Assert.AreEqual(CorrectionBlend.SnapMeters, blend.Offset.magnitude, 1e-5f, "capped");
            Assert.Greater(visible, 0.2f - 1e-4f, "what is over the cap is drawn at once, so it shows");
        }

        [Test]
        public void ASnap_DropsTheOldOffset_AndShowsTheWholeJump()
        {
            CorrectionBlend blend = CorrectionBlend.None.Correct(new Vector3(0.1f, 0f, 0f), Tick, out _);
            CorrectionBlend snapped = blend.Correct(new Vector3(-1f, 0f, 0f), Tick, out float visible);
            Assert.AreEqual(CorrectionBlend.None.Offset, snapped.Offset);
            // Drawn before: simulated + (-0.1); after: simulated - 1. The body jumps 0.9 m.
            Assert.AreEqual(0.9f, visible, 1e-5f);
        }

        [Test]
        public void TheLoadTestBins_CountShiftsBeforeAndAfterSmoothing()
        {
            var stats = new RemoteMotionStats();
            stats.RecordSmoothed(0.02f, 0.006f); // a 2 cm shift: no shift counted, not visible
            stats.RecordSmoothed(0.12f, 0.033f); // blended: 12 cm before, 3.3 cm after
            stats.RecordSmoothed(0.40f, 0.40f);  // snapped
            int[] shift = new int[RemoteMotionStats.SmoothBinEdgesCm.Count + 1];
            int[] vis = new int[RemoteMotionStats.SmoothBinEdgesCm.Count + 1];
            stats.TakeSmoothing(shift, vis, out int shifts, out int visibleOver, out float shiftMax, out float visMax);
            Assert.AreEqual(2, shifts);
            Assert.AreEqual(1, visibleOver, "only the snap moves the drawn body more than 5 cm");
            Assert.AreEqual(40f, shiftMax, 1e-3f);
            Assert.AreEqual(40f, visMax, 1e-3f);
            Assert.AreEqual(1, shift[RemoteMotionStats.SmoothBinOf(12f)]);
            Assert.AreEqual(1, vis[RemoteMotionStats.SmoothBinOf(3.3f)]);
            Assert.AreEqual("owner_vis_cm_le_4", RemoteMotionStats.SmoothBinName("owner_vis", RemoteMotionStats.SmoothBinOf(3.3f)));
            Assert.AreEqual("owner_shift_cm_gt_150", RemoteMotionStats.SmoothBinName("owner_shift", RemoteMotionStats.SmoothBinEdgesCm.Count));
            stats.TakeSmoothing(shift, vis, out shifts, out visibleOver, out _, out _);
            Assert.AreEqual(0, shifts, "a new window");
            Assert.AreEqual(0, visibleOver);
        }

        [Test]
        public void TheSessionModeByte_ReadsBackItsMode_AndAnUnknownByteIsTheDefault()
        {
            Type session = typeof(NetBootstrap).Assembly.GetType("Worktown.Game.Net.SessionPhysics");
            System.Reflection.MethodInfo modeOf = session.GetMethod("ModeOf");
            Assert.IsNotNull(modeOf, "SessionPhysics.ModeOf");
            foreach (PlayerCollisionMode mode in Enum.GetValues(typeof(PlayerCollisionMode)))
                Assert.AreEqual(mode, modeOf.Invoke(null, new object[] { (byte)mode }));
            Assert.AreEqual(PlayerCollisionMode.Soft, modeOf.Invoke(null, new object[] { (byte)200 }));
            Assert.IsTrue(LaunchArgs.TryCollisionMode("soft", out PlayerCollisionMode soft) && soft == PlayerCollisionMode.Soft);
            Assert.IsFalse(LaunchArgs.TryCollisionMode("Soft", out _), "lower case only, like every flag");
        }
    }
}
