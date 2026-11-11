using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Worktown.Sim.Climb;
using Xunit;

namespace Worktown.Sim.Tests.Climb
{
    public class ClimbDeterminismTests
    {
        private static readonly ClimbEngine Engine = new ClimbEngine(ClimbCatalog.CreateDefault());

        [Fact]
        public void SameSetupGivesTheSameDigestAndReport() // AC2
        {
            var setup = new CrewSetup(4, "average", ClimbStrategy.Reference, 1);
            ClimbRun a = Engine.Run(setup, 6 * 3600);
            ClimbRun b = new ClimbEngine(ClimbCatalog.CreateDefault()).Run(setup, 6 * 3600);
            Assert.Equal(a.Digest, b.Digest);
            Assert.Equal(a.Canonical, b.Canonical);
            Assert.Matches("^[0-9a-f]{64}$", a.Digest);
            string ra = ClimbReport.Format(new[] { a }, ClimbEvaluation.Evaluate(new[] { a }, ClimbTargets.Default));
            string rb = ClimbReport.Format(new[] { b }, ClimbEvaluation.Evaluate(new[] { b }, ClimbTargets.Default));
            Assert.Equal(ra, rb);
        }

        [Theory] // spec 037 (D39): money in cents gives the same climb as whole dollars: every stage at the same second, every
        [InlineData(1, "novice", "balanced+match", 3)] // service's money exactly x 100, the same report text
        [InlineData(4, "average", "builder+plus1", 1)]
        [InlineData(16, "expert", "rush+undercut", 2)]
        [InlineData(4, "expert", "balanced+minus1", 5)]
        public void CentsGiveTheSameClimbAsWholeDollars(int players, string profile, string strategy, int seed)
        {
            var setup = new CrewSetup(players, profile, ClimbStrategy.Parse(strategy), (ulong)seed);
            ClimbRun dollars = new ClimbEngine(ClimbCatalog.CreateDefault(1)).Run(setup, 40 * 3600);
            ClimbRun cents = Engine.Run(setup, 40 * 3600);
            Assert.Equal(dollars.Services.Count, cents.Services.Count);
            for (ClimbStage st = ClimbStage.FoodCorner; st <= ClimbStage.BeyondKitchen; st++)
                Assert.Equal(dollars.EnteredSeconds(st), cents.EnteredSeconds(st));
            for (int i = 0; i < cents.Services.Count; i++)
            {
                Assert.Equal(dollars.Services[i].Net * 100, cents.Services[i].Net);
                Assert.Equal(dollars.Services[i].BalanceAfter * 100, cents.Services[i].BalanceAfter);
            }
            string d = ClimbReport.Format(new[] { dollars }, ClimbEvaluation.Evaluate(new[] { dollars }, ClimbTargets.Default)); // spec 052b: the scale is read from the runs
            string c = ClimbReport.Format(new[] { cents }, ClimbEvaluation.Evaluate(new[] { cents }, ClimbTargets.Default));
            Assert.Equal(d, c);
        }

        [Fact]
        public void DifferentSeedsGiveDifferentDigests() // AC2
        {
            ClimbRun one = Engine.Run(new CrewSetup(4, "average", ClimbStrategy.Reference, 1), 3 * 3600);
            ClimbRun two = Engine.Run(new CrewSetup(4, "average", ClimbStrategy.Reference, 2), 3 * 3600);
            Assert.NotEqual(one.Digest, two.Digest);
        }

        [Fact]
        public void SmokeFourAverageBalancedMatchTwentyHours() // spec 052 test plan smoke test
        {
            ClimbRun run = Engine.Run(new CrewSetup(4, "average", ClimbStrategy.Reference, 1), 20 * 3600);
            Assert.True(run.EnteredSeconds(ClimbStage.FirstRestaurant) > 0);
            Assert.True(run.EnteredSeconds(ClimbStage.ServiceMix) > run.EnteredSeconds(ClimbStage.FirstRestaurant));
            Assert.True(run.EnteredSeconds(ClimbStage.Automation) > run.EnteredSeconds(ClimbStage.ServiceMix));
            Assert.True(run.Final.PlaySeconds >= 20 * 3600);
            Assert.Equal(run.Services.Count, run.Final.ServiceIndex);
        }

        [Fact]
        public void ClimbRuntimeUsesNoClockAndNoSystemRandom() // AC2
        {
            string dir = FindClimbSources();
            var bad = new Regex("DateTime|Stopwatch|TickCount|System\\.Random|float|double");
            foreach (string file in Directory.GetFiles(dir, "*.cs"))
            {
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                    Assert.False(bad.IsMatch(lines[i]), Path.GetFileName(file) + ":" + (i + 1) + ": " + lines[i].Trim());
            }
        }

        private static string FindClimbSources()
        {
            for (DirectoryInfo? d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            {
                string candidate = Path.Combine(d.FullName, "Runtime", "Climb");
                if (Directory.Exists(candidate)) return candidate;
                candidate = Path.Combine(d.FullName, "sim", "Runtime", "Climb");
                if (Directory.Exists(candidate)) return candidate;
            }
            throw new DirectoryNotFoundException("sim/Runtime/Climb not found above " + AppContext.BaseDirectory);
        }
    }
}
