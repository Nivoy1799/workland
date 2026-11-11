using System;
using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Climb;
using Worktown.Sim.Revival;
using Xunit;
using R = Worktown.Sim.Tests.Revival.TestRevival;
using T = Worktown.Sim.Tests.Revival.TestRuns;

namespace Worktown.Sim.Tests.Revival
{
    public class RevivalReplayTests
    {
        private static readonly RevivalPlan Plan = RevivalPlan.Default with
        {
            Shares = new[] { ("corner_counter", "main_street", 1000), ("delivery", "homes", 500), ("delivery", "tower_rise", 300), ("delivery", "low_ring", 200) },
        };

        [Fact]
        public void TheAc9RunGivesExactMealsPerDistrict() // AC9
        {
            RevivalCatalog c = R.Spec();
            RevivalTrace t = RevivalReplay.Replay(T.Ac9(), c, Plan);
            // Service 0: 10 corner meals at Main Street. Service 1 (stage 2): 20 served over 9 + 3 guests = 15 corner + 5 delivery;
            // delivery 5 x 500/300/200 = 2.5/1.5/1.0, the tie goes to Homes: 3/1/1. Service 2: the street festival's 30 at the Meadow.
            Assert.Equal(25, t.MealsIn("main_street"));
            Assert.Equal(3, t.MealsIn("homes"));
            Assert.Equal(1, t.MealsIn("tower_rise"));
            Assert.Equal(1, t.MealsIn("low_ring"));
            Assert.Equal(30, t.MealsIn("festival_meadow"));
            Assert.Equal(60, t.Meals.Sum());
            Assert.Equal(1, t.StageEntries);
            Assert.False(t.LevelDropped);
            Assert.Equal(4, t.Players);
            Assert.Equal(new long[] { 0, 420, -1, -1, -1 }, t.StageEntered);

            // The same calls by hand give the same state.
            RevivalState s = RevivalEngine.Start(c);
            s = RevivalEngine.AddMeals(s, c, "main_street", 4, 10, 4).State;
            s = RevivalEngine.EnterStage(s, c, 2).State;
            foreach ((string d, int n) in new[] { ("main_street", 15), ("homes", 3), ("tower_rise", 1), ("low_ring", 1) })
                s = RevivalEngine.AddMeals(s, c, d, 4, n, 4).State;
            s = RevivalEngine.AddMeals(s, c, "festival_meadow", 3, 30, 4).State; // quality 450 = 3 stars
            Assert.Equal(s.Digest, t.FinalDigest);
        }

        [Fact]
        public void LevelTimesAreTheServiceEnd()
        {
            RevivalCatalog c = R.Flat(spill: 0);
            // 40 + 36 four-star meals (each call floors its ppm, so 40 + 35 = 300 points gives 999,999): Patched at the end of
            // service 1 (2 x 420 s).
            ClimbRun run = T.Run(4, new[] { T.Normal(0, ClimbStage.FoodCorner, 40, 650, ("corner_counter", 40)), T.Normal(1, ClimbStage.FoodCorner, 36, 650, ("corner_counter", 36)) });
            RevivalTrace t = RevivalReplay.Replay(run, c, Plan);
            Assert.Equal(840, t.LevelSeconds("main_street", RevivalLevel.Patched));
            Assert.Equal(-1, t.LevelSeconds("main_street", RevivalLevel.Lively));
            Assert.Equal(0, t.LevelSeconds("main_street", RevivalLevel.Grey));
            Assert.Equal(0, t.StageEnteredSeconds(1));
            Assert.Equal(-1, t.StageEnteredSeconds(0));
            Assert.Equal(-1, t.StageEnteredSeconds(6));
            Assert.Throws<ArgumentException>(() => t.LevelSeconds("moon_base", RevivalLevel.Patched));
            Assert.Throws<ArgumentException>(() => t.MealsIn("moon_base"));
        }

        [Fact]
        public void ACompetitionAddsNothingAndAStageJumpGivesEveryBonus()
        {
            RevivalCatalog c = R.Spec();
            ClimbRun run = T.Run(4, new[]
            {
                T.Normal(0, ClimbStage.FoodCorner, 0, 650, ("corner_counter", 0)),
                T.Job(1, ClimbStage.BeyondKitchen, "cook_off", 12, 900), // listed as a competition: no meals even with Served
            });
            RevivalTrace t = RevivalReplay.Replay(run, c, Plan);
            Assert.Equal(0, t.Meals.Sum());
            Assert.Equal(4, t.StageEntries); // 1 to 5: stages 2, 3, 4 and 5
            RevivalState s = RevivalEngine.Start(c);
            for (int st = 2; st <= 5; st++) s = RevivalEngine.EnterStage(s, c, st).State;
            Assert.Equal(s.Digest, t.FinalDigest);
            Assert.Equal(900_000, s.ProgressPpm("station_square")); // 500,000 + 4 x 100,000: still Grey
        }

        [Fact]
        public void ChannelsWithoutASharePlanGoHome()
        {
            RevivalPlan plan = RevivalPlan.Default with { Shares = Array.Empty<(string, string, int)>() };
            Assert.Equal(new[] { ("main_street", 1) }, plan.SharesOf("delivery"));
            Assert.Equal(new[] { ("homes", 350), ("workshop_lane", 120), ("greenhouse_hill", 120), ("tower_rise", 100), ("festival_meadow", 100),
                ("garden_promenade", 60), ("low_ring", 50), ("station_square", 50) }, RevivalPlan.Default.SharesOf("delivery"));
            Assert.True(RevivalPlan.Default.IsCompetition("cook_off"));
            Assert.False(RevivalPlan.Default.IsCompetition("street_festival"));
        }

        [Fact]
        public void PlanValidationThrows()
        {
            RevivalCatalog c = R.Spec();
            RevivalPlan.Default.Validate(c);
            Assert.Throws<ArgumentException>(() => (RevivalPlan.Default with { HomeDistrict = "moon_base" }).Validate(c));
            Assert.Throws<ArgumentException>(() => (RevivalPlan.Default with { EventDistrict = "moon_base" }).Validate(c));
            Assert.Throws<ArgumentException>(() => (RevivalPlan.Default with { Shares = new[] { ("delivery", "moon_base", 1) } }).Validate(c));
            Assert.Throws<ArgumentException>(() => (RevivalPlan.Default with { Shares = new[] { ("delivery", "homes", 0) } }).Validate(c));
            Assert.Throws<ArgumentException>(() => (RevivalPlan.Default with { Shares = new[] { ("", "homes", 1) } }).Validate(c));
            Assert.Throws<ArgumentException>(() => RevivalReplay.Replay(T.Ac9(), c, RevivalPlan.Default with { EventDistrict = "moon_base" }));
        }

        [Fact]
        public void StarsFollow052StepFive()
        {
            Assert.Equal(new[] { 1, 1, 2, 3, 4, 5, 5, 5 }, new[] { -50, 199, 200, 450, 650, 800, 999, 1200 }.Select(RevivalReplay.StarsOf));
        }

        [Fact]
        public void SplitIsLargestRemainderWithTiesInOrder()
        {
            Assert.Equal(new[] { 3, 1, 1 }, RevivalReplay.Split(5, new[] { 500, 300, 200 }));
            Assert.Equal(new[] { 15, 5 }, RevivalReplay.Split(20, new[] { 9, 3 }));
            Assert.Equal(new[] { 1, 1, 0 }, RevivalReplay.Split(2, new[] { 1, 1, 1 }));
            Assert.Equal(new[] { 0, 0 }, RevivalReplay.Split(7, new[] { 0, 0 }));
            Assert.Equal(new[] { 0, 7 }, RevivalReplay.Split(7, new[] { 0, 4 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => RevivalReplay.Split(-1, new[] { 1 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => RevivalReplay.Split(1, new[] { -1 }));
        }

        [Fact]
        public void TraceRejectsBadShapes()
        {
            var ids = new[] { "a" };
            var one = new List<IReadOnlyList<long>> { new long[] { -1, -1, -1 } };
            Assert.Throws<ArgumentException>(() => new RevivalTrace(4, 1, 1, ids, one, new long[] { 0, -1, -1, -1 }, new[] { 0 }, 0, false, ""));
            Assert.Throws<ArgumentException>(() => new RevivalTrace(4, 1, 1, ids, new List<IReadOnlyList<long>> { new long[] { -1 } }, new long[5], new[] { 0 }, 0, false, ""));
            Assert.Throws<ArgumentException>(() => new RevivalTrace(4, 1, 1, new[] { "a", "a" }, one.Concat(one).ToList(), new long[5], new[] { 0, 0 }, 0, false, ""));
            Assert.Throws<ArgumentException>(() => new RevivalTrace(4, 1, 1, ids, one, new long[5], new[] { 0, 0 }, 0, false, ""));
        }
    }
}
