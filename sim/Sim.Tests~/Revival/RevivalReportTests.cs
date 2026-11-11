using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Worktown.Sim.Climb;
using Worktown.Sim.Revival;
using Worktown.Sim.Town;
using Xunit;
using T = Worktown.Sim.Tests.Revival.TestRuns;

namespace Worktown.Sim.Tests.Revival
{
    public class RevivalReportTests
    {
        private static (RevivalCatalog Catalog, IReadOnlyList<RevivalTrace> Traces) Smoke(int seeds = 1, long hours = 20)
        {
            RevivalCatalog c = RevivalCatalog.CreateDefault(TownCatalog.CreateDefault());
            var engine = new ClimbEngine(ClimbCatalog.CreateDefault());
            var traces = Enumerable.Range(1, seeds)
                .Select(s => RevivalReplay.Replay(engine.Run(new CrewSetup(4, "average", ClimbStrategy.Reference, (ulong)s), hours * 3600), c, RevivalPlan.Default))
                .ToList();
            return (c, traces);
        }

        [Fact]
        public void FormatPrintsTheSectionsAndTheResult()
        {
            RevivalCatalog c = TestRevival.Spec();
            var traces = new[] { T.Trace(4, T.Stages, T.Good()), T.Trace(1, T.Stages, T.Good()) };
            IReadOnlyList<TargetCheck> checks = RevivalEvaluation.Evaluate(traces, RevivalTargets.Default);
            string text = RevivalReport.Format(c, traces, checks, new[] { new TargetCheck("V6", true, "footfall 1.20x 4p T1 pass") });
            Assert.StartsWith("REVIVAL REPORT (spec 065)\n", text);
            Assert.Contains("goals 300/4000/20000 points; points by stars 0/1/2/4/8; spill 250 permille; stage bonus 100000 ppm\n", text);
            Assert.Contains("== 1 players, 1 seeds, horizon 40.0 h (median hours to each level) ==\n", text);
            Assert.Contains("== 4 players, 1 seeds, horizon 40.0 h (median hours to each level) ==\n", text);
            Assert.Contains("main_street       1000  0%      1.5      5.0      16.0     0\n", text);
            Assert.Contains("station_square    1000  50%     10.0     never    never    0\n", text);
            Assert.Contains("shop main_street: Patched 1.5 h, Lively 5.0 h, Thriving 16.0 h; stages S2 1.2 h, S3 3.9 h, S4 7.9 h, S5 15.3 h\n", text);
            Assert.Contains("== targets ==\nPASS V1 1p", text);
            Assert.Contains("INFO V6 footfall 1.20x 4p T1 pass\n", text);
            Assert.EndsWith("RESULT: PASS (" + checks.Count.ToString(CultureInfo.InvariantCulture) + "/" + checks.Count.ToString(CultureInfo.InvariantCulture) + ")\n", text);
            Assert.DoesNotContain("\r", text);
        }

        [Fact]
        public void FormatCountsFailuresButNotInfo()
        {
            RevivalCatalog c = TestRevival.Spec();
            var traces = new[] { T.Trace(4, T.Stages, T.Good(), dropped: true) };
            var checks = RevivalEvaluation.Evaluate(traces, RevivalTargets.Default);
            string text = RevivalReport.Format(c, traces, checks, new[] { new TargetCheck("V6", false, "x") });
            Assert.EndsWith("RESULT: FAIL (" + (checks.Count - 1).ToString(CultureInfo.InvariantCulture) + "/" + checks.Count.ToString(CultureInfo.InvariantCulture) + ")\n", text);
            Assert.Throws<ArgumentException>(() => RevivalReport.Format(null!, traces, checks));
        }

        [Fact]
        public void SmokeFourPlayers20Hours() // the spec's smoke test: the shop street comes back first
        {
            (RevivalCatalog c, IReadOnlyList<RevivalTrace> traces) = Smoke();
            RevivalTrace t = traces[0];
            Assert.False(t.LevelDropped);
            Assert.True(t.StageEntries >= 3);
            long patched = t.LevelSeconds("main_street", RevivalLevel.Patched);
            Assert.InRange(patched, 1, 3 * 3600);
            Assert.True(t.LevelSeconds("main_street", RevivalLevel.Lively) > patched);
            Assert.True(t.MealsIn("main_street") > t.Meals.Where((m, i) => c.Districts[i].DistrictId != "main_street").Max());
            string text = RevivalReport.Format(c, traces, RevivalEvaluation.Evaluate(traces, RevivalTargets.Default));
            Assert.Contains("RESULT: ", text);
        }

        [Fact]
        public void SameInputsGiveEqualDigestsAndReportText() // AC2
        {
            (RevivalCatalog c1, IReadOnlyList<RevivalTrace> a) = Smoke(seeds: 2, hours: 6);
            (RevivalCatalog c2, IReadOnlyList<RevivalTrace> b) = Smoke(seeds: 2, hours: 6);
            Assert.Equal(a.Select(t => t.FinalDigest), b.Select(t => t.FinalDigest));
            Assert.NotEqual(a[0].FinalDigest, a[1].FinalDigest); // the seed matters
            Assert.Equal(RevivalReport.Format(c1, a, RevivalEvaluation.Evaluate(a, RevivalTargets.Default)),
                RevivalReport.Format(c2, b, RevivalEvaluation.Evaluate(b, RevivalTargets.Default)));
        }
    }
}
