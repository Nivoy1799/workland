using System;
using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Climb;
using Xunit;
using T = Worktown.Sim.Tests.Climb.TestClimb;

namespace Worktown.Sim.Tests.Climb
{
    public class ClimbPolicyTests
    {
        private static readonly ClimbCatalog C = T.Catalog;

        private static (ClimbState State, IReadOnlyList<string> Bought, long Spent) Decide(ClimbState s, SpendPolicy spend,
            PricePolicy price = PricePolicy.Match, int players = 1) =>
            ClimbPolicy.Decide(C, s, new CrewSetup(players, "worker", new ClimbStrategy(price, spend), 1));

        [Fact]
        public void EachPricePolicy() // AC8
        {
            ClimbState s = T.State();
            DishDef chips = C.Dish("chips");
            Assert.Equal(4, ClimbPolicy.PriceOf(C, s, PricePolicy.Minus1, chips));
            Assert.Equal(5, ClimbPolicy.PriceOf(C, s, PricePolicy.Match, chips));
            Assert.Equal(6, ClimbPolicy.PriceOf(C, s, PricePolicy.Plus1, chips));
            Assert.Equal(7, ClimbPolicy.PriceOf(C, s, PricePolicy.Plus2, chips));
            Assert.Equal(4, ClimbPolicy.PriceOf(C, s, PricePolicy.Undercut, chips)); // rival at the reference
            ClimbState cut = s with { Rivals = new[] { new RivalState("rival_stand", 1, 0, 0, 0) } };
            Assert.Equal(3, ClimbPolicy.PriceOf(C, cut, PricePolicy.Undercut, chips)); // rival cut once
            var cheap = new DishDef("cheap", "Cheap", 1, 1, 2, 500);
            Assert.Equal(2, ClimbPolicy.PriceOf(C, s, PricePolicy.Minus1, cheap)); // never below cost + 1 unit
            Assert.Equal(2, ClimbPolicy.PriceOf(C, cut, PricePolicy.Undercut, cheap));
            ClimbCatalog noRival = T.Build(rivals: new RivalDef[0]);
            ClimbState lone = new ClimbEngine(noRival).Start(T.Setup());
            Assert.Equal(5, ClimbPolicy.PriceOf(noRival, lone, PricePolicy.Undercut, chips)); // Match without a rival
            ClimbCatalog cents = T.Build(moneyScale: 100);
            Assert.Equal(600, ClimbPolicy.PriceOf(cents, s, PricePolicy.Plus1, cents.Dish("chips"))); // one unit = MoneyScale
        }

        [Fact]
        public void PricesCoverTheMenu()
        {
            ClimbState s = T.State(ClimbStage.FirstRestaurant, 0, "soup_recipe");
            IReadOnlyDictionary<string, long> p = ClimbPolicy.Prices(C, s, PricePolicy.Plus1);
            Assert.Equal(new[] { "chips", "soup" }, p.Keys.OrderBy(k => k));
            Assert.Equal(9, p["soup"]);
        }

        [Fact]
        public void CounterRivalIsTheRivalOfTheOpenCounter()
        {
            Assert.Equal("rival_stand", ClimbPolicy.CounterRival(C, ClimbStage.FoodCorner)!.Id);
            Assert.Null(ClimbPolicy.CounterRival(T.Build(rivals: new RivalDef[0]), ClimbStage.FoodCorner));
            Assert.Equal("miras_wraps", ClimbPolicy.CounterRival(ClimbCatalog.CreateDefault(), ClimbStage.ServiceMix)!.Id);
        }

        [Fact]
        public void CandidatesAreOrderedAndSkipAFullMenu()
        {
            Assert.Equal(new[] { "peeler", "sign" }, ClimbPolicy.Candidates(C, T.State()).Select(p => p.Id));
            Assert.Equal(new[] { "sign", "helper", "soup_recipe", "booth_kit" },
                ClimbPolicy.Candidates(C, T.State(ClimbStage.FirstRestaurant, 0, "peeler")).Select(p => p.Id));
            ClimbCatalog d = ClimbCatalog.CreateDefault();
            ClimbState full = T.State() with { Stage = ClimbStage.FirstRestaurant, Owned = new[] { "dish_lemonade", "dish_falafel_wrap", "dish_grilled_veggies" } };
            Assert.DoesNotContain(ClimbPolicy.Candidates(d, full), p => p.Id == "dish_seitan_wrap"); // 4 dishes max in stage 2
            Assert.Contains(ClimbPolicy.Candidates(d, full with { Stage = ClimbStage.ServiceMix }), p => p.Id == "dish_seitan_wrap");
            Assert.Empty(ClimbPolicy.StatusCandidates(C, T.State()));
            Assert.Equal(new[] { "trophy_case" }, ClimbPolicy.StatusCandidates(C, T.State(ClimbStage.BeyondKitchen)).Select(p => p.Id));
        }

        [Fact]
        public void GatePurchaseAndConditions()
        {
            Assert.Equal("lease", ClimbPolicy.GatePurchase(C, T.State())!.Id);
            Assert.Null(ClimbPolicy.GatePurchase(C, T.State(ClimbStage.FoodCorner, 0, "lease")));
            Assert.True(ClimbPolicy.GateConditionsMet(C, T.State())); // stage 1: only the purchase
            ClimbState two = T.State(ClimbStage.FirstRestaurant, 0, "booth_kit") with { RecentStars = new[] { 40, 40 } };
            Assert.True(ClimbPolicy.GateConditionsMet(C, two));
            Assert.False(ClimbPolicy.GateConditionsMet(C, two with { RecentStars = new[] { 40 } })); // window of 2
            Assert.False(ClimbPolicy.GateConditionsMet(C, two with { RecentStars = new[] { 20, 30 } })); // 2.5 < 3.0 stars
            Assert.False(ClimbPolicy.GateConditionsMet(C, two with { Owned = new string[0] })); // one extra channel needed
            Assert.False(ClimbPolicy.GateConditionsMet(C, T.State(ClimbStage.BeyondKitchen))); // no stage 6
        }

        [Fact]
        public void ReadyJobsNeedStageFiveTheRequirementAndNoCooldown()
        {
            Assert.Empty(ClimbPolicy.ReadyJobs(C, T.State(ClimbStage.Automation)));
            Assert.Equal(new[] { "contest" }, ClimbPolicy.ReadyJobs(C, T.State(ClimbStage.BeyondKitchen)).Select(j => j.Id));
            ClimbState lab = T.State(ClimbStage.BeyondKitchen, 0, "lab");
            Assert.Equal(new[] { "contest", "fair" }, ClimbPolicy.ReadyJobs(C, lab).Select(j => j.Id));
            var cooling = lab with { JobReadyAt = new Dictionary<string, int> { ["contest"] = 5 } };
            Assert.Equal(new[] { "fair" }, ClimbPolicy.ReadyJobs(C, cooling).Select(j => j.Id));
        }

        [Fact]
        public void GoalCostsListWhatMoneyCanBeSavedFor()
        {
            Assert.Equal(new[] { 10L, 20L, 50L }, ClimbPolicy.GoalCosts(C, T.State()));
            ClimbState five = T.State(ClimbStage.BeyondKitchen, 0, "peeler", "sign", "lease", "soup_recipe", "booth_kit", "helper", "lab");
            Assert.Equal(new[] { 10L, 20L, 100L }, ClimbPolicy.GoalCosts(C, five)); // both jobs' deposits and the status item
            var cooling = five with { JobReadyAt = new Dictionary<string, int> { ["contest"] = 99 } };
            Assert.Equal(new[] { 10L, 20L, 100L }, ClimbPolicy.GoalCosts(C, cooling)); // a cooldown only delays a job
        }

        [Fact]
        public void RushSavesForTheGate() // AC8
        {
            var (s, bought, spent) = Decide(T.State(ClimbStage.FoodCorner, 49), SpendPolicy.Rush);
            Assert.Empty(bought); // 49 - 10 < 50
            Assert.Equal(0, spent);
            (s, bought, spent) = Decide(T.State(ClimbStage.FoodCorner, 65), SpendPolicy.Rush);
            Assert.Equal(new[] { "lease", "peeler" }, bought); // the gate first; stage 2 has no gate cost to keep
            Assert.Equal(ClimbStage.FirstRestaurant, s.Stage);
            Assert.Equal(5, s.Balance);
            Assert.Equal(60, spent);
        }

        [Fact]
        public void BalancedKeepsTwiceTheCost() // AC8
        {
            var (s, bought, _) = Decide(T.State(ClimbStage.FoodCorner, 25), SpendPolicy.Balanced);
            Assert.Equal(new[] { "peeler" }, bought);
            Assert.Equal(15, s.Balance);
            (s, bought, _) = Decide(T.State(ClimbStage.FoodCorner, 60), SpendPolicy.Balanced);
            Assert.Equal(new[] { "lease" }, bought);
            Assert.Equal(ClimbStage.FirstRestaurant, s.Stage);
        }

        [Fact]
        public void BuilderBuysTheGateOnlyWhenNoProductiveItemIsLeft() // AC8
        {
            var (s, bought, _) = Decide(T.State(ClimbStage.FoodCorner, 55), SpendPolicy.Builder);
            Assert.Equal(new[] { "peeler", "sign" }, bought); // 55 would buy the gate, but items are left first
            Assert.Equal(ClimbStage.FoodCorner, s.Stage);
            (s, bought, _) = Decide(T.State(ClimbStage.FoodCorner, 100), SpendPolicy.Builder);
            Assert.Equal(new[] { "peeler", "sign", "lease" }, bought);
            Assert.Equal(ClimbStage.FirstRestaurant, s.Stage);
            (s, bought, _) = Decide(T.State(ClimbStage.FoodCorner, 50, "peeler", "sign"), SpendPolicy.Builder);
            Assert.Equal(new[] { "lease" }, bought);
            (s, bought, _) = Decide(T.State(ClimbStage.FoodCorner, 40), SpendPolicy.Builder);
            Assert.Equal(new[] { "peeler", "sign" }, bought);
            Assert.Equal(ClimbStage.FoodCorner, s.Stage);
        }

        [Fact]
        public void StatusItemsOnlyInStageFiveWhenNothingProductiveIsLeft() // AC8
        {
            string[] all = { "peeler", "sign", "lease", "soup_recipe", "booth_kit", "helper", "lab" };
            Assert.Equal(new[] { "trophy_case" }, Decide(T.State(ClimbStage.BeyondKitchen, 250, all), SpendPolicy.Rush).Bought);
            Assert.Empty(Decide(T.State(ClimbStage.BeyondKitchen, 150, all), SpendPolicy.Builder).Bought); // 2 x cost needed
            var (_, bought, _) = Decide(T.State(ClimbStage.BeyondKitchen, 250, all.Take(6).ToArray()), SpendPolicy.Balanced);
            Assert.Equal(new[] { "lab", "trophy_case" }, bought); // productive first
        }

        [Fact]
        public void InDebtNothingIsBoughtButPricesAreSet() // AC6
        {
            foreach (SpendPolicy spend in new[] { SpendPolicy.Rush, SpendPolicy.Balanced, SpendPolicy.Builder })
            {
                var (s, bought, spent) = Decide(T.State(ClimbStage.FoodCorner, -100), spend, PricePolicy.Plus1);
                Assert.Empty(bought);
                Assert.Equal(0, spent);
                Assert.Equal(-100, s.Balance);
                Assert.Equal(6, s.Prices["chips"]);
            }
        }

        [Fact]
        public void AGateWithoutPurchaseIsPassedWhenItsConditionsHold()
        {
            ClimbState two = T.State(ClimbStage.FirstRestaurant, 0, "booth_kit") with { RecentStars = new[] { 40, 40 }, PlaySeconds = 4200 };
            var (s, bought, _) = Decide(two, SpendPolicy.Balanced);
            Assert.Empty(bought);
            Assert.Equal(ClimbStage.ServiceMix, s.Stage);
            Assert.Equal(4200, s.EnteredSeconds(ClimbStage.ServiceMix));
        }

        [Fact]
        public void EnterNextStageRecordsTimeClearsStarsAndLiftsReputation()
        {
            ClimbState s = T.State() with { Reputation = 350, RecentStars = new[] { 40 }, PlaySeconds = 840 };
            ClimbState next = ClimbPolicy.EnterNextStage(C, s);
            Assert.Equal(ClimbStage.FirstRestaurant, next.Stage);
            Assert.Equal(840, next.EnteredSeconds(ClimbStage.FirstRestaurant));
            Assert.Empty(next.RecentStars);
            Assert.Equal(400, next.Reputation);
            Assert.Empty(ClimbPolicy.EnterNextStage(C, s with { RecentMissed = new[] { 500 } }).RecentMissed); // 052b review: a new menu history
            ClimbState five = T.State(ClimbStage.BeyondKitchen);
            Assert.Same(five, ClimbPolicy.EnterNextStage(C, five)); // a stage is never lost or passed beyond 5
        }
    }
}
