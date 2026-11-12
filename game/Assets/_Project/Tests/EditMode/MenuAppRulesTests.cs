using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Climb;
using Worktown.Game.Cooking;
using Worktown.Game.Npc;
using Worktown.Game.Office;
using Worktown.Game.Shop;
using Worktown.Sim.Climb;
using Worktown.Sim.Economy;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 059 AC3: the Menu app's rows per stage; a terminal price step and a board step give the same price (one server method); a guest
    /// who joined before a change keeps its quote; on and off follow 057's rules; the board tags the special; the log says who.
    /// </summary>
    public class MenuAppRulesTests
    {
        private const byte Restaurant = ShopStageRules.FirstRestaurant;
        private static ClimbCatalog Climb => FoodCornerRules.DefaultClimb;
        private static readonly string[] FalafelAndDrinks =
            { FoodCornerRules.StartUnlock, FoodCornerRules.GoalPurchase, "dish_falafel_wrap", "dish_lemonade" };

        private static int Fries => MenuPrices.LineOfDish(Menu.Fries);
        private static int Falafel => MenuPrices.LineOfDish(Menu.FalafelWrap);

        /// <summary>An office terminal on an offline shop (its services set as the server would find them).</summary>
        private static OfficeTerminal Office(OfflineShop shop, List<GameObject> made)
        {
            var go = new GameObject("Offline office terminal");
            made.Add(go);
            var office = go.AddComponent<OfficeTerminal>();
            typeof(OfficeTerminal).GetField("_economy", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(office, shop.Economy);
            typeof(OfficeTerminal).GetField("_stage", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(office, shop.Stage);
            return office;
        }

        private static int Row(byte stage, byte owned, int line) => MenuAppRules.Lines(stage, Climb, owned).ToList().IndexOf(line);

        [Test]
        public void WithoutTheEconomyOrTheStage_ThePressFailsClosed()
        {
            var go = new GameObject("Lonely office terminal");
            try
            {
                var office = go.AddComponent<OfficeTerminal>();
                if (Object.FindFirstObjectByType<Worktown.Game.Economy.EconomyService>() != null || ShopStage.Find() != null)
                    Assert.Inconclusive("another test left an economy or a stage in the scene");
                Assert.AreEqual(CookOutcome.UnknownStation, office.ServerPress(1, TerminalOptions.Pack(TerminalApp.Menu, 0, TerminalOptions.Plus)));
                Assert.AreEqual(CookOutcome.UnknownStation, office.ServerPress(1, TerminalOptions.Pack(TerminalApp.Services, 0, TerminalOptions.Press)));
                Assert.AreEqual(CookOutcome.InvalidOption, office.ServerPress(1, -1), "a bad option is still refused first");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheRows_AreWhatTheStageMaySell()
        {
            CollectionAssert.AreEqual(MenuBoardRules.VisibleLines(ShopStageRules.OpenKitchen).ToArray(),
                MenuAppRules.Lines(ShopStageRules.OpenKitchen, Climb, 0).ToArray(), "every line in the open kitchen");
            CollectionAssert.AreEqual(new[] { Fries }, MenuAppRules.Lines(ShopStageRules.FoodCorner, Climb, 0b1111).ToArray(), "the fries at the corner");
            CollectionAssert.AreEqual(new[] { Fries }, MenuAppRules.Lines(Restaurant, Climb, 0).ToArray(), "no recipe owned yet");
            int[] owned = MenuAppRules.Lines(Restaurant, Climb, 0b1001).ToArray();
            CollectionAssert.AreEqual(new[] { Fries, Falafel, MenuPrices.LineOfDrink("lemonade"), MenuPrices.LineOfDrink("iced_tea"),
                MenuPrices.LineOfDrink("soda") }, owned, "the fries, the falafel wrap and every drink (the drinks recipe)");
            Assert.AreEqual(-1, MenuAppRules.RecipeRowOf(Climb, Fries));
            Assert.AreEqual(0, MenuAppRules.RecipeRowOf(Climb, Falafel));
            Assert.AreEqual(3, MenuAppRules.RecipeRowOf(Climb, MenuPrices.LineOfDrink("soda")));
            Assert.IsTrue(MenuAppRules.IsOn(Restaurant, Climb, 0, Fries), "the fries are always on");
            Assert.IsFalse(MenuAppRules.IsOn(Restaurant, Climb, 0b1000, Falafel));
            Assert.IsTrue(MenuAppRules.IsOn(Restaurant, Climb, 0b1000, MenuPrices.LineOfDrink("lemonade")));
        }

        [Test]
        public void ARow_ReadsLikeANote()
        {
            Assert.AreEqual("Falafel wrap  $9.00  on  special", MenuAppRules.RowText(Falafel, 900, true, true, true));
            Assert.AreEqual("Fries  $4.50  on", MenuAppRules.RowText(Fries, 450, true, true, false));
            Assert.AreEqual("Falafel wrap  $9.00  off next", MenuAppRules.RowText(Falafel, 900, true, false, false), "off from the next service");
            Assert.AreEqual("Falafel wrap  $9.00  on next", MenuAppRules.RowText(Falafel, 900, false, true, false));
            foreach (int line in MenuAppRules.Lines(ShopStageRules.OpenKitchen, Climb, 0))
                foreach (bool now in new[] { true, false })
                    foreach (bool next in new[] { true, false })
                    {
                        string text = MenuAppRules.RowText(line, 99950, now, next, true);
                        Assert.IsTrue(ResultsRules.PassesNoTable(text), text);
                    }
        }

        [Test]
        public void ATerminalStep_AndABoardStep_GiveTheSamePrice_AndAJoinedGuestKeepsItsQuote()
        {
            var made = new List<GameObject>();
            using OfflineShop shop = OfflineShop.Make(100000).Restaurant(FalafelAndDrinks);
            try
            {
                OfficeTerminal office = Office(shop, made);
                MenuBoardDesk desk = shop.Economy.Menu;
                int boardFries = MenuBoardRules.IndexIn(MenuBoardRules.VisibleLines(Restaurant), Fries);
                Assert.AreEqual(0, boardFries, "the board selects the fries first");
                Assert.AreEqual(CookOutcome.Ok, desk.Use(MenuBoardRules.DearerOption, Restaurant, 2));
                long byBoard = desk.PriceOf(Fries);
                desk.Reset();
                BoardQuote joined = desk.Quote(Menu.Fries, null);
                int option = TerminalOptions.Pack(TerminalApp.Menu, Row(Restaurant, shop.Stage.ServerOwned, Fries), TerminalOptions.Plus);
                Assert.AreEqual(CookOutcome.Ok, office.ServerPress(3, option));
                Assert.AreEqual(byBoard, desk.PriceOf(Fries), "the terminal and the board step the same way");
                Assert.AreEqual(MenuPrices.Reference(Fries) + MenuPricing.StepCents, desk.PriceOf(Fries));
                Assert.AreEqual(MenuPrices.Reference(Fries), joined.DishPrice, "a guest who joined before keeps its quote");
                Assert.AreEqual(desk.PriceOf(Fries), desk.Quote(Menu.Fries, null).DishPrice, "a guest joining now pays the new price");
                Assert.AreEqual(CookOutcome.Ok, office.ServerPress(3, TerminalOptions.Pack(TerminalApp.Menu, 0, TerminalOptions.Minus)));
                Assert.AreEqual(MenuPrices.Reference(Fries), desk.PriceOf(Fries));
                // The log names who (slot 3 is in no roster here: "A cook"); the board's own step is logged too.
                Assert.AreEqual(CookOutcome.Ok, desk.Use(MenuBoardRules.DearerOption, Restaurant, 1));
                string[] log = office.ServerLog.Select(e => ChangeLog.Text(e, slot => slot == 1 ? "Mira" : null)).ToArray();
                CollectionAssert.AreEqual(new[] { "A cook: fries $4.50", "A cook: fries $4.00", "Mira: fries $4.50" }, log);
            }
            finally
            {
                foreach (GameObject go in made) Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void OnAndOff_Follow057sRules()
        {
            var made = new List<GameObject>();
            using OfflineShop shop = OfflineShop.Make(100000).Restaurant(FalafelAndDrinks);
            try
            {
                OfficeTerminal office = Office(shop, made);
                byte owned = shop.Stage.ServerOwned;
                Assert.AreEqual(0b1001, owned);
                Assert.AreEqual(0b1001, shop.Stage.ServerMenuNext);
                int falafel = TerminalOptions.Pack(TerminalApp.Menu, Row(Restaurant, owned, Falafel), TerminalOptions.Toggle);
                Assert.AreEqual(CookOutcome.Ok, office.ServerPress(1, falafel));
                Assert.AreEqual(0b1000, shop.Stage.ServerMenuNext, "off the next menu");
                Assert.AreEqual(0b1001, shop.Stage.ServerMenuNow, "the running service's menu stays (057: frozen at its start)");
                Assert.AreEqual(CookOutcome.Ok, office.ServerPress(1, falafel));
                Assert.AreEqual(0b1001, shop.Stage.ServerMenuNext);
                Assert.AreEqual(CookOutcome.AlwaysOn, office.ServerPress(1, TerminalOptions.Pack(TerminalApp.Menu, 0, TerminalOptions.Toggle)), "the fries stay on");
                int lemonade = Row(Restaurant, owned, MenuPrices.LineOfDrink("lemonade"));
                Assert.AreEqual(CookOutcome.Ok, office.ServerPress(1, TerminalOptions.Pack(TerminalApp.Menu, lemonade, TerminalOptions.Toggle)));
                Assert.AreEqual(0b0001, shop.Stage.ServerMenuNext, "the drinks recipe goes off as one");
                Assert.AreEqual(CookOutcome.InvalidOption, office.ServerPress(1, TerminalOptions.Pack(TerminalApp.Menu, 9, TerminalOptions.Toggle)), "no such row");
                string[] log = office.ServerLog.Select(e => ChangeLog.Text(e, _ => "Lu")).ToArray();
                CollectionAssert.AreEqual(new[] { "Lu: falafel wrap off the menu next", "Lu: falafel wrap on the menu next", "Lu: drinks off the menu next" }, log);
            }
            finally
            {
                foreach (GameObject go in made) Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TheMenuHoldsMaxDishes_AndTheCornerRefuses()
        {
            var made = new List<GameObject>();
            string[] all = { FoodCornerRules.StartUnlock, FoodCornerRules.GoalPurchase, "dish_falafel_wrap", "dish_seitan_wrap", "dish_grilled_veggies", "dish_lemonade" };
            using OfflineShop shop = OfflineShop.Make(100000).Restaurant(all, menuOff: new[] { "dish_lemonade" });
            try
            {
                OfficeTerminal office = Office(shop, made);
                Assert.AreEqual(0b0111, shop.Stage.ServerMenuNext, "three recipes and the fries: the menu is full");
                int lemonade = Row(Restaurant, shop.Stage.ServerOwned, MenuPrices.LineOfDrink("lemonade"));
                Assert.AreEqual(CookOutcome.MenuFull, office.ServerPress(1, TerminalOptions.Pack(TerminalApp.Menu, lemonade, TerminalOptions.Toggle)));
                Assert.AreEqual(0b0111, shop.Stage.ServerMenuNext, "a refusal changes nothing");
                Assert.AreEqual(CookOutcome.MenuFull, shop.Stage.ServerToggleRecipe(1, 3), "the goal board's rules (one way to switch a dish)");
            }
            finally
            {
                foreach (GameObject go in made) Object.DestroyImmediate(go);
            }
            using OfflineShop corner = OfflineShop.Make(100000).Corner();
            var cornerMade = new List<GameObject>();
            try
            {
                OfficeTerminal office = Office(corner, cornerMade);
                Assert.AreEqual(CookOutcome.AlwaysOn, office.ServerPress(1, TerminalOptions.Pack(TerminalApp.Menu, 0, TerminalOptions.Toggle)));
                Assert.AreEqual(CookOutcome.NotOpenYet, office.ServerPress(1, TerminalOptions.Pack(TerminalApp.Menu, 0, TerminalOptions.Special)));
                Assert.AreEqual(CookOutcome.Ok, office.ServerPress(1, TerminalOptions.Pack(TerminalApp.Menu, 0, TerminalOptions.Plus)), "the fries' price at the corner");
                Assert.AreEqual(CookOutcome.InvalidOption, office.ServerPress(1, TerminalOptions.Pack(TerminalApp.Menu, 1, TerminalOptions.Plus)), "one row only");
                Assert.AreEqual(CookOutcome.InvalidOption, office.ServerPress(1, TerminalOptions.Pack(TerminalApp.Results, 0, TerminalOptions.Press)), "Results is read-only");
                Assert.AreEqual(CookOutcome.InvalidOption, office.ServerPress(1, -1));
            }
            finally
            {
                foreach (GameObject go in cornerMade) Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TheSpecial_IsSetAtTheTerminal_AndTaggedOnTheBoard()
        {
            var made = new List<GameObject>();
            using OfflineShop shop = OfflineShop.Make(100000).Restaurant(FalafelAndDrinks);
            try
            {
                OfficeTerminal office = Office(shop, made);
                int falafel = TerminalOptions.Pack(TerminalApp.Menu, Row(Restaurant, shop.Stage.ServerOwned, Falafel), TerminalOptions.Special);
                Assert.AreEqual(CookOutcome.Ok, office.ServerPress(2, falafel));
                Assert.AreEqual(Menu.FalafelWrap, office.ServerSpecial);
                Assert.AreEqual(CookOutcome.InvalidOption, office.ServerPress(2,
                    TerminalOptions.Pack(TerminalApp.Menu, Row(Restaurant, shop.Stage.ServerOwned, MenuPrices.LineOfDrink("soda")), TerminalOptions.Special)), "a drink is no special");
                Assert.AreEqual("Tobi: falafel wrap is the special", ChangeLog.Text(office.ServerLog.Last(), _ => "Tobi"));
                string board = MenuBoardRules.DisplayText(Restaurant, null, Falafel, office.ServerSpecial);
                StringAssert.Contains("Falafel wrap  $9.00  " + MenuBoardRules.SpecialTag, board);
                StringAssert.DoesNotContain("Fries  $4.00  " + MenuBoardRules.SpecialTag, board);
                Assert.AreEqual(MenuBoardRules.DisplayText(Restaurant, null, Falafel), MenuBoardRules.DisplayText(Restaurant, null, Falafel, Menu.None), "no special, no tag");
                Assert.AreEqual(CookOutcome.Ok, office.ServerPress(2, falafel), "pressed again: no special");
                Assert.AreEqual(Menu.None, office.ServerSpecial);
                Assert.AreEqual("Tobi: no special today", ChangeLog.Text(office.ServerLog.Last(), _ => "Tobi"));
            }
            finally
            {
                foreach (GameObject go in made) Object.DestroyImmediate(go);
            }
        }
    }
}
