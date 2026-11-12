using System.Linq;
using NUnit.Framework;
using Worktown.Game.Climb;
using Worktown.Game.Cooking;
using Worktown.Game.UI;
using Worktown.Sim.Climb;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 057 AC7: the goal board at stage 2: a recipe is bought (two presses, the shop's money, nothing owed), refused when short, when
    /// rent is owed, and at stages 0 and 1; toggles respect the menu's size with the fries always on; changes apply at the next service start.
    /// Plus BoardConfirm's rule and the board's texts.
    /// </summary>
    public class GoalBoardRulesTests
    {
        private static long Cost(string id) => RestaurantRules.RecipeCost(id, OfflineShop.EconomyData, OfflineShop.Climb, OfflineShop.Solo);

        private static readonly string[] AllRecipes =
            { FoodCornerRules.StartUnlock, FoodCornerRules.GoalPurchase, "dish_falafel_wrap", "dish_seitan_wrap", "dish_grilled_veggies", "dish_lemonade" };

        [Test]
        public void ARecipe_IsBoughtWithTwoPresses_AndGoesOnTheNextMenu()
        {
            long cost = Cost("dish_falafel_wrap");
            using OfflineShop shop = OfflineShop.Make(cost + 5000).Restaurant();
            Assert.AreEqual(CookOutcome.ConfirmAgain, shop.Stage.ServerPressBoard(2, 1, 1f));
            Assert.AreEqual(0, shop.Stage.ServerOwned);
            Assert.AreEqual(CookOutcome.Ok, shop.Stage.ServerPressBoard(2, 1, 2f));
            Assert.AreEqual(5000, shop.Economy.ShopBalance, "paid from the shop wallet, exactly");
            Assert.IsTrue(RestaurantMenu.Has(shop.Stage.ServerOwned, 0));
            Assert.IsTrue(RestaurantMenu.Has(shop.Stage.ServerMenuNext, 0), "on the next menu");
            Assert.IsFalse(RestaurantMenu.Has(shop.Stage.ServerMenuNow, 0), "not on this service's menu");
            CollectionAssert.Contains(shop.Stage.ServerUnlocks.ToArray(), "dish_falafel_wrap");
            shop.Stage.ServerServiceOpened();
            Assert.IsTrue(RestaurantMenu.Has(shop.Stage.ServerMenuNow, 0), "frozen at the next service start");
        }

        [Test]
        public void ABuy_IsRefused_WhenShort_WhenRentIsOwed_AndBeforeTheRestaurant()
        {
            long cost = Cost("dish_seitan_wrap");
            using (OfflineShop shop = OfflineShop.Make(cost - 1).Restaurant())
                Assert.AreEqual(CookOutcome.NotEnoughMoney, shop.Stage.ServerPressBoard(1, 2, 1f));
            using (OfflineShop shop = OfflineShop.Make(cost * 10).Restaurant(owed: 300))
            {
                Assert.AreEqual(CookOutcome.PayRentFirst, shop.Stage.ServerPressBoard(1, 2, 1f));
                Assert.AreEqual(CookOutcome.PayRentFirst, shop.Stage.ServerPressBoard(1, 2, 2f));
                Assert.AreEqual(cost * 10, shop.Economy.ShopBalance);
            }
            using (OfflineShop shop = OfflineShop.Make(cost * 10).Corner())
                for (int row = 1; row <= 4; row++) Assert.AreEqual(CookOutcome.NotOpenYet, shop.Stage.ServerPressBoard(1, row, row), "row " + row);
            Assert.AreEqual(RentRules.PayFirstText, "Pay the rent first");
        }

        [Test]
        public void Toggles_RespectTheMenuSize_TheFriesStayOn_AndApplyAtTheNextService()
        {
            using OfflineShop shop = OfflineShop.Make(100000).Restaurant(AllRecipes);
            Assert.AreEqual(0b1111, shop.Stage.ServerOwned);
            Assert.AreEqual(0b0111, shop.Stage.ServerMenuNext, "the fries and three recipes: four dishes");
            shop.Stage.ServerServiceOpened();
            Assert.AreEqual(CookOutcome.MenuFull, shop.Stage.ServerPressBoard(1, 4, 1f), "the drinks do not fit");
            Assert.AreEqual(CookOutcome.Ok, shop.Stage.ServerPressBoard(1, 2, 2f), "the seitan wrap off (no confirm: no money)");
            Assert.AreEqual(0b0101, shop.Stage.ServerMenuNext);
            Assert.AreEqual(CookOutcome.Ok, shop.Stage.ServerPressBoard(1, 4, 3f), "now the drinks fit");
            Assert.AreEqual(0b1101, shop.Stage.ServerMenuNext);
            Assert.AreEqual(0b0111, shop.Stage.ServerMenuNow, "this service keeps its menu");
            Assert.AreEqual(100000, shop.Economy.ShopBalance, "toggles cost nothing");
            shop.Stage.ServerServiceEnded(new Npc.ShiftSummary(1, 500, 4, 0));
            shop.Stage.ServerServiceOpened();
            Assert.AreEqual(0b1101, shop.Stage.ServerMenuNow, "the next service has the new menu");
            Assert.AreEqual(CookOutcome.Ok, shop.Stage.ServerPressBoard(1, 0, 4f), "the gate row: nothing to press");
            CollectionAssert.AreEquivalent(new[] { "dish_seitan_wrap" }, shop.Stage.ServerMenuOff, "the save's menuOff");
        }

        [Test]
        public void Decide_IsThePureRule()
        {
            var corner = new GoalBoardRules.State(1, 32500, 30000, false, 0, 0, 0, 2500);
            Assert.AreEqual(CookOutcome.NotEnoughMoney, GoalBoardRules.Decide(new GoalBoardRules.State(1, 32499, 30000, false, 0, 0, 0, 2500), 0, 4, 0, 4, out _),
                "057 review: renting keeps the $25 reserve");
            Assert.AreEqual(CookOutcome.Ok, GoalBoardRules.Decide(corner, 0, 4, 0, 4, out GoalBoardAction rent));
            Assert.AreEqual(GoalBoardAction.Rent, rent);
            Assert.IsTrue(GoalBoardRules.NeedsConfirm(rent));
            var restaurant = new GoalBoardRules.State(2, 100, 0, false, 0b0001, 0b0001, 0);
            Assert.AreEqual(CookOutcome.Ok, GoalBoardRules.Decide(restaurant, 1, 4, 99999, 4, out GoalBoardAction toggle));
            Assert.AreEqual(GoalBoardAction.Toggle, toggle);
            Assert.IsFalse(GoalBoardRules.NeedsConfirm(toggle));
            Assert.AreEqual(CookOutcome.Ok, GoalBoardRules.Decide(restaurant, 2, 4, 100, 4, out GoalBoardAction buy));
            Assert.AreEqual(GoalBoardAction.Buy, buy);
            Assert.AreEqual(CookOutcome.NotEnoughMoney, GoalBoardRules.Decide(restaurant, 2, 4, 101, 4, out _));
            Assert.AreEqual(CookOutcome.InvalidOption, GoalBoardRules.Decide(restaurant, -1, 4, 0, 4, out _));
            Assert.AreEqual(CookOutcome.InvalidOption, GoalBoardRules.Decide(restaurant, 3, 2, 0, 4, out _), "only as many rows as recipes");
            Assert.AreEqual(CookOutcome.MenuFull, GoalBoardRules.Decide(new GoalBoardRules.State(2, 0, 0, false, 0b11, 0b01, 0), 2, 4, 0, 2, out _),
                "a menu of two dishes: the fries and one");
        }

        [Test]
        public void BoardConfirm_ThePlayerAndTheRowAndFourSeconds()
        {
            var confirm = new BoardConfirm();
            Assert.IsFalse(confirm.Press(1, 0, 0f));
            Assert.IsTrue(confirm.IsArmed(1, 0, 3.9f));
            Assert.IsFalse(confirm.Press(1, 2, 1f), "another row arms that row");
            Assert.IsTrue(confirm.Press(1, 2, 2f));
            Assert.IsFalse(confirm.IsArmed(1, 2, 2f), "a confirm disarms");
            Assert.IsFalse(confirm.Press(1, 0, 10f));
            Assert.IsFalse(confirm.Press(2, 0, 11f), "player 2 does not confirm player 1");
            Assert.IsTrue(confirm.Press(1, 0, 14f), "exactly 4 s still confirms");
            Assert.IsFalse(confirm.Press(3, 0, 20f));
            Assert.IsFalse(confirm.Press(3, 0, 24.01f), "past 4 s it arms again");
            confirm.Clear();
            Assert.IsFalse(confirm.IsArmed(3, 0, 24.5f));
        }

        [Test]
        public void TheBoardsTexts_ComeFromTheSyncedValues()
        {
            ClimbCatalog climb = OfflineShop.Climb;
            string[] corner = GoalBoardView.Texts(1, 1000, 4100, false, 0, 0, 0, 0, 0, 0, 1, false, OfflineShop.EconomyData, climb);
            Assert.AreEqual("Rent the restaurant: $41.00 + $25.00 kept for stock (press twice)", corner[0]);
            Assert.AreEqual("Falafel Wrap Recipe: opens with your restaurant", corner[1]);
            string[] pending = GoalBoardView.Texts(1, 1000, 4100, true, 0, 0, 0, 0, 0, 0, 1, false, OfflineShop.EconomyData, climb);
            StringAssert.StartsWith("Restaurant rented", pending[0]);
            string[] restaurant = GoalBoardView.Texts(2, 4120, 16400, false, 0b0011, 0b0011, 0b0001, 0, 32, 3, 1, false, OfflineShop.EconomyData, climb);
            Assert.AreEqual("Next: service licence · stars 3.2 / 3.5 (last 5) · $41.20 / $164.00", restaurant[0]);
            Assert.AreEqual("Falafel wrap: on the menu", restaurant[1]);
            Assert.AreEqual("Seitan wrap: on the menu (from the next service)", restaurant[2]);
            Assert.AreEqual("Grilled Veggies Recipe: $27.00 (press twice)", restaurant[3], "the solo crew's cost");
            Assert.AreEqual("Lemonade Recipe: $20.00 (press twice)", restaurant[4]);
            string[] first = GoalBoardView.Texts(2, 0, 16400, false, 0, 0, 0, 3050, 0, 0, 4, false, OfflineShop.EconomyData, climb);
            StringAssert.Contains("stars - / 3.5", first[0]);
            StringAssert.Contains(RentRules.OwedText(3050), first[0]);
            StringAssert.Contains(GoalBoardRules.OrderStockText, first[0], "until the first restaurant service ended");
            StringAssert.Contains("$200.00", first[3], "a 4-player crew's cost");
            string[] open = GoalBoardView.Texts(0, 0, 0, false, 0, 0, 0, 0, 0, 0, 1, false, OfflineShop.EconomyData, climb);
            Assert.IsNotEmpty(open[0]);
            Assert.IsEmpty(open[1]);
        }
    }
}
