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

        /// <summary>Registers a food cut by a chain of board options (HeatTestKit.Cut) and returns its object id.</summary>
        private int Food(KitchenState kitchen, string baseId, params int[] cuts)
        {
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryCreateIngredient(baseId, out Item item));
            int id = _nextId++;
            kitchen.Register(id, item);
            foreach (int option in cuts) HeatTestKit.Cut(kitchen, id, option); // spec 021: on a board vessel
            return id;
        }

        private int GoldenFries(KitchenState k)
        {
            int id = Food(k, "potato", 3);
            HeatTestKit.PanPreset(k, id, 2); // spec 016: the pan cooks over time
            return id;
        }
        private int TomatoSlices(KitchenState k) => Food(k, "tomato", 2);

        [Test]
        public void Plate_TakesAllItemsAsOnePlatedDish_AndEmptiesTheTable()
        {
            var kitchen = KitchenState.CreateDemo();
            int fries = GoldenFries(kitchen);
            int tomato = TomatoSlices(kitchen);
            kitchen.TryGet(fries, out Item friesItem);
            kitchen.TryGet(tomato, out Item tomatoItem);

            Assert.AreEqual(CookOutcome.Ok, kitchen.TryAddToVessel(fries, Table, VesselKind.Plate));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryAddToVessel(tomato, Table, VesselKind.Plate));
            Assert.IsFalse(kitchen.TryGet(fries, out _), "food on the table is no longer a loose item");
            Assert.AreEqual(2, kitchen.NamesIn(Table).Count);

            Assert.AreEqual(CookOutcome.Ok, kitchen.TryTakeFromVessel(Table, VesselKind.Plate, out Item plate));
            var factory = new ItemFactory(new ItemCatalog(DemoIngredients.All), OperationCatalog.CreateDefault());
            Item expected = factory.Apply("plate", new[] { friesItem, tomatoItem }, new Dictionary<string, ParamValue>()).Item;
            Assert.AreEqual(expected.Hash, plate.Hash);
            Assert.IsTrue(AssemblyRules.IsPlated(plate));
            Assert.AreEqual(2, plate.Weight);
            Assert.AreEqual(0, kitchen.NamesIn(Table).Count);
        }

        [Test]
        public void Plate_RefusesBeyondLimit()
        {
            var kitchen = KitchenState.CreateDemo();
            for (int i = 0; i < CookingLimits.MaxPlateInputs; i++)
                Assert.AreEqual(CookOutcome.Ok, kitchen.TryAddToVessel(TomatoSlices(kitchen), Table, VesselKind.Plate));
            int extra = TomatoSlices(kitchen);
            Assert.AreEqual(CookOutcome.VesselFull, kitchen.TryAddToVessel(extra, Table, VesselKind.Plate));
            Assert.IsTrue(kitchen.TryGet(extra, out _), "refused food stays in hand");
        }

        [Test]
        public void PlatedDish_CannotGoOntoAnotherPlateOrIntoABowl()
        {
            var kitchen = KitchenState.CreateDemo();
            kitchen.TryAddToVessel(GoldenFries(kitchen), Table, VesselKind.Plate);
            kitchen.TryTakeFromVessel(Table, VesselKind.Plate, out Item plate);
            kitchen.Register(500, plate);
            Assert.AreEqual(CookOutcome.AlreadyPlated, kitchen.TryAddToVessel(500, Table, VesselKind.Plate));
            Assert.AreEqual(CookOutcome.AlreadyPlated, kitchen.TryAddToVessel(500, Bowl, VesselKind.Bowl));
        }

        [Test]
        public void Bowl_MixesTwo_AndGivesASingleItemBackUnchanged()
        {
            var kitchen = KitchenState.CreateDemo();
            int tomato = TomatoSlices(kitchen);
            kitchen.TryGet(tomato, out Item tomatoItem);
            kitchen.TryAddToVessel(tomato, Bowl, VesselKind.Bowl);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryTakeFromVessel(Bowl, VesselKind.Bowl, out Item single));
            Assert.AreEqual(tomatoItem.Hash, single.Hash);

            kitchen.TryAddToVessel(TomatoSlices(kitchen), Bowl, VesselKind.Bowl);
            kitchen.TryAddToVessel(Food(kitchen, "onion", 4), Bowl, VesselKind.Bowl);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryTakeFromVessel(Bowl, VesselKind.Bowl, out Item mix));
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
                Assert.AreEqual(CookOutcome.Ok, kitchen.TryAddToVessel(TomatoSlices(kitchen), Bowl, VesselKind.Bowl));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryTakeFromVessel(Bowl, VesselKind.Bowl, out Item fullMix));
            kitchen.Register(700, fullMix);

            Assert.AreEqual(CookOutcome.Ok, kitchen.TryAddToVessel(700, Bowl, VesselKind.Bowl), "a single mix goes in");
            int extra = TomatoSlices(kitchen);
            Assert.AreEqual(CookOutcome.SimRefused, kitchen.TryAddToVessel(extra, Bowl, VesselKind.Bowl));
            Assert.IsTrue(kitchen.TryGet(extra, out _), "the refused item stays in hand");

            Assert.AreEqual(CookOutcome.Ok, kitchen.TryTakeFromVessel(Bowl, VesselKind.Bowl, out Item back));
            Assert.AreEqual(fullMix.Hash, back.Hash, "the bowl is not locked");
        }

        [Test]
        public void PreviewTake_DoesNotEmptyTheStation()
        {
            var kitchen = KitchenState.CreateDemo();
            kitchen.TryAddToVessel(GoldenFries(kitchen), Table, VesselKind.Plate);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewTake(Table, VesselKind.Plate, out _));
            Assert.AreEqual(1, kitchen.NamesIn(Table).Count);
            kitchen.ClearVessel(Table);
            Assert.AreEqual(0, kitchen.NamesIn(Table).Count);
        }

        [Test]
        public void EmptyStation_IsRefused_AndOtherStationsAreNotAssemblies()
        {
            var kitchen = KitchenState.CreateDemo();
            Assert.AreEqual(CookOutcome.StationEmpty, kitchen.TryTakeFromVessel(Table, VesselKind.Plate, out _));
            Assert.AreEqual(CookOutcome.InvalidOption, kitchen.TryTakeFromVessel(Table, VesselKind.Pan, out _));
            Assert.AreEqual(CookOutcome.InvalidOption, kitchen.TryAddToVessel(TomatoSlices(kitchen), Table, VesselKind.Board));
            Assert.AreEqual(CookOutcome.NotFood, kitchen.TryAddToVessel(999, Table, VesselKind.Plate));
        }

        [Test]
        public void TryPlate_SellsAPlatedDishAsItIs()
        {
            var kitchen = KitchenState.CreateDemo();
            kitchen.TryAddToVessel(GoldenFries(kitchen), Table, VesselKind.Plate);
            kitchen.TryAddToVessel(TomatoSlices(kitchen), Table, VesselKind.Plate);
            kitchen.TryTakeFromVessel(Table, VesselKind.Plate, out Item plate);
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

            kitchen.TryAddToVessel(GoldenFries(kitchen), Table, VesselKind.Plate);
            kitchen.TryAddToVessel(TomatoSlices(kitchen), Table, VesselKind.Plate);
            kitchen.TryTakeFromVessel(Table, VesselKind.Plate, out Item meal);

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
