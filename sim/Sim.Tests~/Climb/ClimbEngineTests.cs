using System;
using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Climb;
using Xunit;
using T = Worktown.Sim.Tests.Climb.TestClimb;

namespace Worktown.Sim.Tests.Climb
{
    public class ClimbEngineTests
    {
        private static ClimbEngine NoRivalNoOutside() =>
            new ClimbEngine(T.Build(rivals: new RivalDef[0], weights: T.Weights with { Outside = 0 }));

        [Fact]
        public void StartIsStageOneWithTheStartingMoney()
        {
            var engine = new ClimbEngine(T.Build(startingMoney: 7));
            ClimbState s = engine.Start(T.Setup());
            Assert.Equal(ClimbStage.FoodCorner, s.Stage);
            Assert.Equal(7, s.Balance);
            Assert.Equal(ClimbEngine.StartReputation, s.Reputation);
            Assert.Equal(0, s.EnteredSeconds(ClimbStage.FoodCorner));
            Assert.Equal(-1, s.EnteredSeconds(ClimbStage.FirstRestaurant));
            Assert.Empty(s.Owned);
            Assert.Equal(600, s.LastQuality);
        }

        [Fact]
        public void OneServiceStepByStep()
        {
            ClimbState start = T.Engine.Start(T.Setup());
            (ClimbState s, ServiceResult r) = T.Engine.RunService(start, T.Setup());
            Assert.Equal(0, r.Index);
            Assert.Equal(0, r.StartSeconds);
            Assert.Equal(420, r.Seconds);
            Assert.Equal(1, s.ServiceIndex);
            Assert.Equal(420, s.PlaySeconds); // the injected clock: service + between seconds
            Assert.Empty(r.Bought); // 0 money
            Assert.Equal(new[] { "counter" }, r.ChannelIds);
            Assert.True(r.YourGuests[0] + r.RivalGuests[0] <= 20);
            Assert.Equal(r.YourGuests[0], r.Chosen);
            Assert.Equal(r.Chosen, r.Served + r.Missed);
            Assert.Equal(5L * r.Served, r.Revenue); // Match: the reference price $5
            Assert.Equal(r.Served + r.Mistakes, r.IngredientCost);
            Assert.Equal(1, r.Upkeep);
            Assert.Equal(r.Revenue - r.IngredientCost - r.Upkeep, r.BalanceAfter);
            Assert.Equal(r.BalanceAfter, s.Balance);
            Assert.Equal(r.Net, s.Balance);
            Assert.Equal(new[] { 10L, 20L, 50L }, r.GoalCosts);
        }

        [Fact]
        public void WithoutRivalsAndOutsideEveryGuestOfTheCeilingChoosesYou() // AC3
        {
            ClimbEngine e = NoRivalNoOutside();
            (_, ServiceResult r) = e.RunService(e.Start(T.Setup()), T.Setup());
            Assert.Equal(20, r.Chosen);
            Assert.Equal(-1, r.RivalGuests[0]);
        }

        [Fact]
        public void PlayersBeyondTheWorkSpotsAddExactlyTheOverflow() // AC5
        {
            ClimbState s = T.State();
            Assert.Equal(1, ClimbEngine.WorkSpots(T.Catalog, s));
            Assert.Equal((60, 500), ClimbEngine.Capacity(T.Catalog, s, T.Worker, 1, 360));
            Assert.Equal((90, 500), ClimbEngine.Capacity(T.Catalog, s, T.Worker, 2, 360));
            Assert.Equal((120, 500), ClimbEngine.Capacity(T.Catalog, s, T.Worker, 3, 360));
            ClimbState two = T.State(ClimbStage.FirstRestaurant, 0, "booth_kit", "helper");
            Assert.Equal(3, ClimbEngine.WorkSpots(T.Catalog, two));
            // 3 players in 3 spots (180) + the helper 60 at skill 1000.
            Assert.Equal((240, 625), ClimbEngine.Capacity(T.Catalog, two, T.Worker, 3, 360));
        }

        [Fact]
        public void SoloNoviceServesFewerThanSoloExpert() // AC5
        {
            ClimbEngine e = NoRivalNoOutside();
            (_, ServiceResult slow) = e.RunService(e.Start(T.Setup(profile: "worker")), T.Setup(profile: "worker"));
            (_, ServiceResult fast) = e.RunService(e.Start(T.Setup(profile: "expert")), T.Setup(profile: "expert"));
            Assert.True(slow.Served < fast.Served);
            Assert.Equal(20, fast.Served);
            Assert.True(slow.Missed > 0);

            var d = new ClimbEngine(ClimbCatalog.CreateDefault());
            int Served(string p) => Enumerable.Range(1, 5).Sum(seed =>
                d.Run(new CrewSetup(1, p, ClimbStrategy.Reference, (ulong)seed), 3 * 3600).Services.Sum(x => x.Served));
            Assert.True(Served("novice") < Served("expert"));
        }

        [Fact]
        public void AMistakeCostsActionsAndIngredientsTwice() // AC5
        {
            ClimbEngine e = NoRivalNoOutside();
            int mistakes = 0;
            foreach (int players in new[] { 1, 10 })
            {
                CrewSetup setup = T.Setup(players, "clumsy");
                ClimbState s = e.Start(setup);
                int capacity = ClimbEngine.Capacity(e.Catalog, s, T.Clumsy, players, 360).Actions;
                for (int k = 0; k < 8; k++)
                {
                    ServiceResult r;
                    (s, r) = e.RunService(s with { Balance = 0 }, setup);
                    Assert.Equal(r.Served + r.Mistakes, r.IngredientCost); // chips cost $1: a remake buys them again
                    Assert.True(5L * (r.Served + r.Mistakes) <= capacity); // 5 actions per make, a remake costs them again
                    if (players == 10) Assert.Equal(0, r.Missed);
                    mistakes += r.Mistakes;
                }
            }
            Assert.True(mistakes > 0);
        }

        [Fact]
        public void DemandCeilingCapsBigCrewsAtStageOne() // AC4
        {
            var d = new ClimbEngine(ClimbCatalog.CreateDefault());
            long MedianNet(int players)
            {
                var nets = new List<long>();
                for (int seed = 1; seed <= 5; seed++)
                {
                    ClimbRun run = d.Run(new CrewSetup(players, "average", ClimbStrategy.Reference, (ulong)seed), 2 * 3600);
                    nets.AddRange(run.Services.Where(x => x.Stage == ClimbStage.FoodCorner).Select(x => x.Net));
                }
                return ClimbEvaluation.Median(nets);
            }
            long four = MedianNet(4), sixteen = MedianNet(16);
            Assert.True(four > 0);
            Assert.True(sixteen * 1000 <= four * 1150, $"16p {sixteen} vs 4p {four}");
        }

        [Fact]
        public void InDebtNothingIsBoughtUntilTheBalanceIsBackAboveZero() // AC6
        {
            CrewSetup setup = T.Setup(3, "expert");
            ClimbState s = T.Engine.Start(setup) with { Balance = -100 };
            bool boughtAfterDebt = false;
            for (int k = 0; k < 40; k++)
            {
                long before = s.Balance;
                ServiceResult r;
                (s, r) = T.Engine.RunService(s, setup);
                if (before < 0) Assert.Empty(r.Bought);
                else if (r.Bought.Count > 0) boughtAfterDebt = true;
            }
            Assert.True(boughtAfterDebt);
        }

        [Fact]
        public void StageNeverDecreasesAndReputationStaysAboveTheFloor() // AC6
        {
            ClimbCatalog c = ClimbCatalog.CreateDefault();
            foreach (string profile in new[] { "novice", "expert" })
            {
                ClimbRun run = new ClimbEngine(c).Run(new CrewSetup(4, profile, ClimbStrategy.Reference, 2), 20 * 3600);
                ClimbStage last = ClimbStage.FoodCorner;
                foreach (ServiceResult r in run.Services)
                {
                    Assert.True(r.Stage >= last);
                    last = r.Stage;
                    Assert.True(r.Reputation >= c.StageOf(r.Stage).ReputationFloorPermille);
                }
            }
        }

        [Fact]
        public void BigJobsReplaceAServiceAndRespectCooldowns()
        {
            CrewSetup setup = T.Setup(1, "expert");
            ClimbState s = T.State(ClimbStage.BeyondKitchen, 1000);
            ServiceResult r;
            (s, r) = T.Engine.RunService(s, setup);
            Assert.Equal("contest", r.BigJobId); // the first ready job by id
            Assert.Contains("lab", r.Bought); // Balanced buys what it can first (lab: +50 quality)
            Assert.Equal(10, r.JobCost);
            Assert.Equal(100, r.Revenue); // skill 900 + 50 against 0: always a win
            Assert.Equal(1, s.Trophies);
            Assert.Equal(4, s.JobReadyAt["contest"]);
            (s, r) = T.Engine.RunService(s, setup);
            Assert.Equal("fair", r.BigJobId); // needs the lab
            Assert.Equal(5, r.Chosen);
            Assert.True(r.Served > 0);
            (s, r) = T.Engine.RunService(s, setup);
            Assert.Null(r.BigJobId); // both on cooldown
            (_, r) = T.Engine.RunService(s with { ServiceIndex = 4, Balance = 1000 }, setup);
            Assert.Equal("contest", r.BigJobId);
        }

        [Fact]
        public void RunStopsAtTheHorizon()
        {
            ClimbRun run = T.Engine.Run(T.Setup(), 4200);
            Assert.Equal(10, run.Services.Count);
            Assert.Equal(4200, run.Final.PlaySeconds);
        }

        [Fact]
        public void BadSetupsThrow()
        {
            Assert.Throws<ArgumentException>(() => T.Engine.Start(T.Setup(players: 0)));
            Assert.Throws<ArgumentException>(() => T.Engine.Start(T.Setup(profile: "nobody")));
            Assert.Throws<ArgumentException>(() => T.Engine.Start(T.Setup() with { ServiceSeconds = 30 }));
            Assert.Throws<ArgumentException>(() => T.Engine.Start(T.Setup() with { BetweenSeconds = -1 }));
            Assert.Throws<ArgumentNullException>(() => new ClimbEngine(null!));
        }

        [Fact]
        public void GuestActionsUseChannelFactorSavingsPaymentAndCleaning()
        {
            ClimbCatalog c = T.Catalog;
            DishDef chips = c.Dish("chips");
            ChannelDef counter = c.Channel("counter");
            Assert.Equal(5, ClimbEngine.GuestActions(c, T.State(), counter, chips));
            Assert.Equal(3, ClimbEngine.GuestActions(c, T.State(ClimbStage.FoodCorner, 0, "peeler"), counter, chips));
            Assert.Equal(6, ClimbEngine.GuestActions(c, T.State(ClimbStage.FirstRestaurant), counter, chips));
            ChannelDef slow = counter with { ActionsPermille = 1500 };
            Assert.Equal(8, ClimbEngine.GuestActions(c, T.State(ClimbStage.FirstRestaurant), slow, chips));
            ClimbCatalog d = ClimbCatalog.CreateDefault();
            ClimbState s = T.State() with { Stage = ClimbStage.Automation, Owned = new[] { "second_basket", "fry_dump", "dishwasher" } };
            Assert.Equal(3, ClimbEngine.GuestActions(d, s, d.Channel("street_counter"), d.Dish("fries"))); // 6-2-2 make, 1 pay, 0 clean
        }

        [Fact]
        public void SmallFormulas()
        {
            Assert.Equal(62, ClimbEngine.MistakePermille(500));
            Assert.Equal(125, ClimbEngine.MistakePermille(0));
            Assert.Equal(0, ClimbEngine.MistakePermille(1000));
            Assert.Equal(600, ClimbEngine.DishQuality(600, 500, 0, 0));
            Assert.Equal(900, ClimbEngine.DishQuality(600, 1000, 0, 0));
            Assert.Equal(1000, ClimbEngine.DishQuality(900, 1000, 200, 40));
            Assert.Equal(0, ClimbEngine.DishQuality(0, 0, 0, -40));
            Assert.Equal(5, ClimbEngine.Stars(800, 80));
            Assert.Equal(4, ClimbEngine.Stars(799, 80));
            Assert.Equal(4, ClimbEngine.Stars(800, 121));
            Assert.Equal(1, ClimbEngine.Stars(100, 200));
            Assert.Equal(80, ClimbEngine.WaitSeconds(10, 10));
            Assert.Equal(120, ClimbEngine.WaitSeconds(10, 5));
            Assert.Equal(240, ClimbEngine.WaitSeconds(5, 0));
            Assert.Equal(45, ClimbEngine.StarsTenths(new[] { 800, 600 }, 0, 80));
            Assert.Equal(33, ClimbEngine.StarsTenths(new[] { 800, 600 }, 2, 80));
            Assert.Equal(0, ClimbEngine.StarsTenths(new int[0], 0, 80));
            Assert.Equal(650, ClimbEngine.NextReputation(500, 50, 300));
            Assert.Equal(450, ClimbEngine.NextReputation(500, 10, 450)); // never below the stage floor (critique P4)
            Assert.Equal(1000, ClimbEngine.NextReputation(1000, 50, 0));
        }
    }
}
