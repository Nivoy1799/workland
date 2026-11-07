using System.Collections.Generic;
using NUnit.Framework;
using Worktown.Game.Economy;
using Worktown.Game.Npc;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 022 AC2 and AC6: every menu dish cooked with the spec 021 kitchen matches exactly and sells to its market;
    /// one Exact, one Close and one Wrong case per dish, all built with real sim items through KitchenState.
    /// </summary>
    public class OrderRulesTests
    {
        private static MenuDish Dish(byte id)
        {
            MenuDish dish = Menu.ById(id);
            Assert.IsNotNull(dish, "dish " + id);
            return dish;
        }

        private static void AssertMatch(OrderMatch expected, Item served, byte dish)
        {
            Assert.AreEqual(expected, OrderRules.Match(served, Dish(dish)),
                Menu.NameOf(dish) + " served " + Worktown.Game.Cooking.KitchenState.NameOf(served));
        }

        // ---- AC6: each dish can be cooked with the spec 021 kitchen and sold ----

        private static IEnumerable<TestCaseData> ExactDishes()
        {
            yield return new TestCaseData(Menu.Fries).SetName("Cook_Fries");
            yield return new TestCaseData(Menu.PotatoSoup).SetName("Cook_PotatoSoup");
            yield return new TestCaseData(Menu.OnionSoup).SetName("Cook_OnionSoup");
            yield return new TestCaseData(Menu.TomatoSalad).SetName("Cook_TomatoSalad");
            yield return new TestCaseData(Menu.FriedApple).SetName("Cook_FriedApple");
            yield return new TestCaseData(Menu.VeggieMix).SetName("Cook_VeggieMix");
        }

        private static Item CookExact(byte dish)
        {
            switch (dish)
            {
                case Menu.Fries: return MenuKitchen.ExactFries();
                case Menu.PotatoSoup: return MenuKitchen.ExactPotatoSoup();
                case Menu.OnionSoup: return MenuKitchen.ExactOnionSoup();
                case Menu.TomatoSalad: return MenuKitchen.ExactTomatoSalad();
                case Menu.FriedApple: return MenuKitchen.ExactFriedApple();
                case Menu.VeggieMix: return MenuKitchen.ExactVeggieMix();
                default: throw new System.ArgumentOutOfRangeException(nameof(dish));
            }
        }

        [TestCaseSource(nameof(ExactDishes))]
        public void EveryMenuDish_CanBeCooked_MatchesExactly_AndSellsToItsMarket(byte id)
        {
            Item served = CookExact(id);
            MenuDish dish = Dish(id);
            Assert.AreEqual(OrderMatch.Exact, OrderRules.Match(served, dish), Worktown.Game.Cooking.KitchenState.NameOf(served));

            TownEconomy economy = TownEconomy.CreateDemo();
            Assert.IsTrue(economy.TryOpenAccount("p0", 500));
            SaleResult sale = economy.TrySell("p0", served);
            Assert.IsTrue(sale.Ok, dish.Name + ": " + sale.Error);
            Assert.AreEqual(dish.Market, sale.MarketId, dish.Name + " sells to the " + dish.Market + " market");
        }

        [Test]
        public void TheMenuCoversEveryDish_WithUniqueIds()
        {
            Assert.AreEqual(6, Menu.All.Count);
            var ids = new HashSet<byte>();
            foreach (MenuDish dish in Menu.All)
            {
                Assert.IsTrue(ids.Add(dish.Id), "unique id " + dish.Id);
                Assert.AreNotEqual(Menu.None, dish.Id);
                Assert.AreEqual(dish, Menu.ById(dish.Id));
                Assert.IsTrue(Menu.CanMake(dish, Menu.CrateIngredients), dish.Name + " from the crates");
            }
            Assert.IsNull(Menu.ById(Menu.None));
            Assert.AreEqual("?", Menu.NameOf(200));
        }

        [Test]
        public void MenuNames_AreVegan_D27()
        {
            string[] animal = { "meat", "beef", "pork", "chicken", "fish", "bacon", "ham", "egg", "eggs", "milk", "cheese", "butter", "cream", "honey" };
            foreach (MenuDish dish in Menu.All)
                foreach (string word in dish.Name.ToLowerInvariant().Split(' '))
                    CollectionAssert.DoesNotContain(animal, word, dish.Name); // whole words: "veggie" is not "egg"
        }

        // ---- AC2: Exact, Close, Wrong per dish ----

        [Test]
        public void Fries()
        {
            AssertMatch(OrderMatch.Exact, MenuKitchen.ExactFries(), Menu.Fries);
            AssertMatch(OrderMatch.Close, MenuKitchen.ShortFriedSticks(), Menu.Fries); // sticks not fried enough
            AssertMatch(OrderMatch.Wrong, MenuKitchen.RawWholePotato(), Menu.Fries);   // not cut, not fried
            AssertMatch(OrderMatch.Wrong, MenuKitchen.ExactTomatoSalad(), Menu.Fries); // no potato
        }

        [Test]
        public void PotatoSoup()
        {
            AssertMatch(OrderMatch.Exact, MenuKitchen.ExactPotatoSoup(), Menu.PotatoSoup);
            AssertMatch(OrderMatch.Close, MenuKitchen.BoiledWholePotato(), Menu.PotatoSoup); // not cut
            AssertMatch(OrderMatch.Close, MenuKitchen.ExactFries(), Menu.PotatoSoup);        // fried, not boiled
            AssertMatch(OrderMatch.Wrong, MenuKitchen.RawWholePotato(), Menu.PotatoSoup);
        }

        [Test]
        public void OnionSoup()
        {
            AssertMatch(OrderMatch.Exact, MenuKitchen.ExactOnionSoup(), Menu.OnionSoup);
            AssertMatch(OrderMatch.Close, MenuKitchen.BoiledOnionOnly(), Menu.OnionSoup);     // one ingredient missing
            AssertMatch(OrderMatch.Wrong, MenuKitchen.ExactTomatoSalad(), Menu.OnionSoup);    // nothing boiled
            AssertMatch(OrderMatch.Wrong, MenuKitchen.ExactPotatoSoup(), Menu.OnionSoup);     // no onion
        }

        [Test]
        public void TomatoSalad()
        {
            AssertMatch(OrderMatch.Exact, MenuKitchen.ExactTomatoSalad(), Menu.TomatoSalad);
            AssertMatch(OrderMatch.Close, MenuKitchen.TomatoAndOnionNotMixed(), Menu.TomatoSalad); // not mixed in the bowl
            AssertMatch(OrderMatch.Wrong, MenuKitchen.ExactFries(), Menu.TomatoSalad);
        }

        [Test]
        public void FriedApple()
        {
            AssertMatch(OrderMatch.Exact, MenuKitchen.ExactFriedApple(), Menu.FriedApple);
            AssertMatch(OrderMatch.Close, MenuKitchen.FriedNotSauteedApple(), Menu.FriedApple); // fried, not sauteed
            AssertMatch(OrderMatch.Wrong, MenuKitchen.BoiledWholeApple(), Menu.FriedApple);     // not cut, boiled
        }

        [Test]
        public void VeggieMix()
        {
            AssertMatch(OrderMatch.Exact, MenuKitchen.ExactVeggieMix(), Menu.VeggieMix);
            AssertMatch(OrderMatch.Close, MenuKitchen.TwoCutVeggies(), Menu.VeggieMix); // one ingredient short
            AssertMatch(OrderMatch.Wrong, MenuKitchen.ExactFries(), Menu.VeggieMix);
        }

        [Test]
        public void ExtraIngredient_IsOneStepWrong()
        {
            // Onion soup (onion + potato) for a potato soup order: right potato, one ingredient too many.
            AssertMatch(OrderMatch.Close, MenuKitchen.ExactOnionSoup(), Menu.PotatoSoup);
        }

        [Test]
        public void NullItemOrDish_IsWrong()
        {
            Assert.AreEqual(OrderMatch.Wrong, OrderRules.Match(null, Dish(Menu.Fries)));
            Assert.AreEqual(OrderMatch.Wrong, OrderRules.Match(MenuKitchen.ExactFries(), null));
        }

        // ---- pay factors (spec 022 open question 2) ----

        [Test]
        public void PayPermille_ExactPlusTipPerStarAboveTwo_Close60_Wrong25()
        {
            Assert.AreEqual(1000, OrderRules.PayPermille(OrderMatch.Exact, 1));
            Assert.AreEqual(1000, OrderRules.PayPermille(OrderMatch.Exact, 2));
            Assert.AreEqual(1100, OrderRules.PayPermille(OrderMatch.Exact, 3));
            Assert.AreEqual(1300, OrderRules.PayPermille(OrderMatch.Exact, 5));
            Assert.AreEqual(600, OrderRules.PayPermille(OrderMatch.Close, 5));
            Assert.AreEqual(250, OrderRules.PayPermille(OrderMatch.Wrong, 5));
        }

        [Test]
        public void ApplyPermille_RoundsHalfUp_NeverNegative()
        {
            Assert.AreEqual(60, EconomyRules.ApplyPermille(100, 600));
            Assert.AreEqual(25, EconomyRules.ApplyPermille(101, 250));
            Assert.AreEqual(1, EconomyRules.ApplyPermille(3, 250));
            Assert.AreEqual(130, EconomyRules.ApplyPermille(100, 1300));
            Assert.AreEqual(0, EconomyRules.ApplyPermille(-5, 600));
            Assert.AreEqual(0, EconomyRules.ApplyPermille(100, -1));
        }
    }
}
