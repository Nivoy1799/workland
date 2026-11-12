using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Worktown.Game.Climb;
using Worktown.Sim.Climb;
using Worktown.Sim.Economy;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 057 AC2: stage 2's amounts come from the spec 052 catalog (cents since spec 037: the scale is 1) and follow it; D40 (spec 052b):
    /// running costs scale with the crew's demand (n/4), goals and one-time costs with n x h(n)/4, floored to whole dollars like the 052b
    /// GAME NUMBERS.
    /// </summary>
    public class RestaurantRulesTests
    {
        private static readonly EconomyCatalog Economy = EconomyCatalog.CreateDefault();
        private static readonly ClimbCatalog Climb = ClimbCatalog.CreateDefault();

        [Test]
        public void TheDefaultCatalog_ForTheReferenceCrew()
        {
            CrewFactors four = RestaurantRules.Reference(Climb);
            Assert.AreEqual(4, four.Players);
            Assert.AreEqual(5000, RestaurantRules.RentPerService(Economy, Climb, four), "$50 per service (052b GAME NUMBERS, 4 players)");
            Assert.AreEqual(0, RestaurantRules.UpkeepPerService(Economy, Climb, four, new[] { "dish_fries", "rent_restaurant" }));
            Assert.AreEqual(5000, RestaurantRules.Due(Economy, Climb, four, null));
            Assert.AreEqual(120000, RestaurantRules.GateCost(Economy, Climb, four), "the service licence: $1,200");
            Assert.AreEqual(30000, RestaurantRules.RentGoal(Economy, Climb, four), "stage 1's goal: $300 (053 and 037)");
            Assert.AreEqual(4, RestaurantRules.MaxDishes(Climb));
            Assert.AreEqual(35, RestaurantRules.GateStarsTenths(Climb));
            Assert.AreEqual(5, RestaurantRules.GateWindow(Climb));
            Assert.AreEqual("service_license", RestaurantRules.GatePurchaseId(Climb));
            Assert.AreEqual(2500, RestaurantRules.RentReserve(Economy, Climb), "the starting money, $25");
            Assert.AreEqual(10000, RestaurantRules.OwedCap(5000), "two rents");
            IReadOnlyList<PurchaseDef> recipes = RestaurantRules.Recipes(Climb);
            CollectionAssert.AreEqual(new[] { "dish_falafel_wrap", "dish_seitan_wrap", "dish_grilled_veggies", "dish_lemonade" },
                recipes.Select(r => r.Id).ToArray(), "catalog order");
            CollectionAssert.AreEqual(new long[] { 20000, 25000, 20000, 15000 },
                recipes.Select(r => RestaurantRules.RecipeCost(r.Id, Economy, Climb, four)).ToArray());
        }

        [Test]
        public void D40_ASoloCrew_PaysLessRent_AndSmallerGoals()
        {
            CrewFactors one = RestaurantRules.Crew(Climb, 1);
            Assert.AreEqual(250, one.DemandPermille);
            Assert.AreEqual(137, one.GoalPermille);
            Assert.AreEqual(1200, RestaurantRules.RentPerService(Economy, Climb, one), "$12 (GAME NUMBERS: 1 player)");
            Assert.AreEqual(16400, RestaurantRules.GateCost(Economy, Climb, one), "$164");
            Assert.AreEqual(4100, RestaurantRules.RentGoal(Economy, Climb, one), "$41");
            Assert.AreEqual(2700, RestaurantRules.RecipeCost("dish_falafel_wrap", Economy, Climb, one), "$200 x 0.137, floored to $27");
            Assert.AreEqual(2500, RestaurantRules.RentReserve(Economy, Climb), "the reserve never scales (stock prices do not)");
            CrewFactors sixteen = RestaurantRules.Crew(Climb, 16);
            Assert.AreEqual(20000, RestaurantRules.RentPerService(Economy, Climb, sixteen), "$200");
            Assert.AreEqual(480000, RestaurantRules.GateCost(Economy, Climb, sixteen), "$4,800");
            Assert.AreEqual(RestaurantRules.Crew(Climb, 16), RestaurantRules.Crew(Climb, 40), "clamped to 16 players");
            Assert.AreEqual(RestaurantRules.Crew(Climb, 1), RestaurantRules.Crew(Climb, 0), "at least one player");
        }

        [Test]
        public void AnotherCatalog_IsFollowedByEveryRule()
        {
            ClimbCatalog other = TestCatalogs.Restaurant(rentDollars: 80, gateWindow: 3, gateStarsTenths: 30, changedPurchase: "dish_falafel_wrap",
                changedCostDollars: 300);
            CrewFactors four = RestaurantRules.Reference(other);
            Assert.AreEqual(8000, RestaurantRules.RentPerService(Economy, other, four));
            Assert.AreEqual(3, RestaurantRules.GateWindow(other));
            Assert.AreEqual(30, RestaurantRules.GateStarsTenths(other));
            Assert.AreEqual(30000, RestaurantRules.RecipeCost("dish_falafel_wrap", Economy, other, four));
            Assert.AreEqual(25000, RestaurantRules.RecipeCost("dish_seitan_wrap", Economy, other, four), "the others unchanged");
            Assert.AreEqual(16000, RestaurantRules.OwedCap(RestaurantRules.Due(Economy, other, four, null)));
            Assert.AreEqual(2000, RestaurantRules.RentPerService(Economy, other, RestaurantRules.Crew(other, 1)), "$80 x 0.25");
        }
    }
}
