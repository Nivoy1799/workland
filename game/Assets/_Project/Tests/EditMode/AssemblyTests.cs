using System.Collections.Generic;
using NUnit.Framework;
using Worktown.Game.Cooking;
using Worktown.Game.Economy;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 013 AC1 (plate and bowl) and AC2 (meal sells better than a snack).</summary>
    public class AssemblyTests
    {
        private const int Table = 10;
        private const int Bowl = 11;
        private int _nextId = 100;

        /// <summary>Registers a food made by a chain of station options and returns its object id.</summary>
        private int Food(KitchenState kitchen, string baseId, params (StationKind kind, int option)[] steps)
        {
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryCreateIngredient(baseId, out Item item));
            int id = _nextId++;
            kitchen.Register(id, item);
            foreach ((StationKind kind, int option) in steps)
                Assert.AreEqual(CookOutcome.Ok, kitchen.TryApply(id, kind, option, out _, out _));
            return id;
        }

        private int GoldenFries(KitchenState k) => Food(k, "potato", (StationKind.Board, 3), (StationKind.Pan, 2));
        private int TomatoSlices(KitchenState k) => Food(k, "tomato", (StationKind.Board, 2));

        [Test]
        public void Plate_TakesAllItemsAsOnePlatedDish_AndEmptiesTheTable()
        {
            var kitchen = KitchenState.CreateDemo();
            int fries = GoldenFries(kitchen);
            int tomato = TomatoSlices(kitchen);
            kitchen.TryGet(fries, out Item friesItem);
            kitchen.TryGet(tomato, out Item tomatoItem);

            Assert.AreEqual(CookOutcome.Ok, kitchen.TryAddToStation(fries, Table, StationKind.PlatingTable, 0));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryAddToStation(tomato, Table, StationKind.PlatingTable, 2));
            Assert.IsFalse(kitchen.TryGet(fries, out _), "food on the table is no longer a loose item");
            Assert.AreEqual(2, kitchen.NamesOnStation(Table).Count);

            Assert.AreEqual(CookOutcome.Ok, kitchen.TryTakeFromStation(Table, StationKind.PlatingTable, out Item plate, out byte color));
            var factory = new ItemFactory(new ItemCatalog(DemoIngredients.All), OperationCatalog.CreateDefault());
            Item expected = factory.Apply("plate", new[] { friesItem, tomatoItem }, new Dictionary<string, ParamValue>()).Item;
            Assert.AreEqual(expected.Hash, plate.Hash);
            Assert.IsTrue(AssemblyRules.IsPlated(plate));
            Assert.AreEqual(2, plate.Weight);
            Assert.AreEqual(0, color, "color of the first item");
            Assert.AreEqual(0, kitchen.NamesOnStation(Table).Count);
        }

        [Test]
        public void Plate_RefusesBeyondLimit()
        {
            var kitchen = KitchenState.CreateDemo();
            for (int i = 0; i < CookingLimits.MaxPlateInputs; i++)
                Assert.AreEqual(CookOutcome.Ok, kitchen.TryAddToStation(TomatoSlices(kitchen), Table, StationKind.PlatingTable, 2));
            int extra = TomatoSlices(kitchen);
            Assert.AreEqual(CookOutcome.StationFull, kitchen.TryAddToStation(extra, Table, StationKind.PlatingTable, 2));
            Assert.IsTrue(kitchen.TryGet(extra, out _), "refused food stays in hand");
        }

        [Test]
        public void PlatedDish_CannotGoOntoAnotherPlateOrIntoABowl()
        {
            var kitchen = KitchenState.CreateDemo();
            kitchen.TryAddToStation(GoldenFries(kitchen), Table, StationKind.PlatingTable, 0);
            kitchen.TryTakeFromStation(Table, StationKind.PlatingTable, out Item plate, out _);
            kitchen.Register(500, plate);
            Assert.AreEqual(CookOutcome.AlreadyPlated, kitchen.TryAddToStation(500, Table, StationKind.PlatingTable, 0));
            Assert.AreEqual(CookOutcome.AlreadyPlated, kitchen.TryAddToStation(500, Bowl, StationKind.Bowl, 0));
        }

        [Test]
        public void Bowl_MixesTwo_AndGivesASingleItemBackUnchanged()
        {
            var kitchen = KitchenState.CreateDemo();
            int tomato = TomatoSlices(kitchen);
            kitchen.TryGet(tomato, out Item tomatoItem);
            kitchen.TryAddToStation(tomato, Bowl, StationKind.Bowl, 2);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryTakeFromStation(Bowl, StationKind.Bowl, out Item single, out _));
            Assert.AreEqual(tomatoItem.Hash, single.Hash);

            kitchen.TryAddToStation(TomatoSlices(kitchen), Bowl, StationKind.Bowl, 2);
            kitchen.TryAddToStation(Food(kitchen, "onion", (StationKind.Board, 4)), Bowl, StationKind.Bowl, 3);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryTakeFromStation(Bowl, StationKind.Bowl, out Item mix, out _));
            Assert.AreEqual("mix", mix.OperationId);
            Assert.IsFalse(AssemblyRules.IsPlated(mix));
        }

        [Test]
        public void AddingThatTheSimCouldNotCombine_IsRefusedAtAdd_AndTheBowlStaysUsable()
        {
            // Review blocker: the sim flattens nested mixes, so a full 6-item mix plus one more item
            // is 7 inputs. That must be refused while the item is still in hand.
            var kitchen = KitchenState.CreateDemo();
            for (int i = 0; i < CookingLimits.MaxMixInputs; i++)
                Assert.AreEqual(CookOutcome.Ok, kitchen.TryAddToStation(TomatoSlices(kitchen), Bowl, StationKind.Bowl, 2));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryTakeFromStation(Bowl, StationKind.Bowl, out Item fullMix, out _));
            kitchen.Register(700, fullMix);

            Assert.AreEqual(CookOutcome.Ok, kitchen.TryAddToStation(700, Bowl, StationKind.Bowl, 2), "a single mix goes in");
            int extra = TomatoSlices(kitchen);
            Assert.AreEqual(CookOutcome.SimRefused, kitchen.TryAddToStation(extra, Bowl, StationKind.Bowl, 2));
            Assert.IsTrue(kitchen.TryGet(extra, out _), "the refused item stays in hand");

            Assert.AreEqual(CookOutcome.Ok, kitchen.TryTakeFromStation(Bowl, StationKind.Bowl, out Item back, out _));
            Assert.AreEqual(fullMix.Hash, back.Hash, "the bowl is not locked");
        }

        [Test]
        public void PreviewTake_DoesNotEmptyTheStation()
        {
            var kitchen = KitchenState.CreateDemo();
            kitchen.TryAddToStation(GoldenFries(kitchen), Table, StationKind.PlatingTable, 0);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewTake(Table, StationKind.PlatingTable, out _, out _));
            Assert.AreEqual(1, kitchen.NamesOnStation(Table).Count);
            kitchen.ClearStation(Table);
            Assert.AreEqual(0, kitchen.NamesOnStation(Table).Count);
        }

        [Test]
        public void EmptyStation_IsRefused_AndOtherStationsAreNotAssemblies()
        {
            var kitchen = KitchenState.CreateDemo();
            Assert.AreEqual(CookOutcome.StationEmpty, kitchen.TryTakeFromStation(Table, StationKind.PlatingTable, out _, out _));
            Assert.AreEqual(CookOutcome.InvalidOption, kitchen.TryTakeFromStation(Table, StationKind.Pan, out _, out _));
            Assert.AreEqual(CookOutcome.InvalidOption, kitchen.TryAddToStation(TomatoSlices(kitchen), Table, StationKind.Board, 0));
            Assert.AreEqual(CookOutcome.NotFood, kitchen.TryAddToStation(999, Table, StationKind.PlatingTable, 0));
        }

        [Test]
        public void TryPlate_SellsAPlatedDishAsItIs()
        {
            var kitchen = KitchenState.CreateDemo();
            kitchen.TryAddToStation(GoldenFries(kitchen), Table, StationKind.PlatingTable, 0);
            kitchen.TryAddToStation(TomatoSlices(kitchen), Table, StationKind.PlatingTable, 2);
            kitchen.TryTakeFromStation(Table, StationKind.PlatingTable, out Item plate, out _);
            kitchen.Register(600, plate);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPlate(600, out Item forSale));
            Assert.AreEqual(plate.Hash, forSale.Hash, "no plate of a plate");
        }

        // AC2
        [Test]
        public void MealOfFriesAndTomato_PaysMoreThanFriesAlone()
        {
            var kitchen = KitchenState.CreateDemo();
            int friesOnly = GoldenFries(kitchen);
            kitchen.TryPlate(friesOnly, out Item snack);

            kitchen.TryAddToStation(GoldenFries(kitchen), Table, StationKind.PlatingTable, 0);
            kitchen.TryAddToStation(TomatoSlices(kitchen), Table, StationKind.PlatingTable, 2);
            kitchen.TryTakeFromStation(Table, StationKind.PlatingTable, out Item meal, out _);

            TownEconomy forSnack = TownEconomy.CreateDemo();
            TownEconomy forMeal = TownEconomy.CreateDemo();
            for (int i = 0; i < 4; i++) { forSnack.TickHour(); forMeal.TickHour(); } // noon: lunch demand
            forSnack.TryOpenAccount("p0", 0);
            forMeal.TryOpenAccount("p0", 0);

            SaleResult snackSale = forSnack.TrySell("p0", snack);
            SaleResult mealSale = forMeal.TrySell("p0", meal);
            Assert.IsTrue(snackSale.Ok, snackSale.Error.ToString());
            Assert.IsTrue(mealSale.Ok, mealSale.Error.ToString());
            Assert.AreEqual("snack", snackSale.MarketId);
            Assert.AreEqual("meal", mealSale.MarketId);
            Assert.Greater(mealSale.Payout, snackSale.Payout * 2, "a meal is worth well over two snacks");
        }
    }
}
