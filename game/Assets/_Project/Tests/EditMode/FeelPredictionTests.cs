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
            public int Request; // the number the click went out with (the server echoes it)
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
                Request = Book.Sent();
                Book.Add(this, Root, new FeelPrediction(PredictionKind.PickUp, Me, State(this), now, FeelSettings.PresetA().ConfirmTimeout), Request);
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
                    c.Book.Answer(c.Request, AnswerKind.Pickup, ok, t);
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
            int older = book.Sent();             // an older request (refused below)
            int newer = book.Sent();             // the click we predicted
            FeelPrediction p = Pending(PredictionKind.PickUp, 0.25f);
            book.Add(a, a, p, newer);
            book.Answer(older, AnswerKind.Pickup, false, 0.3f); // the answer to the older request
            Assert.AreEqual(PredictionPhase.Pending, p.Phase);
            book.Answer(newer, AnswerKind.Pickup, false, 0.32f); // its own answer
            Assert.AreEqual(PredictionPhase.Denied, p.Phase);
        }

        [Test]
        public void Answer_OfAnotherKind_DoesNotReachThePrediction()
        {
            var book = new PredictionBook<Thing>();
            var a = new Thing();
            FeelPrediction p = Pending(PredictionKind.PutInto, 0f);
            int request = book.Sent();
            book.Add(a, a, p, request);
            book.Answer(request, AnswerKind.Place, false, 0.1f);
            Assert.AreEqual(PredictionPhase.Pending, p.Phase);
            Assert.AreEqual(AnswerKind.Cook, PredictionBook<Thing>.AnswerFor(PredictionKind.PutInto));
            Assert.AreEqual(AnswerKind.Pickup, PredictionBook<Thing>.AnswerFor(PredictionKind.PickUp));
            Assert.AreEqual(AnswerKind.Drop, PredictionBook<Thing>.AnswerFor(PredictionKind.Drop));
            Assert.AreEqual(AnswerKind.Place, PredictionBook<Thing>.AnswerFor(PredictionKind.Place));
        }

        [Test]
        public void AnUnansweredRequest_NeverShiftsTheNextOnesAnswer()
        {
            // Was DroppedRequests_AreForgottenAfterAQuietSecond (spec 045: answers carry their request's number, nothing is counted).
            var book = new PredictionBook<Thing>();
            book.Sent(); // never answered
            var a = new Thing();
            FeelPrediction p = Pending(PredictionKind.PickUp, 2f);
            int own = book.Sent();
            book.Add(a, a, p, own);
            book.Answer(own, AnswerKind.Pickup, false, 2.05f);
            Assert.AreEqual(PredictionPhase.Denied, p.Phase, "its own answer reaches it");
        }

        [Test]
        public void UnansweredRequestInsideABurst_ThenARefusalOfTheNext_NeverDeniesTheClick()
        {
            // Second review: request n-1 is never answered (well under a second before the next ones), n is our pick-up and is
            // answered ok, n+1 is refused. Counted naively, n+1's refusal would land on n; with the echoed number it cannot.
            var book = new PredictionBook<Thing>();
            book.Sent();                                     // n-1: lost
            var a = new Thing();
            FeelPrediction p = Pending(PredictionKind.PickUp, 0.25f);
            int n = book.Sent();
            book.Add(a, a, p, n);                            // n
            book.Answer(n, AnswerKind.Pickup, true, 0.3f);   // n's ok
            int next = book.Sent();                          // n+1
            book.Answer(next, AnswerKind.Pickup, false, 0.55f); // n+1's refusal
            Assert.AreEqual(PredictionPhase.Pending, p.Phase, "n+1's refusal is not n's");
            Assert.AreEqual(PredictionPhase.Confirmed, p.Observe(new ItemSnapshot(true, Me, Vector3.zero, 0), 0.6f), "the state still decides");
        }

        [Test]
        public void SlowRoundTrip_ALateAnswerOfTheLastRequest_IsNotTheNewOnes()
        {
            // Second review: with 1.5 s of round trip a request sent 1.2 s after the last must not take the last one's answer.
            var book = new PredictionBook<Thing>();
            int first = book.Sent();                         // still in flight
            var a = new Thing();
            FeelPrediction p = Pending(PredictionKind.PickUp, 1.2f);
            int ours = book.Sent();
            book.Add(a, a, p, ours);
            book.Answer(first, AnswerKind.Pickup, false, 1.5f); // the first request's late refusal
            Assert.AreEqual(PredictionPhase.Pending, p.Phase, "it was not ours");
            book.Answer(ours, AnswerKind.Pickup, false, 2.7f);  // ours
            Assert.AreEqual(PredictionPhase.Denied, p.Phase);
        }

        [Test]
        public void LateRefusal_AfterAServerStallOverASecond_NeverBouncesANewerPrediction()
        {
            // Spec 045 AC2: the server stalls 1.5 s. A request sent at 0 is answered (refused) only at 1.6 s, after the player's next
            // click at 1.2 s, more than a second later (the old counting book took the quiet second as "all answered" and gave the late
            // refusal to the new click: a bounce).
            var book = new PredictionBook<Thing>();
            int stalled = book.Sent();
            var a = new Thing();
            FeelPrediction p = Pending(PredictionKind.PickUp, 1.2f);
            int newer = book.Sent();
            book.Add(a, a, p, newer);
            Assert.AreEqual(stalled, book.Answer(stalled, AnswerKind.Pickup, false, 1.6f), "the stalled request's late refusal");
            Assert.AreEqual(PredictionPhase.Pending, p.Phase, "the newer prediction keeps its look");
            Assert.AreEqual(newer, book.Answer(newer, AnswerKind.Pickup, true, 1.65f));
            Assert.AreEqual(PredictionPhase.Confirmed, p.Observe(new ItemSnapshot(true, Me, Vector3.zero, 0), 1.7f), "no bounce");
        }

        [Test]
        public void ARefusal_WithMoreRequestsInFlight_DeniesItsOwnPredictionAtOnce()
        {
            // Spec 045: the old book ignored a refusal while two were in flight (round trips over about 0.24 s), so the look waited for
            // its whole timeout; with the echo the refusal is unambiguous.
            var book = new PredictionBook<Thing>();
            var a = new Thing();
            var b = new Thing();
            FeelPrediction first = Pending(PredictionKind.PickUp, 0f);
            FeelPrediction second = Pending(PredictionKind.PickUp, 0.25f);
            int one = book.Sent();
            book.Add(a, a, first, one);
            int two = book.Sent();
            book.Add(b, b, second, two);
            book.Answer(one, AnswerKind.Pickup, false, 0.3f);
            Assert.AreEqual(PredictionPhase.Denied, first.Phase, "denied at its answer, not after the timeout");
            Assert.AreEqual(PredictionPhase.Pending, second.Phase);
        }

        [Test]
        public void LateAnswer_WithNothingInFlight_IsIgnored()
        {
            // Our request's own "ok" arrives, then a stray answer for the same number, then one for a number never sent: neither may
            // deny the click.
            var book = new PredictionBook<Thing>();
            var a = new Thing();
            FeelPrediction p = Pending(PredictionKind.PickUp, 0f);
            int request = book.Sent();
            book.Add(a, a, p, request);
            book.Answer(request, AnswerKind.Pickup, true, 0.1f);
            Assert.AreEqual(0, book.Answer(request, AnswerKind.Pickup, false, 0.2f), "already answered");
            Assert.AreEqual(0, book.Answer(request + 5, AnswerKind.Pickup, false, 0.25f), "never sent");
            Assert.AreEqual(PredictionPhase.Pending, p.Phase, "the ok restarted its timeout, the stray refusals are ignored");
        }

        [Test]
        public void RoundTrip_IsAssumedUntilTheFirstPing()
        {
            Assert.AreEqual(FeelRules.AssumedRoundTrip, FeelRules.RoundTrip(0f), 1e-6f);
            Assert.AreEqual(FeelRules.AssumedRoundTrip, FeelRules.RoundTrip(float.NaN), 1e-6f);
            Assert.AreEqual(0.04f, FeelRules.RoundTrip(0.04f), 1e-6f);
            Assert.GreaterOrEqual(FeelRules.AssumedRoundTrip, 0.1f, "conservative");
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
            book.Add(null, second, Pending(PredictionKind.PickUp, 0.25f), book.Sent());
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
            book.Add(a, a, Pending(PredictionKind.PickUp, 0f), book.Sent());
            book.Add(a, a, Pending(PredictionKind.Drop, 0.3f), book.Sent());
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
