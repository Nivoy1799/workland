using System;
using System.Linq;
using Worktown.Sim.Climb;
using Xunit;
using T = Worktown.Sim.Tests.Climb.TestClimb;

namespace Worktown.Sim.Tests.Climb
{
    /// <summary>Spec 052b (a), D40: the crew table, <see cref="CrewScaling.For"/> and what it scales (AC3).</summary>
    public class CrewScalingTests
    {
        private static readonly CrewScaleRow[] Rows =
        {
            new CrewScaleRow(1, 550, 60), new CrewScaleRow(2, 750, 40), new CrewScaleRow(3, 900, 20),
            new CrewScaleRow(7, 1000), new CrewScaleRow(16, 1000),
        };

        private static ClimbCatalog WithRows(params CrewScaleRow[] rows) =>
            new ClimbCatalog(T.Profiles, T.Dishes, T.Channels, T.Rivals, T.Purchases, T.Stages, T.BigJobs, T.Weights, "chips", 0, 1, rows);

        [Fact]
        public void RowForPicksTheFirstRowThatCoversTheCrew() // AC3
        {
            ClimbCatalog c = WithRows(Rows);
            Assert.Equal(1, c.RowFor(1).MaxPlayers);
            Assert.Equal(2, c.RowFor(2).MaxPlayers);
            Assert.Equal(3, c.RowFor(3).MaxPlayers);
            Assert.Equal(7, c.RowFor(4).MaxPlayers);
            Assert.Equal(7, c.RowFor(7).MaxPlayers);
            Assert.Equal(16, c.RowFor(8).MaxPlayers);
            Assert.Equal(16, c.RowFor(16).MaxPlayers);
            Assert.Equal(16, c.RowFor(40).MaxPlayers); // beyond the table: the last row
        }

        [Fact]
        public void TheD40RuleGivesDemandNOverFourAndGoalNTimesHOverFour()
        {
            ClimbCatalog d = ClimbCatalog.CreateDefault();
            Assert.Equal(new[] { 550, 750, 900, 1000, 1000, 1000 }, ClimbCatalog.DefaultCrewTable.Select(r => r.DifficultyPermille));
            CrewFactors one = CrewScaling.For(d, 1), two = CrewScaling.For(d, 2), four = CrewScaling.For(d, 4), sixteen = CrewScaling.For(d, 16);
            Assert.Equal((250, 137), (one.DemandPermille, one.GoalPermille)); // 1 x 550 / 4
            Assert.Equal((500, 375), (two.DemandPermille, two.GoalPermille));
            Assert.Equal((1000, 1000), (four.DemandPermille, four.GoalPermille)); // the reference crew
            Assert.Equal((4000, 4000), (sixteen.DemandPermille, sixteen.GoalPermille)); // the plateau: h = 1
            Assert.Equal(1000, CrewScaling.For(d, 8).DifficultyPermille);
            int stock4 = d.StageOf(ClimbStage.FoodCorner).StockPerService;
            Assert.Equal(stock4, four.StockPerService);
            Assert.Equal(4 * stock4, sixteen.StockPerService);
            Assert.Equal((stock4 + 3) / 4, one.StockPerService); // rounded up
            Assert.Throws<ArgumentOutOfRangeException>(() => CrewScaling.For(d, 0));
        }

        [Fact]
        public void FixedFieldsOverrideTheRule()
        {
            ClimbCatalog n = T.Catalog; // the neutral table of the constructor
            CrewFactors f = CrewScaling.For(n, 9);
            Assert.Equal((1000, 1000, 0), (f.DemandPermille, f.GoalPermille, f.StockPerService));
            ClimbCatalog cal = ClimbCatalog.CreateDefault(crewTable: ClimbCatalog.CalibrationCrewTable(ClimbCatalog.DefaultCrewTable));
            foreach (int p in new[] { 1, 4, 16 })
            {
                CrewFactors c = CrewScaling.For(cal, p);
                Assert.Equal((1000, 1000, 12), (c.DemandPermille, c.GoalPermille, c.StockPerService)); // the 053 corner
            }
            Assert.Single(ClimbCatalog.NeutralCrewTable);
        }

        [Fact]
        public void ASoloCeilingIsTheChannelCeilingTimesTheDemandFactor() // AC3 (D40: 250 permille)
        {
            ClimbCatalog d = ClimbCatalog.CreateDefault();
            ClimbState s = T.State() with { Owned = new string[0] };
            ChannelDef corner = d.Channel("corner_counter");
            CrewFactors solo = CrewScaling.For(d, 1);
            Assert.Equal(corner.GuestsPerService * 250 / 1000, ClimbEngine.ChannelCeiling(d, s, corner, solo));
            Assert.Equal(corner.GuestsPerService * 4, ClimbEngine.ChannelCeiling(d, s, corner, CrewScaling.For(d, 16)));
            Assert.Equal(1, CrewScaling.ScaleCount(1, 250)); // never below 1 guest
            Assert.Equal(0, CrewScaling.ScaleCount(0, 4000));
        }

        [Fact]
        public void PatienceMovesTheStarThreshold() // AC3: a solo wait of 170 s costs no star, 190 s does
        {
            ClimbCatalog c = WithRows(Rows);
            int patience = CrewScaling.For(c, 1).PatienceSeconds;
            Assert.Equal(60, patience);
            Assert.Equal(5, ClimbEngine.Stars(800, 170, patience));
            Assert.Equal(4, ClimbEngine.Stars(800, 190, patience));
            Assert.Equal(4, ClimbEngine.Stars(800, 170)); // without patience
            Assert.Equal(45, ClimbEngine.StarsTenths(new[] { 800, 600 }, 0, 170, patience));
        }

        [Fact]
        public void TheCatalogValidatesTheTable() // AC3
        {
            Assert.Throws<ArgumentException>(() => WithRows(new CrewScaleRow(4, 1000), new CrewScaleRow(2, 1000), new CrewScaleRow(16, 1000)));
            Assert.Throws<ArgumentException>(() => WithRows(new CrewScaleRow(1, 1000), new CrewScaleRow(8, 1000))); // last row below 16
            Assert.Throws<ArgumentException>(() => WithRows());
            Assert.Throws<ArgumentException>(() => WithRows(new CrewScaleRow(16, 0)));
            Assert.Throws<ArgumentException>(() => WithRows(new CrewScaleRow(16, 1000, 601)));
            Assert.Throws<ArgumentException>(() => WithRows(new CrewScaleRow(16, 1000, 0, 5001)));
            Assert.Throws<ArgumentException>(() => WithRows(new CrewScaleRow(16, 1000, 0, 0, 0, -1)));
            WithRows(Rows);
        }

        [Fact]
        public void MoneyConversionsFloorToWholeUnits()
        {
            Assert.Equal(4100, CrewScaling.ToReal(30000, 137, 100)); // $41.10 floored to $41
            Assert.Equal(500, CrewScaling.ScaleMoney(500, 1000, 100));
            Assert.Equal(100, CrewScaling.ScaleMoney(500, 250, 100)); // $1.25 floored to $1
            Assert.Equal(7200, CrewScaling.ToReference(1000, 137, 100)); // $10 real is $72.99 of progress, floored to $72
            Assert.Equal(-200, CrewScaling.ToReference(-100, 500, 100)); // a loss floors down
            Assert.Equal(1000, CrewScaling.ToReference(1000, 1000));
            Assert.Throws<ArgumentOutOfRangeException>(() => CrewScaling.ToReference(1, 0));
        }

        [Fact]
        public void ProgressIsNetOverTheGoalFactorSoAJoinChangesOnlyLaterServices()
        {
            ClimbCatalog d = ClimbCatalog.CreateDefault();
            var engine = new ClimbEngine(d);
            var solo = new CrewSetup(1, "average", ClimbStrategy.Reference, 1);
            ClimbState s = engine.Start(solo);
            (ClimbState a, ServiceResult r1) = engine.RunService(s, solo);
            Assert.Equal(137, r1.GoalPermille);
            Assert.Equal(s.Balance + r1.NetReference, a.Balance);
            var four = solo with { Players = 4 }; // three friends join: the next service uses the 4-player factors
            (ClimbState b, ServiceResult r2) = engine.RunService(a, four);
            Assert.Equal(1000, r2.GoalPermille);
            Assert.Equal(a.Balance - r2.Spent + r2.NetReference, b.Balance); // the progress so far is kept as it was
        }

        [Fact]
        public void ALeaveChangesOnlyLaterServices() // 052b review: 4 -> 1
        {
            ClimbCatalog d = ClimbCatalog.CreateDefault();
            var engine = new ClimbEngine(d);
            var four = new CrewSetup(4, "average", ClimbStrategy.Reference, 1);
            ClimbState s = engine.Start(four);
            (ClimbState a, ServiceResult r1) = engine.RunService(s, four);
            Assert.Equal(1000, r1.GoalPermille);
            var solo = four with { Players = 1 }; // three players leave
            (ClimbState b, ServiceResult r2) = engine.RunService(a, solo);
            Assert.Equal(137, r2.GoalPermille);
            Assert.Equal(a.Balance - r2.Spent + r2.NetReference, b.Balance); // the 4-player progress stays
            Assert.True(r2.Revenue < r1.Revenue); // a quarter of the guests
            Assert.Equal(CrewScaling.ToReference(r2.Net, 137, d.MoneyScale), r2.NetReference); // each solo dollar counts 1 / 0.137
        }

        [Fact]
        public void CapacityScalesWorkSpotsAndStaffWithDemand()
        {
            ClimbState two = T.State(ClimbStage.FirstRestaurant, 0, "booth_kit", "helper");
            Assert.Equal((240, 625), ClimbEngine.Capacity(T.Catalog, two, T.Worker, 3, 360));
            // Demand 2000: 6 spots (3 players all full), the helper's 60 actions doubled.
            Assert.Equal((300, 700), ClimbEngine.Capacity(T.Catalog, two, T.Worker, 3, 360, 2000));
            // Demand 500: 1 spot (3 x 0.5 = 1.5): 1 full player, 2 at 500 permille overflow, the helper halved.
            Assert.Equal((150, 600), ClimbEngine.Capacity(T.Catalog, two, T.Worker, 3, 360, 500));
        }
    }
}
