using System.Collections.Generic;
using NUnit.Framework;
using Worktown.Game.Cooking;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 007 AC2 (crate -> board -> pan matches the sim) and AC3 (refusals change nothing).</summary>
    public class KitchenStateTests
    {
        private const int FoodId = 42;

        private static KitchenState NewKitchenWithPotato(out Item potato)
        {
            var kitchen = KitchenState.CreateDemo();
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryCreateIngredient("potato", out potato));
            kitchen.Register(FoodId, potato);
            return kitchen;
        }

        [Test]
        public void PotatoSticksGolden_MatchesSimFriesHashAndName()
        {
            KitchenState kitchen = NewKitchenWithPotato(out Item potato);

            Assert.AreEqual(CookOutcome.Ok, kitchen.TryApply(FoodId, StationKind.Board, 3, out _, out _));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryApply(FoodId, StationKind.Pan, 2, out Item fries, out _));

            // Built directly with the sim, the way spec 005's tests build fries.
            var factory = new ItemFactory(new ItemCatalog(DemoIngredients.All), OperationCatalog.CreateDefault());
            Item sticks = factory.Apply("cut", new[] { potato },
                new Dictionary<string, ParamValue> { ["shape"] = ParamValue.Choice("sticks") }).Item;
            Item expected = factory.Apply("fry", new[] { sticks }, new Dictionary<string, ParamValue>
            {
                ["temp"] = ParamValue.Number(190f),
                ["time"] = ParamValue.Number(150f)
            }).Item;

            Assert.AreEqual(expected.Hash, fries.Hash);
            Assert.AreEqual("2ab1f86227c96055d1d69a98ce1485820e94a0805096d923ef51a6b4286857d3", fries.Hash);
            Assert.AreEqual("crispy fried potato sticks", KitchenState.NameOf(fries));
            Assert.IsTrue(kitchen.TryGet(FoodId, out Item stored));
            Assert.AreEqual(fries.Hash, stored.Hash);
        }

        [Test]
        public void GoldenFries_RateBetterThanRawAndBurnt()
        {
            KitchenState kitchen = NewKitchenWithPotato(out Item raw);
            kitchen.TryApply(FoodId, StationKind.Board, 3, out Item sticks, out _);
            kitchen.TryApply(FoodId, StationKind.Pan, 2, out Item golden, out _);

            kitchen.Register(7, sticks);
            kitchen.TryApply(7, StationKind.Pan, 3, out Item dark, out _);

            Assert.AreEqual(1, StarRating.Stars(KitchenState.QualityOf(raw)));
            Assert.AreEqual(4, StarRating.Stars(KitchenState.QualityOf(golden)));
            Assert.Less(KitchenState.QualityOf(dark), KitchenState.QualityOf(golden));
        }

        [Test]
        public void UnknownFood_IsRefused()
        {
            var kitchen = KitchenState.CreateDemo();
            Assert.AreEqual(CookOutcome.NotFood, kitchen.TryApply(1, StationKind.Pan, 2, out _, out _));
        }

        [TestCase(StationKind.Pan, 0)]
        [TestCase(StationKind.Board, 9)]
        [TestCase(StationKind.Crate, 1)]
        public void InvalidOption_IsRefusedAndChangesNothing(StationKind station, int option)
        {
            KitchenState kitchen = NewKitchenWithPotato(out Item potato);
            Assert.AreEqual(CookOutcome.InvalidOption, kitchen.TryApply(FoodId, station, option, out Item result, out _));
            Assert.IsNull(result);
            kitchen.TryGet(FoodId, out Item stored);
            Assert.AreEqual(potato.Hash, stored.Hash);
        }

        [Test]
        public void UnknownIngredient_IsRefused()
        {
            Assert.AreEqual(CookOutcome.SimRefused, KitchenState.CreateDemo().TryCreateIngredient("dragonfruit", out Item item));
            Assert.IsNull(item);
        }

        [Test]
        public void Serve_RemovesFood()
        {
            KitchenState kitchen = NewKitchenWithPotato(out Item potato);
            Assert.IsTrue(kitchen.TryRemove(FoodId, out Item served));
            Assert.AreEqual(potato.Hash, served.Hash);
            Assert.IsFalse(kitchen.TryGet(FoodId, out _));
            Assert.IsFalse(kitchen.TryRemove(FoodId, out _));
        }

        [Test]
        public void AllDemoIngredients_CanBeCreated()
        {
            var kitchen = KitchenState.CreateDemo();
            foreach (BaseItemDef def in DemoIngredients.All)
                Assert.AreEqual(CookOutcome.Ok, kitchen.TryCreateIngredient(def.Id, out _), def.Id);
        }
    }
}
