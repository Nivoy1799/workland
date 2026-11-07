using System.Collections.Generic;
using NUnit.Framework;
using Worktown.Game.Cooking;
using Worktown.Game.Economy;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 021 AC5: pouring a pan, pot or bowl onto a plate or into a bowl applies the heat or mix ops at that moment
    /// (exactly like a take) and adds the result as one part; a full plate refuses and nothing changes; a plate sold at
    /// the counter is the sim plate op on its parts, with today's price and stars.
    /// </summary>
    public class PourAndSellTests
    {
        private const int Pan = HeatTestKit.PanIndex;
        private const int Pot = HeatTestKit.PotIndex;
        private const int Plate = 70;
        private const int Bowl = 71;
        private const uint Start = HeatTestKit.StartTick;
        private const double Rate = HeatTestKit.TickRate;
        private static readonly IReadOnlyDictionary<string, ParamValue> NoParams = new Dictionary<string, ParamValue>();
        private int _nextId = 200;

        private static ItemFactory NewFactory()
        {
            return new ItemFactory(new ItemCatalog(DemoIngredients.All), OperationCatalog.CreateDefault());
        }

        private int Food(KitchenState kitchen, string baseId, params int[] cuts)
        {
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryCreateIngredient(baseId, out Item item));
            int id = _nextId++;
            kitchen.Register(id, item);
            foreach (int option in cuts) HeatTestKit.Cut(kitchen, id, option);
            return id;
        }

        private static Item ItemOf(KitchenState kitchen, int id)
        {
            Assert.IsTrue(kitchen.TryGet(id, out Item item));
            return item;
        }

        /// <summary>Sticks in the pan on a burner that is on, 750 ticks = 150 sim s (the golden fries).</summary>
        private Item FriesInThePan(KitchenState kitchen)
        {
            int sticks = Food(kitchen, "potato", 3);
            HeatTestKit.SwitchOn(kitchen, Pan, HeatTestKit.PanBurner);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutInto(sticks, Pan, VesselKind.Pan, Start));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewHeatTake(Pan, Start + 750, Rate, out Item fries));
            return fries;
        }

        [Test]
        public void PanToPlate_PoursTheFries_AndEmptiesThePan()
        {
            var kitchen = KitchenState.CreateDemo();
            Item fries = FriesInThePan(kitchen);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPour(Pan, VesselKind.Pan, Plate, VesselKind.Plate, Start + 750, Rate, out Item poured));
            Assert.AreEqual(fries.Hash, poured.Hash, "the same item a take would give");
            Assert.AreEqual(0, kitchen.HeatCount(Pan));
            CollectionAssert.AreEqual(new[] { "crispy fried potato sticks" }, kitchen.NamesIn(Plate));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewTake(Plate, VesselKind.Plate, out Item dish));
            Assert.AreEqual(NewFactory().Apply("plate", new[] { fries }, NoParams).Item.Hash, dish.Hash);
        }

        [Test]
        public void PotToBowl_PoursTheMix()
        {
            var kitchen = KitchenState.CreateDemo();
            int potato = Food(kitchen, "potato");
            int onion = Food(kitchen, "onion", 4);
            HeatTestKit.SwitchOn(kitchen, Pot, HeatTestKit.PotBurner);
            kitchen.TryPutInto(potato, Pot, VesselKind.Pot, Start);
            kitchen.TryPutInto(onion, Pot, VesselKind.Pot, Start);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewHeatTake(Pot, Start + 1500, Rate, out Item expected));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPour(Pot, VesselKind.Pot, Bowl, VesselKind.Bowl, Start + 1500, Rate, out Item poured));
            Assert.AreEqual(expected.Hash, poured.Hash);
            Assert.AreEqual(0, kitchen.HeatCount(Pot));
            Assert.AreEqual(1, kitchen.CountIn(Bowl), "one part: the pot's mix");
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewTakeOut(Bowl, VesselKind.Bowl, Start, Rate, out Item back));
            Assert.AreEqual(expected.Hash, back.Hash, "a single part comes back unchanged");
        }

        [Test]
        public void BowlToPlate_PoursTheBowlsMix()
        {
            var kitchen = KitchenState.CreateDemo();
            int tomato = Food(kitchen, "tomato", 2);
            int onion = Food(kitchen, "onion", 4);
            Item tomatoItem = ItemOf(kitchen, tomato), onionItem = ItemOf(kitchen, onion);
            kitchen.TryPutInto(tomato, Bowl, VesselKind.Bowl, Start);
            kitchen.TryPutInto(onion, Bowl, VesselKind.Bowl, Start);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPour(Bowl, VesselKind.Bowl, Plate, VesselKind.Plate, Start, Rate, out Item poured));
            Item mix = NewFactory().Apply("mix", new[] { tomatoItem, onionItem }, NoParams).Item;
            Assert.AreEqual(mix.Hash, poured.Hash);
            Assert.AreEqual(0, kitchen.CountIn(Bowl));
            Assert.AreEqual(1, kitchen.CountIn(Plate));
        }

        [Test]
        public void FullPlate_RefusesThePour_AndNothingChanges()
        {
            var kitchen = KitchenState.CreateDemo();
            for (int i = 0; i < VesselRules.Capacity(VesselKind.Plate); i++)
                Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutInto(Food(kitchen, "tomato", 2), Plate, VesselKind.Plate, Start));
            Item fries = FriesInThePan(kitchen);
            Assert.AreEqual(CookOutcome.VesselFull, kitchen.TryPour(Pan, VesselKind.Pan, Plate, VesselKind.Plate, Start + 750, Rate, out Item poured));
            Assert.IsNull(poured);
            Assert.AreEqual(1, kitchen.HeatCount(Pan), "the fries stay in the pan");
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewHeatTake(Pan, Start + 750, Rate, out Item still));
            Assert.AreEqual(fries.Hash, still.Hash);
            Assert.AreEqual(VesselRules.Capacity(VesselKind.Plate), kitchen.CountIn(Plate));
        }

        [Test]
        public void WrongDirections_EmptySources_AndPlatedDishes_AreRefused()
        {
            var kitchen = KitchenState.CreateDemo();
            Assert.AreEqual(CookOutcome.CannotPour, kitchen.TryPour(Plate, VesselKind.Plate, Pan, VesselKind.Pan, Start, Rate, out _));
            Assert.AreEqual(CookOutcome.CannotPour, kitchen.TryPour(Pan, VesselKind.Pan, Pot, VesselKind.Pot, Start, Rate, out _));
            Assert.AreEqual(CookOutcome.CannotPour, kitchen.TryPour(Bowl, VesselKind.Bowl, Bowl, VesselKind.Bowl, Start, Rate, out _));
            Assert.AreEqual(CookOutcome.StationEmpty, kitchen.TryPour(Pan, VesselKind.Pan, Plate, VesselKind.Plate, Start, Rate, out _));

            // A finished plate in a bowl cannot go onto a plate.
            int dish = Food(kitchen, "tomato", 2);
            kitchen.TryPutInto(dish, Plate, VesselKind.Plate, Start);
            kitchen.TryTakeFromVessel(Plate, VesselKind.Plate, out Item plated);
            kitchen.Register(999, plated);
            Assert.AreEqual(CookOutcome.AlreadyPlated, kitchen.TryPutInto(999, Bowl, VesselKind.Bowl, Start), "plated never fits");
        }

        [Test]
        public void TooShortInThePan_PoursUnchanged_AndSaysSo()
        {
            var kitchen = KitchenState.CreateDemo();
            int sticks = Food(kitchen, "potato", 3);
            Item raw = ItemOf(kitchen, sticks);
            kitchen.TryPutInto(sticks, Pan, VesselKind.Pan, Start); // not on a burner
            Assert.AreEqual(CookOutcome.TooShort, kitchen.TryPour(Pan, VesselKind.Pan, Plate, VesselKind.Plate, Start + 9000, Rate, out Item poured));
            Assert.AreEqual(raw.Hash, poured.Hash);
            Assert.AreEqual(1, kitchen.CountIn(Plate));
        }

        [Test]
        public void SellingAPlate_IsThePlateOpOnItsParts_WithTodaysPriceAndStars()
        {
            var kitchen = KitchenState.CreateDemo();
            Item fries = FriesInThePan(kitchen);
            kitchen.TryPour(Pan, VesselKind.Pan, Plate, VesselKind.Plate, Start + 750, Rate, out _);
            int tomato = Food(kitchen, "tomato", 2);
            Item tomatoItem = ItemOf(kitchen, tomato);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutInto(tomato, Plate, VesselKind.Plate, Start));
            Assert.AreEqual(KitchenState.NameOf(NewFactory().Apply("plate", new[] { fries, tomatoItem }, NoParams).Item),
                kitchen.ResultName(Plate, VesselKind.Plate, Start, Rate), "the meal's name the client sees");

            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewTake(Plate, VesselKind.Plate, out Item meal));
            Item expected = NewFactory().Apply("plate", new[] { fries, tomatoItem }, NoParams).Item;
            Assert.AreEqual(expected.Hash, meal.Hash, "the spec 013 plating table made the same dish");
            Assert.AreEqual(StarRating.Stars(KitchenState.QualityOf(expected)), StarRating.Stars(KitchenState.QualityOf(meal)));

            TownEconomy forVessel = Noon();
            TownEconomy forDirect = Noon();
            SaleResult sold = forVessel.TrySell("p0", meal);
            SaleResult today = forDirect.TrySell("p0", expected);
            Assert.IsTrue(sold.Ok, sold.Error.ToString());
            Assert.AreEqual("meal", sold.MarketId);
            Assert.AreEqual(today.Payout, sold.Payout);

            kitchen.RemoveVessel(Plate); // the plate goes away with the meal
            Assert.AreEqual(0, kitchen.CountIn(Plate));
        }

        private static TownEconomy Noon()
        {
            TownEconomy economy = TownEconomy.CreateDemo();
            for (int i = 0; i < 4; i++) economy.TickHour(); // lunch demand
            economy.TryOpenAccount("p0", 0);
            return economy;
        }
    }
}
