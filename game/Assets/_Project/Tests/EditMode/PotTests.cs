using System.Collections.Generic;
using NUnit.Framework;
using Worktown.Game.Cooking;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 021 AC3: several ingredients in a pot. Each counts its own on-ticks since it went in, only while the pot
    /// stands on a burner that is on; pouring boils each for its own time (existing sim boil) and mixes them (sim mix).
    /// </summary>
    public class PotTests
    {
        private const int Pot = HeatTestKit.PotIndex;
        private const int Burner = HeatTestKit.PotBurner;
        private const uint Start = HeatTestKit.StartTick;
        private const double Rate = HeatTestKit.TickRate;

        private static readonly IReadOnlyDictionary<string, ParamValue> NoParams = new Dictionary<string, ParamValue>();

        private static ItemFactory NewFactory()
        {
            return new ItemFactory(new ItemCatalog(DemoIngredients.All), OperationCatalog.CreateDefault());
        }

        private static Item Boil(Item item, double seconds)
        {
            return NewFactory().Apply("boil", new[] { item }, new Dictionary<string, ParamValue> { ["time"] = ParamValue.Number((float)seconds) }).Item;
        }

        private static double Seconds(uint ticks) => CookingTime.SimSeconds(ticks, Rate);

        private static KitchenState KitchenWith(out Item potato, out Item onion)
        {
            var kitchen = KitchenState.CreateDemo();
            kitchen.TryCreateIngredient("potato", out potato);
            kitchen.TryCreateIngredient("onion", out onion);
            kitchen.Register(1, potato);
            kitchen.Register(2, onion);
            return kitchen;
        }

        [Test]
        public void TwoIngredientsAtDifferentTicks_PourIsTheMixOfEachBoiledForItsOwnTime()
        {
            KitchenState kitchen = KitchenWith(out Item potato, out Item onion);
            HeatTestKit.SwitchOn(kitchen, Pot, Burner);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutInto(1, Pot, VesselKind.Pot, Start));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutInto(2, Pot, VesselKind.Pot, Start + 600));
            Assert.AreEqual(2, kitchen.ContentCount(Pot, VesselKind.Pot));

            uint pour = Start + 1800;
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewHeatTake(Pot, pour, Rate, out Item poured));
            Item expected = NewFactory().Apply("mix", new[] { Boil(potato, Seconds(1800)), Boil(onion, Seconds(1200)) }, NoParams).Item;
            Assert.AreEqual(expected.Hash, poured.Hash);
            Assert.AreEqual("mix", poured.OperationId);
            // Canonical order: the sim sorts mix inputs, so the order of putting in does not matter.
            Item reversed = NewFactory().Apply("mix", new[] { Boil(onion, Seconds(1200)), Boil(potato, Seconds(1800)) }, NoParams).Item;
            Assert.AreEqual(reversed.Hash, poured.Hash);
        }

        [Test]
        public void OneIngredient_IsTodaysBoiledItem()
        {
            KitchenState kitchen = KitchenWith(out Item potato, out _);
            Item viaKit = HeatTestKit.Boil(kitchen, 1, 300);
            Assert.AreEqual(Boil(potato, 300).Hash, viaKit.Hash);
        }

        [Test]
        public void OffTheBurner_NoTimeCounts()
        {
            KitchenState kitchen = KitchenWith(out Item potato, out _);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryUseBurner(Burner, StationRules.StoveSwitch, Start), "the burner is on");
            kitchen.TryPutInto(1, Pot, VesselKind.Pot, Start); // but the pot stands on a worktop
            Assert.IsFalse(kitchen.IsCooking(Pot));
            Assert.AreEqual(CookOutcome.TooShort, kitchen.TryPreviewHeatTake(Pot, Start + 9000, Rate, out Item back));
            Assert.AreEqual(potato.Hash, back.Hash, "nothing happened");
            Assert.AreEqual("potato · raw", kitchen.DescribeHeat(Pot, Start + 9000, Rate));
        }

        [Test]
        public void PickedUpFromTheBurner_StopsTheClocks_PutBackContinues()
        {
            KitchenState kitchen = KitchenWith(out Item potato, out Item onion);
            HeatTestKit.SwitchOn(kitchen, Pot, Burner);
            kitchen.TryPutInto(1, Pot, VesselKind.Pot, Start);
            kitchen.TryPutInto(2, Pot, VesselKind.Pot, Start + 300);
            kitchen.SetVesselBurner(Pot, KitchenState.NoBurner, Start + 900);   // carried away: potato 900, onion 600
            Assert.IsFalse(kitchen.IsCooking(Pot));
            kitchen.SetVesselBurner(Pot, Burner, Start + 5000);                 // back on: + 600 each
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewHeatTake(Pot, Start + 5600, Rate, out Item poured));
            Item expected = NewFactory().Apply("mix", new[] { Boil(potato, Seconds(1500)), Boil(onion, Seconds(1200)) }, NoParams).Item;
            Assert.AreEqual(expected.Hash, poured.Hash);
        }

        [Test]
        public void AnIngredientTooShort_GoesInRaw_TheRestBoiled()
        {
            KitchenState kitchen = KitchenWith(out Item potato, out Item onion);
            HeatTestKit.SwitchOn(kitchen, Pot, Burner);
            kitchen.TryPutInto(1, Pot, VesselKind.Pot, Start);
            kitchen.TryPutInto(2, Pot, VesselKind.Pot, Start + 1000);
            // 1050 ticks = 210 s for the potato, 50 ticks = 10 s for the onion (below boil's 30 s minimum).
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewHeatTake(Pot, Start + 1050, Rate, out Item poured));
            Item expected = NewFactory().Apply("mix", new[] { Boil(potato, Seconds(1050)), onion }, NoParams).Item;
            Assert.AreEqual(expected.Hash, poured.Hash);
        }

        [Test]
        public void Labels_ShowEachIngredientsOwnDoneness()
        {
            KitchenState kitchen = KitchenWith(out _, out _);
            HeatTestKit.SwitchOn(kitchen, Pot, Burner);
            kitchen.TryPutInto(1, Pot, VesselKind.Pot, Start);
            kitchen.TryPutInto(2, Pot, VesselKind.Pot, Start + 1400);
            // Spec 029b: player words from each food's zone (the potato is still cooking, the onion just went in).
            CollectionAssert.AreEqual(new[] { "potato · warm", "onion · raw" }, kitchen.HeatNames(Pot, Start + 1500, Rate));
            IReadOnlyList<FoodLook> looks = kitchen.HeatLooks(Pot, Start + 1500, Rate);
            Assert.AreEqual(2, looks.Count);
            Assert.AreEqual(FoodLook.HeatBoiled, looks[0].Heat);
            Assert.AreEqual(FoodLook.HeatNone, looks[1].Heat);
        }

        [Test]
        public void APotIsPoured_NotEmptiedByHand()
        {
            KitchenState kitchen = KitchenWith(out _, out _);
            kitchen.TryPutInto(1, Pot, VesselKind.Pot, Start);
            Assert.AreEqual(CookOutcome.PourInstead, kitchen.TryPreviewTakeOut(Pot, VesselKind.Pot, Start, Rate, out Item none));
            Assert.IsNull(none);
            Assert.AreEqual(1, kitchen.HeatCount(Pot));
        }
    }
}
