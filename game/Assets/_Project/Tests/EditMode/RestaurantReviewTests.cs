using NUnit.Framework;
using Worktown.Game.Climb;
using Worktown.Game.Cooking;
using Worktown.Game.Npc;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// 057 review fixes: one-time prices use the largest crew of the last 3 service starts and the players connected now (never solo
    /// right after a load or during a short drop); a second confirm on a row another player just bought does nothing; two players
    /// confirming the rent pay it once; a player who leaves is disarmed; stage 2 has no stock drop.
    /// </summary>
    public class RestaurantReviewTests
    {
        private static long RecipeFor(int players) =>
            RestaurantRules.RecipeCost("dish_falafel_wrap", OfflineShop.EconomyData, OfflineShop.Climb, RestaurantRules.Crew(OfflineShop.Climb, players));

        private static long LicenceFor(int players) =>
            RestaurantRules.GateCost(OfflineShop.EconomyData, OfflineShop.Climb, RestaurantRules.Crew(OfflineShop.Climb, players));

        private static long GoalFor(int players) =>
            RestaurantRules.RentGoal(OfflineShop.EconomyData, OfflineShop.Climb, RestaurantRules.Crew(OfflineShop.Climb, players));

        private static ShiftSummary OneService => new ShiftSummary(2, 1000, 8, 0);

        [Test]
        public void OneTimePlayers_IsTheLargestOfTheLast3StartsAndNow()
        {
            Assert.AreEqual(4, RestaurantRules.OneTimePlayers(new[] { 4, 1, 1 }, 1));
            Assert.AreEqual(1, RestaurantRules.OneTimePlayers(new[] { 4, 1, 1, 1 }, 1), "only the last 3 count");
            Assert.AreEqual(6, RestaurantRules.OneTimePlayers(new[] { 2 }, 6));
            Assert.AreEqual(1, RestaurantRules.OneTimePlayers(null, 0), "at least one player");
            Assert.AreEqual(16, RestaurantRules.OneTimePlayers(new[] { 30 }, 0));
            CollectionAssert.AreEqual(new[] { 2, 3, 4 }, RestaurantRules.PushCrew(new[] { 1, 2, 3 }, 4));
            CollectionAssert.AreEqual(new[] { 1, 16 }, RestaurantRules.PushCrew(new[] { -2, 99 }, 0));
        }

        [Test]
        public void BeginWithOnePlayer_FourConnect_ThePressChargesTheFourPlayerCost()
        {
            using OfflineShop shop = OfflineShop.Make(100000);
            int connected = 1;
            shop.Stage.ConnectedPlayers = () => connected;
            shop.Restaurant();
            Assert.AreEqual(LicenceFor(1), shop.Stage.ServerGoal, "the load: one player so far");
            connected = 4;
            Assert.AreEqual(CookOutcome.ConfirmAgain, shop.Stage.ServerPressBoard(1, 1, 1f));
            Assert.AreEqual(LicenceFor(4), shop.Stage.ServerGoal, "the press re-measured the crew (and pushed it)");
            Assert.AreEqual(CookOutcome.Ok, shop.Stage.ServerPressBoard(1, 1, 2f));
            Assert.AreEqual(100000 - RecipeFor(4), shop.Economy.ShopBalance, "the 4-player cost ($200), not the solo $27");
        }

        [Test]
        public void FourLeave_PricesStayForThreeServices_ThenDrop()
        {
            using OfflineShop shop = OfflineShop.Make(1000000);
            int connected = 4;
            shop.Stage.ConnectedPlayers = () => connected;
            shop.Restaurant();
            shop.Stage.ServerServiceOpened(); // service 1: four players
            Assert.AreEqual(LicenceFor(4), shop.Stage.ServerGoal);
            shop.Stage.ServerServiceEnded(OneService);
            connected = 1;
            shop.Stage.ServerServiceOpened(); // service 2: alone
            Assert.AreEqual(LicenceFor(4), shop.Stage.ServerGoal, "service 2");
            shop.Stage.ServerServiceEnded(OneService);
            shop.Stage.ServerServiceOpened(); // service 3
            Assert.AreEqual(LicenceFor(4), shop.Stage.ServerGoal, "service 3");
            CollectionAssert.AreEqual(new[] { 4, 1, 1 }, shop.Stage.ServerCrewStarts);
            shop.Stage.ServerServiceEnded(OneService);
            shop.Stage.ServerServiceOpened(); // service 4: the four left three starts ago
            Assert.AreEqual(LicenceFor(1), shop.Stage.ServerGoal, "then the price drops");
            Assert.AreEqual(1, shop.Stage.ServerPlayers);
        }

        [Test]
        public void ADropToOnePlayer_ForAMoment_KeepsTheFourPlayerPrice()
        {
            using OfflineShop shop = OfflineShop.Make(100000);
            int connected = 4;
            shop.Stage.ConnectedPlayers = () => connected;
            shop.Restaurant();
            shop.Stage.ServerServiceOpened();
            connected = 1;
            shop.Stage.ServerPressBoard(2, 1, 1f);
            Assert.AreEqual(CookOutcome.Ok, shop.Stage.ServerPressBoard(2, 1, 2f));
            Assert.AreEqual(100000 - RecipeFor(4), shop.Economy.ShopBalance);
        }

        [Test]
        public void ALoad_KeepsTheSavedCrew_BeforeAnyoneJoined()
        {
            using OfflineShop shop = OfflineShop.Make(100000);
            shop.Stage.ConnectedPlayers = () => 0;
            shop.Stage.ServerBegin(ShopStageRules.FoodCorner, 500, false, false, new[] { FoodCornerRules.StartUnlock }, 0, null, null, new[] { 4, 4 });
            Assert.AreEqual(GoalFor(4), shop.Stage.ServerGoal, "the rent goal of the crew that played, not a solo one");
            CollectionAssert.AreEqual(new[] { 4, 4 }, shop.Stage.ServerCrewStarts, "saved again as it came");
        }

        [Test]
        public void ASecondConfirm_OnARowAnotherPlayerJustBought_DoesNothing()
        {
            long cost = RecipeFor(1);
            using OfflineShop shop = OfflineShop.Make(cost * 5).Restaurant();
            Assert.AreEqual(CookOutcome.ConfirmAgain, shop.Stage.ServerPressBoard(1, 1, 1f), "player 1 arms");
            shop.Stage.ServerPressBoard(2, 1, 1.5f);
            Assert.AreEqual(CookOutcome.Ok, shop.Stage.ServerPressBoard(2, 1, 2f), "player 2 buys it");
            Assert.IsTrue(RestaurantMenu.Has(shop.Stage.ServerMenuNext, 0));
            Assert.AreEqual(CookOutcome.Ok, shop.Stage.ServerPressBoard(1, 1, 3f), "player 1's confirm");
            Assert.IsTrue(RestaurantMenu.Has(shop.Stage.ServerMenuNext, 0), "it did not toggle the new dish off");
            Assert.AreEqual(cost * 4, shop.Economy.ShopBalance, "paid once");
            Assert.AreEqual(CookOutcome.Ok, shop.Stage.ServerPressBoard(1, 1, 4f), "a fresh press toggles as usual");
            Assert.IsFalse(RestaurantMenu.Has(shop.Stage.ServerMenuNext, 0));
        }

        [Test]
        public void TwoPlayersConfirmingTheRent_PayItOnce()
        {
            long goal = GoalFor(1);
            using OfflineShop shop = OfflineShop.Make(goal * 3 + 2500).Corner();
            shop.Stage.ServerServiceOpened();
            Assert.AreEqual(CookOutcome.ConfirmAgain, shop.Stage.ServerPressBoard(1, 0, 1f));
            Assert.AreEqual(CookOutcome.ConfirmAgain, shop.Stage.ServerPressBoard(2, 0, 1.2f));
            Assert.AreEqual(CookOutcome.Ok, shop.Stage.ServerPressBoard(1, 0, 2f));
            Assert.AreEqual(CookOutcome.MoveWaits, shop.Stage.ServerPressBoard(2, 0, 2.2f), "the second confirm finds it rented");
            Assert.AreEqual(goal * 2 + 2500, shop.Economy.ShopBalance, "the rent left once");
            Assert.AreEqual(StageEnd.Moved, shop.Stage.ServerServiceEnded(OneService));
            Assert.AreEqual(ShopStageRules.FirstRestaurant, shop.Stage.ServerStage);
        }

        [Test]
        public void APlayerWhoLeft_IsDisarmed()
        {
            using OfflineShop shop = OfflineShop.Make(100000).Restaurant();
            Assert.AreEqual(CookOutcome.ConfirmAgain, shop.Stage.ServerPressBoard(5, 1, 1f));
            shop.Stage.ServerPlayerLeft(5);
            Assert.AreEqual(CookOutcome.ConfirmAgain, shop.Stage.ServerPressBoard(5, 1, 2f), "a rejoin must arm again");
            Assert.AreEqual(100000, shop.Economy.ShopBalance);
        }

        [Test]
        public void AC6_Stage2_HasNoStockDrop()
        {
            Assert.IsTrue(ShopStageRules.DropsStock(ShopStageRules.FoodCorner));
            Assert.IsFalse(ShopStageRules.DropsStock(ShopStageRules.FirstRestaurant), "the restaurant orders at the terminal");
            Assert.IsFalse(ShopStageRules.DropsStock(ShopStageRules.OpenKitchen));
            using OfflineShop shop = OfflineShop.Make(100000).Restaurant();
            Assert.AreEqual(0, shop.Stage.ServerServiceOpened(), "nothing dropped at a stage 2 service start");
            Assert.AreEqual(100000, shop.Economy.ShopBalance, "and nothing paid for a drop");
        }
    }
}
