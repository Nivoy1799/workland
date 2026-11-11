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
        }
    }
}
