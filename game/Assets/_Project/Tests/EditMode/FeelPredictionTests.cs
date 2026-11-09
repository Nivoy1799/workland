using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Art;
using Worktown.Game.Player;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 027b AC3 (and its review): the predicted look with the server's confirm and deny paths simulated in EditMode,
    /// through the same pure parts the game uses. PredictionBook / FeelPrediction judge the click from the synced state
    /// and the numbered answers (PickupInteractor), ItemLook picks the goal and the tween each frame and ItemTween draws
    /// the model (Pickup). The only glue here is what Pickup.ComputeFramePose and PickupInteractor.UpdatePrediction do:
    /// start the tween ItemLook asks for, end the prediction when the book resolves it, count a "nope". The networked
    /// root is checked every frame, also while the model is drawn, for an item with a model child and for one without.
    /// </summary>
    public class FeelPredictionTests
    {
        private const int Me = 1;
        private const int NoHolder = -1;
        private const float Dt = 1f / 60f;
        private static readonly Vector3 RestPosition = new Vector3(2f, 0.9f, 3f);
        private static readonly Pose Hand = new Pose(new Vector3(2.4f, 1.35f, 2.5f), Quaternion.Euler(0f, 30f, 0f));

        private readonly List<GameObject> _objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _objects)
                if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        private GameObject Item(bool withModel)
        {
            GameObject item = withModel ? new GameObject("Pan") : GameObject.CreatePrimitive(PrimitiveType.Cube);
            _objects.Add(item);
            item.transform.SetPositionAndRotation(RestPosition, Quaternion.identity);
            if (withModel)
            {
                var model = new GameObject("Model").transform;
                model.SetParent(item.transform, false);
                model.localPosition = new Vector3(0f, 0.02f, 0.05f);
            }
            else
            {
                item.transform.localScale = Vector3.one * 0.5f;
            }
            return item;
        }

        /// <summary>One item as one client sees it: the synced state is set by the test, everything else is the game's.</summary>
        private sealed class Client
        {
            public readonly Transform Root;
            public readonly ItemTween Tween;
            public readonly ItemLook Look = new ItemLook();
            public readonly PredictionBook<Client> Book = new PredictionBook<Client>();
            public readonly List<PredictionBook<Client>.Resolved> Resolved = new List<PredictionBook<Client>.Resolved>();
            public int Holder = NoHolder;
            public int Nopes;
            public PredictionPhase Phase = PredictionPhase.Pending;
            public float ResolvedAt = float.NaN;
            public bool RootStayed = true;
            public Pose Drawn;

            public Client(Transform root)
            {
                Root = root;
                Tween = new ItemTween(root);
                Look.Reset(NoHolder);
            }

            public ItemSnapshot State(Client c) => c != null ? new ItemSnapshot(true, c.Holder, c.Root.position, 0) : new ItemSnapshot(true, NoHolder, Vector3.zero, 0);

            private static bool HandPose(int holder, out ItemGoalKind kind, out Pose pose)
            {
                kind = ItemGoalKind.Hand;
                pose = Hand;
                return true;
            }

            /// <summary>The click: the request goes out, then the look is predicted in the hand (PickupInteractor order).</summary>
            public void ClickPickUp(float now)
            {
                int request = Book.Sent(now);
                Book.Add(this, Root, new FeelPrediction(PredictionKind.PickUp, Me, State(this), now, FeelSettings.PresetA().ConfirmTimeout), request);
                Look.PredictHeld(Me);
            }

            /// <summary>One frame: the book reads the state, then the item draws itself.</summary>
            public void Frame(float now)
            {
                Resolved.Clear();
                Book.Update(now, State, Resolved);
                foreach (PredictionBook<Client>.Resolved r in Resolved)
                {
                    if (r.Value != this) continue; // another item's look (it ends on its own)
                    Phase = r.Prediction.Phase;
                    ResolvedAt = now;
                    bool confirmed = Phase == PredictionPhase.Confirmed;
                    Look.EndPrediction(confirmed, now);
                    if (!confirmed) Nopes++;
                }
                Vector3 rootPosition = Root.position;
                Quaternion rootRotation = Root.rotation;
                ItemFrame frame = Look.Resolve(Holder, ItemTween.RootPose(Root), Tween.HasDrawn, Tween.Drawn, now, HandPose);
                FeelSettings feel = FeelSettings.PresetA();
                if (frame.Begin != TweenStyle.None)
                    Tween.Begin(frame.Begin, FeelRules.TweenSeconds(frame.Begin, frame.Kind, feel), FeelRules.TweenAmount(frame.Begin, frame.Kind, feel), now - Dt);
                Drawn = Tween.Evaluate(frame.Goal, now);
                Tween.Apply(Drawn);
                RootStayed &= Root.position == rootPosition && Root.rotation == rootRotation;
                Tween.Restore();
                RootStayed &= Root.position == rootPosition && Root.rotation == rootRotation;
            }
        }

        private static void Run(Client c, float seconds, System.Func<float, int> holder, System.Func<float, bool?> answer = null)
        {
            c.Frame(0f);
            c.ClickPickUp(0f);
            bool answered = false;
            for (float t = Dt; t <= seconds; t += Dt)
            {
                c.Holder = holder(t);
                if (answer != null && !answered && answer(t) is bool ok)
                {
                    answered = true;
                    c.Book.Answer(AnswerKind.Pickup, ok, t);
                }
                c.Frame(t);
            }
        }

        [Test]
        public void ConfirmedPickup_KeepsTheVisualInTheHand()
        {
            var c = new Client(Item(true).transform);
            Run(c, 1f, t => t >= 0.06f ? Me : NoHolder);
            Assert.AreEqual(PredictionPhase.Confirmed, c.Phase);
            Assert.AreEqual(0, c.Nopes);
            Assert.That(Vector3.Distance(c.Drawn.position, Hand.position), Is.LessThan(1e-4f));
            Assert.IsTrue(c.RootStayed, "the networked transform never moves on the client");
        }

        [Test]
        public void DeniedPickup_GoesBackWithinTimeoutPlusTween_AndPlaysNope()
        {
            var c = new Client(Item(true).transform);
            FeelSettings feel = FeelSettings.PresetA();
            float backAt = float.NaN;
            c.Frame(0f);
            c.ClickPickUp(0f);
            for (float t = Dt; t <= 1.5f; t += Dt)
            {
                c.Frame(t);
                if (c.Phase == PredictionPhase.Denied && float.IsNaN(backAt) && Vector3.Distance(c.Drawn.position, RestPosition) < 1e-4f) backAt = t;
            }
            Assert.AreEqual(PredictionPhase.Denied, c.Phase);
            Assert.AreEqual(1, c.Nopes, "one nope");
            Assert.IsFalse(float.IsNaN(backAt), "the model is back on its spot");
            // Two observations past the deadline (one frame of grace) plus the settle back.
            Assert.LessOrEqual(backAt, feel.ConfirmTimeout + 3f * Dt + feel.PlaceSeconds);
            Assert.IsTrue(c.RootStayed, "the networked transform never moves on the client");
        }

        [Test]
        public void ChildlessItem_GetsAVisualChild_AndItsRootNeverMoves()
        {
            GameObject cube = Item(false);
            Assert.IsTrue(ItemTween.EnsureVisualChild(cube.transform));
            Assert.IsFalse(cube.GetComponent<MeshRenderer>().enabled, "the root's own renderer is off");
            Transform visual = cube.transform.Find(ItemTween.VisualChildName);
            Assert.IsNotNull(visual);
            Assert.AreSame(cube.GetComponent<MeshFilter>().sharedMesh, visual.GetComponent<MeshFilter>().sharedMesh);
            Assert.IsFalse(ItemTween.EnsureVisualChild(cube.transform), "only once");

            var c = new Client(cube.transform);
            Run(c, 1f, t => t >= 0.06f ? Me : NoHolder);
            Assert.AreEqual(PredictionPhase.Confirmed, c.Phase);
            Assert.IsTrue(c.RootStayed, "the cube's root (collider, network transform) never moves");
        }

        [Test]
        public void ChildlessItemWithoutAVisualChild_IsNeverMoved()
        {
            GameObject cube = Item(false);
            var tween = new ItemTween(cube.transform);
            tween.Apply(Hand);
            Assert.AreEqual(RestPosition, cube.transform.position);
            tween.Restore();
            Assert.AreEqual(RestPosition, cube.transform.position);
        }

        [Test]
        public void RefusedPickup_GoesBackAtOnce_WithoutWaitingForTheTimeout()
        {
            var c = new Client(Item(true).transform);
            Run(c, 1f, t => NoHolder, t => t >= 0.08f ? false : (bool?)null);
            Assert.AreEqual(PredictionPhase.Denied, c.Phase);
            Assert.AreEqual(0.08f, c.ResolvedAt, 2f * Dt);
            Assert.AreEqual(1, c.Nopes);
        }

        [Test]
        public void LateConfirmation_WithinTheTimeout_NeverSnapsBack()
        {
            // A slow host or 100 ms of round trip: the holder arrives at 0.3 s, inside the 0.35 s timeout.
            var c = new Client(Item(true).transform);
            Run(c, 1f, t => t >= 0.3f ? Me : NoHolder);
            Assert.AreEqual(PredictionPhase.Confirmed, c.Phase);
            Assert.AreEqual(0, c.Nopes);
            Assert.That(Vector3.Distance(c.Drawn.position, Hand.position), Is.LessThan(1e-4f));
        }

        [Test]
        public void OkAnswer_CountsTheTimeoutFromThen()
        {
            var c = new Client(Item(true).transform);
            Run(c, 1f, t => t >= 0.6f ? Me : NoHolder, t => t >= 0.3f ? true : (bool?)null);
            Assert.AreEqual(PredictionPhase.Confirmed, c.Phase);
            Assert.AreEqual(0, c.Nopes);
        }

        [Test]
        public void OneLongFrame_PastTheDeadline_DoesNotDeny_WhenTheStateArrivesWithIt()
        {
            var p = new FeelPrediction(PredictionKind.PickUp, Me, new ItemSnapshot(true, NoHolder, RestPosition, 0), 0f, 0.35f);
            p.Observe(new ItemSnapshot(true, NoHolder, RestPosition, 0), 0.02f);
            p.Observe(new ItemSnapshot(true, NoHolder, RestPosition, 0), 0.52f); // a 0.5 s hitch
            Assert.AreEqual(PredictionPhase.Confirmed, p.Observe(new ItemSnapshot(true, Me, RestPosition, 0), 0.54f));
        }

        [Test]
        public void SomeoneElseGetsIt_DeniesAtOnce()
        {
            var c = new Client(Item(true).transform);
            Run(c, 1f, t => t >= 0.05f ? 7 : NoHolder);
            Assert.AreEqual(PredictionPhase.Denied, c.Phase);
            Assert.AreEqual(0.05f, c.ResolvedAt, 2f * Dt);
        }

        // ---- PredictionBook: answers and supersede (review of spec 027b) ----

        private static FeelPrediction Pending(PredictionKind kind, float now) =>
            new FeelPrediction(kind, Me, new ItemSnapshot(true, kind == PredictionKind.PickUp ? NoHolder : Me, Vector3.zero, 0), now, 0.35f);

        private sealed class Thing
        {
        }

        [Test]
        public void StaleRefusal_NeverDeniesANewerPrediction()
        {
            var book = new PredictionBook<Thing>();
            var a = new Thing();
            book.Sent(0f);                       // an older request (refused below)
            int newer = book.Sent(0.25f);        // the click we predicted
            FeelPrediction p = Pending(PredictionKind.PickUp, 0.25f);
            book.Add(a, a, p, newer);
            book.Answer(AnswerKind.Pickup, false, 0.3f); // the answer to the older request
            Assert.AreEqual(PredictionPhase.Pending, p.Phase);
            book.Answer(AnswerKind.Pickup, false, 0.32f); // its own answer
            Assert.AreEqual(PredictionPhase.Denied, p.Phase);
        }

        [Test]
        public void Answer_OfAnotherKind_DoesNotReachThePrediction()
        {
            var book = new PredictionBook<Thing>();
            var a = new Thing();
            FeelPrediction p = Pending(PredictionKind.PutInto, 0f);
            book.Add(a, a, p, book.Sent(0f));
            book.Answer(AnswerKind.Place, false, 0.1f);
            Assert.AreEqual(PredictionPhase.Pending, p.Phase);
            Assert.AreEqual(AnswerKind.Cook, PredictionBook<Thing>.AnswerFor(PredictionKind.PutInto));
            Assert.AreEqual(AnswerKind.Pickup, PredictionBook<Thing>.AnswerFor(PredictionKind.PickUp));
            Assert.AreEqual(AnswerKind.Drop, PredictionBook<Thing>.AnswerFor(PredictionKind.Drop));
            Assert.AreEqual(AnswerKind.Place, PredictionBook<Thing>.AnswerFor(PredictionKind.Place));
        }

        [Test]
        public void DroppedRequests_AreForgottenAfterAQuietSecond()
        {
            var book = new PredictionBook<Thing>();
            book.Sent(0f); // rate-limited by the server: never answered
            var a = new Thing();
            FeelPrediction p = Pending(PredictionKind.PickUp, 2f);
            book.Add(a, a, p, book.Sent(2f));
            book.Answer(AnswerKind.Pickup, false, 2.05f);
            Assert.AreEqual(PredictionPhase.Denied, p.Phase, "the numbering caught up, so its own answer reaches it");
        }

        [Test]
        public void SupersededPickup_KeepsItsLookUntilItResolves_NoBounce()
        {
            // Two pans clicked quickly: the first pan's look stays in the hand until its own state confirms it.
            var first = new Client(Item(true).transform);
            var book = first.Book;
            first.Frame(0f);
            first.ClickPickUp(0f);
            var second = new Thing();
            book.Add(null, second, Pending(PredictionKind.PickUp, 0.25f), book.Sent(0.25f));
            for (float t = Dt; t <= 0.6f; t += Dt)
            {
                first.Holder = t >= 0.3f ? Me : NoHolder;
                first.Frame(t);
                if (t > FeelSettings.PresetA().PickupSeconds + Dt)
                    Assert.That(Vector3.Distance(first.Drawn.position, Hand.position), Is.LessThan(1e-4f), "no bounce at " + t);
            }
            Assert.AreEqual(PredictionPhase.Confirmed, first.Phase);
            Assert.AreEqual(0, first.Nopes);
        }

        [Test]
        public void SamePredictedTwice_ReplacesTheOlderSilently()
        {
            var book = new PredictionBook<Thing>();
            var a = new Thing();
            book.Add(a, a, Pending(PredictionKind.PickUp, 0f), book.Sent(0f));
            book.Add(a, a, Pending(PredictionKind.Drop, 0.3f), book.Sent(0.3f));
            Assert.AreEqual(1, book.Count);
            var ended = new List<Thing>();
            book.Clear(ended);
            Assert.AreEqual(1, ended.Count);
            Assert.AreEqual(0, book.Count);
        }

        // ---- ItemLook: settling (review of spec 027b) ----

        [Test]
        public void ItemLook_PutDown_WaitsForTheRoot_ThenSettlesOnIt()
        {
            var look = new ItemLook();
            look.Reset(Me);
            Pose rest = new Pose(new Vector3(1f, 0.9f, 1f), Quaternion.identity);
            Pose hold = new Pose(new Vector3(0f, 2.5f, 0f), Quaternion.identity);
            ItemFrame f = look.Resolve(Me, hold, false, default, 0f, HandAt);
            Assert.AreEqual(ItemGoalKind.Hand, f.Kind);
            // Put down: the holder is gone, the root is still at the hold point (its update arrives later).
            f = look.Resolve(NoHolder, hold, true, Hand, 0.02f, HandAt);
            Assert.AreEqual(Hand.position, f.Goal.position, "waits where it was drawn");
            Assert.AreEqual(TweenStyle.Settle, f.Begin);
            f = look.Resolve(NoHolder, rest, true, Hand, 0.05f, HandAt); // the root jumps (interpolating)
            Assert.AreEqual(Hand.position, f.Goal.position, "never chases the root mid-way");
            f = look.Resolve(NoHolder, rest, true, Hand, 0.11f, HandAt); // quiet for 0.06 s: arrived
            Assert.AreEqual(rest.position, f.Goal.position);
            Assert.AreEqual(TweenStyle.Settle, f.Begin, "settles from where it was drawn");
            Assert.IsFalse(look.IsSettling);
        }

        [Test]
        public void ItemLook_PickedUpWhileSettling_ForgetsTheSettle()
        {
            var look = new ItemLook();
            look.Reset(Me);
            Pose hold = new Pose(new Vector3(0f, 2.5f, 0f), Quaternion.identity);
            look.Resolve(Me, hold, false, default, 0f, HandAt);
            look.Resolve(NoHolder, hold, true, Hand, 0.02f, HandAt);
            Assert.IsTrue(look.IsSettling);
            ItemFrame f = look.Resolve(5, hold, true, Hand, 0.04f, HandAt); // someone took it at once
            Assert.IsFalse(look.IsSettling, "held: nothing to settle on");
            Assert.AreEqual(ItemGoalKind.Hand, f.Kind);
            Assert.AreEqual(5, f.Holder);
        }

        private static bool HandAt(int holder, out ItemGoalKind kind, out Pose pose)
        {
            kind = ItemGoalKind.Hand;
            pose = Hand;
            return true;
        }

        // ---- ItemTween ----

        [Test]
        public void ItemTween_FirstFrameAfterTheClickAlreadyMoves_AndNeverJumps()
        {
            FeelSettings feel = FeelSettings.PresetA();
            GameObject item = Item(true);
            var tween = new ItemTween(item.transform);
            Pose rest = ItemTween.RootPose(item.transform);
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
            GameObject item = Item(true);
            var tween = new ItemTween(item.transform);
            Pose rest = ItemTween.RootPose(item.transform);
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
            GameObject item = Item(true);
            Transform model = item.transform.Find("Model");
            var label = new GameObject("Label").AddComponent<TextMesh>();
            label.transform.SetParent(item.transform, false);
            label.transform.localPosition = Vector3.up;
            var tween = new ItemTween(item.transform);
            tween.Apply(Hand);
            Assert.That(Vector3.Distance(model.position, Hand.position + Hand.rotation * new Vector3(0f, 0.02f, 0.05f)), Is.LessThan(1e-4f));
            Assert.AreEqual(Vector3.up, label.transform.localPosition, "labels stay");
            Assert.AreEqual(RestPosition, item.transform.position, "the root stays");
            tween.Restore();
            Assert.AreEqual(new Vector3(0f, 0.02f, 0.05f), model.localPosition);
        }
    }
}
