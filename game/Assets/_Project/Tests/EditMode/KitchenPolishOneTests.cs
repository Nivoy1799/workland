using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Art;
using Worktown.Game.Audio;
using Worktown.Game.Cooking;
using Worktown.Game.Net.Rules;
using Worktown.Game.Player;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 029b AC2: the stock take fixes (items 1 to 4), the readable heat labels (5) and the fryer touches (6): the take is
    /// confirmed only by new food, nothing is offered while it is on its way, nobody pays without the item, the copy goes with
    /// its interactor, labels in player words, the idle oil hum and the basket's owed shakes.
    /// </summary>
    public class KitchenPolishOneTests
    {
        private const int Me = 3;

        // ---- 1. the take is confirmed by a new held food only ----

        [Test]
        public void Take_IsConfirmedByNewFood_NeverByAKnifeFromAPendingPickUp()
        {
            ItemSnapshot empty = FeelRules.HandsSnapshot(Me, -1, false);
            Assert.AreEqual(ItemSnapshot.Gone.Exists, empty.Exists, "nothing held");
            ItemSnapshot knife = FeelRules.HandsSnapshot(Me, 21, false);
            ItemSnapshot cucumber = FeelRules.HandsSnapshot(Me, 22, true);
            Assert.AreEqual(PredictionPhase.Pending, FeelRules.Judge(PredictionKind.Take, Me, empty, knife), "the knife arrived: no take yet");
            Assert.AreEqual(PredictionPhase.Confirmed, FeelRules.Judge(PredictionKind.Take, Me, empty, cucumber));
            Assert.AreEqual(PredictionPhase.Confirmed, FeelRules.Judge(PredictionKind.Take, Me, knife, cucumber), "new food after the knife");
            Assert.AreEqual(PredictionPhase.Pending, FeelRules.Judge(PredictionKind.Take, Me, cucumber, cucumber),
                "the food that was already in the hands is not the taken one");
            Assert.IsTrue(FeelRules.IsRelevantChange(knife, cucumber), "a new held object restarts the timeout");

            var take = new FeelPrediction(PredictionKind.Take, Me, empty, 0f, 0.35f);
            take.Observe(knife, 0.1f);
            Assert.AreEqual(PredictionPhase.Pending, take.Phase);
            take.NoteAnswer(false, 0.12f); // the server: hands full (the knife came first)
            Assert.AreEqual(PredictionPhase.Denied, take.Phase, "the copy goes back; the knife stays the knife");
        }

        [Test]
        public void APendingPickUp_CountsAsHolding_SoNoStockTakeIsOffered()
        {
            // The interactor describes a pending pick-up as held (the knife); with a knife the stock offers nothing.
            AimTarget slot = AimTarget.Crate("cucumber", 50);
            Assert.AreEqual(InteractionAction.Buy, InteractionRules.ActionFor(Held.Nothing, slot));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Knife, slot));
        }

        // ---- 2. nothing is offered while a take is on its way ----

        [Test]
        public void WhileATakeIsPending_AndNothingIsReallyHeld_NoActionIsOffered()
        {
            Assert.IsTrue(InteractionRules.WaitsForTake(true, false));
            Assert.IsFalse(InteractionRules.WaitsForTake(true, true), "the food is there: the rules apply again");
            Assert.IsFalse(InteractionRules.WaitsForTake(false, false));
            // What the take's pending hands would offer without the rule (the reason for it): place, put in, sell.
            Held pending = Held.Food("cucumber", false);
            Assert.AreEqual(InteractionAction.Place, InteractionRules.ActionFor(pending, AimTarget.Spot(0)));
            Assert.AreEqual(InteractionAction.PutInto, InteractionRules.ActionFor(pending, AimTarget.Cookware(VesselKind.Board, 0, null)));
            Assert.AreEqual(InteractionAction.Sell, InteractionRules.ActionFor(pending, AimTarget.OnStation(StationKind.Counter)));
            Assert.IsNull(InteractionRules.PromptFor(Held.Nothing, AimTarget.Nothing), "the cleared aim shows no prompt");
        }

        // ---- 3. never charged without the item ----

        private sealed class FakeTake : IStockTakeSteps
        {
            public CookOutcome CheckAnswer = CookOutcome.Ok;
            public CookOutcome CreateAnswer = CookOutcome.Ok;
            public bool CanHand = true;
            public CookOutcome PayAnswer = CookOutcome.Ok;
            public bool HandAnswer = true;
            public int Checks, Creates, HandChecks, Pays, HandOvers, Refunds;

            public CookOutcome Check() { Checks++; return CheckAnswer; }
            public CookOutcome Create() { Creates++; return CreateAnswer; }
            public bool CanHandOver() { HandChecks++; return CanHand; }
            public CookOutcome Pay() { Pays++; return PayAnswer; }
            public bool HandOver() { HandOvers++; return HandAnswer; }
            public void Refund() { Refunds++; } // spec 034
        }

        [Test]
        public void StockTake_PaysExactlyOnceOnSuccess_AndNeverOnARefusal()
        {
            var ok = new FakeTake();
            Assert.AreEqual(CookOutcome.Ok, StockTakeSequence.Run(ok));
            Assert.AreEqual(1, ok.Pays, "paid once");
            Assert.AreEqual(1, ok.HandOvers);
            Assert.AreEqual(1, ok.HandChecks);

            foreach (CookOutcome refusal in new[] { CookOutcome.TooFar, CookOutcome.HandsFull, CookOutcome.InvalidOption, CookOutcome.UnknownStation })
            {
                var refused = new FakeTake { CheckAnswer = refusal };
                Assert.AreEqual(refusal, StockTakeSequence.Run(refused));
                Assert.AreEqual(0, refused.Pays, refusal + ": nothing charged");
                Assert.AreEqual(0, refused.HandOvers);
            }
            var noItem = new FakeTake { CreateAnswer = CookOutcome.SimRefused };
            Assert.AreEqual(CookOutcome.SimRefused, StockTakeSequence.Run(noItem));
            Assert.AreEqual(0, noItem.Pays, "no sim item: nothing charged");

            var noHands = new FakeTake { CanHand = false };
            Assert.AreEqual(CookOutcome.SimRefused, StockTakeSequence.Run(noHands));
            Assert.AreEqual(0, noHands.Pays, "the hand-over could not happen: nothing charged");
            Assert.AreEqual(0, noHands.HandOvers);

            var broke = new FakeTake { PayAnswer = CookOutcome.NotEnoughMoney };
            Assert.AreEqual(CookOutcome.NotEnoughMoney, StockTakeSequence.Run(broke));
            Assert.AreEqual(1, broke.Pays);
            Assert.AreEqual(0, broke.HandOvers, "no money, no potato");
        }

        [Test]
        public void TheRegistry_SaysWhetherFreshFoodCouldBeHanded_WithoutClaimingIt()
        {
            var registry = new PickupRegistry();
            Assert.IsTrue(registry.CanClaim(7));
            Assert.IsFalse(registry.CanClaim(-1), "no client");
            Assert.IsTrue(registry.CanClaim(7), "asking changes nothing");
            Assert.IsTrue(registry.TryClaim(40, 7));
            Assert.IsFalse(registry.CanClaim(7), "full hands");
            Assert.IsTrue(registry.TryRelease(40, 7));
            Assert.IsTrue(registry.CanClaim(7));
        }

        // ---- 4. the copy goes with its interactor ----

        [Test]
        public void StockTakeLook_GoesWhenItsInteractorIsGone_WithoutAskingTheDeadHand()
        {
            var slot = new GameObject("Slot");
            var owner = new GameObject("Interactor");
            GameObject item = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            item.transform.SetParent(slot.transform, false);
            StockTakeLook look = null;
            try
            {
                int asked = 0;
                Transform hand = owner.transform;
                look = StockTakeLook.Begin(slot.transform, () =>
                {
                    asked++;
                    return new Pose(hand.position, hand.rotation); // throws once the owner is destroyed
                }, 0f, FeelSettings.PresetA(), owner);
                Assert.IsNotNull(look);
                Assert.IsTrue(look.OwnerAlive);
                look.Step(0.05f);
                Assert.IsFalse(look == null, "flying while its interactor is there");
                Object.DestroyImmediate(owner);
                int before = asked;
                Assert.IsFalse(look.OwnerAlive);
                Assert.DoesNotThrow(() => look.Evaluate(0.06f), "no MissingReferenceException");
                look.Step(0.07f);
                Assert.AreEqual(before, asked, "the destroyed hand is never asked");
                Assert.IsTrue(look == null, "the copy went with its interactor");
                look = null;
            }
            finally
            {
                if (look != null) Object.DestroyImmediate(look.gameObject);
                if (owner != null) Object.DestroyImmediate(owner);
                Object.DestroyImmediate(slot);
            }
        }

        [Test]
        public void StockTakeLook_RemembersTheFrameItWasFirstDrawn()
        {
            var slot = new GameObject("Slot");
            GameObject item = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            item.transform.SetParent(slot.transform, false);
            StockTakeLook look = StockTakeLook.Begin(slot.transform, () => new Pose(Vector3.up, Quaternion.identity), 0f, FeelSettings.PresetA());
            try
            {
                // Spec 030 (029b nit): Begin only places the copy; the frame is taken when a frame really draws it (the real
                // frame path: Step is what LateUpdate runs; the render hook does the same), so the check is no tautology.
                Assert.AreEqual(-1, look.DrawnFrame, "not drawn yet: nothing has rendered it");
                look.Step(0.01f);
                Assert.AreEqual(Time.frameCount, look.DrawnFrame, "drawn in the click's frame (measured, not assumed)");
                int first = look.DrawnFrame;
                look.Step(0.02f);
                Assert.AreEqual(first, look.DrawnFrame, "the first drawn frame is kept");
            }
            finally
            {
                if (look != null) Object.DestroyImmediate(look.gameObject);
                Object.DestroyImmediate(slot);
            }
        }

        // ---- 5. readable labels ----

        [Test]
        public void HeatLabels_AreThePreviewName_NeverAZoneWordOfTheirOwn()
        {
            // Spec 030 (orchestrator decision): the label is exactly the name a take would give now; the ring shows the zone.
            Assert.AreEqual("golden fries", HeatLabelRules.Label("golden fries"));
            Assert.AreEqual("potato sticks", HeatLabelRules.Label("potato sticks"), "raw, never heated: the sim name");
            Assert.AreEqual(string.Empty, HeatLabelRules.Label(null));
            Assert.AreEqual("half-grilled seitan strips · side 2 down", HeatLabelRules.GriddleLabel("half-grilled seitan strips", 1));
            Assert.AreEqual("seitan strips · side 1 down", HeatLabelRules.GriddleLabel("seitan strips", 0));
        }

        [Test]
        public void TheBasketLabel_GoesPaleGoldenDark_AndOffTheHeatTheSimNameStays()
        {
            KitchenState kitchen = KitchenState.CreateDemo();
            const int basket = 70, food = 42, fryer = 6;
            const uint start = HeatTestKit.StartTick;
            const double rate = HeatTestKit.TickRate;
            kitchen.RegisterBasket(basket, fryer, 0);
            kitchen.TryCreateIngredient("potato", out Worktown.Sim.Cooking.Item potato);
            kitchen.Register(food, potato);
            HeatTestKit.Cut(kitchen, food, 3);
            kitchen.TryPutInto(food, basket, VesselKind.Basket, start);
            kitchen.TryToggleFryer(fryer, start);
            kitchen.TryLowerBasket(basket, start);
            uint Real(double s) => start + (uint)System.Math.Round(s * rate);
            Assert.AreEqual("pale fries", kitchen.DescribeHeat(basket, Real(10), rate));
            Assert.AreEqual("golden fries", kitchen.DescribeHeat(basket, Real(28), rate));
            Assert.AreEqual("dark fries", kitchen.DescribeHeat(basket, Real(42), rate));
            kitchen.TryRaiseBasket(basket, Real(42), rate);
            // Spec 030: while it drains the label says what a take gives now (greasy), so taking it never renames it.
            kitchen.TryPreviewTakeOut(basket, VesselKind.Basket, Real(43), rate, out Worktown.Sim.Cooking.Item dripping);
            Assert.AreEqual(KitchenState.NameOf(dripping), kitchen.DescribeHeat(basket, Real(43), rate));
            Assert.AreEqual("greasy dark fries", kitchen.DescribeHeat(basket, Real(43), rate));
            kitchen.TryPreviewTakeOut(basket, VesselKind.Basket, Real(45), rate, out Worktown.Sim.Cooking.Item fries);
            Assert.AreEqual("dark fries", KitchenState.NameOf(fries), "off the heat: the sim's name");
        }

        // ---- 6. fryer touches ----

        [Test]
        public void TheIdleOilHum_PlaysOnlyWhileTheFryerIsOn_WithBothBasketsUp()
        {
            Assert.AreEqual(0f, SoundRules.FryerIdleHum(FryerRules.FryerFlags(false, false, false)), "off");
            Assert.AreEqual(SoundRules.FryerIdleLevel, SoundRules.FryerIdleHum(FryerRules.FryerFlags(true, false, false)), "on, both up");
            Assert.AreEqual(0f, SoundRules.FryerIdleHum(FryerRules.FryerFlags(true, true, false)), "a basket fries: its own loop");
            Assert.AreEqual(0f, SoundRules.FryerIdleHum(FryerRules.FryerFlags(true, false, true)));
            Assert.Less(SoundRules.FryerIdleLevel, SoundRules.LoopLevel(true, HeatZone.Raw), "quieter than a basket frying");
            Assert.Less(SoundRules.FryerIdlePitch, SoundRules.LoopPitchOf(VesselKind.Basket, HeatZone.Raw), "lower than a basket frying");
        }

        [Test]
        public void TheDrainDrops_AreBiggerAndDenserThanIn029()
        {
            Assert.GreaterOrEqual(FryerArtRules.DripSizeMin, 0.02f);
            Assert.Greater(FryerArtRules.DripSizeMax, FryerArtRules.DripSizeMin);
            Assert.GreaterOrEqual(FryerArtRules.DripRate, 30f);
        }

        [Test]
        public void TipIntoAPlate_IsTheBasketsTake_WhenHoldingAPlateOrBowlWithRoom()
        {
            byte up = FryerRules.Mode(BasketState.Raised, 0), down = FryerRules.Mode(BasketState.Lowered, 0);
            byte dripping = FryerRules.Mode(BasketState.Draining, 0);
            AimTarget raised = AimTarget.Cookware(VesselKind.Basket, 1, "golden fries", up);
            Held plate = Held.Cookware(VesselKind.Plate, 0, null);
            Assert.AreEqual(InteractionAction.Take, InteractionRules.ActionFor(plate, raised));
            Assert.AreEqual("Click · tip golden fries on the plate", InteractionRules.PromptFor(plate, raised));
            Assert.AreEqual("Click · tip golden fries on the plate (still dripping)",
                InteractionRules.PromptFor(plate, AimTarget.Cookware(VesselKind.Basket, 1, "golden fries", dripping)));
            Assert.AreEqual(InteractionAction.Take, InteractionRules.ActionFor(Held.Cookware(VesselKind.Bowl, 0, null), raised));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(plate, AimTarget.Cookware(VesselKind.Basket, 0, null, up)), "empty basket");
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(plate, AimTarget.Cookware(VesselKind.Basket, 1, "fries", down)), "in the oil");
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Cookware(VesselKind.Pan, 0, null), raised), "a pan takes nothing");
            Held fullPlate = Held.Cookware(VesselKind.Plate, VesselRules.Capacity(VesselKind.Plate), "a dish");
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(fullPlate, raised), "no room");
            Assert.IsTrue(VesselRules.TipsFromBasket(VesselKind.Plate) && VesselRules.TipsFromBasket(VesselKind.Bowl));
            Assert.IsFalse(VesselRules.TipsFromBasket(VesselKind.Pan));
        }

        // ---- 9. the basket's owed shakes and a fresh view ----

        [Test]
        public void OwedShakes_SkipTheOwnStep_ExpireAfterTheTimeout_AndClearOnAStateChange()
        {
            var ledger = new BasketShakeLedger();
            Assert.AreEqual(0, ledger.Observe(2, 0f), "the first counter is a baseline");
            ledger.NoteOwn(1f, 0.3f);
            Assert.AreEqual(0, ledger.Observe(3, 1.1f), "my own shake rattled at the click already");
            Assert.AreEqual(0, ledger.Owed);

            ledger.NoteOwn(2f, 0.3f); // refused by the server: never arrives
            Assert.AreEqual(1, ledger.Owed);
            Assert.AreEqual(1, ledger.Observe(0, 2.5f), "after the timeout another player's shake plays");
            Assert.AreEqual(0, ledger.Owed);

            ledger.NoteOwn(3f, 1f);
            ledger.StateChanged(); // the basket came up: the owed shake can no longer come
            Assert.AreEqual(1, ledger.Observe(1, 3.1f), "another player's shake is not swallowed");
        }

        [Test]
        public void AResetLedger_PlaysNoStaleRattles()
        {
            var ledger = new BasketShakeLedger();
            ledger.Observe(1, 0f);
            ledger.NoteOwn(0f, 5f);
            ledger.Reset(); // the view was enabled again (a respawn)
            Assert.AreEqual(0, ledger.Owed);
            Assert.AreEqual(0, ledger.Observe(3, 1f), "the counter after a respawn is a baseline");
            Assert.AreEqual(1, ledger.Observe(0, 1.1f), "the next real shake plays");
        }
    }
}
