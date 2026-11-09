using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Art;
using Worktown.Game.Cooking;
using Worktown.Game.Net.Rules;
using Worktown.Game.Npc;
using Worktown.Game.Player;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 029 AC3: the fryer's server rules through KitchenState (lower and raise, capacity, the shake window, the drain
    /// penalty, the zones and the ring bytes), plus the pure fryer rules the clients use (prompts, bytes, poses).
    /// </summary>
    public class FryerTests
    {
        private const int Fryer = 6;
        private const int Basket0 = 70;
        private const int Basket1 = 71;
        private const int Food = 42;
        private const uint Start = HeatTestKit.StartTick;
        private const double Rate = HeatTestKit.TickRate;

        private static uint Real(double seconds) => (uint)System.Math.Round(seconds * Rate);

        private static KitchenState Kitchen()
        {
            var kitchen = KitchenState.CreateDemo();
            kitchen.RegisterBasket(Basket0, Fryer, 0);
            kitchen.RegisterBasket(Basket1, Fryer, 1);
            return kitchen;
        }

        private static void Sticks(KitchenState kitchen, int id = Food)
        {
            kitchen.TryCreateIngredient("potato", out Item potato);
            kitchen.Register(id, potato);
            HeatTestKit.Cut(kitchen, id, 3);
        }

        private static void Falafel(KitchenState kitchen, int id = Food)
        {
            kitchen.TryCreateIngredient("falafel_mix", out Item mix);
            kitchen.Register(id, mix);
        }

        /// <summary>Sticks in basket 0, the fryer on, the basket lowered at <see cref="Start"/>.</summary>
        private static KitchenState Frying(System.Action<KitchenState, int> food = null)
        {
            KitchenState kitchen = Kitchen();
            (food ?? ((k, id) => Sticks(k, id)))(kitchen, Food);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutInto(Food, Basket0, VesselKind.Basket, Start));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryToggleFryer(Fryer, Start));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryLowerBasket(Basket0, Start));
            Assert.AreEqual(BasketState.Lowered, kitchen.BasketStateOf(Basket0, Start));
            return kitchen;
        }

        // ---- lower and raise ----

        [Test]
        public void Lowering_NeedsFood_AndTheFryerOn_RaisingIsAlwaysAllowed()
        {
            KitchenState kitchen = Kitchen();
            Assert.AreEqual(CookOutcome.StationEmpty, kitchen.TryLowerBasket(Basket0, Start), "an empty basket stays up");
            Sticks(kitchen);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutInto(Food, Basket0, VesselKind.Basket, Start));
            Assert.AreEqual(CookOutcome.FryerOff, kitchen.TryLowerBasket(Basket0, Start), "cold oil");
            kitchen.TryToggleFryer(Fryer, Start);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryLowerBasket(Basket0, Start));
            Assert.AreEqual(BasketState.Lowered, kitchen.BasketStateOf(Basket0, Start));
            kitchen.TryToggleFryer(Fryer, Start + 10); // off with the basket down: still down, cooking paused
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryRaiseBasket(Basket0, Start + 20, Rate), "raising works with the fryer off");
            Assert.AreEqual(BasketState.Draining, kitchen.BasketStateOf(Basket0, Start + 20));
            Assert.AreEqual(CookOutcome.NotAVessel, kitchen.TryRaiseBasket(999, Start, Rate));
        }

        [Test]
        public void TheFood_FriesOnlyWhileLoweredInHotOil()
        {
            KitchenState kitchen = Frying();
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryRaiseBasket(Basket0, Start + Real(10), Rate)); // up after 10 s
            kitchen.TryGetHeat(Basket0, 0, out HeatSession session);
            uint cooked = session.CookedTicks(Start + Real(40));
            Assert.AreEqual(Real(10), cooked, "nothing fries on the hook");
            kitchen.TryLowerBasket(Basket0, Start + Real(40)); // down again
            kitchen.TryToggleFryer(Fryer, Start + Real(45));          // fryer off: paused
            Assert.AreEqual(Real(15), session.CookedTicks(Start + Real(90)));
        }

        [Test]
        public void TheDrain_IsAServerTimer_OfTwoSeconds()
        {
            KitchenState kitchen = Frying();
            uint up = Start + Real(28);
            kitchen.TryRaiseBasket(Basket0, up, Rate);
            Assert.AreEqual(BasketState.Draining, kitchen.BasketStateOf(Basket0, up + Real(1.9)));
            Assert.IsFalse(kitchen.SettleDrain(Basket0, up + Real(1.9)));
            Assert.AreEqual(BasketState.Raised, kitchen.BasketStateOf(Basket0, up + Real(FryerRules.DrainSeconds)));
            Assert.IsTrue(kitchen.SettleDrain(Basket0, up + Real(2)));
            Assert.IsFalse(kitchen.SettleDrain(Basket0, up + Real(3)), "settled once");
            Assert.AreEqual(2f, FryerRules.DrainSeconds);
            Assert.AreEqual(60u, FryerRules.DrainTicks(30));
        }

        // ---- capacity ----

        [Test]
        public void OnePortionPerBasket_NothingGoesInOrOutWhileItIsInTheOil()
        {
            KitchenState kitchen = Frying();
            Sticks(kitchen, 43);
            Assert.AreEqual(CookOutcome.BasketLowered, kitchen.TryPutInto(43, Basket0, VesselKind.Basket, Start + 5));
            Assert.AreEqual(CookOutcome.BasketLowered, kitchen.TryPreviewTakeOut(Basket0, VesselKind.Basket, Start + 5, Rate, out _));
            kitchen.TryRaiseBasket(Basket0, Start + 10, Rate);
            Assert.AreEqual(CookOutcome.VesselFull, kitchen.TryPutInto(43, Basket0, VesselKind.Basket, Start + 10), "one portion");
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutInto(43, Basket1, VesselKind.Basket, Start + 10), "the other basket has room");
            Assert.AreEqual(1, VesselRules.Capacity(VesselKind.Basket));
            Assert.IsTrue(kitchen.TryGet(43, out _) == false, "the food left the hands");
        }

        // ---- the shake window ----

        [Test]
        public void AShake_InTheGoodWindow_GivesTheBonusOnce()
        {
            KitchenState kitchen = Frying();
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryShake(Basket0, Start + Real(10), Rate), "a shake rattles any time");
            Assert.IsFalse(kitchen.BasketShaken(Basket0), "10 s: still cooking, no bonus");
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryShake(Basket0, Start + Real(27), Rate));
            Assert.IsTrue(kitchen.BasketShaken(Basket0), "27 s: in the good window");
            Assert.AreEqual(2, FryerRules.ShakesOf(kitchen.BasketMode(Basket0, Start + Real(27))));
            kitchen.TryShake(Basket0, Start + Real(29), Rate);
            kitchen.TryRaiseBasket(Basket0, Start + Real(30), Rate);
            uint drained = Start + Real(33);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewTakeOut(Basket0, VesselKind.Basket, drained, Rate, out Item shaken));
            Assert.AreEqual(1f + DeepFry.ShakeBonus, shaken.QualityFactor, 1e-5f, "one bonus, however often it was shaken");

            KitchenState late = Frying();
            late.TryShake(Basket0, Start + Real(40), Rate); // overdone: too late
            Assert.IsFalse(late.BasketShaken(Basket0));
            Assert.IsTrue(FryerRules.ShakeCounts(HeatZone.Good) && FryerRules.ShakeCounts(HeatZone.Perfect));
            Assert.IsFalse(FryerRules.ShakeCounts(HeatZone.Cooking) || FryerRules.ShakeCounts(HeatZone.Overdone));
        }

        [Test]
        public void AShake_NeedsALoweredBasketWithFood_AndTheNextFryStartsFresh()
        {
            KitchenState kitchen = Kitchen();
            Assert.AreEqual(CookOutcome.NotLowered, kitchen.TryShake(Basket0, Start, Rate));
            kitchen = Frying();
            kitchen.TryShake(Basket0, Start + Real(26), Rate);
            kitchen.TryRaiseBasket(Basket0, Start + Real(30), Rate);
            Assert.AreEqual(CookOutcome.NotLowered, kitchen.TryShake(Basket0, Start + Real(31), Rate), "on the hook");
            kitchen.ClearContents(Basket0, VesselKind.Basket);
            Assert.IsFalse(kitchen.BasketShaken(Basket0), "taken out: the next portion has no bonus yet");
        }

        [Test]
        public void ReloadingFries_AndShakingAgain_NeverStacksTheBonus()
        {
            // Spec 029b 9.0: the fries come out, go back into the basket, are shaken again: still one bonus (x1.1).
            KitchenState kitchen = Frying();
            kitchen.TryShake(Basket0, Start + Real(27), Rate);
            kitchen.TryRaiseBasket(Basket0, Start + Real(28), Rate);
            uint t = Start + Real(31);
            for (int round = 0; round < 3; round++)
            {
                Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewTakeOut(Basket0, VesselKind.Basket, t, Rate, out Item fries));
                kitchen.ClearContents(Basket0, VesselKind.Basket);
                Assert.AreEqual(1f + DeepFry.ShakeBonus, fries.QualityFactor, 1e-5f, "round " + round + ": one bonus");
                int id = 100 + round;
                kitchen.Register(id, fries);
                Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutInto(id, Basket0, VesselKind.Basket, t), "the fries go back in");
                Assert.AreEqual(CookOutcome.Ok, kitchen.TryLowerBasket(Basket0, t));
                Assert.AreEqual(CookOutcome.Ok, kitchen.TryShake(Basket0, t + Real(1.2), Rate)); // past the op minimum (1 s): in the window
                Assert.IsTrue(kitchen.BasketShaken(Basket0), "re-loaded fries are in the window at once: the shake counts again");
                kitchen.TryRaiseBasket(Basket0, t + Real(2), Rate); // 12 sim s: longer than the op minimum
                t += Real(5);
            }
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewTakeOut(Basket0, VesselKind.Basket, t, Rate, out Item last));
            Assert.AreEqual(1f + DeepFry.ShakeBonus, last.QualityFactor, 1e-5f, "still x1.1 after three re-fries");
        }

        // ---- spec 029b: the basket button is an intent, not a toggle ----

        [Test]
        public void TheBasketIntent_LowerOrRaise_RefusesANoOp_SoTwoPlayersNeverCancel()
        {
            KitchenState kitchen = Kitchen();
            Sticks(kitchen);
            kitchen.TryPutInto(Food, Basket0, VesselKind.Basket, Start);
            kitchen.TryToggleFryer(Fryer, Start);
            Assert.AreEqual(CookOutcome.NotLowered, kitchen.TryRaiseBasket(Basket0, Start, Rate), "raising a raised basket does nothing");
            // Two players press "lower" at once: the first lowers it, the second is refused, the basket stays down.
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryLowerBasket(Basket0, Start + 1));
            Assert.AreEqual(CookOutcome.BasketLowered, kitchen.TryLowerBasket(Basket0, Start + 2));
            Assert.AreEqual(BasketState.Lowered, kitchen.BasketStateOf(Basket0, Start + 3));
            // Two players press "raise" at once: the first raises it, the second is refused, it drains.
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryRaiseBasket(Basket0, Start + Real(10), Rate));
            Assert.AreEqual(CookOutcome.NotLowered, kitchen.TryRaiseBasket(Basket0, Start + Real(10) + 1, Rate));
            Assert.AreEqual(BasketState.Draining, kitchen.BasketStateOf(Basket0, Start + Real(11)));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryLowerBasket(Basket0, Start + Real(11)), "a draining basket may go back in");
            Assert.AreEqual(CookOutcome.NotAVessel, kitchen.TryLowerBasket(999, Start));
        }

        [Test]
        public void TheIntentOptions_AreSeparateValues_TheButtonsKeepTheirSceneOption()
        {
            Assert.AreEqual(2, FryerRules.LowerOption(0));
            Assert.AreEqual(3, FryerRules.LowerOption(1));
            Assert.AreEqual(4, FryerRules.RaiseOption(0));
            Assert.AreEqual(5, FryerRules.RaiseOption(1));
            Assert.AreEqual(FryerRules.LowerOption(1), FryerRules.BasketOption(1), "the scene button keeps its option");
            Assert.AreEqual(FryerRules.RaiseOption(1), FryerRules.IntentOption(1, true), "seen in the oil: raise");
            Assert.AreEqual(FryerRules.LowerOption(1), FryerRules.IntentOption(1, false), "seen up: lower");
            for (int b = 0; b < FryerRules.BasketsPerFryer; b++)
            {
                Assert.IsTrue(FryerRules.TryParseBasketOption(FryerRules.LowerOption(b), out int lb, out bool lower) && lb == b && lower);
                Assert.IsTrue(FryerRules.TryParseBasketOption(FryerRules.RaiseOption(b), out int rb, out bool raise) && rb == b && !raise);
            }
            foreach (int bad in new[] { -1, 0, FryerRules.PowerOption, 6, 99 })
                Assert.IsFalse(FryerRules.TryParseBasketOption(bad, out _, out _), "option " + bad);
            Assert.AreEqual("Click · lower the basket", InteractionRules.FryerButtonPrompt(FryerRules.BasketOption(0), FryerRules.FryerFlags(true, false, false)));
        }

        // ---- spec 029b: the use request on a basket, the answer kind, the tip ----

        [Test]
        public void TheUseRequest_OnABasket_NeedsNoPickup_ReachIsToThePivot_AShakeNeedsEmptyHands()
        {
            Vector3 pivot = new Vector3(6.44f, 0.9f, 14.3f);
            Vector3 cook = new Vector3(6.44f, 0f, 12.95f);
            Assert.AreEqual(CookOutcome.Ok, VesselRules.CheckUseTarget(VesselKind.Basket, false, false, cook, pivot), "no Pickup, fixed to its fryer");
            Assert.AreEqual(CookOutcome.NotAVessel, VesselRules.CheckUseTarget(VesselKind.Pan, false, false, cook, pivot), "a pan needs its Pickup");
            Assert.AreEqual(CookOutcome.NotAVessel, VesselRules.CheckUseTarget(VesselKind.Pan, true, true, cook, pivot), "a held pan");
            Assert.AreEqual(CookOutcome.Ok, VesselRules.CheckUseTarget(VesselKind.Pan, true, false, cook, pivot));
            Vector3 far = pivot + new Vector3(0f, 0f, -(PickupRules.MaxReach + 0.1f));
            Assert.AreEqual(CookOutcome.TooFar, VesselRules.CheckUseTarget(VesselKind.Basket, false, false, far, pivot), "reach to the pivot");
            Assert.AreEqual(CookOutcome.HandsFull, VesselRules.CheckShake(VesselKind.Basket, true));
            Assert.AreEqual(CookOutcome.Ok, VesselRules.CheckShake(VesselKind.Basket, false));
            Assert.AreEqual(CookOutcome.InvalidOption, VesselRules.CheckShake(VesselKind.Pan, false), "only a basket shakes");
            Assert.AreEqual(AnswerKind.Cook, PredictionBook<object>.AnswerFor(PredictionKind.Basket), "a basket button is answered by ReportCook");
        }

        [Test]
        public void TippingTheBasket_OntoAPlate_KeepsTheBonusAndRefusesWhileInTheOil()
        {
            const int plate = 80;
            KitchenState kitchen = Frying();
            kitchen.TryShake(Basket0, Start + Real(27), Rate);
            Assert.AreEqual(CookOutcome.BasketLowered, kitchen.TryTipBasket(Basket0, plate, VesselKind.Plate, Start + Real(29), Rate, out _));
            kitchen.TryRaiseBasket(Basket0, Start + Real(30), Rate);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryTipBasket(Basket0, plate, VesselKind.Plate, Start + Real(33), Rate, out Item tipped));
            Assert.AreEqual("golden fries", KitchenState.NameOf(tipped));
            Assert.AreEqual(1f + DeepFry.ShakeBonus, tipped.QualityFactor, 1e-5f);
            Assert.AreEqual(0, kitchen.HeatCount(Basket0), "the basket is empty");
            Assert.IsFalse(kitchen.BasketShaken(Basket0), "the next fry starts fresh");
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewTake(plate, VesselKind.Plate, out Item dish));
            Assert.AreEqual(OrderMatch.Exact, OrderRules.Match(dish, Menu.ById(Menu.Fries)), "a Fries order");
            Assert.AreEqual(CookOutcome.StationEmpty, kitchen.TryTipBasket(Basket0, plate, VesselKind.Plate, Start + Real(34), Rate, out _));
            Assert.AreEqual(CookOutcome.NotAVessel, kitchen.TryTipBasket(999, plate, VesselKind.Plate, Start, Rate, out _));
        }

        // ---- the drain penalty ----

        [Test]
        public void TakingItOut_BeforeItDrained_IsGreasy()
        {
            KitchenState kitchen = Frying();
            uint up = Start + Real(30);
            kitchen.TryRaiseBasket(Basket0, up, Rate);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewTakeOut(Basket0, VesselKind.Basket, up + Real(1), Rate, out Item greasy));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewTakeOut(Basket0, VesselKind.Basket, up + Real(2.5), Rate, out Item drained));
            Assert.AreEqual(1f - DeepFry.GreasyPenalty, greasy.QualityFactor, 1e-5f);
            Assert.AreEqual(1f, drained.QualityFactor);
            Assert.AreEqual(KitchenState.QualityOf(drained) * (1f - DeepFry.GreasyPenalty), KitchenState.QualityOf(greasy), 1e-4f);
            StringAssert.StartsWith("greasy", KitchenState.NameOf(greasy));
            Assert.AreEqual("golden fries", KitchenState.NameOf(drained));
        }

        // ---- zones and ring bytes ----

        [Test]
        public void Zones_AreTheSpecTimings_ForFriesAndFalafel()
        {
            KitchenState kitchen = Kitchen();
            kitchen.TryCreateIngredient("potato", out Item potato);
            kitchen.Register(Food, potato);
            Item sticks = HeatTestKit.Cut(kitchen, Food, 3);
            kitchen.TryCreateIngredient("falafel_mix", out Item mix);
            HeatTimeline fries = kitchen.FreshTimeline(sticks, DeepFry.OperationId, FryerRules.OilTempC);
            HeatTimeline falafel = kitchen.FreshTimeline(mix, DeepFry.OperationId, FryerRules.OilTempC);
            double s = CookingTime.TimeScale;
            TestContext.WriteLine("fries (real s): good " + (fries.GoodFrom / s).ToString("0.0") + ", perfect " + (fries.PerfectFrom / s).ToString("0.0")
                + ", warning " + ((fries.OverdoneFrom - HeatZoneRules.WarnSimSeconds) / s).ToString("0.0") + ", overdone "
                + (fries.OverdoneFrom / s).ToString("0.0") + ", burnt " + (fries.BurntFrom / s).ToString("0.0"));
            TestContext.WriteLine("falafel (real s): good " + (falafel.GoodFrom / s).ToString("0.0") + ", perfect " + (falafel.PerfectFrom / s).ToString("0.0")
                + ", overdone " + (falafel.OverdoneFrom / s).ToString("0.0") + ", burnt " + (falafel.BurntFrom / s).ToString("0.0"));
            Assert.AreEqual(25.0, fries.GoodFrom / s, 0.2);
            Assert.AreEqual(30.0, fries.PerfectFrom / s, 0.2);
            Assert.AreEqual(45.0, fries.BurntFrom / s, 0.2);
            Assert.AreEqual(20.0, falafel.GoodFrom / s, 0.2);
            Assert.AreEqual(25.0, falafel.PerfectFrom / s, 0.2);
            Assert.AreEqual(38.0, falafel.BurntFrom / s, 0.2);
            Assert.AreEqual((float)CookingTime.TimeScale, DeepFry.KitchenTimeScale, "the sim's kitchen seconds are the game's");
        }

        [Test]
        public void TheRingBytes_FollowTheBasket_AndTheModeAndFryerBytesSayWhereItIs()
        {
            KitchenState kitchen = Frying();
            Assert.AreEqual(HeatZone.Raw, kitchen.HeatReadingOf(Basket0, Start, Rate).Zone);
            HeatReading good = kitchen.HeatReadingOf(Basket0, Start + Real(27), Rate);
            Assert.AreEqual(HeatZone.Good, good.Zone);
            Assert.That(good.Progress, Is.InRange((byte)153, (byte)200));
            Assert.AreEqual(HeatZone.Perfect, kitchen.HeatReadingOf(Basket0, Start + Real(31), Rate).Zone);
            Assert.IsTrue(kitchen.HeatReadingOf(Basket0, Start + Real(33), Rate).Warn, "3 s before overdone");
            Assert.AreEqual(HeatZone.Burnt, kitchen.HeatReadingOf(Basket0, Start + Real(46), Rate).Zone);

            byte flags = kitchen.FryerFlags(Fryer, Start);
            Assert.IsTrue(FryerRules.IsOn(flags));
            Assert.IsTrue(FryerRules.IsLowered(flags, 0));
            Assert.IsFalse(FryerRules.IsLowered(flags, 1));
            Assert.AreEqual(BasketState.Lowered, FryerRules.StateOf(kitchen.BasketMode(Basket0, Start)));
            kitchen.TryRaiseBasket(Basket0, Start + Real(30), Rate);
            Assert.AreEqual(BasketState.Draining, FryerRules.StateOf(kitchen.BasketMode(Basket0, Start + Real(31))));
            Assert.IsFalse(FryerRules.IsLowered(kitchen.FryerFlags(Fryer, Start + Real(31)), 0));
            // The ring holds where it was while the basket hangs on the hook.
            Assert.AreEqual(kitchen.HeatReadingOf(Basket0, Start + Real(31), Rate).Progress, kitchen.HeatReadingOf(Basket0, Start + Real(60), Rate).Progress);
        }

        [Test]
        public void Falafel_FromTheBasket_IsFourBalls_AndMatchesItsOrder()
        {
            KitchenState kitchen = Frying(Falafel);
            kitchen.TryRaiseBasket(Basket0, Start + Real(24), Rate);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewTakeOut(Basket0, VesselKind.Basket, Start + Real(27), Rate, out Item balls));
            Assert.AreEqual("golden falafel balls", KitchenState.NameOf(balls));
            FoodLook look = FoodLookRules.From(balls);
            Assert.AreEqual(FoodLook.HeatDeepFried, look.Heat);
            Assert.IsTrue(FoodArtRules.TryGetPiece(look, out FoodPiece piece));
            Assert.AreEqual(ArtKey.FoodFalafelBall, piece.Key);
            Assert.AreEqual(4, piece.Count);
            Assert.AreEqual(OrderMatch.Exact, OrderRules.Match(balls, Menu.ById(Menu.Falafel)));
            Assert.AreEqual("Falafel (4)", Menu.NameOf(Menu.Falafel));
        }

        [Test]
        public void Fries_FromTheBasket_AreExactFries_LooseInTheBasket_InABoxElsewhere()
        {
            KitchenState kitchen = Frying();
            kitchen.TryRaiseBasket(Basket0, Start + Real(30), Rate);
            kitchen.TryPreviewTakeOut(Basket0, VesselKind.Basket, Start + Real(33), Rate, out Item fries);
            Assert.AreEqual(OrderMatch.Exact, OrderRules.Match(fries, Menu.ById(Menu.Fries)));
            FoodLook look = FoodLookRules.From(fries);
            Assert.IsTrue(FoodArtRules.TryGetPiece(look, false, out FoodPiece boxed));
            Assert.AreEqual(ArtKey.FoodFries, boxed.Key, "the fries box");
            Assert.IsTrue(FoodArtRules.TryGetPiece(look, true, out FoodPiece loose));
            Assert.AreEqual(ArtKey.FoodPieceStick, loose.Key, "loose sticks in the basket");
            Assert.Greater(Menu.All.Count, 6);
        }

        // ---- clients: interaction, bytes, looks ----

        [Test]
        public void Interaction_Basket_PutInWhenUp_ShakeWhenDown_NeverPickedUp()
        {
            byte up = FryerRules.Mode(BasketState.Raised, 0), down = FryerRules.Mode(BasketState.Lowered, 1);
            byte dripping = FryerRules.Mode(BasketState.Draining, 0);
            Held food = Held.Food("potato sticks", false);
            Assert.AreEqual(InteractionAction.PutInto, InteractionRules.ActionFor(food, AimTarget.Cookware(VesselKind.Basket, 0, null, up)));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(food, AimTarget.Cookware(VesselKind.Basket, 1, "fries", up)), "full");
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Nothing, AimTarget.Cookware(VesselKind.Basket, 0, null, up)), "no pick-up");
            Assert.AreEqual(InteractionAction.Shake, InteractionRules.ActionFor(Held.Nothing, AimTarget.Cookware(VesselKind.Basket, 1, "fries", down)));
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(food, AimTarget.Cookware(VesselKind.Basket, 1, "fries", down)));
            Assert.AreEqual("Raise the basket first", InteractionRules.PromptFor(food, AimTarget.Cookware(VesselKind.Basket, 1, "fries", down)));
            Assert.AreEqual("Click · shake the basket", InteractionRules.PromptFor(Held.Nothing, AimTarget.Cookware(VesselKind.Basket, 1, "fries", down)));
            Assert.AreEqual("Click · take golden fries (still dripping)",
                InteractionRules.PromptFor(Held.Nothing, AimTarget.Cookware(VesselKind.Basket, 1, "golden fries", dripping)));
            Assert.AreEqual("Click · put potato sticks into the basket", InteractionRules.PromptFor(food, AimTarget.Cookware(VesselKind.Basket, 0, null, up)));
        }

        [Test]
        public void FryerButtons_SayWhatTheyDo()
        {
            byte off = FryerRules.FryerFlags(false, false, false), on = FryerRules.FryerFlags(true, true, false);
            Assert.AreEqual("Click · fryer on", InteractionRules.FryerButtonPrompt(FryerRules.PowerOption, off));
            Assert.AreEqual("Click · fryer off", InteractionRules.FryerButtonPrompt(FryerRules.PowerOption, on));
            Assert.AreEqual("Click · raise the basket", InteractionRules.FryerButtonPrompt(FryerRules.BasketOption(0), on));
            Assert.AreEqual("Click · lower the basket", InteractionRules.FryerButtonPrompt(FryerRules.BasketOption(1), on));
            Assert.AreEqual(InteractionAction.Press, InteractionRules.ActionFor(Held.Nothing, AimTarget.Button(StationKind.Fryer, 2, on)));
            Assert.AreEqual("Click · raise the basket", InteractionRules.PromptFor(Held.Nothing, AimTarget.Button(StationKind.Fryer, 2, on)));
            Assert.IsTrue(FryerRules.TryParseBasketOption(3, out int b) && b == 1);
            Assert.IsFalse(FryerRules.TryParseBasketOption(FryerRules.PowerOption, out _));
            Assert.IsTrue(FryerRules.TryParseBasketOption(4, out int r, out bool lower) && r == 0 && !lower, "4 raises basket 0");
            Assert.IsFalse(FryerRules.TryParseBasketOption(6, out _));
        }

        [Test]
        public void ModeByte_CarriesTheStateAndAShakeCounter()
        {
            for (int shakes = 0; shakes < 9; shakes++)
                foreach (BasketState state in new[] { BasketState.Raised, BasketState.Lowered, BasketState.Draining })
                {
                    byte mode = FryerRules.Mode(state, shakes);
                    Assert.AreEqual(state, FryerRules.StateOf(mode));
                    Assert.AreEqual(shakes & 3, FryerRules.ShakesOf(mode));
                }
            Assert.AreEqual(1, FryerArtRules.ShakesBetween(3, 0), "the counter wraps");
            Assert.AreEqual(BasketState.Raised, FryerRules.StateOf(3), "an unknown state reads raised");
        }

        [Test]
        public void BasketPrediction_ConfirmsWhenTheSyncedStateCrossesTheOilLine()
        {
            var up = new ItemSnapshot(true, -1, UnityEngine.Vector3.zero, 0);
            var down = new ItemSnapshot(true, -1, UnityEngine.Vector3.zero, 1);
            Assert.AreEqual(PredictionPhase.Pending, FeelRules.Judge(PredictionKind.Basket, 1, up, up));
            Assert.AreEqual(PredictionPhase.Confirmed, FeelRules.Judge(PredictionKind.Basket, 1, up, down));
            Assert.AreEqual(PredictionPhase.Confirmed, FeelRules.Judge(PredictionKind.Basket, 1, down, up));
            Assert.AreEqual(PredictionPhase.Denied, FeelRules.Judge(PredictionKind.Basket, 1, up, ItemSnapshot.Gone));
            var prediction = new FeelPrediction(PredictionKind.Basket, 1, up, 0f, 0.3f);
            prediction.NoteAnswer(false, 0.05f);
            Assert.AreEqual(PredictionPhase.Denied, prediction.Phase, "a refusal sends the basket back at once");
        }

        [Test]
        public void ArtRules_ArcShakeAndBubbles()
        {
            Assert.AreEqual(1f, FryerArtRules.Pose(1f, false, 0f));
            Assert.AreEqual(0f, FryerArtRules.Pose(1f, true, FryerArtRules.ArcSeconds), 1e-5f);
            Assert.That(FryerArtRules.Pose(1f, true, FryerArtRules.ArcSeconds * 0.5f), Is.InRange(0.3f, 0.7f));
            Assert.AreEqual(0f, FryerArtRules.Lift(0f), 1e-5f);
            Assert.Greater(FryerArtRules.Lift(FryerArtRules.ArcSeconds * 0.5f), 0.02f);
            Assert.AreEqual(0f, FryerArtRules.ShakeOffset(-1f));
            Assert.AreEqual(0f, FryerArtRules.ShakeOffset(FryerArtRules.ShakeSeconds));
            Assert.AreEqual(0f, FryerArtRules.BubbleRate(false, BasketState.Lowered, 1, HeatZone.Cooking), "cold oil");
            float idle = FryerArtRules.BubbleRate(true, BasketState.Raised, 1, HeatZone.Cooking);
            float frying = FryerArtRules.BubbleRate(true, BasketState.Lowered, 1, HeatZone.Cooking);
            Assert.Greater(frying, idle * 5f, "food in the oil boils hard");
            Assert.Greater(frying, FryerArtRules.BubbleRate(true, BasketState.Lowered, 1, HeatZone.Perfect), "it calms as it gets done");
            Assert.Greater(FryerArtRules.BubbleRate(true, BasketState.Lowered, 2, HeatZone.Raw), FryerArtRules.BubbleRate(true, BasketState.Lowered, 1, HeatZone.Raw), "more food, more bubbles");
        }
    }
}
