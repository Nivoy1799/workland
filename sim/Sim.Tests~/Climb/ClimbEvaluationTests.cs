using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Climb;
using Xunit;
using T = Worktown.Sim.Tests.Climb.TestClimb;

namespace Worktown.Sim.Tests.Climb
{
    public class ClimbEvaluationTests
    {
        private static readonly ClimbStrategy Ref = ClimbStrategy.Reference;

        /// <summary>A run that meets every target: good pacing, upkeep 10 x stage on a net of 100, goals at 5 x net.</summary>
        private static ClimbRun Good(string profile = "average", ClimbStrategy? strategy = null, int[]? perStage = null,
            System.Func<int, ClimbStage, ServiceResult, ServiceResult>? custom = null, ulong seed = 1) =>
            T.Run(4, profile, strategy ?? Ref, seed, perStage ?? T.Good, (i, st, r) =>
            {
                ServiceResult x = T.Svc(i, st, upkeep: 10 * (int)st);
                return custom != null ? custom(i, st, x) : x;
            });

        private static IReadOnlyList<TargetCheck> Eval(params ClimbRun[] runs) => ClimbEvaluation.Evaluate(runs, ClimbTargets.Default);

        private static TargetCheck One(IReadOnlyList<TargetCheck> checks, string id, string detailPart) =>
            Assert.Single(checks, c => c.Id == id && c.Detail.Contains(detailPart));

        [Fact]
        public void AGoodRunPassesEveryTarget() // AC10 passing side of T1, T2, T4-T8
        {
            IReadOnlyList<TargetCheck> checks = Eval(Good());
            Assert.All(checks, c => Assert.True(c.Passed, c.Line));
            foreach (string id in new[] { "T1", "T2", "T4", "T5", "T6", "T7", "T8" }) Assert.Contains(checks, c => c.Id == id);
            Assert.Equal(4, checks.Count(c => c.Id == "T1"));
            Assert.Equal("PASS T1 4p average S2 1.0 h in 0.75-1.5", One(checks, "T1", "S2").Line);
        }

        [Fact]
        public void T1FailsOutsideTheBand() // AC10
        {
            Assert.False(One(Eval(Good(perStage: new[] { 2, 22, 30, 36, 20 })), "T1", "S2").Passed); // S2 at 0.3 h
            Assert.False(One(Eval(Good(perStage: new[] { 6, 18, 30, 300 })), "T1", "S5").Passed); // DNF
        }

        [Fact]
        public void T2FailsWhenAStageIsTooShort() // AC10
        {
            TargetCheck t2 = One(Eval(Good(perStage: new[] { 6, 2, 46, 36, 20 })), "T2", "4p average");
            Assert.False(t2.Passed);
            Assert.Contains("min 2 services", t2.Detail);
        }

        [Fact]
        public void T3ComparesExpertAndNovice() // AC10
        {
            ClimbRun expert = Good("expert");
            Assert.True(One(Eval(expert, Good("novice", perStage: new[] { 6, 30, 40, 50, 20 })), "T3", "4p").Passed); // 15 h <= 0.8 x 21 h
            Assert.False(One(Eval(expert, Good("novice")), "T3", "4p").Passed); // 15 h vs 15 h
            Assert.False(One(Eval(expert, Good("novice", perStage: new[] { 6, 18, 30, 300 })), "T3", "4p").Passed); // novice DNF
        }

        [Fact]
        public void T4FailsWhenOneStrategyIsAShortcut() // AC10
        {
            var rush = new ClimbStrategy(PricePolicy.Match, SpendPolicy.Rush);
            var builder = new ClimbStrategy(PricePolicy.Match, SpendPolicy.Builder);
            var fast = new ClimbStrategy(PricePolicy.Plus1, SpendPolicy.Rush);
            Assert.True(One(Eval(Good(), Good(strategy: rush), Good(strategy: builder)), "T4", "4p average").Passed);
            TargetCheck t4 = One(Eval(Good(), Good(strategy: builder), Good(strategy: fast, perStage: new[] { 3, 9, 15, 18, 20 })), "T4", "4p average");
            Assert.False(t4.Passed);
            Assert.Contains("rush+plus1", t4.Detail);
            Assert.False(One(Eval(Good(perStage: new[] { 6, 18, 30, 300 })), "T4", "4p average").Passed); // nobody finishes
        }

        [Fact]
        public void T5FailsOnARunawayShareOrASnowball() // AC10
        {
            TargetCheck share = One(Eval(Good(custom: (i, st, r) => r with { YourGuests = new[] { 9 }, RivalGuests = new[] { 1 } })), "T5", "share");
            Assert.False(share.Passed);
            Assert.Contains("90%", share.Detail);
            Assert.False(One(Eval(Good(perStage: new[] { 6, 18, 30, 8, 20 })), "T5", "ratio").Passed); // S4 is 0.27 x S3
            Assert.True(One(Eval(Good()), "T5", "ratio").Passed);
        }

        [Fact]
        public void T6FailsWhenUpkeepDoesNotRiseOrIsTooHigh() // AC10
        {
            Assert.False(One(Eval(Good(custom: (i, st, r) => r with { Upkeep = 10, Revenue = 110 })), "T6", "4p").Passed); // flat
            Assert.False(One(Eval(Good(custom: (i, st, r) => st == ClimbStage.Automation ? r with { Upkeep = 800, Revenue = 900 } : r)), "T6", "4p").Passed);
        }

        [Fact]
        public void T7FailsOnIdleMoney() // AC10
        {
            TargetCheck t7 = One(Eval(Good(custom: (i, st, r) => st == ClimbStage.ServiceMix ? r with { GoalCosts = new long[0] } : r)), "T7", "4p");
            Assert.False(t7.Passed);
            Assert.Contains("(in S3)", t7.Detail);
            Assert.False(One(Eval(Good(custom: (i, st, r) => r with { GoalCosts = new[] { 100000L } })), "T7", "4p").Passed); // out of reach
        }

        [Fact]
        public void T8FailsOnStuckDebt() // AC10
        {
            ClimbRun Debt(int n) => Good(custom: (i, st, r) => i >= 10 && i < 10 + n ? r with { BalanceAfter = -1 } : r);
            Assert.True(One(Eval(Debt(5)), "T8", "4p").Passed);
            Assert.False(One(Eval(Debt(6)), "T8", "4p").Passed);
        }

        [Fact]
        public void MedianAndDnf()
        {
            Assert.Equal(2, ClimbEvaluation.Median(new long[] { 3, 1, 2 }));
            Assert.Equal(2, ClimbEvaluation.Median(new long[] { 4, 1, 3, 2 }));
            Assert.Equal(ClimbEvaluation.Dnf, ClimbEvaluation.Median(new[] { 1, ClimbEvaluation.Dnf }));
            Assert.Equal(ClimbEvaluation.Dnf, ClimbEvaluation.Median(new long[0]));
            ClimbRun run = Good(perStage: new[] { 6, 18 });
            Assert.Equal(3600, ClimbEvaluation.EnteredOrDnf(run, ClimbStage.FirstRestaurant));
            Assert.Equal(ClimbEvaluation.Dnf, ClimbEvaluation.EnteredOrDnf(run, ClimbStage.ServiceMix));
            Assert.Equal(3600, ClimbEvaluation.SecondsOrDnf(run, ClimbStage.FoodCorner));
            Assert.Equal(ClimbEvaluation.Dnf, ClimbEvaluation.SecondsOrDnf(run, ClimbStage.FirstRestaurant));
        }

        // ---------- spec 052b ----------

        private static ClimbRun P(int players, int[] perStage, CrewFactors? crew = null) =>
            T.Run(players, "average", Ref, 1, perStage, (i, st, r) => T.Svc(i, st, upkeep: 10 * (int)st), crew: crew);

        [Fact]
        public void T4ComparesTheFastestWithTheReference() // AC9 (spec 052b (b))
        {
            var fast = new ClimbStrategy(PricePolicy.Plus1, SpendPolicy.Balanced);
            int[] slow = { 6, 30, 50, 60, 20 };
            // Two slow strategies pull the old median up; against the reference a 10 % faster strategy is fine.
            ClimbRun[] runs =
            {
                Good(), Good(strategy: new ClimbStrategy(PricePolicy.Minus1, SpendPolicy.Balanced), perStage: slow),
                Good(strategy: new ClimbStrategy(PricePolicy.Plus2, SpendPolicy.Balanced), perStage: slow),
                Good(strategy: fast, perStage: new[] { 6, 16, 27, 32, 20 }),
            };
            TargetCheck t4 = One(Eval(runs), "T4", "4p average");
            Assert.True(t4.Passed, t4.Line);
            Assert.Contains("x reference 15.0 h", t4.Detail);
            Assert.False(Assert.Single(ClimbEvaluation.Info(runs, ClimbTargets.Default)).Passed); // the old rule would fail
            Assert.False(One(Eval(Good(), Good(strategy: fast, perStage: new[] { 6, 12, 20, 24, 20 })), "T4", "4p average").Passed); // 10.3 h < 0.85 x 15 h
            TargetCheck dnf = One(Eval(Good(perStage: new[] { 6, 18, 30, 300 }), Good(strategy: fast)), "T4", "4p average");
            Assert.False(dnf.Passed);
            Assert.Contains("the reference balanced+match does not reach S5", dnf.Detail);
        }

        [Fact]
        public void InfoKeepsTheOldMedianRule() // AC9: INFO T4m, not counted
        {
            var rush = new ClimbStrategy(PricePolicy.Match, SpendPolicy.Rush);
            TargetCheck m = Assert.Single(ClimbEvaluation.Info(new[] { Good(), Good(strategy: rush) }, ClimbTargets.Default));
            Assert.Equal("T4m", m.Id);
            Assert.True(m.Passed);
            Assert.Contains("x median", m.Detail);
            Assert.DoesNotContain(Eval(Good()), c => c.Id == "T4m");
        }

        [Fact]
        public void T1LetsAnEasierCrewBeFasterByItsDifficulty() // D40
        {
            var solo = new CrewFactors(1, 550, 250, 137, 0, 2);
            int[] quick = { 3, 8, 18, 20, 20 }; // S2 0.5 h, S3 1.8 h, S4 4.8 h, S5 8.2 h
            Assert.All(Eval(P(1, quick, solo)).Where(c => c.Id == "T1"), c => Assert.True(c.Passed, c.Line));
            Assert.False(One(Eval(P(1, quick)), "T1", "S2").Passed); // with h = 1 it is too fast
            Assert.Equal("PASS T1 1p average S2 0.5 h in 0.41-1.5", One(Eval(P(1, quick, solo)), "T1", "S2").Line);
            Assert.Equal((1485L, 5400L), ClimbTargets.Default.BandOf(ClimbStage.FirstRestaurant, 550));
            Assert.Equal((2700L, 5400L), ClimbTargets.Default.BandOf(ClimbStage.FirstRestaurant, 1400)); // never above the band
        }

        [Fact]
        public void T1pFailsWhenABigCrewTakesLongerThanFour() // D40 plateau
        {
            ClimbRun four = P(4, T.Good);
            Assert.True(One(Eval(four, P(8, T.Good)), "T1p", "8p").Passed);
            Assert.True(One(Eval(four, P(16, new[] { 5, 16, 30, 36, 20 })), "T1p", "16p").Passed); // faster is fine
            TargetCheck slow = One(Eval(four, P(8, new[] { 6, 24, 30, 36, 20 })), "T1p", "8p");
            Assert.False(slow.Passed);
            Assert.Contains("1.33x of 4p (S2", slow.Detail);
            Assert.DoesNotContain(Eval(P(8, T.Good)), c => c.Id == "T1p"); // no 4-player runs: no line
            Assert.DoesNotContain(Eval(four, P(2, T.Good)), c => c.Id == "T1p"); // only above the reference crew
        }

        [Fact]
        public void C1ChecksTheCalibrationRuns() // AC9
        {
            IReadOnlyList<TargetCheck> ok = ClimbEvaluation.Calibration(new[] { P(1, new[] { 20, 1 }), P(4, new[] { 6, 1 }), P(16, new[] { 5, 1 }) }, ClimbTargets.Default);
            Assert.Equal(3, ok.Count);
            Assert.All(ok, c => Assert.True(c.Passed, c.Line));
            Assert.Equal("PASS C1 1p calibration S1 20 services in 16.5-21.5 (053 measured 19.0)", ok[0].Line);
            Assert.False(ClimbEvaluation.Calibration(new[] { P(1, new[] { 25, 1 }) }, ClimbTargets.Default)[0].Passed);
            Assert.False(ClimbEvaluation.Calibration(new[] { P(4, new[] { 8, 1 }) }, ClimbTargets.Default)[0].Passed);
            Assert.False(ClimbEvaluation.Calibration(new[] { P(4, new[] { 30 }) }, ClimbTargets.Default)[0].Passed); // S1 not completed
        }

        [Fact]
        public void T1gChecksTheGameNumbersRows() // AC9
        {
            GameNumbersRow Row(int players, int h, int tenths) => new GameNumbersRow(players, h, 1000, 1000, 30000, 8, 500, 2500, 5000, 120000, 9, tenths);
            IReadOnlyList<TargetCheck> checks = ClimbEvaluation.GameNumberChecks(
                new[] { Row(4, 1000, 9), Row(4, 1000, 5), Row(1, 550, 5), Row(4, 1000, -1), Row(4, 1000, 16) }, ClimbTargets.Default);
            Assert.Equal(new[] { true, false, true, false, false }, checks.Select(c => c.Passed));
            Assert.Equal("PASS T1g 4p game S1 0.9 h in 0.75-1.5", checks[0].Line);
            Assert.Contains("DNF", checks[3].Detail);
        }
    }
}
