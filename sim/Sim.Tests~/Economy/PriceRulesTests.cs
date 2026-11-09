using System.Linq;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;
using Xunit;
using T = Worktown.Sim.Tests.Cooking.TestItems;

namespace Worktown.Sim.Tests.Economy
{
    public class PriceRulesTests
    {
        private static readonly MarketDef Snack = TestEconomy.Catalog.Markets.Single(m => m.Id == "snack"); // F100 C400 smoothing 25

        [Fact]
        public void QualityPermilleTruncatesClampsAndMapsNanToZero()
        {
            Assert.Equal(0, PriceRules.QualityPermille(0f));
            Assert.Equal(1000, PriceRules.QualityPermille(1f));
            Assert.Equal(250, PriceRules.QualityPermille(0.25f));
            Assert.Equal(999, PriceRules.QualityPermille(0.9999f)); // 999.9 truncates
            Assert.Equal(0, PriceRules.QualityPermille(-3f));
            Assert.Equal(1000, PriceRules.QualityPermille(7f));
            Assert.Equal(0, PriceRules.QualityPermille(float.NaN));
        }

        [Fact]
        public void DemandPayoutScalesHalfToOneAndAHalf()
        {
            Assert.Equal(480, PriceRules.DemandPayout(400, 700));   // 400*1200/1000
            Assert.Equal(200, PriceRules.DemandPayout(400, 0));     // 400*500/1000
            Assert.Equal(1800, PriceRules.DemandPayout(1200, 1000)); // 1200*1500/1000
            Assert.Equal(1498, PriceRules.DemandPayout(999, 1000)); // 1498.5 floors
            Assert.Equal(1, PriceRules.DemandPayout(1, 0));         // 0 raised to 1
        }

        [Fact]
        public void WholesalePayoutScalesHalfToOneOfFloor()
        {
            Assert.Equal(85, PriceRules.WholesalePayout(100, 700));  // 100*(500+350)/1000
            Assert.Equal(100, PriceRules.WholesalePayout(100, 1000)); // 100*1000/1000
            Assert.Equal(172, PriceRules.WholesalePayout(300, 150)); // 300*575/1000 = 172.5
            Assert.Equal(1, PriceRules.WholesalePayout(1, 0));       // 0 raised to 1
        }

        [Fact]
        public void NextPriceFallsWithSupplyAndRisesWithoutIt()
        {
            // D=10, S=20: target = 400 - 300*min(20,20)/20 = 100; step = (100-400)*25/100 = -75
            Assert.Equal(325, PriceRules.NextPrice(Snack, 400, 10, 20));
            // D=10, S=5: target = 400 - 300*5/20 = 325; step = (325-400)*25/100 = -18 (toward zero)
            Assert.Equal(382, PriceRules.NextPrice(Snack, 400, 10, 5));
            // D=10, S=0: target = 400; from 100 step = 300*25/100 = 75
            Assert.Equal(175, PriceRules.NextPrice(Snack, 100, 10, 0));
            // S far above 2D is capped: same as S = 2D
            Assert.Equal(325, PriceRules.NextPrice(Snack, 400, 10, 999));
        }

        [Fact]
        public void NextPriceWithoutDemandHoldsOrDropsToFloor()
        {
            Assert.Equal(250, PriceRules.NextPrice(Snack, 250, 0, 0));
            // D=0, S=3: target = Floor 100; step = (100-250)*25/100 = -37
            Assert.Equal(213, PriceRules.NextPrice(Snack, 250, 0, 3));
        }

        [Fact]
        public void NextPriceMovesAtLeastOneAndClampsToBounds()
        {
            Assert.Equal(100, PriceRules.NextPrice(Snack, 101, 10, 20)); // step 0 -> sign -> -1
            Assert.Equal(399, PriceRules.NextPrice(Snack, 398, 10, 0));  // step 0 -> sign -> +1
            Assert.Equal(400, PriceRules.NextPrice(Snack, 5000, 10, 0)); // clamped to Ceiling
            Assert.Equal(100, PriceRules.NextPrice(Snack, 3, 0, 5));     // clamped to Floor
            Assert.Equal(400, PriceRules.NextPrice(Snack, 400, 10, 0));  // stable at the ceiling
        }

        [Fact]
        public void ItemCostSumsGrocerPricesAndIsNullWhenUnpriced()
        {
            Assert.Equal(60L, PriceRules.ItemCost(TestEconomy.Catalog, TestEconomy.PlatedFries()));
            Assert.Equal(220L, PriceRules.ItemCost(TestEconomy.Catalog, TestEconomy.PlatedTomatoCashew())); // 70+150
            var noBasil = TestEconomy.Build(grocer: TestEconomy.GrocerWithout("basil"));
            Assert.Null(PriceRules.ItemCost(noBasil, T.Ok(T.Plate(T.Base("basil")))));
        }

        [Fact]
        public void SurplusCapIsCostMinusOneMinusWeightTimesB()
        {
            // Spec 034: the cost is the supplier's bulk unit price (80 %, never below B + 1 = 32), not the grocer price.
            Assert.Equal(16, PriceRules.SurplusCap(TestEconomy.Catalog, TestEconomy.PlatedFries()));          // 48-1-31
            Assert.Equal(113, PriceRules.SurplusCap(TestEconomy.Catalog, TestEconomy.PlatedTomatoCashew())); // (56+120)-1-62
            Assert.Equal(0, PriceRules.SurplusCap(TestEconomy.Catalog, TestEconomy.PlatedSalt()));           // 32-1-31
            var noBasil = TestEconomy.Build(grocer: TestEconomy.GrocerWithout("basil"));
            Assert.Equal(0, PriceRules.SurplusCap(noBasil, T.Ok(T.Plate(T.Base("basil")))));
        }

        [Fact]
        public void ItemWholesalePayoutIsTheLowerOfFloorPayoutAndCap()
        {
            var snack = Snack;
            var meal = TestEconomy.Catalog.Markets.Single(m => m.Id == "meal");
            // Spec 034: the caps at the bulk unit price.
            // fries q=696: WholesalePayout(100,696) = 100*848/1000 = 84, cap 16
            Assert.Equal(16, PriceRules.ItemWholesalePayout(TestEconomy.Catalog, snack, TestEconomy.PlatedFries(), 696));
            // plated salt q=166: 100*583/1000 = 58, cap 0 (the wholesaler refuses below 1)
            Assert.Equal(0, PriceRules.ItemWholesalePayout(TestEconomy.Catalog, snack, TestEconomy.PlatedSalt(), 166));
            // tomato+cashew q=746: 300*873/1000 = 261, cap 113
            Assert.Equal(113, PriceRules.ItemWholesalePayout(TestEconomy.Catalog, meal, TestEconomy.PlatedTomatoCashew(), 746));
            // apple q=0: 100*500/1000 = 50, cap 64-1-31 = 32 -> 32
            Assert.Equal(32, PriceRules.ItemWholesalePayout(TestEconomy.Catalog, snack, TestEconomy.PlatedApple(), 0));
            // a low quality pays the floor payout when it is below the cap: tomato+cashew q=0: 300*500/1000 = 150, cap 113 -> 113
            Assert.Equal(113, PriceRules.ItemWholesalePayout(TestEconomy.Catalog, meal, TestEconomy.PlatedTomatoCashew(), 0));
        }

        [Fact]
        public void ArbitrageGridWholesaleAndNpcDishNeverReachCeiling() // AC2
        {
            long[] floors = { 1, 2, 3, 7, 100, 300, 999 };
            foreach (long f in floors)
            {
                long[] ceilings = { (3 * f + 1) / 2, 2 * f, 4 * f, EconomyLimits.MaxPrice };
                foreach (long c in ceilings)
                {
                    for (int i = 0; i <= 8; i++)
                    {
                        long p = f + (c - f) * i / 8;
                        for (int q = 0; q <= 1000; q += 10)
                            Assert.True(PriceRules.WholesalePayout(f, q) < c, $"wholesale F={f} C={c} q={q}");
                        for (int qn = 0; qn <= 499; qn += 7)
                            Assert.True(PriceRules.DemandPayout(p, qn) < c, $"demand F={f} C={c} P={p} qN={qn}");
                    }
                }
            }
        }

        [Fact]
        public void NextPriceNeverLeavesTheBand()
        {
            for (long price = 100; price <= 400; price += 13)
                for (int d = 0; d <= 6; d++)
                    for (int s = 0; s <= 14; s++)
                    {
                        long n = PriceRules.NextPrice(Snack, price, d, s);
                        Assert.InRange(n, 100L, 400L);
                    }
        }
    }
}
