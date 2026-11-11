using System.Linq;
using Worktown.Sim.Climb;
using Xunit;
using T = Worktown.Sim.Tests.Climb.TestClimb;

namespace Worktown.Sim.Tests.Climb
{
    /// <summary>Spec 052b (c): big-job stakes grow with success (AC6).</summary>
    public class BigJobTierTests
    {
        private static readonly BigJobDef Job = new BigJobDef("j", "J", BigJobKind.Competition, 1000, 100, 1000, 0, 5000, null, 3, 1400, 3);

        [Fact]
        public void DepositPrizeAndGuestsPerTierMatchLiterals() // AC6
        {
            Assert.Equal(new long[] { 1000, 1400, 1960, 2744, 2744 }, Enumerable.Range(0, 5).Select(t => Job.DepositAt(t)));
            Assert.Equal(new long[] { 5000, 7000, 9800, 13720 }, Enumerable.Range(0, 4).Select(t => Job.PrizeAt(t)));
            Assert.Equal(new[] { 100, 140, 196, 274 }, Enumerable.Range(0, 4).Select(t => Job.GuestsAt(t)));
            Assert.Equal(274400, (Job with { Deposit = 100000 }).DepositAt(3, 100)); // cents, floored to whole dollars each step
        }

        [Fact]
        public void NextTierRisesOnAWinOrAnEventAndStopsAtMaxTier() // AC6
        {
            Assert.Equal(1, Job.NextTier(0, true));
            Assert.Equal(0, Job.NextTier(0, false)); // a lost competition keeps the tier
            Assert.Equal(3, Job.NextTier(3, true)); // MaxTier
            BigJobDef ev = Job with { Kind = BigJobKind.Event };
            Assert.Equal(2, ev.NextTier(1, false));
        }

        private static ClimbCatalog Jobs(int rivalScore) => T.Build(bigJobs: new[]
        {
            new BigJobDef("contest", "Contest", BigJobKind.Competition, 10, 0, 1000, rivalScore, 100, null, 0, 2000, 2),
            new BigJobDef("fair", "Fair", BigJobKind.Event, 20, 5, 2000, 0, 0, "lab", 0, 2000, 2),
        });

        [Fact]
        public void TheEngineRaisesTheTierAfterAWinAndAnEventNotAfterALoss() // AC6
        {
            CrewSetup setup = T.Setup(1, "expert");
            string[] owned = { "peeler", "sign", "lease", "soup_recipe", "booth_kit", "helper", "lab", "trophy_case" };
            var win = new ClimbEngine(Jobs(0));
            ClimbState s = T.WithPrices(T.State(ClimbStage.BeyondKitchen, 10_000, owned));
            ServiceResult r;
            (s, r) = win.RunService(s, setup);
            Assert.Equal("contest", r.BigJobId);
            Assert.Equal(10, r.JobCost);
            Assert.Equal(100, r.Revenue); // tier 0 prize
            Assert.Equal(1, s.TierOf("contest"));
            (s, r) = win.RunService(s, setup);
            Assert.Equal("contest", r.BigJobId); // no cooldown
            Assert.Equal(20, r.JobCost); // tier 1: deposit x 2
            Assert.Equal(200, r.Revenue);
            Assert.Equal(2, s.TierOf("contest"));
            (s, r) = win.RunService(s, setup);
            Assert.Equal(40, r.JobCost); // tier 2 is MaxTier
            Assert.Equal(2, s.TierOf("contest"));

            var lose = new ClimbEngine(Jobs(5000));
            ClimbState l = T.WithPrices(T.State(ClimbStage.BeyondKitchen, 10_000, owned));
            (l, r) = lose.RunService(l, setup);
            Assert.Equal("contest", r.BigJobId);
            Assert.Equal(0, r.Revenue);
            Assert.Equal(0, l.TierOf("contest")); // a loss leaves it
            l = l with { JobReadyAt = new System.Collections.Generic.Dictionary<string, int> { ["contest"] = 99 } };
            (l, r) = lose.RunService(l, setup);
            Assert.Equal("fair", r.BigJobId);
            Assert.Equal(5, r.Chosen); // tier 0 guests
            Assert.Equal(1, l.TierOf("fair"));
            Assert.Equal(40, ClimbPolicy.DepositNow(lose.Catalog, l, lose.Catalog.BigJob("fair")));
            Assert.Contains(40L, ClimbPolicy.GoalCosts(lose.Catalog, l)); // T7 reads the current tier's deposit
        }
    }
}
