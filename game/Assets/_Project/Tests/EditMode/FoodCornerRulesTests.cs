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
            Assert.AreEqual(115, FoodCornerRules.Scale(1, 460, 4)); // the 053 scale (the old whole dollars), kept as the rule's example
            Assert.AreEqual(2, FoodCornerRules.Scale(1, 5, 4), "rounded up");
            Assert.AreEqual(0, FoodCornerRules.Scale(0, 460, 4));
            Assert.AreEqual(0, FoodCornerRules.Scale(-3, 460, 4));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => FoodCornerRules.Scale(5, 460, 0));
        }

        [Test]
        public void TheDishPrice_IsTheMenuBoardsReferencePrice() // spec 037 re-pin: it was the snack ceiling payout ($460 of the old scale)
        {
            Assert.IsTrue(Economy.TryGetMarket("snack", out MarketDef _));
            Assert.AreEqual("fries", Climb.Stage1DishId, "open question 1: fries");
            Assert.AreEqual(Worktown.Game.Npc.MenuPrices.Reference(Worktown.Game.Npc.MenuPrices.LineOfDish(FoodCornerRules.GameDish)),
                FoodCornerRules.GameDishPrice(Economy, Climb));
            Assert.AreEqual(400, FoodCornerRules.GameDishPrice(Economy, Climb), "the board's fries: $4.00, the climb's own price");
            Assert.AreEqual(400, FoodCornerRules.SimDishPrice(Climb), "the climb's fries in cents (D39)");
            Assert.AreEqual(1, FoodCornerRules.Scale(1, FoodCornerRules.GameDishPrice(Economy, Climb), FoodCornerRules.SimDishPrice(Climb)),
                "the scale is 1: the corner's amounts are the climb's in cents");
        }

        [Test]
        public void TheRentGoal_ThePitchFeeAndTheStartingMoney_AreTheirScaled052Values()
        {
            long game = FoodCornerRules.GameDishPrice(Economy, Climb), sim = FoodCornerRules.SimDishPrice(Climb);
            Assert.AreEqual(FoodCornerRules.Scale(Climb.Purchase("rent_restaurant").Cost, game, sim), FoodCornerRules.RentGoal(Economy, Climb));
            Assert.AreEqual(FoodCornerRules.Scale(Climb.StageOf(ClimbStage.FoodCorner).Upkeep, game, sim), FoodCornerRules.PitchFee(Economy, Climb));
            Assert.AreEqual(FoodCornerRules.Scale(Climb.StartingMoney, game, sim), FoodCornerRules.StartingMoney(Economy, Climb));
            // Spec 037 re-pin (D39): 052's rent $300, upkeep $5 and start $25 in cents, scale 1 (it was x 115 of the old whole dollars:
            // $34,500, $575 and $2,875; a v1 save's money is converted by the same 100 / 115, SaveMigrations.V1ToV2).
            Assert.AreEqual(30000, FoodCornerRules.RentGoal(Economy, Climb));
            Assert.AreEqual(500, FoodCornerRules.PitchFee(Economy, Climb));
            Assert.AreEqual(2500, FoodCornerRules.StartingMoney(Economy, Climb));
            Assert.AreEqual(30000, Worktown.Game.Save.SaveMigrations.ToCents(34500), "the old goal is the new goal");
            Assert.AreEqual(2500, Worktown.Game.Save.SaveMigrations.ToCents(2875));
            Assert.AreEqual(500, Worktown.Game.Save.SaveMigrations.ToCents(575));
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
            Assert.AreEqual("Save for the rent: $2.30 / $4.00", FoodCornerRules.GoalLine(230, 400)); // spec 037 re-pin: cents
            Assert.AreEqual("Save for the rent: $2.30 / $4.00 (57 %)", FoodCornerRules.GoalProgressLine(230, 400));
            Assert.AreEqual(100, FoodCornerRules.GoalPercent(900, 400));
            Assert.AreEqual(0, FoodCornerRules.GoalPercent(-5, 400));
            Assert.AreEqual(string.Empty, UI.HudView.GoalText(ShopStageRules.OpenKitchen, 230, 400, false), "no goal in the open kitchen");
            Assert.AreEqual("Save for the rent: $2.30 / $4.00", UI.HudView.GoalText(ShopStageRules.FoodCorner, 230, 400, false));
            Assert.AreEqual(FoodCornerRules.GoalReachedText, UI.ShiftSummaryView.GoalText(500, 400, true));
            Assert.AreEqual("Save for the rent: $2.30 / $4.00 (57 %)", UI.ShiftSummaryView.GoalText(230, 400, false));
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

        [Test]
        public void TheDrop_RefundsWhenTheFillFailsAfterThePayment()
        {
            var room = new StockRoom();
            int potato = StockCatalog.IndexOf("potato");
            // The charge goes through, but meanwhile the slot filled up (another path): the fill fails and the payment comes back.
            var wallet = new Wallet(100000) { OnCharge = () => room.ReturnUpToCapacity(potato, 99) };
            int units = room.Drop(potato, Economy, wallet, out long cost);
            Assert.AreEqual(0, units, "nothing dropped");
            Assert.AreEqual(0, cost);
            Assert.AreEqual(1, wallet.Charges, "charged once");
            Assert.AreEqual(1, wallet.Refunds, "refunded once");
            Assert.AreEqual(100000, wallet.Money, "never charged without the stock");
        }

        private sealed class Wallet : ISupplyWallet
        {
            private long _money;
            public int Charges;
            public int Refunds;
            public System.Action OnCharge;

            public Wallet(long money) => _money = money;

            public long Money => _money;

            public bool CanAfford(long cost) => _money >= cost;

            public Cooking.CookOutcome Charge(SupplierOffer offer, int packs, out long cost)
            {
                cost = Supplier.OrderCost(Economy, offer, packs);
                if (cost > _money) return Cooking.CookOutcome.NotEnoughMoney;
                _money -= cost;
                Charges++;
                OnCharge?.Invoke();
                return Cooking.CookOutcome.Ok;
            }

            public void Refund(long cost)
            {
                _money += cost;
                Refunds++;
            }
        }
    }
}
