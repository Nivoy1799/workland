using System.Collections.Generic;
using NUnit.Framework;
using Worktown.Game.Climb;
using Worktown.Game.Cooking;
using Worktown.Game.Npc;
using Worktown.Game.Office;
using Worktown.Sim.Climb;
using Worktown.Sim.Core;
using Worktown.Sim.Economy;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 059 AC4: without a special a seeded service orders exactly as today; with one, every 4th joining guest switches to it and an
    /// off-menu draw takes it instead of 057's mapping; the food corner never changes.
    /// </summary>
    public class SpecialRulesTests
    {
        private const byte Restaurant = ShopStageRules.FirstRestaurant;
        private static ClimbCatalog Climb => FoodCornerRules.DefaultClimb;
        private const byte FalafelAndDrinks = 0b1001;

        /// <summary>A seeded service at <paramref name="stage"/>: each guest's dish after 057's mapping and the special.</summary>
        private static List<(byte Drawn, byte Dish, int Modifiers)> Service(byte stage, byte menu, byte special, int guests = 48)
        {
            var orders = new List<(byte, byte, int)>();
            var rng = new SeededRandom(CustomerRules.OrderSeed);
            IReadOnlyList<byte> onMenu = SpecialRules.MenuDishes(stage, menu, Climb);
            for (int ordinal = 1; ordinal <= guests; ordinal++)
            {
                string market = ordinal % 3 == 0 ? "snack" : "meal";
                if (!FoodCornerRules.WantsMarket(stage, menu, Climb, market)) market = "snack";
                MenuDish dish = Menu.Draw(rng, market, Menu.CrateIngredients);
                GuestOrder drawn = GuestOrders.Draw(rng, null);
                byte drawnDish = dish.Id;
                string drink = null;
                bool eatIn = false;
                GuestOrder order = FoodCornerRules.StageOrder(stage, menu, Climb, drawn, ref dish, ref drink, ref eatIn);
                order = SpecialRules.Apply(stage, special, onMenu, drawnDish, ordinal, order, ref dish);
                orders.Add((drawnDish, dish.Id, order.Modifiers.Count));
            }
            return orders;
        }

        [Test]
        public void NoSpecial_OrdersExactlyAsToday()
        {
            foreach (byte stage in new[] { ShopStageRules.OpenKitchen, ShopStageRules.FoodCorner, Restaurant })
            {
                var rng = new SeededRandom(CustomerRules.OrderSeed);
                List<(byte Drawn, byte Dish, int Modifiers)> with = Service(stage, FalafelAndDrinks, Menu.None);
                for (int i = 0; i < with.Count; i++)
                {
                    int ordinal = i + 1;
                    string market = ordinal % 3 == 0 ? "snack" : "meal";
                    if (!FoodCornerRules.WantsMarket(stage, FalafelAndDrinks, Climb, market)) market = "snack";
                    MenuDish dish = Menu.Draw(rng, market, Menu.CrateIngredients);
                    GuestOrder drawn = GuestOrders.Draw(rng, null);
                    string drink = null;
                    bool eatIn = false;
                    GuestOrder today = FoodCornerRules.StageOrder(stage, FalafelAndDrinks, Climb, drawn, ref dish, ref drink, ref eatIn);
                    Assert.AreEqual((dish.Id, today.Modifiers.Count), (with[i].Dish, with[i].Modifiers), "stage " + stage + " guest " + ordinal);
                }
            }
        }

        [Test]
        public void WithASpecial_Every4thGuestSwitches_AndOffMenuDrawsTakeIt()
        {
            List<(byte Drawn, byte Dish, int Modifiers)> today = Service(Restaurant, FalafelAndDrinks, Menu.None);
            List<(byte Drawn, byte Dish, int Modifiers)> special = Service(Restaurant, FalafelAndDrinks, Menu.FalafelWrap);
            IReadOnlyList<byte> onMenu = SpecialRules.MenuDishes(Restaurant, FalafelAndDrinks, Climb);
            int switched = 0, offMenu = 0;
            for (int i = 0; i < today.Count; i++)
            {
                int ordinal = i + 1;
                bool drawnOn = ((List<byte>)onMenu).Contains(today[i].Drawn);
                if (!drawnOn)
                {
                    offMenu++;
                    Assert.AreEqual(Menu.FalafelWrap, special[i].Dish, "guest " + ordinal + " drew an off-menu dish: the special, not 057's mapping");
                }
                else if (ordinal % SpecialRules.EveryNth == 0)
                {
                    switched++;
                    Assert.AreEqual(Menu.FalafelWrap, special[i].Dish, "guest " + ordinal + " is a 4th");
                }
                else Assert.AreEqual(today[i].Dish, special[i].Dish, "guest " + ordinal + " keeps its order");
            }
            Assert.Greater(switched, 0);
            Assert.Greater(offMenu, 0);
        }

        [Test]
        public void TheFoodCorner_NeverChanges_AndAnOffMenuSpecialDoesNothing()
        {
            List<(byte Drawn, byte Dish, int Modifiers)> corner = Service(ShopStageRules.FoodCorner, 0, Menu.FalafelWrap);
            foreach (var (_, dish, _) in corner) Assert.AreEqual(Menu.Fries, dish);
            Assert.IsFalse(SpecialRules.TakesSpecial(ShopStageRules.FoodCorner, Menu.Fries, true, false, 4));
            // The seitan wrap is not on this menu: as if there were no special.
            CollectionAssert.AreEqual(Service(Restaurant, FalafelAndDrinks, Menu.None), Service(Restaurant, FalafelAndDrinks, Menu.SeitanWrap));
            // The open kitchen sells everything: only every 4th guest switches.
            List<(byte Drawn, byte Dish, int Modifiers)> open = Service(ShopStageRules.OpenKitchen, 0, Menu.PotatoSoup);
            for (int i = 0; i < open.Count; i++)
                if ((i + 1) % 4 == 0) Assert.AreEqual(Menu.PotatoSoup, open[i].Dish);
        }

        [Test]
        public void Decide_SetsClearsAndRefuses()
        {
            Assert.AreEqual(CookOutcome.Ok, SpecialRules.Decide(Restaurant, Menu.None, Menu.FalafelWrap, true, out byte next));
            Assert.AreEqual(Menu.FalafelWrap, next);
            Assert.AreEqual(CookOutcome.Ok, SpecialRules.Decide(Restaurant, Menu.FalafelWrap, Menu.FalafelWrap, true, out next), "the special again clears it");
            Assert.AreEqual(Menu.None, next);
            Assert.AreEqual(CookOutcome.Ok, SpecialRules.Decide(Restaurant, Menu.FalafelWrap, Menu.Fries, true, out next));
            Assert.AreEqual(Menu.Fries, next, "another dish on the menu takes over");
            Assert.AreEqual(CookOutcome.NotOnMenu, SpecialRules.Decide(Restaurant, Menu.None, Menu.SeitanWrap, false, out next));
            Assert.AreEqual(Menu.None, next);
            Assert.AreEqual(CookOutcome.NotOpenYet, SpecialRules.Decide(ShopStageRules.FoodCorner, Menu.None, Menu.Fries, true, out _));
            Assert.AreEqual(CookOutcome.InvalidOption, SpecialRules.Decide(Restaurant, Menu.None, Menu.None, true, out _));
            Assert.AreEqual(Menu.FalafelWrap, SpecialRules.FromLineId(SpecialRules.ToLineId(Menu.FalafelWrap)));
            Assert.AreEqual(Menu.None, SpecialRules.FromLineId("lemonade"), "a drink is no special");
            Assert.AreEqual(Menu.None, SpecialRules.FromLineId("a_newer_dish"));
            Assert.AreEqual(string.Empty, SpecialRules.ToLineId(Menu.None));
            CollectionAssert.AreEqual(new[] { Menu.Fries }, SpecialRules.MenuDishes(ShopStageRules.FoodCorner, 0b1111, Climb));
            CollectionAssert.AreEqual(new[] { Menu.Fries, Menu.FalafelWrap }, SpecialRules.MenuDishes(Restaurant, FalafelAndDrinks, Climb));
        }
    }
}
