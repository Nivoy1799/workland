using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Worktown.Game.Climb;
using Worktown.Game.Cooking;
using Worktown.Game.Npc;
using Worktown.Game.Save;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 057 AC5: the move from the food corner to the first restaurant at the goal board: a press below the goal refuses; one press only
    /// arms; another player's press does not confirm; a confirm after 4 s expires; during a service the move waits for its end; what carries
    /// over; the shop's name; the StageChanged save. The stage runs offline (OfflineShop: one player, D40's solo crew).
    /// </summary>
    public class ShopMoveTests
    {
        private static long Goal => RestaurantRules.RentGoal(OfflineShop.EconomyData, OfflineShop.Climb, OfflineShop.Solo);

        [Test]
        public void APressBelowTheGoal_Refuses()
        {
            using OfflineShop shop = OfflineShop.Make(Goal - 1).Corner();
            Assert.AreEqual(CookOutcome.NotEnoughMoney, shop.Stage.ServerPressBoard(1, 0, 10f));
            Assert.AreEqual(CookOutcome.NotEnoughMoney, shop.Stage.ServerPressBoard(1, 0, 11f), "a refusal arms nothing");
            Assert.AreEqual(ShopStageRules.FoodCorner, shop.Stage.ServerStage);
            Assert.AreEqual(Goal - 1, shop.Economy.ShopBalance);
        }

        [Test]
        public void OnePressArms_AnotherPlayerDoesNotConfirm_AndAfter4sItExpires()
        {
            using OfflineShop shop = OfflineShop.Make(Goal + 900).Corner();
            Assert.AreEqual(4100, Goal, "D40: a solo crew's rent goal ($300 x 0.137, floored to $41)");
            Assert.AreEqual(CookOutcome.ConfirmAgain, shop.Stage.ServerPressBoard(1, 0, 10f));
            Assert.AreEqual(CookOutcome.ConfirmAgain, shop.Stage.ServerPressBoard(2, 0, 11f), "player 2 arms its own confirm");
            Assert.AreEqual(Goal + 900, shop.Economy.ShopBalance, "nothing paid yet");
            Assert.AreEqual(CookOutcome.ConfirmAgain, shop.Stage.ServerPressBoard(1, 0, 14.5f), "4.5 s later: armed again, not paid");
            Assert.AreEqual(ShopStageRules.FoodCorner, shop.Stage.ServerStage);
            Assert.AreEqual(CookOutcome.Ok, shop.Stage.ServerPressBoard(1, 0, 16f), "the second press within 4 s pays");
            Assert.AreEqual(900, shop.Economy.ShopBalance, "the rent goal left the wallet, exactly");
            Assert.AreEqual(ShopStageRules.FirstRestaurant, shop.Stage.ServerStage, "no service open: the move happens at once");
            Assert.AreEqual(1, shop.StageChanges, "the save writes StageChanged");
        }

        [Test]
        public void DuringAService_TheMoveWaitsForItsEnd_AfterThePitchFee()
        {
            using OfflineShop shop = OfflineShop.Make(Goal + 2000).Corner();
            shop.Stage.ServerServiceOpened();
            shop.Stage.ServerPressBoard(1, 0, 1f);
            Assert.AreEqual(CookOutcome.Ok, shop.Stage.ServerPressBoard(1, 0, 2f));
            Assert.IsTrue(shop.Stage.ServerMovePending);
            Assert.AreEqual(ShopStageRules.FoodCorner, shop.Stage.ServerStage, "still the corner while the service runs");
            Assert.AreEqual(CookOutcome.MoveWaits, shop.Stage.ServerPressBoard(1, 0, 3f), "rented already");
            Assert.AreEqual(0, shop.StageChanges);
            StageEnd end = shop.Stage.ServerServiceEnded(new ShiftSummary(2, 800, 9, 0));
            Assert.AreEqual(StageEnd.Moved, end);
            Assert.AreEqual(SaveTrigger.StageChanged, SaveService.TriggerOf(end));
            Assert.IsTrue(AutosaveRules.ShouldSave(SaveTrigger.StageChanged, true, true, 0f, false));
            Assert.AreEqual(ShopStageRules.FirstRestaurant, shop.Stage.ServerStage);
            Assert.IsFalse(shop.Stage.ServerMovePending);
            Assert.AreEqual(2000 - 500, shop.Economy.ShopBalance, "the pitch fee came first, then the move");
            Assert.AreEqual(0, shop.StageChanges, "the service end's own save (StageChanged), not a second one");
        }

        [Test]
        public void TheMove_CarriesTheShopOver_AndRenamesIt()
        {
            using OfflineShop shop = OfflineShop.Make(Goal + 1234).Corner(reached: true);
            ShopSave before = ShopSaveCodecTests.Full();
            before.climb.unlocks = shop.Stage.ServerUnlocks.ToArray();
            shop.Stage.ServerPressBoard(3, 0, 1f);
            shop.Stage.ServerPressBoard(3, 0, 2f);
            Assert.AreEqual(ShopStageRules.FirstRestaurant, shop.Stage.ServerStage);
            Assert.IsFalse(shop.Stage.ServerReached, "the next goal starts open");
            Assert.AreEqual(RestaurantRules.GateCost(OfflineShop.EconomyData, OfflineShop.Climb, OfflineShop.Solo), shop.Stage.ServerGoal,
                "the next goal: the service licence");
            CollectionAssert.IsSubsetOf(before.climb.unlocks, shop.Stage.ServerUnlocks.ToArray(), "unlocks are never lost");
            CollectionAssert.Contains(shop.Stage.ServerUnlocks.ToArray(), FoodCornerRules.GoalPurchase);
            CollectionAssert.Contains(shop.Stage.ServerUnlocks.ToArray(), "future_unlock", "an unknown unlock stays");

            ShopSave after = ShopSaveCodecTests.Full();
            int[] counts = SaveService.CountsOf(before);
            SaveService.Fill(after, shop.Economy.ShopBalance, before.money.earnedTotal, before.money.spentTotal, before.clock.day,
                before.clock.playSeconds, counts, null, before.hygienePercent, shop.Stage.ServerStage, shop.Stage.ServerReached);
            SaveService.FillClimb(after, shop.Stage.ServerMovePending, shop.Stage.ServerRentOwed, shop.Stage.ServerWindow, shop.Stage.ServerMenuOff,
                shop.Stage.ServerUnlocks);
            Assert.AreEqual(1234, after.money.balance, "the wallet less the rent goal");
            Assert.AreEqual(before.saveId, after.saveId);
            Assert.AreEqual(before.clock.day, after.clock.day);
            Assert.AreEqual(before.hygienePercent, after.hygienePercent);
            Assert.AreEqual(before.stats.guestsServed, after.stats.guestsServed);
            Assert.AreEqual(before.stats.starSum, after.stats.starSum);
            Assert.AreEqual(2, after.climb.stage);
            Assert.AreEqual(GoalBoardRules.RestaurantName, after.shopName, "Your restaurant");
            Assert.AreEqual(FoodCornerRules.ShopName, SaveService.ShopNameOf(1));
            SaveLoadResult loaded = ShopSaveCodec.Default.TryParse(ShopSaveCodec.Default.ToJson(after));
            Assert.IsTrue(loaded.Ok, loaded.Error.ToString());
            Assert.AreEqual(2, loaded.Save.climb.stage, "a stage is never lost");
            Assert.AreEqual(GoalBoardRules.RestaurantName, loaded.Save.shopName);
        }

        [Test]
        public void ASaveWrittenWhileTheMoveWaits_MovesInAtTheLoad()
        {
            using OfflineShop shop = OfflineShop.Make(500);
            shop.Stage.ServerBegin(ShopStageRules.FoodCorner, 500, true, true, new[] { FoodCornerRules.StartUnlock }, 0, null, null);
            Assert.AreEqual(ShopStageRules.FirstRestaurant, shop.Stage.ServerStage);
            Assert.IsFalse(shop.Stage.ServerMovePending);
            Assert.AreEqual(500, shop.Economy.ShopBalance, "the rent was paid before the save: nothing is paid twice");
        }

        [Test]
        public void TheOpenKitchen_RefusesEveryRow()
        {
            using OfflineShop shop = OfflineShop.Make(1000000);
            shop.Stage.ServerBegin(ShopStageRules.OpenKitchen, 0, 0, false);
            for (int row = 0; row < GoalBoardRules.RowCount; row++)
                Assert.AreEqual(CookOutcome.NotOpenYet, shop.Stage.ServerPressBoard(1, row, row));
            Assert.AreEqual(CookOutcome.InvalidOption, shop.Stage.ServerPressBoard(1, GoalBoardRules.RowCount, 9f));
        }
    }
}
