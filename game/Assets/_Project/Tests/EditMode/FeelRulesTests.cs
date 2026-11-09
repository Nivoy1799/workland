using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Player;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 027b AC2: easing, the arc, the settle, cone scoring, stickiness, the click buffer, the presets.</summary>
    public class FeelRulesTests
    {
        private static readonly System.Func<float, float>[] Curves = { FeelRules.EaseOut, FeelRules.EaseIn, FeelRules.EaseInOut };

        [Test]
        public void Easing_StartsAtZero_EndsAtOne_AndIsMonotonic()
        {
            foreach (var curve in Curves)
            {
                Assert.AreEqual(0f, curve(0f), 1e-6f);
                Assert.AreEqual(1f, curve(1f), 1e-6f);
                float last = 0f;
                for (int i = 1; i <= 100; i++)
                {
                    float v = curve(i / 100f);
                    Assert.GreaterOrEqual(v, last - 1e-6f);
                    last = v;
                }
                Assert.AreEqual(0f, curve(-1f), 1e-6f, "clamped below");
                Assert.AreEqual(1f, curve(2f), 1e-6f, "clamped above");
                Assert.AreEqual(0f, curve(float.NaN), 1e-6f, "NaN counts as the start");
            }
        }

        [Test]
        public void EaseOut_IsFastAtTheStart()
        {
            // An ease-out arc shows most of its way early: a frame after the click the item has visibly moved.
            Assert.Greater(FeelRules.EaseOut(1f / 60f / 0.15f), 0.25f);
            Assert.Greater(FeelRules.EaseOut(0.5f), 0.8f);
        }

        [Test]
        public void Arc_LiftsByTheLiftInTheMiddle_AndNotAtTheEnds()
        {
            const float lift = 0.08f;
            Assert.AreEqual(0f, FeelRules.ArcLift(0f, lift), 1e-6f);
            Assert.AreEqual(0f, FeelRules.ArcLift(1f, lift), 1e-6f);
            Assert.AreEqual(lift, FeelRules.ArcLift(0.5f, lift), 1e-6f);
            float highest = 0f;
            for (int i = 0; i <= 100; i++) highest = Mathf.Max(highest, FeelRules.ArcLift(i / 100f, lift));
            Assert.AreEqual(lift, highest, 1e-5f, "the middle is the highest point");

            Vector3 from = new Vector3(0f, 0.9f, 0f), to = new Vector3(0.4f, 1.3f, 0.2f);
            Assert.AreEqual(from, FeelRules.TweenPosition(TweenStyle.Arc, from, to, 0f, lift));
            Assert.AreEqual(to, FeelRules.TweenPosition(TweenStyle.Arc, from, to, 1f, lift));
            Vector3 middle = FeelRules.TweenPosition(TweenStyle.Arc, from, to, 0.5f, lift);
            Vector3 straight = Vector3.Lerp(from, to, FeelRules.EaseOut(0.5f));
            Assert.AreEqual(lift, middle.y - straight.y, 1e-5f, "the arc rises above the straight line");
        }

        [Test]
        public void Settle_SinksByTheSettleThenEndsExactlyOnTheSpot()
        {
            const float settle = 0.03f;
            Assert.AreEqual(0f, FeelRules.SettleProgress(0f, settle), 1e-6f);
            Assert.AreEqual(1f, FeelRules.SettleProgress(1f, settle), 1e-6f);
            float peak = 0f;
            for (int i = 0; i <= 200; i++) peak = Mathf.Max(peak, FeelRules.SettleProgress(i / 200f, settle));
            Assert.AreEqual(1f + settle, peak, 1e-4f);
            Assert.AreEqual(1f, FeelRules.SettleProgress(1f, 0f), 1e-6f);
            Assert.AreEqual(FeelRules.EaseOut(0.4f), FeelRules.SettleProgress(0.4f, 0f), 1e-6f, "no settle is a plain ease-out");
        }

        [Test]
        public void Drop_FallsFasterAtTheEnd()
        {
            Vector3 from = Vector3.up * 0.12f, to = Vector3.zero;
            float early = from.y - FeelRules.TweenPosition(TweenStyle.Drop, from, to, 0.25f, 0f).y;
            float late = FeelRules.TweenPosition(TweenStyle.Drop, from, to, 0.75f, 0f).y - FeelRules.TweenPosition(TweenStyle.Drop, from, to, 1f, 0f).y;
            Assert.Greater(late, early);
        }

        [TestCase(ItemGoalKind.Rest, -1, ItemGoalKind.Hand, 1, TweenStyle.Arc)]
        [TestCase(ItemGoalKind.Rest, -1, ItemGoalKind.View, 1, TweenStyle.Arc)]
        [TestCase(ItemGoalKind.Hand, 1, ItemGoalKind.Rest, -1, TweenStyle.Settle)]
        [TestCase(ItemGoalKind.View, 1, ItemGoalKind.Rest, -1, TweenStyle.Settle)]
        [TestCase(ItemGoalKind.Hand, 1, ItemGoalKind.Into, -1, TweenStyle.Arc)]
        [TestCase(ItemGoalKind.Into, -1, ItemGoalKind.View, 1, TweenStyle.Arc)]
        [TestCase(ItemGoalKind.Hand, 1, ItemGoalKind.View, 1, TweenStyle.None)]
        [TestCase(ItemGoalKind.Hand, 1, ItemGoalKind.Hand, 2, TweenStyle.Arc)]
        [TestCase(ItemGoalKind.Rest, -1, ItemGoalKind.Rest, -1, TweenStyle.None)]
        public void StyleFor_PicksTheTweenOfAChangeOfPlace(ItemGoalKind from, int fromHolder, ItemGoalKind to, int toHolder, TweenStyle expected)
        {
            Assert.AreEqual(expected, FeelRules.StyleFor(from, fromHolder, to, toHolder));
        }

        [Test]
        public void TweenTimes_ComeFromTheSettings()
        {
            FeelSettings a = FeelSettings.PresetA();
            Assert.AreEqual(0.15f, FeelRules.TweenSeconds(TweenStyle.Arc, ItemGoalKind.Hand, a), 1e-6f);
            Assert.AreEqual(0.12f, FeelRules.TweenSeconds(TweenStyle.Settle, ItemGoalKind.Rest, a), 1e-6f);
            Assert.AreEqual(0.12f, FeelRules.TweenSeconds(TweenStyle.Arc, ItemGoalKind.Into, a), 1e-6f);
            Assert.AreEqual(0.08f, FeelRules.TweenAmount(TweenStyle.Arc, ItemGoalKind.Hand, a), 1e-6f);
            Assert.AreEqual(0.03f, FeelRules.TweenAmount(TweenStyle.Settle, ItemGoalKind.Rest, a), 1e-6f);
            Assert.AreEqual(0f, FeelRules.TweenSeconds(TweenStyle.None, ItemGoalKind.Rest, a));
        }

        // ---- Aim ----

        [Test]
        public void Cone_SmallerAngleWins()
        {
            var c = new List<AimCandidate> { new AimCandidate(4f, false, false), new AimCandidate(1f, false, false), new AimCandidate(2.5f, false, false) };
            Assert.AreEqual(1, FeelRules.PickTarget(c, FeelRules.None, 6f, 0.25f, 0.4f));
        }

        [Test]
        public void Cone_OutsideTheConeIsNoTarget()
        {
            var c = new List<AimCandidate> { new AimCandidate(7f, false, false), new AimCandidate(30f, true, false) };
            Assert.AreEqual(FeelRules.None, FeelRules.PickTarget(c, FeelRules.None, 6f, 0.25f, 0.4f));
            Assert.AreEqual(FeelRules.None, FeelRules.PickTarget(new List<AimCandidate>(), FeelRules.None, 6f, 0.25f, 0.4f));
            Assert.AreEqual(FeelRules.None, FeelRules.PickTarget(null, FeelRules.None, 6f, 0.25f, 0.4f));
        }

        [Test]
        public void Cone_CookwareThatTakesTheHeldItemGetsTheBonus()
        {
            // A potato in hand, the pot at 3 degrees and a plain spot at 2: the pot wins the tie-ish case.
            var c = new List<AimCandidate> { new AimCandidate(2f, false, false), new AimCandidate(3f, true, false) };
            Assert.AreEqual(1, FeelRules.PickTarget(c, FeelRules.None, 6f, 0.25f, 0.4f));
            Assert.Less(FeelRules.AimCost(3f, true, 0.4f), FeelRules.AimCost(3f, false, 0.4f));
        }

        [Test]
        public void Cone_TheBonusDoesNotStealASpotRightUnderTheCrosshair()
        {
            // Spec 027 finding: aiming at the free spot next to the pan must give the spot, not the pan.
            var c = new List<AimCandidate> { new AimCandidate(0.3f, false, false), new AimCandidate(4.5f, true, false) };
            Assert.AreEqual(0, FeelRules.PickTarget(c, FeelRules.None, 6f, 0.25f, 0.4f));
        }

        [Test]
        public void DirectHit_AlwaysWins()
        {
            var c = new List<AimCandidate> { new AimCandidate(0.1f, true, false), new AimCandidate(3f, false, true) };
            Assert.AreEqual(1, FeelRules.PickTarget(c, 0, 6f, 0.25f, 0.4f));
        }

        [Test]
        public void Stickiness_NoSwitchBelowTheMargin_SwitchAbove()
        {
            // Current target 0 at 2 degrees (cost 2.5).
            var close = new List<AimCandidate> { new AimCandidate(2f, false, false), new AimCandidate(1.6f, false, false) };
            Assert.AreEqual(0, FeelRules.PickTarget(close, 0, 6f, 0.25f, 0.4f), "2.1 is not 25 % better than 2.5");
            var clearly = new List<AimCandidate> { new AimCandidate(2f, false, false), new AimCandidate(1f, false, false) };
            Assert.AreEqual(1, FeelRules.PickTarget(clearly, 0, 6f, 0.25f, 0.4f), "1.5 beats 2.5 by 40 %");
            Assert.AreEqual(1, FeelRules.PickTarget(close, FeelRules.None, 6f, 0.25f, 0.4f), "without a current target the best wins");

            Assert.IsFalse(FeelRules.ShouldSwitch(2f, 1.6f, 0.25f));
            Assert.IsTrue(FeelRules.ShouldSwitch(2f, 1.4f, 0.25f));
        }

        [Test]
        public void Stickiness_ACurrentTargetOutsideTheConeIsDropped()
        {
            var c = new List<AimCandidate> { new AimCandidate(8f, false, false), new AimCandidate(3f, false, false) };
            Assert.AreEqual(1, FeelRules.PickTarget(c, 0, 6f, 0.25f, 0.4f));
        }

        [Test]
        public void AngleTo_MeasuresDegrees()
        {
            Assert.AreEqual(0f, FeelRules.AngleTo(Vector3.forward, Vector3.forward * 2f), 1e-4f);
            Assert.AreEqual(90f, FeelRules.AngleTo(Vector3.forward, Vector3.right), 1e-4f);
            Assert.AreEqual(180f, FeelRules.AngleTo(Vector3.forward, Vector3.zero));
        }

        [Test]
        public void KeepShown_HoldsATargetForTheMinimumTime()
        {
            Assert.IsTrue(FeelRules.KeepShown(1f, 1.05f, 0.1f));
            Assert.IsFalse(FeelRules.KeepShown(1f, 1.1f, 0.1f));
        }

        // ---- Prediction verdicts (the synced state only) ----

        private static ItemSnapshot Item(int holder, int vesselCount = 0) => new ItemSnapshot(true, holder, Vector3.zero, vesselCount);

        [Test]
        public void Judge_PlaceAndDrop_ConfirmWhenReleased_DenyWhenSomeoneElseHoldsIt()
        {
            foreach (PredictionKind kind in new[] { PredictionKind.Place, PredictionKind.Drop })
            {
                Assert.AreEqual(PredictionPhase.Pending, FeelRules.Judge(kind, 1, Item(1), Item(1)), kind.ToString());
                Assert.AreEqual(PredictionPhase.Confirmed, FeelRules.Judge(kind, 1, Item(1), Item(-1)), kind.ToString());
                Assert.AreEqual(PredictionPhase.Denied, FeelRules.Judge(kind, 1, Item(1), Item(4)), kind.ToString());
                Assert.AreEqual(PredictionPhase.Denied, FeelRules.Judge(kind, 1, Item(1), ItemSnapshot.Gone), kind.ToString());
            }
        }

        [Test]
        public void Judge_PutInto_ConfirmsOnlyWhenOurFoodIsGone()
        {
            Assert.AreEqual(PredictionPhase.Pending, FeelRules.Judge(PredictionKind.PutInto, 1, Item(1, 2), Item(1, 2)));
            Assert.AreEqual(PredictionPhase.Confirmed, FeelRules.Judge(PredictionKind.PutInto, 1, Item(1, 2), ItemSnapshot.Gone));
            // Review of spec 027b: another player's put-in (or a pot that went away) changes the count, not our food.
            Assert.AreEqual(PredictionPhase.Pending, FeelRules.Judge(PredictionKind.PutInto, 1, Item(1, 2), Item(1, 3)));
            Assert.AreEqual(PredictionPhase.Pending, FeelRules.Judge(PredictionKind.PutInto, 1, Item(1, 2), Item(1, 0)));
            Assert.IsTrue(FeelRules.IsRelevantChange(Item(1, 2), Item(1, 3)), "but the timeout counts from it");
            Assert.AreEqual(PredictionPhase.Denied, FeelRules.Judge(PredictionKind.PutInto, 1, Item(1, 2), Item(-1, 2)));
        }

        [Test]
        public void RelevantChange_IgnoresAHeldItemFollowingItsHolder()
        {
            var a = new ItemSnapshot(true, 1, Vector3.zero, 0);
            var b = new ItemSnapshot(true, 1, Vector3.one, 0);
            Assert.IsFalse(FeelRules.IsRelevantChange(a, b), "walking with it");
            var lyingA = new ItemSnapshot(true, -1, Vector3.zero, 0);
            var lyingB = new ItemSnapshot(true, -1, Vector3.right * 0.2f, 0);
            Assert.IsTrue(FeelRules.IsRelevantChange(lyingA, lyingB), "a lying item moved");
            Assert.IsTrue(FeelRules.IsRelevantChange(a, new ItemSnapshot(true, 1, Vector3.zero, 1)), "the pot changed");
            Assert.IsFalse(FeelRules.IsRelevantChange(lyingA, lyingA));
        }

        [Test]
        public void Prediction_PlaceThatIsNeverAnswered_TimesOutEvenWhileWalking()
        {
            var p = new FeelPrediction(PredictionKind.Place, 1, Item(1), 0f, 0.35f);
            for (float t = 0.02f; t < 1f && p.IsPending; t += 0.02f)
                p.Observe(new ItemSnapshot(true, 1, Vector3.forward * t * 2.5f, 0), t);
            Assert.AreEqual(PredictionPhase.Denied, p.Phase);
            Assert.IsTrue(p.TimedOut);
            Assert.That(p.ResolvedTime, Is.InRange(0.35f, 0.42f));
        }

        // ---- Review of spec 027b: request spacing, timeout, queued clicks ----

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 20f)]
        public void ClientInterval_IsAtLeastTheServerIntervalPlusOneTick(float tick)
        {
            float client = Worktown.Game.Net.Rules.PickupRules.ClientRequestInterval(tick);
            Assert.GreaterOrEqual(client, Worktown.Game.Net.Rules.PickupRules.ServerRequestInterval + tick + 0.01f - 1e-6f);
            var buffer = new ClickBuffer(client);
            Assert.GreaterOrEqual(buffer.Interval, Worktown.Game.Net.Rules.PickupRules.ServerRequestInterval + tick);
            Assert.IsTrue(buffer.Click(0f));
            Assert.IsFalse(buffer.Click(0.2f + tick * 0.5f), "a click a tick inside the server limit waits");
        }

        [Test]
        public void ClientInterval_BadTickCountsAs30Hz()
        {
            float at30 = Worktown.Game.Net.Rules.PickupRules.ClientRequestInterval(1f / 30f);
            Assert.AreEqual(at30, Worktown.Game.Net.Rules.PickupRules.ClientRequestInterval(0f), 1e-6f);
            Assert.AreEqual(at30, Worktown.Game.Net.Rules.PickupRules.ClientRequestInterval(float.NaN), 1e-6f);
        }

        [Test]
        public void ConfirmTimeout_GrowsWithTheRoundTrip()
        {
            Assert.AreEqual(0.35f, FeelRules.ConfirmTimeout(0.35f, 0.05f, 1f / 30f), 1e-6f, "a fast host keeps the setting");
            Assert.AreEqual(0.3f * 1.5f + 2f / 30f, FeelRules.ConfirmTimeout(0.35f, 0.3f, 1f / 30f), 1e-5f, "300 ms of round trip");
            Assert.AreEqual(0.35f, FeelRules.ConfirmTimeout(0.35f, float.NaN, float.NaN), 1e-6f);
        }

        [Test]
        public void QueuedClick_IsDroppedWhenTheTargetChangedOrItDidNothing()
        {
            Assert.IsTrue(FeelRules.KeepQueuedClick(true, 42, -1, 42, -1));
            Assert.IsFalse(FeelRules.KeepQueuedClick(true, 42, -1, 43, -1), "the dot moved to something else");
            Assert.IsFalse(FeelRules.KeepQueuedClick(true, 42, 2, 42, 3), "another spot of the same worktop");
            Assert.IsFalse(FeelRules.KeepQueuedClick(false, 42, -1, 42, -1), "it did nothing when it was clicked");
        }

        // ---- Click buffer ----

        [Test]
        public void ClickBuffer_FirstClickGoesAtOnce()
        {
            var buffer = new ClickBuffer(0.23f);
            Assert.IsTrue(buffer.Click(10f));
            Assert.IsFalse(buffer.HasQueued);
            Assert.IsTrue(buffer.Click(10.3f), "outside the interval");
        }

        [Test]
        public void ClickBuffer_ASecondClickInsideTheIntervalIsSentOnce_NeverMore()
        {
            var buffer = new ClickBuffer(0.23f);
            Assert.IsTrue(buffer.Click(0f));
            Assert.IsFalse(buffer.Click(0.05f), "queued, not lost");
            Assert.IsFalse(buffer.Click(0.1f), "a third click merges into the queued one");
            int sent = 0;
            for (float t = 0.1f; t < 2f; t += 1f / 60f)
                if (buffer.Poll(t)) sent++;
            Assert.AreEqual(1, sent);
            Assert.IsFalse(buffer.HasQueued);
        }

        [Test]
        public void ClickBuffer_TheQueuedClickWaitsForTheInterval()
        {
            var buffer = new ClickBuffer(0.23f);
            buffer.Click(0f);
            buffer.Click(0.1f);
            Assert.IsFalse(buffer.Poll(0.2f));
            Assert.IsTrue(buffer.Poll(0.23f));
            Assert.IsFalse(buffer.Click(0.3f), "the queued click went out at 0.23: the next waits again");
        }

        [Test]
        public void ClickBuffer_OtherRequestsShareTheInterval_AndClearDropsTheQueue()
        {
            var buffer = new ClickBuffer(0.23f);
            buffer.NoteSent(5f); // a knife stroke
            Assert.IsFalse(buffer.Click(5.1f));
            buffer.Clear();
            Assert.IsFalse(buffer.Poll(6f), "a menu opened: the queued click is dropped");
        }

        // ---- Settings ----

        [Test]
        public void PresetA_IsTheSpecDefaults()
        {
            FeelSettings a = FeelSettings.PresetA();
            Assert.AreEqual(0.35f, a.ConfirmTimeout, 1e-6f);
            Assert.AreEqual(0.15f, a.PickupSeconds, 1e-6f);
            Assert.AreEqual(0.12f, a.PlaceSeconds, 1e-6f);
            Assert.AreEqual(0.08f, a.PickupLift, 1e-6f);
            Assert.AreEqual(6f, a.AimCone, 1e-6f);
            Assert.AreEqual(0.25f, a.StickMargin, 1e-6f);
            Assert.AreEqual(0.1f, a.HighlightMinSeconds, 1e-6f);
            Assert.AreEqual(0.08f, a.ThirdPersonDamping, 1e-6f);
            Assert.AreEqual(0.05f, a.ComposerDamping, 1e-6f);
            Assert.IsFalse(a.HeadBob, "head bob is off by default");
            Assert.AreEqual(FeelSettings.PresetAName, a.Preset);
        }

        [Test]
        public void PresetB_IsSmootherAndHeavier_WithAWiderFraming()
        {
            FeelSettings a = FeelSettings.PresetA(), b = FeelSettings.PresetB();
            Assert.AreEqual(a.PickupSeconds * 1.6f, b.PickupSeconds, 1e-5f);
            Assert.AreEqual(a.PlaceSeconds * 1.6f, b.PlaceSeconds, 1e-5f);
            Assert.AreEqual(a.DropSeconds * 1.6f, b.DropSeconds, 1e-5f);
            Assert.AreEqual(9f, b.AimCone, 1e-6f);
            Assert.AreEqual(0.12f, b.ThirdPersonDamping, 1e-6f);
            Assert.Greater(b.ThirdPersonDistance, a.ThirdPersonDistance);
            Assert.AreEqual(FeelSettings.PresetBName, b.Preset);
        }

        [Test]
        public void ThirdPersonCamera_IsCloserAndLowerThanBefore()
        {
            // Spec 027 finding: the camera sat too far back (5 m) and high (look point 1.6 m, 20 degrees).
            FeelSettings a = FeelSettings.PresetA();
            Assert.Less(a.ThirdPersonDistance, 5f);
            Assert.Less(a.ThirdPersonHeight, 1.6f);
            Assert.Less(a.ThirdPersonPitch, 20f);
            Assert.LessOrEqual(a.ThirdPersonDamping, 0.1f, "feel.md 5: at most 0.1 s follow damping");
        }

        [Test]
        public void Sanitized_ClampsAndReplacesBadNumbers()
        {
            FeelSettings s = FeelSettings.PresetA();
            s.PickupSeconds = float.NaN;
            s.AimCone = 500f;
            s.ThirdPersonDistance = -3f;
            s.Preset = null;
            FeelSettings clean = FeelSettings.Sanitized(s);
            Assert.AreEqual(0.15f, clean.PickupSeconds, 1e-6f);
            Assert.AreEqual(20f, clean.AimCone, 1e-6f);
            Assert.AreEqual(1.5f, clean.ThirdPersonDistance, 1e-6f);
            Assert.AreEqual(FeelSettings.CustomName, clean.Preset);
            Assert.IsNaN(s.PickupSeconds, "the input is not changed");
        }

        [Test]
        public void Json_RoundTrips_AndBrokenTextIsNull()
        {
            FeelSettings b = FeelSettings.PresetB();
            FeelSettings back = FeelSettings.FromJson(b.ToJson());
            Assert.IsNotNull(back);
            Assert.AreEqual(b.PickupSeconds, back.PickupSeconds, 1e-6f);
            Assert.AreEqual(b.AimCone, back.AimCone, 1e-6f);
            Assert.AreEqual(b.Preset, back.Preset);
            Assert.IsNull(FeelSettings.FromJson(""));
            Assert.IsNull(FeelSettings.FromJson("{ not json"));
        }

        [Test]
        public void Use_BumpsTheVersion_AndKeepsACopy()
        {
            FeelSettings before = FeelSettings.Current;
            int version = FeelSettings.Version;
            try
            {
                FeelSettings b = FeelSettings.PresetB();
                FeelSettings.Use(b);
                Assert.AreEqual(version + 1, FeelSettings.Version);
                b.AimCone = 1f;
                Assert.AreEqual(9f, FeelSettings.Current.AimCone, 1e-6f, "a copy, not the caller's object");
            }
            finally
            {
                FeelSettings.Use(before);
            }
        }

        // ---- Movement (docs/feel.md 5: kept numbers, verified) ----

        [Test]
        public void Walk_ReachesFullSpeedIn0_10To0_15s_AndStopsIn0_08To0_12s()
        {
            const float dt = 1f / 240f;
            float start = TimeTo(Vector3.zero, Vector3.forward * MovementRules.WalkSpeed, dt);
            float stop = TimeTo(Vector3.forward * MovementRules.WalkSpeed, Vector3.zero, dt);
            Assert.That(start, Is.InRange(0.10f, 0.15f), "start " + start);
            Assert.That(stop, Is.InRange(0.08f, 0.12f), "stop " + stop);
        }

        private static float TimeTo(Vector3 velocity, Vector3 target, float dt)
        {
            float t = 0f;
            while ((velocity - target).sqrMagnitude > 1e-6f && t < 5f)
            {
                velocity = MovementRules.Accelerate(velocity, target, dt);
                t += dt;
            }
            return t;
        }
    }
}
