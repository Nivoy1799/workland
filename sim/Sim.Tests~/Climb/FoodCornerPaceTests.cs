using System.Linq;
using Worktown.Sim.Climb;
using Xunit;
using T = Worktown.Sim.Tests.Climb.TestClimb;

namespace Worktown.Sim.Tests.Climb
{
    /// <summary>Spec 052b (e): stage 1 work factor, tip, stock cap and crew goal (AC8).</summary>
    public class FoodCornerPaceTests
    {
        private static ClimbCatalog Corner(int work = 1000, int tip = 0, int stock = 0, params CrewScaleRow[] rows) =>
            new ClimbCatalog(T.Profiles, T.Dishes, T.Channels, new RivalDef[0], T.Purchases,
                T.Stages.Select(s => s.Stage == ClimbStage.FoodCorner ? s with { WorkPermille = work, TipPermille = tip, StockPerService = stock } : s),
                T.BigJobs, T.Weights with { Outside = 0 }, "chips", 0, 1, rows.Length == 0 ? null : rows);

        [Fact]
        public void TheStockCapTurnsGuestsAwayAsSoldOutNotMissed() // AC8
        {
            var e = new ClimbEngine(Corner(stock: 3));
            CrewSetup crew = T.Setup(20, "expert"); // far more capacity than guests
            (ClimbState s, ServiceResult r) = e.RunService(e.Start(crew), crew);
            Assert.Equal(20, r.Chosen);
            Assert.Equal(3, r.Served);
            Assert.Equal(17, r.SoldOut);
            Assert.Equal(0, r.Missed);
            Assert.True(r.StarsTenths >= 40, "sold-out guests cost no stars: " + r.StarsTenths);
            Assert.Equal(40 + 40 * 3 / 3, r.WaitSeconds); // the wait counts the guests who were served or missed
            Assert.Equal(0, s.RecentMissed.Last());
            var free = new ClimbEngine(Corner());
            Assert.Equal(0, free.RunService(free.Start(crew), crew).Result.SoldOut); // 0 = no cap
        }

        [Fact]
        public void WorkPermilleScalesTheGuestActions() // AC8
        {
            ClimbState s = T.State();
            ChannelDef counter = T.Catalog.Channel("counter");
            DishDef chips = T.Catalog.Dish("chips");
            Assert.Equal(5, ClimbEngine.GuestActions(Corner(), s, counter, chips)); // 4 make + 1 pay
            Assert.Equal(10, ClimbEngine.GuestActions(Corner(work: 2000), s, counter, chips));
            Assert.Equal(19, ClimbEngine.GuestActions(Corner(work: 3900), s, counter, chips)); // 19.5 floored
            Assert.Equal(1, ClimbEngine.GuestActions(Corner(work: 1), s, counter, chips)); // at least 1
            Assert.Equal(6, ClimbEngine.GuestActions(Corner(work: 2000), s with { Stage = ClimbStage.FirstRestaurant }, counter, chips)); // other stages keep 1000 (4 + 1 pay + 1 clean)
        }

        [Fact]
        public void TipPermilleAddsExactlyItsShareOfRevenue() // AC8
        {
            Assert.Equal(20, ClimbEngine.Tip(100, 200));
            Assert.Equal(22, ClimbEngine.Tip(103, 220)); // 22.66 floored once
            Assert.Equal(2200, ClimbEngine.Tip(10300, 220, 100)); // whole dollars at cents
            Assert.Equal(0, ClimbEngine.Tip(-5, 200));
            CrewSetup crew = T.Setup(3, "expert");
            var plain = new ClimbEngine(Corner());
            var tipped = new ClimbEngine(Corner(tip: 250));
            ServiceResult a = plain.RunService(plain.Start(crew), crew).Result;
            ServiceResult b = tipped.RunService(tipped.Start(crew), crew).Result;
            Assert.Equal(a.Served, b.Served); // the tip draws nothing
            Assert.Equal(a.Revenue + a.Revenue * 250 / 1000, b.Revenue);
        }

        [Fact]
        public void TheGoalFactorChangesHowMuchOfTheGateAServiceEarns() // AC8 (D40: progress = net / G)
        {
            CrewSetup crew = T.Setup(2, "expert");
            var full = new ClimbEngine(Corner(rows: new CrewScaleRow(16, 1000, 0, 1000, 1000)));
            var half = new ClimbEngine(Corner(rows: new CrewScaleRow(16, 1000, 0, 1000, 500)));
            (ClimbState f, ServiceResult rf) = full.RunService(full.Start(crew), crew);
            (ClimbState h, ServiceResult rh) = half.RunService(half.Start(crew), crew);
            Assert.Equal(rf.Net, rh.Net); // the same service
            Assert.Equal(2 * f.Balance, h.Balance); // a half goal: each dollar is twice the progress toward the same gate
            long gate = full.Catalog.Purchase("lease").Cost;
            Assert.Equal(gate / 2, CrewScaling.ToReal(gate, 500)); // the real money this crew needs
        }
    }
}
