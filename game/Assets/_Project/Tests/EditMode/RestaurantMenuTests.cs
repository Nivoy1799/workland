using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Worktown.Game.Climb;
using Worktown.Game.Cooking;
using Worktown.Game.Npc;
using Worktown.Game.Shop;
using Worktown.Sim.Climb;
using Worktown.Sim.Core;
using Worktown.Sim.Economy;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 057 AC6: stage 2 opens every station and cookware and takes cards; the guests draw as in the open kitchen (the same seeds, the
    /// same values) and every order is on the frozen menu; drinks off means no drink. Every 052 stage 2 recipe maps to a game dish.
    /// </summary>
    public class RestaurantMenuTests
    {
        private const byte Restaurant = ShopStageRules.FirstRestaurant;
        private static ClimbCatalog Climb => FoodCornerRules.DefaultClimb;

        [Test]
        public void Stage2_OpensEverything_AndTakesCards()
        {
            Assert.AreEqual(2, ShopStageRules.HighestStage);
            Assert.AreEqual(2, ShopStageRules.ClampSaved(3));
            foreach (StationKind kind in (StationKind[])Enum.GetValues(typeof(StationKind)))
            {
                Assert.IsTrue(ShopStageRules.IsStationOpen(Restaurant, kind), kind.ToString());
                for (int slot = 0; slot < 40; slot++) Assert.IsTrue(ShopStageRules.IsOpen(Restaurant, kind, slot), kind + " " + slot);
            }
            foreach (VesselKind kind in (VesselKind[])Enum.GetValues(typeof(VesselKind)))
                Assert.IsTrue(ShopStageRules.IsOpenVessel(Restaurant, kind), kind.ToString());
            Assert.IsTrue(ShopStageRules.IsOpen(Restaurant, StationKind.Register, RegisterRules.CardOption), "the card terminal (CustomerService sets AcceptsCard from it)");
            Assert.IsTrue(ShopStageRules.IsOpen(ShopStageRules.FoodCorner, StationKind.GoalBoard, 1), "the board refuses by its own rules");
        }

        [Test]
        public void EveryStage2Recipe_MapsToAGameDish()
        {
            foreach (PurchaseDef recipe in RestaurantRules.Recipes(Climb))
                Assert.IsTrue(RestaurantMenu.TryMap(recipe.Effect.UnlocksDish, out _, out _), recipe.Id + ": no game dish for " + recipe.Effect.UnlocksDish);
            Assert.IsTrue(RestaurantMenu.TryMap("falafel_wrap", out byte falafel, out bool notDrinks) && falafel == Menu.FalafelWrap && !notDrinks);
            Assert.IsTrue(RestaurantMenu.TryMap("lemonade", out _, out bool drinks) && drinks, "lemonade stands for the drinks");
            Assert.IsFalse(RestaurantMenu.TryMap("soup_of_the_day", out _, out _));
        }

        [Test]
        public void TheMenu_HoldsTheFriesAndTheOnRecipes_OfEachMarket()
        {
            CollectionAssert.AreEqual(new[] { Menu.Fries }, RestaurantMenu.OnDishes(Climb, 0, "snack").ToArray(), "the fries are always on");
            CollectionAssert.IsEmpty(RestaurantMenu.OnDishes(Climb, 0, "meal"));
            Assert.IsFalse(FoodCornerRules.WantsMarket(Restaurant, 0, Climb, "meal"), "no meal guests without a meal dish");
            Assert.IsTrue(FoodCornerRules.WantsMarket(Restaurant, 0, Climb, "snack"));
            CollectionAssert.AreEqual(new[] { Menu.GrilledVeggies, Menu.FalafelWrap }, RestaurantMenu.OnDishes(Climb, 0b0101, "meal").ToArray(),
                "menu order");
            Assert.IsTrue(FoodCornerRules.WantsMarket(Restaurant, 0b0101, Climb, "meal"));
            Assert.IsTrue(RestaurantMenu.DrinksOn(Climb, 0b1000));
            Assert.IsFalse(RestaurantMenu.DrinksOn(Climb, 0b0111));
            Assert.IsTrue(RestaurantMenu.Fits(0b0111, 0b1111, 4));
            Assert.IsFalse(RestaurantMenu.Fits(0b1111, 0b1111, 4), "four recipes and the fries are five dishes");
            Assert.IsFalse(RestaurantMenu.Fits(0b0010, 0b0001, 4), "only owned recipes");
            Assert.AreEqual(0b0011, RestaurantMenu.Clamp(0b1111, 0b0011, 4));
        }

        [Test]
        public void Stage2Guests_DrawAsTheOpenKitchen_AndOrderOnlyFromTheFrozenMenu()
        {
            foreach (byte menu in new byte[] { 0b0000, 0b0001, 0b0110, 0b1001, 0b0111 })
                foreach (string market in new[] { "snack", "meal" })
                {
                    if (!FoodCornerRules.WantsMarket(Restaurant, menu, Climb, market)) continue;
                    var open = new SeededRandom(CustomerRules.OrderSeed);
                    var restaurant = new SeededRandom(CustomerRules.OrderSeed);
                    IReadOnlyList<byte> on = RestaurantMenu.OnDishes(Climb, menu, market);
                    for (int guest = 0; guest < 40; guest++)
                    {
                        MenuDish openDish = Menu.Draw(open, market, Menu.CrateIngredients);
                        GuestOrder openOrder = GuestOrders.Draw(open, null);
                        MenuDish dish = Menu.Draw(restaurant, market, Menu.CrateIngredients);
                        GuestOrder drawn = GuestOrders.Draw(restaurant, null);
                        Assert.AreEqual(openDish.Id, dish.Id, "the same draws");
                        string drink = "lemonade";
                        bool eatIn = guest % 2 == 0;
                        GuestOrder order = FoodCornerRules.StageOrder(Restaurant, menu, Climb, drawn, ref dish, ref drink, ref eatIn);
                        CollectionAssert.Contains(on, dish.Id, "guest " + guest + " orders from the menu " + menu);
                        Assert.AreEqual(openOrder.PatienceBaseSeconds, order.PatienceBaseSeconds);
                        Assert.AreEqual(openOrder.PaysByCard, order.PaysByCard, "the card as drawn");
                        Assert.AreEqual(openOrder.Note, order.Note);
                        Assert.AreEqual(guest % 2 == 0, eatIn, "eat-in as drawn");
                        Assert.AreEqual(RestaurantMenu.DrinksOn(Climb, menu) ? "lemonade" : null, drink, "a drink only when the drinks are on");
                        if (dish.Id == openDish.Id) Assert.AreSame(drawn, order, "a dish on the menu keeps its modifiers");
                        else CollectionAssert.IsEmpty(order.Modifiers, "a replaced dish has none");
                    }
                }
        }

        [Test]
        public void TheMenuBoard_PricesTheRestaurantsDishesAndDrinks()
        {
            IReadOnlyList<int> lines = MenuBoardRules.VisibleLines(Restaurant);
            CollectionAssert.Contains(lines, MenuPrices.LineOfDish(Menu.Fries));
            CollectionAssert.Contains(lines, MenuPrices.LineOfDish(Menu.FalafelWrap));
            CollectionAssert.Contains(lines, MenuPrices.LineOfDish(Menu.SeitanWrap));
            CollectionAssert.Contains(lines, MenuPrices.LineOfDish(Menu.GrilledVeggies));
            CollectionAssert.Contains(lines, MenuPrices.LineOfDrink("lemonade"));
            CollectionAssert.DoesNotContain(lines, MenuPrices.LineOfDish(Menu.PotatoSoup), "not a 052 dish (open question 7)");
            CollectionAssert.AreEqual(new[] { MenuPrices.LineOfDish(Menu.Fries) }, MenuBoardRules.VisibleLines(ShopStageRules.FoodCorner).ToArray(),
                "stage 1 unchanged");
        }
    }
}
