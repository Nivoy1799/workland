using NUnit.Framework;
using Worktown.Game.Climb;
using Worktown.Game.Npc;
using Worktown.Game.Save;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 057 AC4: the stage 2 gate: a missed guest counts one star at half weight; an empty service is not counted; the window keeps the
    /// newest services; the gate flips once and survives a save and load.
    /// </summary>
    public class GateRulesTests
    {
        [Test]
        public void AMissedGuest_CountsOneStarAtHalfWeight()
        {
            Assert.AreEqual(33, GateRules.ServiceStarsTenths(3, 2, 12), "w = 8: (240 + 20 + 4) / 8");
            Assert.AreEqual(40, GateRules.ServiceStarsTenths(4, 0, 16));
            Assert.AreEqual(50, GateRules.ServiceStarsTenths(4, 0, 20), "(400 + 4) / 8 rounds to 5.0");
            Assert.AreEqual(10, GateRules.ServiceStarsTenths(0, 3, 0), "only missed guests: one star");
            Assert.AreEqual(30, GateRules.ServiceStarsTenths(1, 2, 5), "w = 4: (100 + 20 + 2) / 4");
            Assert.AreEqual(GateRules.ServiceStarsTenths(3, 2, 12), GateRules.ServiceStarsTenths(new ShiftSummary(3, 900, 12, 2)));
        }

        [Test]
        public void AnEmptyService_IsNotCounted()
        {
            Assert.AreEqual(GateRules.NotCounted, GateRules.ServiceStarsTenths(0, 0, 0));
            CollectionAssert.AreEqual(new[] { 40 }, GateRules.Push(new[] { 40 }, GateRules.NotCounted));
        }

        [Test]
        public void TheWindow_KeepsTheNewest()
        {
            int[] window = new int[0];
            for (int i = 1; i <= 10; i++) window = GateRules.Push(window, 10 + i);
            CollectionAssert.AreEqual(new[] { 13, 14, 15, 16, 17, 18, 19, 20 }, window, "at most 8, newest last");
            Assert.AreEqual(18, GateRules.AverageTenths(window, 5), "the last 5: 16..20");
            Assert.AreEqual(5, GateRules.Counted(window, 5));
            Assert.IsTrue(GateRules.WindowFull(window, 5));
            Assert.IsFalse(GateRules.WindowFull(new[] { 40, 40 }, 5));
            Assert.AreEqual(0b10110, GateRules.Pips(new[] { 50, 20, 36, 35, 34, 40 }, 5, 35), "oldest first of the last 5: 20 36 35 34 40");
        }

        [Test]
        public void TheGate_NeedsAFullWindow_TheStars_AndTheMoney()
        {
            int[] good = { 36, 35, 40, 34, 36 };
            Assert.IsTrue(GateRules.GateHolds(good, 5, 35, 120000, 120000));
            Assert.IsFalse(GateRules.GateHolds(good, 5, 35, 119999, 120000), "short of the licence");
            Assert.IsFalse(GateRules.GateHolds(new[] { 50, 50, 50, 50 }, 5, 35, 999999, 120000), "four services are not five");
            Assert.IsFalse(GateRules.GateHolds(new[] { 36, 35, 40, 34, 29 }, 5, 35, 999999, 120000), "3.5 needed, 3.4 is the average");
            Assert.IsTrue(GateRules.GateHolds(new[] { 36, 35, 40, 34, 30 }, 5, 35, 120000, 120000), "exactly 3.5 holds");
            Assert.AreEqual("* * o - -", GateRules.PipText(0b011, 3, 5));
        }

        [Test]
        public void TheGate_FlipsOnce_AndSurvivesASaveAndALoad()
        {
            using OfflineShop shop = OfflineShop.Make(30000).Restaurant(window: new[] { 40, 40, 40, 40 });
            Assert.AreEqual(StageEnd.GoalReached, shop.Stage.ServerServiceEnded(new ShiftSummary(4, 2000, 20, 0)));
            Assert.IsTrue(shop.Stage.ServerReached);
            Assert.AreEqual(30000 - RestaurantRules.Due(OfflineShop.EconomyData, OfflineShop.Climb, OfflineShop.Solo, null), shop.Economy.ShopBalance,
                "the rent was paid before the gate was checked");
            Assert.AreEqual(StageEnd.None, shop.Stage.ServerServiceEnded(new ShiftSummary(4, 2000, 20, 0)), "once");

            ShopSave save = ShopSaveCodecTests.Full();
            SaveService.Fill(save, shop.Economy.ShopBalance, 0, 0, 5, 0, new int[0], null, 100, shop.Stage.ServerStage, shop.Stage.ServerReached);
            SaveService.FillClimb(save, shop.Stage.ServerMovePending, shop.Stage.ServerRentOwed, shop.Stage.ServerWindow, shop.Stage.ServerMenuOff,
                shop.Stage.ServerUnlocks);
            SaveLoadResult loaded = ShopSaveCodec.Default.TryParse(ShopSaveCodec.Default.ToJson(save));
            Assert.IsTrue(loaded.Ok, loaded.Error.ToString());
            Assert.IsTrue(loaded.Save.climb.goalReached);
            CollectionAssert.AreEqual(new[] { 40, 40, 40, 40, 50, 50 }, loaded.Save.climb.starWindow);
            using OfflineShop again = OfflineShop.Make(loaded.Save.money.balance);
            again.Stage.ServerBegin((byte)loaded.Save.climb.stage, 0, loaded.Save.climb.goalReached, loaded.Save.climb.movePending,
                loaded.Save.climb.unlocks, loaded.Save.climb.rentOwed, loaded.Save.climb.starWindow, loaded.Save.climb.menuOff);
            Assert.IsTrue(again.Stage.ServerReached);
            Assert.AreEqual(StageEnd.None, again.Stage.ServerServiceEnded(new ShiftSummary(4, 2000, 20, 0)), "it does not flip again after a load");
        }
    }
}
