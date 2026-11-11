using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Climb;
using Xunit;
using T = Worktown.Sim.Tests.Climb.TestClimb;

namespace Worktown.Sim.Tests.Climb
{
    /// <summary>052b review: a crew takes a big job only when it is expected to pay; events are capped at what it can serve.</summary>
    public class BigJobPolicyTests
    {
        private static readonly string[] All = { "peeler", "sign", "lease", "soup_recipe", "booth_kit", "helper", "lab", "trophy_case" };

        [Fact]
        public void WinPermilleCountsTheTwoUniformDraws()
        {
            Assert.Equal(497, ClimbPolicy.WinPermille(700, 700)); // ties lose: (201^2 - 201) / 2 of 201^2
            Assert.Equal(280, ClimbPolicy.WinPermille(650, 700)); // 11,325 of 40,401 pairs
            Assert.Equal(1000, ClimbPolicy.WinPermille(950, 700)); // 250 above: always
            Assert.Equal(0, ClimbPolicy.WinPermille(500, 700)); // 200 below: never
            Assert.True(ClimbPolicy.WinPermille(700, 650) + ClimbPolicy.WinPermille(650, 700) < 1000); // ties are nobody's
        }

        [Fact]
        public void ExpectedNetOfACompetitionIsWinChanceTimesPrize()
        {
            ClimbCatalog c = T.Catalog;
            ClimbState s = T.WithPrices(T.State(ClimbStage.BeyondKitchen, 1000, All));
            CrewSetup expert = T.Setup(1, "expert");
            long upkeep = c.Upkeep(ClimbStage.BeyondKitchen, s.Owned);
            Assert.Equal(100 - 10 - upkeep, ClimbPolicy.ExpectedJobNet(c, s, expert, c.BigJob("contest"))); // 900 + 50 against 0: a sure win
            ClimbCatalog hard = T.Build(bigJobs: new[] { c.BigJob("contest") with { RivalScorePermille = 950 } });
            Assert.Equal(100 * ClimbPolicy.WinPermille(950, 950) / 1000 - 10 - upkeep, ClimbPolicy.ExpectedJobNet(hard, s, expert, hard.BigJob("contest")));
        }

        [Fact]
        public void ExpectedNetOfAnEventServesWhatTheCrewCan()
        {
            ClimbCatalog c = T.Catalog;
            ClimbState s = T.WithPrices(T.State(ClimbStage.BeyondKitchen, 1000, All)) with { ActiveMenu = new[] { "chips" } };
            CrewSetup expert = T.Setup(1, "expert");
            long upkeep = c.Upkeep(ClimbStage.BeyondKitchen, s.Owned);
            // 5 guests x chips $5 x 2000 permille = $50; ingredients 5 x $1 with remakes at the mean rate, x 0.5 (lab), floored = $2.
            Assert.Equal(50 - 2 - upkeep - 20, ClimbPolicy.ExpectedJobNet(c, s, expert, c.BigJob("fair")));
        }

        [Fact]
        public void ServableGuestsIsCapacityOverGuestActions()
        {
            ClimbCatalog c = T.Catalog;
            ClimbState s = T.State(ClimbStage.BeyondKitchen) with { ActiveMenu = new[] { "chips" } };
            ChannelDef counter = c.Channel("counter");
            // 1 expert: 20 APM x 6 min = 120 actions; chips cost 4 + 1 pay + 1 clean = 6; mistakes 12 permille: 120 / 6.072 = 19.
            Assert.Equal(19, ClimbEngine.ServableGuests(c, s, T.Profiles[2], 1, 360, 1000, counter));
            Assert.Equal(39, ClimbEngine.ServableGuests(c, s, T.Profiles[2], 2, 360, 1000, counter)); // two spots
        }

        [Fact]
        public void TheEngineCapsEventGuestsAndRaisesThePricePerTier()
        {
            ClimbCatalog c = T.Build(bigJobs: new[] { new BigJobDef("fair", "Fair", BigJobKind.Event, 20, 500, 2000, 0, 0, "lab", 0, 1500, 3) });
            var e = new ClimbEngine(c);
            CrewSetup expert = T.Setup(1, "expert");
            ClimbState s = T.WithPrices(T.State(ClimbStage.BeyondKitchen, 1000, All)) with { ActiveMenu = new[] { "chips" } };
            int servable = ClimbEngine.ServableGuests(c, s, T.Profiles[2], 1, 360, 1000, c.Channel("counter"));
            (ClimbState s1, ServiceResult r1) = e.RunService(s, expert);
            Assert.Equal("fair", r1.BigJobId);
            Assert.Equal(servable, r1.Chosen); // not the 500 guests of the data
            (_, ServiceResult r2) = e.RunService(s1 with { Balance = 1000 }, expert);
            Assert.Equal(30, r2.JobCost); // tier 1 deposit
            Assert.True(r2.Revenue > r1.Revenue, $"tier 1 pays more per guest: {r2.Revenue} vs {r1.Revenue}");
            Assert.Equal(3000, c.BigJob("fair").PricePermilleAt(1));
        }

        [Fact]
        public void ChooseJobNeedsTheDepositTwiceAndAnExpectedNetAtLeastTheLastService()
        {
            ClimbCatalog c = T.Catalog;
            CrewSetup expert = T.Setup(1, "expert");
            ClimbState s = T.WithPrices(T.State(ClimbStage.BeyondKitchen, 1000, All));
            long contest = ClimbPolicy.ExpectedJobNet(c, s, expert, c.BigJob("contest"));
            Assert.Equal("contest", ClimbPolicy.ChooseJob(c, s with { LastNet = contest }, expert)!.Id);
            Assert.NotEqual("contest", ClimbPolicy.ChooseJob(c, s with { LastNet = contest + 1 }, expert)?.Id); // a normal service pays more
            Assert.Null(ClimbPolicy.ChooseJob(c, s with { LastNet = 10_000 }, expert));
            Assert.Null(ClimbPolicy.ChooseJob(c, s with { Balance = 19, LastNet = -1000 }, expert)); // 2 x the $10 deposit not held
            Assert.Null(ClimbPolicy.ChooseJob(c, T.WithPrices(T.State(ClimbStage.Automation, 1000, All)), expert)); // stage 5 only
        }

        [Fact]
        public void AJobServiceChangesNoMenuAndRecordsTheLastNormalNet()
        {
            var e = new ClimbEngine(T.Catalog);
            CrewSetup expert = T.Setup(1, "expert");
            ClimbState s = T.WithPrices(T.State(ClimbStage.BeyondKitchen, 1000, All)) with
            {
                ActiveMenu = new[] { "chips", "soup" }, RecentMissed = new[] { 900 },
            };
            (ClimbState j, ServiceResult r) = e.RunService(s, expert);
            Assert.NotNull(r.BigJobId);
            Assert.Null(r.MenuDropped); // the drop waits for a normal service
            Assert.Equal(new[] { "chips", "soup" }, j.ActiveMenu);
            Assert.Equal(new[] { 900 }, j.RecentMissed);
            var cool = new Dictionary<string, int> { ["contest"] = 99, ["fair"] = 99 };
            (ClimbState n, ServiceResult rn) = e.RunService(j with { JobReadyAt = cool }, expert);
            Assert.Null(rn.BigJobId);
            Assert.Equal("soup", rn.MenuDropped);
            Assert.Equal(rn.Net, n.LastNet);
        }
    }
}
