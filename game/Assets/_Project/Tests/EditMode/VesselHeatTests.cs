using System.Collections.Generic;
using NUnit.Framework;
using Worktown.Game.Cooking;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 021 AC4: a pan keyed by its object id cooks only on a burner that is on, keeps its clock while carried
    /// between burners, and gives the same items as the spec 016 pan station did for the same times. Spec 043: the pan runs 2x, so
    /// the golden fries (the same hash) come at 80 sim s (400 ticks), Perfect, instead of 150.
    /// </summary>
    public class VesselHeatTests
    {
        private const int FoodId = 42;
        private const int Pan = HeatTestKit.PanIndex;
        private const int BurnerA = 900;
        private const int BurnerB = 905;
        private const uint Start = HeatTestKit.StartTick;
        private const double Rate = HeatTestKit.TickRate;
        // Spec 007/016: the golden fries pinned before cookware existed (KitchenStateTests).
        private const string GoldenFriesHash = "2ab1f86227c96055d1d69a98ce1485820e94a0805096d923ef51a6b4286857d3";

        private static KitchenState KitchenWithSticks(out Item sticks)
        {
            var kitchen = KitchenState.CreateDemo();
            kitchen.TryCreateIngredient("potato", out Item potato);
            kitchen.Register(FoodId, potato);
            sticks = HeatTestKit.Cut(kitchen, FoodId, 3);
            return kitchen;
        }

        private static Item Direct(string op, Item food, float seconds)
        {
            var factory = new ItemFactory(new ItemCatalog(DemoIngredients.All), OperationCatalog.CreateDefault());
            return factory.Apply(op, new[] { food }, new Dictionary<string, ParamValue>
            {
                ["temp"] = ParamValue.Number(StoveSettings.PanTempC),
                ["time"] = ParamValue.Number(seconds)
            }).Item;
        }

        [Test]
        public void Fry80_OnABurner_IsThePinnedGoldenFries()
        {
            KitchenState kitchen = KitchenWithSticks(out Item sticks);
            HeatTestKit.SwitchOn(kitchen, Pan, BurnerA);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutInto(FoodId, Pan, VesselKind.Pan, Start));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewTakeOut(Pan, VesselKind.Pan, Start + 400, Rate, out Item fries)); // spec 043: 80 sim s (2x pan)
            Assert.AreEqual(GoldenFriesHash, fries.Hash);
            Assert.AreEqual(Direct("fry", sticks, 80f).Hash, fries.Hash);
        }

        [Test]
        public void CarriedBetweenBurners_TheClockPauses_AndContinues()
        {
            KitchenState kitchen = KitchenWithSticks(out _);
            HeatTestKit.SwitchOn(kitchen, Pan, BurnerA);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryUseBurner(BurnerB, StationRules.StoveSwitch, Start));
            kitchen.TryPutInto(FoodId, Pan, VesselKind.Pan, Start);
            kitchen.SetVesselBurner(Pan, KitchenState.NoBurner, Start + 200);  // picked up after 200 ticks
            kitchen.SetVesselBurner(Pan, BurnerB, Start + 1200);              // 1000 ticks in the hands: nothing
            Assert.AreEqual(BurnerB, kitchen.BurnerUnder(Pan));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewHeatTake(Pan, Start + 1400, Rate, out Item fries)); // + 200
            Assert.AreEqual(GoldenFriesHash, fries.Hash, "200 + 200 ticks on = 80 sim s, as on the station (spec 043: the 2x pan)");
        }

        [Test]
        public void OnAWorktop_ABurnerOnElsewhere_DoesNotCook()
        {
            KitchenState kitchen = KitchenWithSticks(out Item sticks);
            kitchen.TryUseBurner(BurnerA, StationRules.StoveSwitch, Start);
            kitchen.TryPutInto(FoodId, Pan, VesselKind.Pan, Start);
            Assert.AreEqual(CookOutcome.TooShort, kitchen.TryPreviewHeatTake(Pan, Start + 750, Rate, out Item back));
            Assert.AreEqual(sticks.Hash, back.Hash);
        }

        [Test]
        public void SwitchingTheBurnerOff_StopsThePanOnIt_NotAnother()
        {
            KitchenState kitchen = KitchenWithSticks(out _);
            HeatTestKit.SwitchOn(kitchen, Pan, BurnerA);
            kitchen.TryPutInto(FoodId, Pan, VesselKind.Pan, Start);
            kitchen.TryUseBurner(BurnerB, StationRules.StoveSwitch, Start + 100); // another burner: no effect on the pan
            Assert.IsTrue(kitchen.IsCooking(Pan));
            kitchen.TryUseBurner(BurnerA, StationRules.StoveSwitch, Start + 400);
            Assert.IsFalse(kitchen.IsCooking(Pan));
            kitchen.TryPreviewHeatTake(Pan, Start + 5000, Rate, out Item fries);
            Assert.AreEqual(GoldenFriesHash, fries.Hash);
        }

        [Test]
        public void ThePansOp_IsItsBurnersModeWhenTheFoodGoesIn()
        {
            KitchenState kitchen = KitchenWithSticks(out Item sticks);
            kitchen.TryUseBurner(BurnerB, StationRules.PanMode, Start);  // burner B sautés
            HeatTestKit.SwitchOn(kitchen, Pan, BurnerB);
            kitchen.TryPutInto(FoodId, Pan, VesselKind.Pan, Start);
            Assert.IsTrue(kitchen.PanIsSaute(Pan));
            kitchen.SetVesselBurner(Pan, KitchenState.NoBurner, Start + 400);
            kitchen.TryPreviewHeatTake(Pan, Start + 400, Rate, out Item sauteed);
            Assert.AreEqual(Direct("saute", sticks, 80f).Hash, sauteed.Hash);
            Assert.IsTrue(kitchen.PanIsSaute(Pan), "the food keeps its op when carried");
        }

        [Test]
        public void TakeOut_ThenClear_EmptiesThePan_RemoveVesselForgetsEverything()
        {
            KitchenState kitchen = KitchenWithSticks(out _);
            HeatTestKit.SwitchOn(kitchen, Pan, BurnerA);
            kitchen.TryPutInto(FoodId, Pan, VesselKind.Pan, Start);
            Assert.AreEqual(1, kitchen.ContentCount(Pan, VesselKind.Pan));
            Assert.AreEqual("crispy fried potato sticks", kitchen.ResultName(Pan, VesselKind.Pan, Start + 400, Rate));
            kitchen.ClearContents(Pan, VesselKind.Pan);
            Assert.AreEqual(0, kitchen.ContentCount(Pan, VesselKind.Pan));
            Assert.IsNull(kitchen.ResultName(Pan, VesselKind.Pan, Start, Rate));
            kitchen.RemoveVessel(Pan);
            Assert.AreEqual(KitchenState.NoBurner, kitchen.BurnerUnder(Pan));
        }
    }
}
