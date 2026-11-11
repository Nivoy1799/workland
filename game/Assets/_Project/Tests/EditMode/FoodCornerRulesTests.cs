using NUnit.Framework;
using Worktown.Game.Climb;
using Worktown.Game.Save;
using Worktown.Game.Shop;
using Worktown.Sim.Climb;
using Worktown.Sim.Economy;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 053 AC7: the stage 1 amounts scaled from spec 052 to the game's price level, the goal, the drop.</summary>
    public class FoodCornerRulesTests
    {
        private static readonly EconomyCatalog Economy = EconomyCatalog.CreateDefault();
        private static readonly ClimbCatalog Climb = ClimbCatalog.CreateDefault();

        [Test]
        public void Scale_IsTheCeilingOfTheDishPriceRatio()
        {
            Assert.AreEqual(115, FoodCornerRules.Scale(1, 460, 4));
            Assert.AreEqual(2, FoodCornerRules.Scale(1, 5, 4), "rounded up");
            Assert.AreEqual(0, FoodCornerRules.Scale(0, 460, 4));
            Assert.AreEqual(0, FoodCornerRules.Scale(-3, 460, 4));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => FoodCornerRules.Scale(5, 460, 0));
        }

        [Test]
        public void TheDishPrice_IsTheSnackCeilingPayoutAtTheDishsBaseQuality()
        {
            Assert.IsTrue(Economy.TryGetMarket("snack", out MarketDef snack));
            int quality = Climb.Dish("fries").BaseQualityPermille;
            Assert.AreEqual("fries", Climb.Stage1DishId, "open question 1: fries");
            Assert.AreEqual(PriceRules.DemandPayout(snack.Ceiling, quality), FoodCornerRules.GameDishPrice(Economy, Climb));
            Assert.AreEqual(460, FoodCornerRules.GameDishPrice(Economy, Climb), "the current snack market (ceiling $400) at 650 permille");
            Assert.AreEqual(4, FoodCornerRules.SimDishPrice(Climb));
        }

        [Test]
        public void TheRentGoal_ThePitchFeeAndTheStartingMoney_AreTheirScaled052Values()
        {
            long game = FoodCornerRules.GameDishPrice(Economy, Climb), sim = FoodCornerRules.SimDishPrice(Climb);
            Assert.AreEqual(FoodCornerRules.Scale(Climb.Purchase("rent_restaurant").Cost, game, sim), FoodCornerRules.RentGoal(Economy, Climb));
            Assert.AreEqual(FoodCornerRules.Scale(Climb.StageOf(ClimbStage.FoodCorner).Upkeep, game, sim), FoodCornerRules.PitchFee(Economy, Climb));
            Assert.AreEqual(FoodCornerRules.Scale(Climb.StartingMoney, game, sim), FoodCornerRules.StartingMoney(Economy, Climb));
            // The default numbers today (052: rent 300, upkeep 5, start 25; x 115).
            Assert.AreEqual(34500, FoodCornerRules.RentGoal(Economy, Climb));
            Assert.AreEqual(575, FoodCornerRules.PitchFee(Economy, Climb));
            Assert.AreEqual(2875, FoodCornerRules.StartingMoney(Economy, Climb));
        }

        [Test]
        public void TheGoal_FlipsOnce_AndSurvivesASaveAndLoad()
        {
            Assert.IsFalse(FoodCornerRules.ReachesGoal(false, 399, 400));
            Assert.IsTrue(FoodCornerRules.ReachesGoal(false, 400, 400));
            Assert.IsFalse(FoodCornerRules.ReachesGoal(true, 900, 400), "once");
            Assert.IsFalse(FoodCornerRules.ReachesGoal(false, 900, 0), "no goal");
            ShopSave save = ShopSaveCodecTests.Full();
            save.climb.goalReached = true;
            SaveLoadResult back = ShopSaveCodec.Default.TryParse(ShopSaveCodec.Default.ToJson(save));
            Assert.IsTrue(back.Ok);
            Assert.IsTrue(back.Save.climb.goalReached);
            save.climb.goalReached = false;
            Assert.IsFalse(ShopSaveCodec.Default.TryParse(ShopSaveCodec.Default.ToJson(save)).Save.climb.goalReached);
        }

        [Test]
        public void TheGoalLines_ReadAsTheSpecSays()
        {
            Assert.AreEqual("Save for the rent: $230 / $400", FoodCornerRules.GoalLine(230, 400));
            Assert.AreEqual("Save for the rent: $230 / $400 (57 %)", FoodCornerRules.GoalProgressLine(230, 400));
            Assert.AreEqual(100, FoodCornerRules.GoalPercent(900, 400));
            Assert.AreEqual(0, FoodCornerRules.GoalPercent(-5, 400));
            Assert.AreEqual(string.Empty, UI.HudView.GoalText(ShopStageRules.OpenKitchen, 230, 400, false), "no goal in the open kitchen");
            Assert.AreEqual("Save for the rent: $230 / $400", UI.HudView.GoalText(ShopStageRules.FoodCorner, 230, 400, false));
            Assert.AreEqual(FoodCornerRules.GoalReachedText, UI.ShiftSummaryView.GoalText(500, 400, true));
            Assert.AreEqual("Save for the rent: $230 / $400 (57 %)", UI.ShiftSummaryView.GoalText(230, 400, false));
        }

        [Test]
        public void TheDrop_FillsThePotatoSlot_OnlyWhatTheMoneyCovers_PaidOnce()
        {
            Assert.AreEqual(6, FoodCornerRules.DropUnits(6, 12, n => true));
            Assert.AreEqual(0, FoodCornerRules.DropUnits(12, 12, n => true), "full");
            Assert.AreEqual(2, FoodCornerRules.DropUnits(6, 12, n => n <= 2), "only what the money covers");
            Assert.AreEqual(0, FoodCornerRules.DropUnits(6, 12, n => false));

            var room = new StockRoom();
            int potato = StockCatalog.IndexOf("potato");
            var wallet = new Wallet(200);
            int units = room.Drop(potato, Economy, wallet, out long cost);
            Assert.IsTrue(Economy.TryGetGrocerPrice("potato", out long grocer));
            Assert.AreEqual(Supplier.OrderCost(Economy, grocer, units), cost, "the bulk price");
            Assert.LessOrEqual(cost, 200);
            Assert.Greater(units, 0);
            Assert.Less(units, 6, "$200 does not cover the whole slot");
            Assert.AreEqual(1, wallet.Charges, "paid once");
            Assert.AreEqual(6 + units, room.LineOf(potato).Count);
            var rich = new Wallet(100000);
            Assert.AreEqual(12 - room.LineOf(potato).Count, room.Drop(potato, Economy, rich, out _));
            Assert.AreEqual(12, room.LineOf(potato).Count, "up to the capacity");
            Assert.AreEqual(0, room.Drop(potato, Economy, rich, out long none), "full: no drop");
            Assert.AreEqual(0, none, "and nothing charged");
            Assert.AreEqual(1, rich.Charges);
        }

        private sealed class Wallet : ISupplyWallet
        {
            private long _money;
            public int Charges;

            public Wallet(long money) => _money = money;

            public bool CanAfford(long cost) => _money >= cost;

            public Cooking.CookOutcome Charge(SupplierOffer offer, int packs, out long cost)
            {
                cost = Supplier.OrderCost(Economy, offer, packs);
                if (cost > _money) return Cooking.CookOutcome.NotEnoughMoney;
                _money -= cost;
                Charges++;
                return Cooking.CookOutcome.Ok;
            }

            public void Refund(long cost) => _money += cost;
        }
    }
}
