using System;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Art;
using Worktown.Game.Player;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 027b AC3: the predicted look with the server's confirm and deny paths simulated in EditMode. The simulation
    /// wires the same parts the game does (Pickup / PickupInteractor): a FeelPrediction reads the synced state each frame,
    /// an ItemTween draws the model child (eased from its last drawn pose when the goal changes, FeelRules.StyleFor), and a
    /// denial plays "nope". The networked transform (the root) is checked every frame, also while the model is drawn.
    /// </summary>
    public class FeelPredictionTests
    {
        private const int Me = 1;
        private const int NoHolder = -1;
        private const float Dt = 1f / 60f;
        private static readonly Vector3 RestPosition = new Vector3(2f, 0.9f, 3f);
        private static readonly Pose Hand = new Pose(new Vector3(2.4f, 1.35f, 2.5f), Quaternion.Euler(0f, 30f, 0f));

        private GameObject _item;
        private Transform _model;

        [SetUp]
        public void SetUp()
        {
            _item = new GameObject("Pan");
            _item.transform.SetPositionAndRotation(RestPosition, Quaternion.identity);
            _model = new GameObject("Model").transform;
            _model.SetParent(_item.transform, false);
            _model.localPosition = new Vector3(0f, 0.02f, 0.05f);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_item);
        }

        /// <summary>One predicted pick-up, frame by frame; <paramref name="server"/> gives the synced holder at a time.</summary>
        private sealed class Run
        {
            public int Nopes;
            public PredictionPhase Phase = PredictionPhase.Pending;
            public float ResolvedAt = float.NaN;
            public Vector3 Drawn;
            public bool RootStayed = true;
            public float BackAt = float.NaN;
        }

        private Run Simulate(Func<float, int> server, float seconds, Func<float, bool?> answer = null, float hitchAt = -1f, float hitch = 0f)
        {
            var run = new Run();
            FeelSettings feel = FeelSettings.PresetA();
            var tween = new ItemTween(_item.transform);
            Pose rest = ItemTween.RootPose(_item.transform);
            Vector3 rootPosition = _item.transform.position;
            Quaternion rootRotation = _item.transform.rotation;

            // Frame 0: the item lies there; then the click predicts it in the hand.
            tween.Evaluate(rest, 0f);
            var prediction = new FeelPrediction(PredictionKind.PickUp, Me, new ItemSnapshot(true, NoHolder, RestPosition, 0), 0f, feel.ConfirmTimeout);
            ItemGoalKind kind = ItemGoalKind.Hand;
            TweenStyle style = FeelRules.StyleFor(ItemGoalKind.Rest, NoHolder, kind, Me);
            tween.Begin(style, FeelRules.TweenSeconds(style, kind, feel), FeelRules.TweenAmount(style, kind, feel), -Dt);
            bool predicted = true;
            bool answered = false;

            float t = 0f;
            while (t < seconds)
            {
                t += hitchAt >= 0f && t < hitchAt && t + Dt >= hitchAt ? hitch : Dt;
                int holder = server(t);
                if (answer != null && !answered && answer(t) is bool ok)
                {
                    answered = true;
                    prediction.NoteAnswer(ok, t);
                }
                PredictionPhase phase = prediction.Observe(new ItemSnapshot(true, holder, RestPosition, 0), t);
                if (predicted && phase != PredictionPhase.Pending)
                {
                    predicted = false;
                    run.Phase = phase;
                    run.ResolvedAt = t;
                    if (phase == PredictionPhase.Denied) run.Nopes++;
                }
                // The goal the item draws: the predicted hand, else the synced state.
                ItemGoalKind next = predicted || holder == Me ? ItemGoalKind.Hand : ItemGoalKind.Rest;
                if (next != kind)
                {
                    TweenStyle back = FeelRules.StyleFor(kind, kind == ItemGoalKind.Hand ? Me : NoHolder, next, next == ItemGoalKind.Hand ? Me : NoHolder);
                    tween.Begin(back, FeelRules.TweenSeconds(back, next, feel), FeelRules.TweenAmount(back, next, feel), t);
                    kind = next;
                }
                Pose drawn = tween.Evaluate(kind == ItemGoalKind.Hand ? Hand : rest, t);
                tween.Apply(drawn);
                run.RootStayed &= _item.transform.position == rootPosition && _item.transform.rotation == rootRotation;
                run.Drawn = _model.position;
                tween.Restore();
                run.RootStayed &= _item.transform.position == rootPosition && _item.transform.rotation == rootRotation;
                if (float.IsNaN(run.BackAt) && run.Phase == PredictionPhase.Denied
                    && Vector3.Distance(_model.position, _item.transform.TransformPoint(_model.localPosition)) < 1e-4f
                    && Vector3.Distance(drawn.position, rest.position) < 1e-4f)
                    run.BackAt = t;
            }
            return run;
        }

        private static Vector3 ModelAt(Pose pose, Vector3 local) => pose.position + pose.rotation * local;

        [Test]
        public void ConfirmedPickup_KeepsTheVisualInTheHand()
        {
            Run run = Simulate(t => t >= 0.06f ? Me : NoHolder, 1f);
            Assert.AreEqual(PredictionPhase.Confirmed, run.Phase);
            Assert.AreEqual(0, run.Nopes);
            Assert.That(Vector3.Distance(run.Drawn, ModelAt(Hand, _model.localPosition)), Is.LessThan(1e-4f));
            Assert.IsTrue(run.RootStayed, "the networked transform never moves on the client");
        }

        [Test]
        public void DeniedPickup_GoesBackWithinTimeoutPlusTween_AndPlaysNope()
        {
            Run run = Simulate(t => NoHolder, 1.5f);
            FeelSettings feel = FeelSettings.PresetA();
            Assert.AreEqual(PredictionPhase.Denied, run.Phase);
            Assert.AreEqual(1, run.Nopes, "one nope");
            Assert.IsFalse(float.IsNaN(run.BackAt), "the model is back on its spot");
            // Two observations past the deadline (one frame of grace) plus the settle back.
            Assert.LessOrEqual(run.BackAt, feel.ConfirmTimeout + 2f * Dt + feel.PlaceSeconds + Dt);
            Assert.That(Vector3.Distance(run.Drawn, _item.transform.TransformPoint(_model.localPosition)), Is.LessThan(1e-4f));
            Assert.IsTrue(run.RootStayed, "the networked transform never moves on the client");
        }

        [Test]
        public void RefusedPickup_GoesBackAtOnce_WithoutWaitingForTheTimeout()
        {
            Run run = Simulate(t => NoHolder, 1f, t => t >= 0.08f ? false : (bool?)null);
            Assert.AreEqual(PredictionPhase.Denied, run.Phase);
            Assert.AreEqual(0.08f, run.ResolvedAt, 2f * Dt);
            Assert.AreEqual(1, run.Nopes);
        }

        [Test]
        public void LateConfirmation_WithinTheTimeout_NeverSnapsBack()
        {
            // A slow host or 100 ms of round trip: the holder arrives at 0.3 s, inside the 0.35 s timeout.
            Run run = Simulate(t => t >= 0.3f ? Me : NoHolder, 1f);
            Assert.AreEqual(PredictionPhase.Confirmed, run.Phase);
            Assert.AreEqual(0, run.Nopes);
            Assert.That(Vector3.Distance(run.Drawn, ModelAt(Hand, _model.localPosition)), Is.LessThan(1e-4f));
        }

        [Test]
        public void OkAnswer_CountsTheTimeoutFromThen()
        {
            // The server said ok at 0.3 s; its state change arrives at 0.6 s (after a fixed 0.35 s clock would have fired).
            Run run = Simulate(t => t >= 0.6f ? Me : NoHolder, 1f, t => t >= 0.3f ? true : (bool?)null);
            Assert.AreEqual(PredictionPhase.Confirmed, run.Phase);
            Assert.AreEqual(0, run.Nopes);
        }

        [Test]
        public void OneLongFrame_PastTheDeadline_DoesNotDeny_WhenTheStateArrivesWithIt()
        {
            // A 0.5 s hitch right after the click; the confirmation was received during it and is seen the frame after.
            Run run = Simulate(t => t >= 0.52f ? Me : NoHolder, 1f, null, 0.02f, 0.5f);
            Assert.AreEqual(PredictionPhase.Confirmed, run.Phase);
            Assert.AreEqual(0, run.Nopes);
        }

        [Test]
        public void SomeoneElseGetsIt_DeniesAtOnce()
        {
            Run run = Simulate(t => t >= 0.05f ? 7 : NoHolder, 1f);
            Assert.AreEqual(PredictionPhase.Denied, run.Phase);
            Assert.AreEqual(0.05f, run.ResolvedAt, 2f * Dt);
        }

        [Test]
        public void ItemTween_FirstFrameAfterTheClickAlreadyMoves_AndNeverJumps()
        {
            FeelSettings feel = FeelSettings.PresetA();
            var tween = new ItemTween(_item.transform);
            Pose rest = ItemTween.RootPose(_item.transform);
            tween.Evaluate(rest, 0f);
            tween.Begin(TweenStyle.Arc, feel.PickupSeconds, feel.PickupLift, -Dt); // started a frame early, as Pickup does
            Pose first = tween.Evaluate(Hand, 0f);
            float total = Vector3.Distance(rest.position, Hand.position);
            Assert.Greater(Vector3.Distance(first.position, rest.position), 0.2f * total, "visible in the click's frame");
            Assert.Less(Vector3.Distance(first.position, rest.position), 0.6f * total, "but not there yet");
            Vector3 last = first.position;
            for (float t = Dt; t <= feel.PickupSeconds + Dt; t += Dt)
            {
                Vector3 p = tween.Evaluate(Hand, t).position;
                Assert.Less(Vector3.Distance(p, last), 0.45f * total, "no single-frame jump at " + t);
                last = p;
            }
            Assert.That(Vector3.Distance(last, Hand.position), Is.LessThan(1e-4f));
        }

        [Test]
        public void ItemTween_ACancelledTweenStartsFromWhereItIsDrawn()
        {
            var tween = new ItemTween(_item.transform);
            Pose rest = ItemTween.RootPose(_item.transform);
            tween.Evaluate(rest, 0f);
            tween.Begin(TweenStyle.Arc, 0.15f, 0.08f, 0f);
            Pose mid = tween.Evaluate(Hand, 0.05f);
            tween.Begin(TweenStyle.Settle, 0.12f, 0.03f, 0.05f);
            Pose restart = tween.Evaluate(rest, 0.05f);
            Assert.That(Vector3.Distance(restart.position, mid.position), Is.LessThan(1e-4f), "no jump when the goal changes mid-way");
        }

        [Test]
        public void ItemTween_RestoresTheModel_AndLeavesLabelsAlone()
        {
            var label = new GameObject("Label").AddComponent<TextMesh>();
            label.transform.SetParent(_item.transform, false);
            label.transform.localPosition = Vector3.up;
            var tween = new ItemTween(_item.transform);
            tween.Apply(Hand);
            Assert.That(Vector3.Distance(_model.position, ModelAt(Hand, new Vector3(0f, 0.02f, 0.05f))), Is.LessThan(1e-4f));
            Assert.AreEqual(Vector3.up, label.transform.localPosition, "labels stay");
            Assert.AreEqual(RestPosition, _item.transform.position, "the root stays");
            tween.Restore();
            Assert.AreEqual(new Vector3(0f, 0.02f, 0.05f), _model.localPosition);
        }
    }
}
