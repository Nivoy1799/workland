using System;
using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Climb;
using Worktown.Sim.Revival;
using Xunit;
using T = Worktown.Sim.Tests.Revival.TestRuns;

namespace Worktown.Sim.Tests.Revival
{
    public class RevivalEvaluationTests
    {
        private static IReadOnlyList<TargetCheck> Eval(params RevivalTrace[] traces) => RevivalEvaluation.Evaluate(traces, RevivalTargets.Default);

        private static RevivalTrace Four(Action<Dictionary<string, double[]>>? change = null, bool dropped = false)
        {
            var levels = T.Good();
            change?.Invoke(levels);
            return T.Trace(4, T.Stages, levels, dropped);
        }

        private static void Passes(IReadOnlyList<TargetCheck> checks, string id) => Assert.All(checks.Where(c => c.Id == id), c => Assert.True(c.Passed, c.Line));

        private static TargetCheck Failing(IReadOnlyList<TargetCheck> checks, string id) => Assert.Single(checks, c => c.Id == id && !c.Passed);

        [Fact]
        public void AGoodTracePassesEveryTarget() // AC10, the passing side of V1-V5 and V7
        {
            IReadOnlyList<TargetCheck> checks = Eval(Four(), T.Trace(1, T.Stages, T.Good()), T.Trace(16, T.Stages, T.Good()));
            Assert.All(checks, c => Assert.True(c.Passed, c.Line));
            Assert.Equal(new[] { "V1", "V2", "V3", "V4", "V5", "V7" }, checks.Select(c => c.Id).Distinct().OrderBy(x => x));
            Assert.Equal(18, checks.Count); // per crew V1, V2 x 2, V5; V3 x 2 for 1 and 16; V4 for 4; V7 once
            Assert.Contains(checks, c => c.Line == "PASS V1 4p main_street Patched 1.5 h in S2 1.2 h -0.5..+1.0 h");
        }

        [Fact]
        public void V1FailsTooLateAndTooEarly()
        {
            TargetCheck late = Failing(Eval(Four(l => l["main_street"] = new[] { 2.5, 5.0, 16.0 })), "V1");
            Assert.Equal("FAIL V1 4p main_street Patched 2.5 h in S2 1.2 h -0.5..+1.0 h", late.Line);
            Failing(Eval(Four(l => l["main_street"] = new[] { 0.6, 5.0, 16.0 })), "V1");
            Failing(Eval(Four(l => l["main_street"] = new[] { -1, -1, -1.0 })), "V1");
        }

        [Fact]
        public void V2FailsLivelyBeforeTheServiceMixAndThrivingTooLate()
        {
            Assert.Contains("Lively 3.5 h", Failing(Eval(Four(l => l["main_street"] = new[] { 1.5, 3.5, 16.0 })), "V2").Detail);
            Assert.Contains("Thriving 21.0 h", Failing(Eval(Four(l => l["main_street"] = new[] { 1.5, 5.0, 21.0 })), "V2").Detail);
            Assert.Contains("Thriving never", Failing(Eval(Four(l => l["main_street"] = new[] { 1.5, 5.0, -1 })), "V2").Detail);
            Passes(Eval(Four(l => l["main_street"] = new[] { 1.5, 5.0, 20.3 })), "V2"); // S5 + 5 h is still inside
        }

        [Fact]
        public void V3FailsAnUnfairCrew()
        {
            var slow = T.Good();
            slow["main_street"] = new[] { 2.2, 5.0, 16.0 };     // V1 still passes, but 2.2 / 1.5 = 1.47x passes too
            Passes(Eval(Four(), T.Trace(1, T.Stages, slow)), "V3");
            slow["main_street"] = new[] { 1.5, 8.0, 16.0 };     // Lively 1.6x of 4 players
            TargetCheck f = Failing(Eval(Four(), T.Trace(1, T.Stages, slow)), "V3");
            Assert.Equal("FAIL V3 1p main_street Lively 8.0 h = 1.60x of 4p 5.0 h in 0.67-1.50x", f.Line);
            var fast = T.Good();
            fast["main_street"] = new[] { 0.9, 5.0, 16.0 };      // 0.6x
            Failing(Eval(Four(), T.Trace(16, T.Stages, fast)), "V3");
        }

        [Fact]
        public void V3LowerEdgeScalesWithTheCrewDifficulty() // 065 review: as T1, a crew with h(n) below 1000 may be faster
        {
            var fast = T.Good();
            fast["main_street"] = new[] { 0.6, 2.5, 16.0 }; // 0.40x and 0.50x of 4 players
            RevivalTrace Solo(int h) { RevivalTrace t = T.Trace(1, T.Stages, fast); return new RevivalTrace(1, 1, t.HorizonSeconds, t.DistrictIds, t.Levels, t.StageEntered, t.Meals, 0, false, "", h); }
            Passes(Eval(Four(), Solo(550)), "V3");                       // lower edge 0.67 x 0.55 = 0.36x
            Assert.Contains(Eval(Four(), Solo(550)), c => c.Line == "PASS V3 1p main_street Patched 0.6 h = 0.40x of 4p 1.5 h in 0.36-1.50x");
            Assert.Equal(2, Eval(Four(), Solo(1000)).Count(c => c.Id == "V3" && !c.Passed)); // without h both fail
            fast["main_street"] = new[] { 0.5, 2.5, 16.0 };              // 0.33x is below even the scaled edge
            Failing(Eval(Four(), Solo(550)), "V3");
        }

        [Fact]
        public void V4FailsADistrictThatNeverComesBack()
        {
            TargetCheck f = Failing(Eval(Four(l => l["workshop_lane"] = new[] { -1, -1, -1.0 })), "V4");
            Assert.Equal("FAIL V4 4p 11/12 districts Patched within 40.0 h (last workshop_lane never)", f.Line);
            Assert.DoesNotContain(Eval(T.Trace(1, T.Stages, T.Good())), c => c.Id == "V4"); // only the reference crew
        }

        [Fact]
        public void V5FailsWhenAnotherDistrictComesBackAfterTheLowRing()
        {
            TargetCheck f = Failing(Eval(Four(l => l["workshop_lane"] = new[] { 31.0, -1, -1 })), "V5");
            Assert.Equal("FAIL V5 4p low_ring (Patched 30.0 h) is not last: workshop_lane Patched 31.0 h", f.Line);
            Passes(Eval(Four(l => l["low_ring"] = new[] { -1, -1, -1.0 })), "V5"); // reaching no level is never early
        }

        [Fact]
        public void V7FailsWhenALevelDropped()
        {
            Passes(Eval(Four()), "V7");
            Assert.Equal("FAIL V7 1 of 2 replays dropped a level", Failing(Eval(Four(), Four(dropped: true)), "V7").Line);
        }

        // ---------- V6 ----------

        private static ClimbRun StageRun(params double[] hours)
        {
            var services = new List<ServiceResult>();
            for (int st = 1; st <= 5; st++)
                services.Add(T.Normal(st - 1, (ClimbStage)st, 1, 650, ("corner_counter", 1)) with { StartSeconds = (long)(hours[st - 1] * 3600) });
            return T.Run(4, services);
        }

        [Fact]
        public void V6PassesInsideTheT1BandsAndFailsOutside() // AC10
        {
            TargetCheck pass = RevivalEvaluation.FootfallCheck(1100, new[] { StageRun(0, 1.2, 3.9, 7.9, 15.3) }, ClimbTargets.Default, 4);
            Assert.True(pass.Passed, pass.Line);
            Assert.Equal("PASS V6 footfall 1.10x 4p T1 pass: S2 1.2 h in 0.75-1.5, S3 3.9 h in 3-5, S4 7.9 h in 7-11, S5 15.3 h in 12-20", pass.Line);
            TargetCheck fail = RevivalEvaluation.FootfallCheck(1300, new[] { StageRun(0, 1.2, 3.0, 6.1, 12.1) }, ClimbTargets.Default, 4);
            Assert.False(fail.Passed);
            Assert.Contains("S4 6.1 h in 7-11", fail.Detail);
            Assert.False(RevivalEvaluation.FootfallCheck(1100, new[] { StageRun(0, 1.2, 3.9, 7.9, 15.3) }, ClimbTargets.Default, 16).Passed); // no 16p runs
        }

        [Fact]
        public void WithFootfallMultipliesOnlyTheGuests()
        {
            ClimbCatalog c = ClimbCatalog.CreateDefault();
            ClimbCatalog same = RevivalEvaluation.WithFootfall(c, 1000);
            var setup = new CrewSetup(4, "average", ClimbStrategy.Reference, 3);
            Assert.Equal(new ClimbEngine(c).Run(setup, 3 * 3600).Digest, new ClimbEngine(same).Run(setup, 3 * 3600).Digest);
            ClimbCatalog more = RevivalEvaluation.WithFootfall(c, 1300);
            Assert.Equal(52, more.Channel("corner_counter").GuestsPerService);  // 40 x 1.3
            Assert.Equal(117, more.Channel("street_counter").GuestsPerService); // 90 x 1.3
            Assert.Equal(c.Channel("delivery").Upkeep, more.Channel("delivery").Upkeep);
            Assert.Equal(c.Purchase("rent_restaurant").Cost, more.Purchase("rent_restaurant").Cost);
            Assert.Equal(c.StartingMoney, more.StartingMoney);
            Assert.Equal(c.MoneyScale, more.MoneyScale);
            Assert.Equal(c.CrewTable, more.CrewTable);
            Assert.Throws<ArgumentOutOfRangeException>(() => RevivalEvaluation.WithFootfall(c, -1));
        }

        [Fact]
        public void MedianAndHours()
        {
            Assert.Equal(3, RevivalEvaluation.Median(new long[] { 5, 1, 3 }));
            Assert.Equal(ClimbEvaluation.Dnf, RevivalEvaluation.Median(new long[0]));
            Assert.Equal(ClimbEvaluation.Dnf, RevivalEvaluation.Median(new[] { 1, ClimbEvaluation.Dnf, ClimbEvaluation.Dnf }));
            Assert.Equal("1.4", RevivalEvaluation.H(5000));
            Assert.Equal("0.0", RevivalEvaluation.H(0));
            Assert.Equal("never", RevivalEvaluation.H(ClimbEvaluation.Dnf));
            Assert.Equal("never", RevivalEvaluation.H(-1));
        }
    }
}
