using System.Linq;
using Worktown.Sim.Climb;
using Xunit;
using T = Worktown.Sim.Tests.Climb.TestClimb;

namespace Worktown.Sim.Tests.Climb
{
    public class ClimbReportTests
    {
        private static ClimbRun Run(int players, ulong seed = 1) =>
            T.Run(players, "average", ClimbStrategy.Reference, seed, T.Good, (i, st, r) => T.Svc(i, st, upkeep: 10 * (int)st));

        [Fact]
        public void OneSectionPerPlayerCountThenTargetsAndResult()
        {
            var runs = new[] { Run(1), Run(4), Run(16), Run(16, 2) };
            var checks = ClimbEvaluation.Evaluate(runs, ClimbTargets.Default);
            string text = ClimbReport.Format(runs, checks);
            string[] lines = text.TrimEnd('\n').Split('\n');
            Assert.Contains("== 1 player ==", lines);
            Assert.Contains("== 4 players ==", lines);
            Assert.Contains("== 16 players ==", lines);
            Assert.True(System.Array.IndexOf(lines, "== 4 players ==") < System.Array.IndexOf(lines, "== 16 players =="));
            Assert.Contains(lines, l => l.StartsWith("average  balanced+match") && l.Contains("1.0 [1.0-1.0]"));
            Assert.Contains(lines, l => l.StartsWith("average  S3"));
            Assert.Equal(checks.Count, lines.Count(l => l.StartsWith("PASS T") || l.StartsWith("FAIL T")));
            Assert.Equal($"RESULT: PASS ({checks.Count}/{checks.Count})", lines.Last());
        }

        [Fact]
        public void AFailingTargetGivesAFailResult()
        {
            ClimbRun slow = T.Run(4, "average", ClimbStrategy.Reference, 1, new[] { 2, 22, 30, 36, 20 }, (i, st, r) => T.Svc(i, st, upkeep: 10 * (int)st));
            var checks = ClimbEvaluation.Evaluate(new[] { slow }, ClimbTargets.Default);
            string last = ClimbReport.Format(new[] { slow }, checks).TrimEnd('\n').Split('\n').Last();
            Assert.Equal($"RESULT: FAIL ({checks.Count(c => c.Passed)}/{checks.Count})", last);
            Assert.StartsWith("RESULT: FAIL", last);
        }

        [Fact]
        public void Formats()
        {
            Assert.Equal("1.1", ClimbReport.FormatHours(3960));
            Assert.Equal("0.0", ClimbReport.FormatHours(0));
            Assert.Equal("40.0", ClimbReport.FormatHours(144000));
            Assert.Equal("0.75", ClimbReport.FormatBand(2700));
            Assert.Equal("1.5", ClimbReport.FormatBand(5400));
            Assert.Equal("12", ClimbReport.FormatBand(43200));
            Assert.Equal("0.8", ClimbReport.FormatRatio(800));
            Assert.Equal("0.85", ClimbReport.FormatRatio(850));
            Assert.Equal("1", ClimbReport.FormatRatio(1000));
        }

        [Fact]
        public void ShareOfRivalsAddsEveryRivalChannel()
        {
            ClimbRun run = T.Run(4, "average", ClimbStrategy.Reference, 1, new[] { 4 }, (i, st, r) => r with { YourGuests = new[] { 3 }, RivalGuests = new[] { 1 } });
            Assert.Equal(750, ClimbReport.ShareOfRivals(run, ClimbStage.FoodCorner));
            Assert.Equal(0, ClimbReport.ShareOfRivals(run, ClimbStage.FirstRestaurant));
        }
    }
}
