using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Art;
using Worktown.Game.Player;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 028b (docs/feel.md 1–2): a take from a stock slot shows at once. The verdict reads only the hands (confirmed when
    /// food is in them; a refusal or the timeout denies), and the copy of the slot's item flies on the pick-up arc into the
    /// hand, waits, settles back when denied, and hides the real item until it has landed when confirmed early.
    /// </summary>
    public class StockTakeLookTests
    {
        private GameObject _slot;
        private GameObject _real;
        private StockTakeLook _look;

        private static readonly Pose Hand = new Pose(new Vector3(0f, 1.5f, -1f), Quaternion.Euler(0f, 30f, 0f));

        [SetUp]
        public void SetUp()
        {
            _slot = new GameObject("Slot");
            _slot.transform.position = new Vector3(0f, 1.2f, 0.5f);
            GameObject item = GameObject.CreatePrimitive(PrimitiveType.Sphere); // with a collider, which the copy must not keep
            item.name = "Item";
            item.transform.SetParent(_slot.transform, false);
            item.transform.localPosition = new Vector3(0f, -0.05f, 0f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_look != null) Object.DestroyImmediate(_look.gameObject);
            if (_slot != null) Object.DestroyImmediate(_slot);
            if (_real != null) Object.DestroyImmediate(_real);
        }

        private StockTakeLook Begin(float now) => _look = StockTakeLook.Begin(_slot.transform, () => Hand, now, FeelSettings.PresetA());

        [Test]
        public void Judge_Take_ConfirmsWhenTheHandsHoldSomething()
        {
            var empty = ItemSnapshot.Gone;
            var holding = new ItemSnapshot(true, 3, Vector3.zero, 0);
            Assert.AreEqual(PredictionPhase.Pending, FeelRules.Judge(PredictionKind.Take, 3, empty, empty));
            Assert.AreEqual(PredictionPhase.Confirmed, FeelRules.Judge(PredictionKind.Take, 3, empty, holding));
            Assert.IsTrue(FeelRules.IsRelevantChange(empty, holding));
            Assert.AreEqual(AnswerKind.Cook, PredictionBook<object>.AnswerFor(PredictionKind.Take), "a station request is answered by ReportCook");

            // A refusal from the server denies it at once; without any answer the timeout does.
            var refused = new FeelPrediction(PredictionKind.Take, 3, empty, 0f, 0.35f);
            refused.NoteAnswer(false, 0.05f);
            Assert.AreEqual(PredictionPhase.Denied, refused.Phase);
            var silent = new FeelPrediction(PredictionKind.Take, 3, empty, 0f, 0.35f);
            silent.Observe(empty, 0.4f);
            Assert.AreEqual(PredictionPhase.Denied, silent.Observe(empty, 0.45f));
            Assert.IsTrue(silent.TimedOut);
        }

        [Test]
        public void Copy_HasNoCollider_StartsAtTheSlot_ArcsIntoTheHand()
        {
            StockTakeLook look = Begin(10f);
            Assert.IsNotNull(look);
            Assert.AreEqual(StockTakeLook.ObjectName, look.name);
            Assert.IsEmpty(look.GetComponentsInChildren<Collider>(true), "the copy never blocks a ray");
            Vector3 itemAt = _slot.transform.GetChild(0).position;
            Assert.AreEqual(0f, Vector3.Distance(itemAt, look.Evaluate(10f).position), 1e-4f, "drawn where the item stands, the same frame");
            float seconds = FeelSettings.PresetA().PickupSeconds;
            Vector3 middle = look.Evaluate(10f + seconds * 0.5f).position;
            Vector3 straight = Vector3.Lerp(itemAt, Hand.position, FeelRules.EaseOut(0.5f));
            Assert.Greater(middle.y, straight.y, "an arc, lifted in the middle");
            Assert.AreEqual(0f, Vector3.Distance(Hand.position, look.Evaluate(10f + seconds).position), 1e-4f, "in the hand");
            Assert.AreEqual(0f, Vector3.Distance(Hand.position, look.Evaluate(11f).position), 1e-4f, "waits in the hand");
            Assert.IsTrue(look.HasLanded(10f + seconds + 0.001f));
            Assert.IsFalse(look.HasLanded(10f + seconds * 0.5f));
            Assert.IsNotNull(_slot.transform.GetChild(0), "the slot keeps its item");
        }

        [Test]
        public void Denied_SettlesBackIntoTheSlot()
        {
            StockTakeLook look = Begin(0f);
            Vector3 itemAt = _slot.transform.GetChild(0).position;
            look.Deny(0.5f);
            Assert.IsTrue(look.IsReturning);
            Assert.AreEqual(0f, Vector3.Distance(Hand.position, look.Evaluate(0.5f).position), 1e-4f, "starts back from the hand");
            float back = FeelSettings.PresetA().PlaceSeconds;
            Assert.AreEqual(0f, Vector3.Distance(itemAt, look.Evaluate(0.5f + back).position), 1e-4f, "ends in the slot");
        }

        [Test]
        public void ConfirmedEarly_HidesTheRealItemUntilTheCopyLands_ThenShowsIt()
        {
            _real = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var renderer = _real.GetComponent<Renderer>();
            StockTakeLook look = Begin(0f);
            look.Confirm(_real, 0.05f);
            Assert.IsTrue(renderer.forceRenderingOff, "no second item in the hand while the copy is still flying");
            Assert.IsFalse(look.HasLanded(0.05f), "the copy keeps flying");
            look.Confirm(_real, 0.2f); // landed (what its LateUpdate does then)
            Assert.IsTrue(look == null, "the copy goes once it has landed");
            _look = null;
            Assert.IsFalse(renderer.forceRenderingOff, "the real item shows again when the copy goes");

            StockTakeLook late = Begin(0f);
            late.Confirm(_real, 1f);
            Assert.IsFalse(renderer.forceRenderingOff, "landed already: the copy goes at once and hides nothing");
            Assert.IsTrue(late == null, "destroyed");
            _look = null;
        }

        [Test]
        public void ASlotWithoutModels_GivesNoLook()
        {
            var empty = new GameObject("Empty slot");
            try { Assert.IsNull(StockTakeLook.Begin(empty.transform, () => Hand, 0f)); }
            finally { Object.DestroyImmediate(empty); }
            Assert.IsNull(StockTakeLook.Begin(_slot.transform, null, 0f));
        }
    }
}
