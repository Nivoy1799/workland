using System;
using System.Linq;
using Worktown.Sim.Climb;
using Xunit;
using T = Worktown.Sim.Tests.Climb.TestClimb;

namespace Worktown.Sim.Tests.Climb
{
    public class ClimbModelTests
    {
        private static readonly RivalDef Stand = T.Catalog.Rival("rival_stand"); // 1 cut, 2 quality steps

        [Fact]
        public void StrategiesFormTheGrid()
        {
            Assert.Equal(15, ClimbStrategy.All.Select(s => s.Id).Distinct().Count());
            Assert.Equal("balanced+match", ClimbStrategy.Reference.Id);
            foreach (ClimbStrategy s in ClimbStrategy.All) Assert.Equal(s, ClimbStrategy.Parse(s.Id));
            Assert.Throws<ArgumentException>(() => ClimbStrategy.Parse("lazy+free"));
            Assert.Equal("4p average balanced+match seed 3", new CrewSetup(4, "average", ClimbStrategy.Reference, 3).Label);
            Assert.Equal("FAIL T8 x", new TargetCheck("T8", false, "x").Line);
            Assert.Equal("PASS T1 y", new TargetCheck("T1", true, "y").Line);
            Assert.Equal((2700L, 5400L), ClimbTargets.Default.BandOf(ClimbStage.FirstRestaurant));
            Assert.Equal((43200L, 72000L), ClimbTargets.Default.BandOf(ClimbStage.BeyondKitchen));
            Assert.Equal((0L, 0L), ClimbTargets.Default.BandOf(ClimbStage.FoodCorner));
            Assert.Equal(0, PurchaseEffect.None.ActionsSaved);
            Assert.Null(PurchaseEffect.None.UnlocksChannel);
        }

        [Fact]
        public void StateLookups()
        {
            ClimbState s = T.State(ClimbStage.FoodCorner, 0, "peeler");
            Assert.True(s.Owns("peeler"));
            Assert.False(s.Owns("sign"));
            Assert.Equal(0, s.RivalOf("rival_stand").PriceCuts);
            Assert.Throws<ArgumentException>(() => s.RivalOf("nobody"));
            Assert.Equal(0, s.EnteredSeconds(ClimbStage.FoodCorner));
            Assert.Equal(-1, s.EnteredSeconds(ClimbStage.ServiceMix));
        }

        [Fact]
        public void ThreeServicesAheadTriggerExactlyOneReaction() // AC7
        {
            RivalState r = RivalState.Start("rival_stand");
            r = r.React(Stand, 5, 4).React(Stand, 5, 4);
            Assert.Equal(0, r.PriceCuts);
            Assert.Equal(2, r.AheadStreak);
            r = r.React(Stand, 5, 4);
            Assert.Equal(1, r.PriceCuts);
            Assert.Equal(0, r.QualitySteps);
            Assert.Equal(0, r.AheadStreak);
            r = r.React(Stand, 5, 4).React(Stand, 4, 4).React(Stand, 5, 4).React(Stand, 5, 4); // a tie resets the streak
            Assert.Equal(1, r.PriceCuts);
            Assert.Equal(0, r.QualitySteps);
        }

        [Fact]
        public void CutsThenQualityStepsNeverExceedTheirMaximums() // AC7
        {
            RivalState r = RivalState.Start("rival_stand");
            for (int k = 0; k < 30; k++) r = r.React(Stand, 9, 1);
            Assert.Equal(1, r.PriceCuts);
            Assert.Equal(2, r.QualitySteps);
            Assert.Equal(550, r.QualityOf(Stand));
            Assert.Equal(-1, r.OffsetOf(Stand, 1));
            Assert.Equal(-100, r.OffsetOf(Stand, 100));
        }

        [Fact]
        public void TwiceYourShareThreeTimesUndoesOneStep() // AC7
        {
            RivalState r = new RivalState("rival_stand", 1, 2, 0, 0);
            r = r.React(Stand, 2, 4).React(Stand, 2, 4).React(Stand, 2, 4);
            Assert.Equal((1, 1), (r.PriceCuts, r.QualitySteps)); // a quality step first
            for (int k = 0; k < 6; k++) r = r.React(Stand, 2, 4);
            Assert.Equal((0, 0), (r.PriceCuts, r.QualitySteps));
            for (int k = 0; k < 6; k++) r = r.React(Stand, 2, 4);
            Assert.Equal((0, 0), (r.PriceCuts, r.QualitySteps)); // nothing below zero
            r = new RivalState("rival_stand", 1, 0, 0, 0).React(Stand, 0, 0).React(Stand, 0, 0).React(Stand, 0, 0);
            Assert.Equal(1, r.PriceCuts); // no guests at all is neither ahead nor behind
        }

        [Fact]
        public void ServiceResultNetAndCanonical()
        {
            ServiceResult r = T.Svc(3, ClimbStage.FirstRestaurant, net: 40, upkeep: 7) with { JobCost = 5, Revenue = 60, IngredientCost = 3 };
            Assert.Equal(60 - 3 - 7 - 5, r.Net);
            string c = r.Canonical();
            Assert.StartsWith("3 2 - 1800 600 g=10/10/0/0 $=60/3/7/5/0/100", c);
            Assert.Contains("ch=counter:5:5", c);
            Assert.EndsWith("goals=200", c);
        }

        [Fact]
        public void RunMetrics()
        {
            ClimbRun run = T.Run(4, "average", ClimbStrategy.Reference, 1, new[] { 3, 4 }, (i, st, r) =>
                st == ClimbStage.FoodCorner
                    ? r with { Revenue = 50, Upkeep = 10, Chosen = 10, Served = 8, Missed = 2, StarsTenths = 30, BalanceAfter = i == 2 ? -5 : 5 }
                    : r with { Revenue = 200, Upkeep = 50, YourGuests = new[] { 6 }, RivalGuests = new[] { 2 }, StarsTenths = 40, BalanceAfter = -1 });
            Assert.Equal(0, run.EnteredSeconds(ClimbStage.FoodCorner));
            Assert.Equal(1800, run.EnteredSeconds(ClimbStage.FirstRestaurant));
            Assert.Equal(-1, run.EnteredSeconds(ClimbStage.ServiceMix));
            Assert.True(run.Completed(ClimbStage.FoodCorner));
            Assert.False(run.Completed(ClimbStage.FirstRestaurant));
            Assert.False(run.Completed(ClimbStage.BeyondKitchen));
            Assert.Equal(3, run.ServicesIn(ClimbStage.FoodCorner));
            Assert.Equal(40 * 6, run.NetPerHour(ClimbStage.FoodCorner)); // $40 per 600 s service
            Assert.Equal(0, run.NetPerHour(ClimbStage.ServiceMix));
            Assert.Equal(200, run.UpkeepPermille(ClimbStage.FoodCorner));
            Assert.Equal(250, run.UpkeepPermille(ClimbStage.FirstRestaurant));
            Assert.Equal(200, run.MissedPermille(ClimbStage.FoodCorner));
            Assert.Equal(30, run.StarsTenths(ClimbStage.FoodCorner));
            Assert.Equal(new[] { "counter" }, run.RivalChannels(ClimbStage.FirstRestaurant));
            Assert.Equal(750, run.SharePermille(ClimbStage.FirstRestaurant, "counter", 10));
            Assert.Equal(500, run.SharePermille(ClimbStage.FoodCorner, "counter", 2));
            Assert.Equal(-1, run.SharePermille(ClimbStage.FoodCorner, "booth", 10));
            Assert.Equal(5, run.LongestDebtStreak()); // service 2 and the four of stage 2
            Assert.Equal(0, run.IdleMoneyServices(3, 20, 5));
            Assert.Equal(4, run.IdleMoneyServices(1, 1, 5, ClimbStage.FirstRestaurant));
            Assert.Equal(0, run.IdleMoneyServices(1, 1, 5, ClimbStage.ServiceMix));
        }

        [Fact]
        public void RunNeedsItsParts()
        {
            Assert.Throws<ArgumentNullException>(() => new ClimbRun(null!, 1, new ServiceResult[0], T.State()));
            Assert.Throws<ArgumentNullException>(() => new ClimbRun(T.Setup(), 1, null!, T.State()));
            Assert.Throws<ArgumentNullException>(() => new ClimbRun(T.Setup(), 1, new ServiceResult[0], null!));
        }

        [Fact]
        public void RunCountsMenuChangesRepeatBuysAndStageTime() // spec 052b
        {
            ClimbRun run = T.Run(4, "average", ClimbStrategy.Reference, 1, new[] { 3, 3 }, (i, st, r) => i switch
            {
                0 => r with { Bought = new[] { "stall" } },
                1 => r with { Bought = new[] { "stall", "peeler" }, MenuDropped = "soup" },
                3 => r with { Bought = new[] { "stall" }, MenuAdded = "soup" },
                4 => r with { MenuDropped = "soup" },
                _ => r,
            });
            Assert.Equal(1, run.RepeatBuys(ClimbStage.FoodCorner)); // the second stall
            Assert.Equal(1, run.RepeatBuys(ClimbStage.FirstRestaurant)); // the third
            Assert.Equal((1, 0), (run.MenuDrops(ClimbStage.FoodCorner), run.MenuAdds(ClimbStage.FoodCorner)));
            Assert.Equal((1, 1), (run.MenuDrops(ClimbStage.FirstRestaurant), run.MenuAdds(ClimbStage.FirstRestaurant)));
            Assert.Equal(3 * T.Step, run.SecondsIn(ClimbStage.FoodCorner));
            Assert.Equal(-1, run.SecondsIn(ClimbStage.FirstRestaurant)); // not completed
            Assert.Equal(1000, run.Crew.DifficultyPermille); // neutral when not given
        }

        [Fact]
        public void NetReferenceIsNetOverTheGoalFactor() // spec 052b, D40
        {
            ServiceResult r = T.Svc(0, ClimbStage.FoodCorner, net: 137, upkeep: 0);
            Assert.Equal(137, r.NetReference); // G 1000 by default
            Assert.Equal(1000, (r with { GoalPermille = 137 }).NetReference);
            Assert.Equal(13700, (r with { GoalPermille = 1000, Revenue = 13700, MoneyUnit = 100 }).NetReference);
            Assert.Contains(" so=0 m=-/- G=1000 b=", r.Canonical());
            ClimbState s = T.State(ClimbStage.FoodCorner, 0, "peeler", "peeler");
            Assert.Equal(2, s.Count("peeler"));
            Assert.Equal(0, s.TierOf("contest"));
            Assert.Equal(3, (s with { JobTier = new System.Collections.Generic.Dictionary<string, int> { ["contest"] = 3 } }).TierOf("contest"));
        }
    }
}
