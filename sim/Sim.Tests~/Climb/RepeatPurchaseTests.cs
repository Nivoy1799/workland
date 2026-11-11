using System;
using System.Linq;
using Worktown.Sim.Climb;
using Xunit;
using T = Worktown.Sim.Tests.Climb.TestClimb;

namespace Worktown.Sim.Tests.Climb
{
    /// <summary>Spec 052b (c): repeatable purchases whose next copy costs more (AC5).</summary>
    public class RepeatPurchaseTests
    {
        private static readonly PurchaseDef Stall = new PurchaseDef("stall", "Stall", ClimbStage.FoodCorner, PurchaseKind.Productive, 10, 2,
            PurchaseEffect.None with { WorkSpots = 1, CeilingBonusPermille = 100 }, 3, 1600);
        private static readonly PurchaseDef Statue = new PurchaseDef("statue", "Statue", ClimbStage.BeyondKitchen, PurchaseKind.Status, 10, 0,
            PurchaseEffect.None with { ReputationBonus = 5 }, 0, 1000);

        private static ClimbCatalog Catalog() => T.Build(purchases: T.Purchases.Concat(new[] { Stall, Statue }));

        [Fact]
        public void CostOfMatchesLiterals() // AC5
        {
            var place = new PurchaseDef("p", "P", ClimbStage.ServiceMix, PurchaseKind.Productive, 3000, 40, PurchaseEffect.None, 4, 1600);
            Assert.Equal(new long[] { 3000, 4800, 7680, 12288, 19660 }, Enumerable.Range(0, 5).Select(k => place.CostOf(k)));
            var cents = place with { Cost = 300000 };
            Assert.Equal(new long[] { 300000, 480000, 768000, 1228800, 1966000 }, Enumerable.Range(0, 5).Select(k => cents.CostOf(k, 100))); // whole dollars each step
            Assert.Equal(3000, (place with { CostGrowthPermille = 1000 }).CostOf(4)); // flat
            Assert.Throws<ArgumentOutOfRangeException>(() => place.CostOf(-1));
        }

        [Fact]
        public void CanBuyMoreRespectsMaxCount()
        {
            Assert.True(Stall.CanBuyMore(2));
            Assert.False(Stall.CanBuyMore(3));
            Assert.True(Statue.CanBuyMore(1000)); // 0 = no limit
            Assert.False(T.Catalog.Purchase("peeler").CanBuyMore(1)); // the default: buy once
        }

        [Fact]
        public void MaxCountIsNeverExceeded() // AC5
        {
            ClimbCatalog c = Catalog();
            var (s, bought, spent) = ClimbPolicy.Decide(c, T.State(ClimbStage.FoodCorner, 10_000, "lease") with { Stage = ClimbStage.FirstRestaurant },
                new CrewSetup(1, "worker", new ClimbStrategy(PricePolicy.Match, SpendPolicy.Builder), 1));
            Assert.Equal(3, s.Count("stall"));
            Assert.Equal(3, bought.Count(id => id == "stall"));
            Assert.DoesNotContain(ClimbPolicy.Candidates(c, s), p => p.Id == "stall");
            Assert.True(spent >= 10 + 16 + 25);
        }

        [Fact]
        public void UpkeepAndEffectsCountPerCopy() // AC5
        {
            ClimbCatalog c = Catalog();
            ClimbState one = T.State(ClimbStage.FoodCorner, 0, "stall"), three = T.State(ClimbStage.FoodCorner, 0, "stall", "stall", "stall");
            Assert.Equal(c.Upkeep(ClimbStage.FoodCorner, one.Owned) + 4, c.Upkeep(ClimbStage.FoodCorner, three.Owned));
            Assert.Equal(3, c.PurchaseWorkSpots(three.Owned));
            Assert.Equal(1 + 3, ClimbEngine.WorkSpots(c, three));
            Assert.Equal(300, c.CeilingBonusPermille(three.Owned, "counter"));
            Assert.Equal(3, three.Count("stall"));
            Assert.True(three.Owns("stall"));
            Assert.Equal(0, three.Count("sign"));
            Assert.Equal(25, ClimbPolicy.CostNow(c, T.State(ClimbStage.FoodCorner, 0, "stall", "stall"), Stall)); // 10, 16, then 25.6 floored
        }

        [Fact]
        public void MaxCountZeroKeepsBuyingWhileAffordable() // AC5
        {
            ClimbCatalog c = Catalog();
            string[] everything = { "peeler", "sign", "lease", "soup_recipe", "booth_kit", "helper", "lab", "stall", "stall", "stall" };
            var (s, bought, _) = ClimbPolicy.Decide(c, T.State(ClimbStage.BeyondKitchen, 1000, everything), T.Setup());
            Assert.Equal(99, s.Count("statue")); // flat $10 statues, bought while the balance holds twice the cost
            Assert.Equal(10, s.Balance); // stops only when the next one is not affordable
            Assert.Equal(99, bought.Count);
            Assert.Contains(10L, ClimbPolicy.GoalCosts(c, s)); // the next statue is still something to save for
        }
    }
}
