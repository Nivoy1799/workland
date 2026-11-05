using System;
using System.Linq;
using Worktown.Sim.Core;
using Worktown.Sim.Economy;
using Xunit;

namespace Worktown.Sim.Tests.Economy
{
    public class TickTests
    {
        private static readonly EconomyEngine E = TestEconomy.Engine;

        private sealed class CountingRandom : IRandom
        {
            public int IntCalls;
            public int FloatCalls;
            public float NextFloat() { FloatCalls++; return 0f; }
            public int NextInt(int minInclusive, int maxExclusive) { IntCalls++; return minInclusive; }
        }

        private sealed class ExtremeRandom : IRandom
        {
            private readonly bool _max;
            public ExtremeRandom(bool max) { _max = max; }
            public float NextFloat() => 0f;
            public int NextInt(int minInclusive, int maxExclusive) => _max ? maxExclusive - 1 : minInclusive;
        }

        [Fact]
        public void TickAdvancesAndLeavesInputUntouched()
        {
            var s = E.Initial();
            var before = EconomySnapshot.Canonical(s);
            var next = E.Tick(s, new TickInput(EconomyRng.ForTick(42, 0), 120));
            Assert.Equal(1, next.Tick);
            Assert.Equal(0, s.Tick);
            Assert.Equal(before, EconomySnapshot.Canonical(s));
        }

        [Fact]
        public void ZeroJitterDemandFollowsHourWeightsAndCarriesTheRemainder()
        {
            var e = TestEconomy.ZeroJitterEngine();
            var s = TestEconomy.Advance(e, e.Initial(), 6);
            // meal hour 6: weight 1 of W=41: total = 120*150*1 = 18000; 18000 / 4100 = 4 rem 18000 - 16400 = 1600
            Assert.Equal(4, TestEconomy.Mkt(s, "meal").DemandThisTick);
            Assert.Equal(4, TestEconomy.Mkt(s, "meal").OpenDemand);
            Assert.Equal(1600, TestEconomy.Mkt(s, "meal").DemandCarry);
            Assert.Equal(0, TestEconomy.Mkt(s, "snack").DemandThisTick); // hours 8..21 only
            s = TestEconomy.Advance(e, s, 1);
            // hour 7: meal weight 3: total = 1600 + 54000 = 55600; / 4100 = 13 rem 55600 - 53300 = 2300
            Assert.Equal(13, TestEconomy.Mkt(s, "meal").DemandThisTick);
            Assert.Equal(2300, TestEconomy.Mkt(s, "meal").DemandCarry);
            // drink hour 7: weight 1 of W=16: total = 120*200 = 24000; / 1600 = 15 rem 0
            Assert.Equal(15, TestEconomy.Mkt(s, "drink").DemandThisTick);
            Assert.Equal(0, TestEconomy.Mkt(s, "drink").DemandCarry);
            // the closed hour 6 is reported
            Assert.Equal(4, TestEconomy.Mkt(s, "meal").Last.Demand);
        }

        [Fact]
        public void ZeroJitterDayMatchesPerNpcNeedsExactly()
        {
            var e = TestEconomy.ZeroJitterEngine();
            var s = e.Initial();
            int meal = 0, snack = 0, drink = 0;
            for (int i = 0; i < 24; i++)
            {
                s = TestEconomy.Advance(e, s, 1);
                meal += TestEconomy.Mkt(s, "meal").DemandThisTick;
                snack += TestEconomy.Mkt(s, "snack").DemandThisTick;
                drink += TestEconomy.Mkt(s, "drink").DemandThisTick;
            }
            Assert.Equal(180, meal);  // 120 NPCs * 1.50
            Assert.Equal(96, snack);  // 120 * 0.80
            Assert.Equal(240, drink); // 120 * 2.00
            Assert.Equal(0, TestEconomy.Mkt(s, "meal").DemandCarry);
        }

        [Fact]
        public void TownWorksWithoutPlayers() // AC3
        {
            var s = E.Initial();
            int meal = 0;
            long expectedRevenue = 0;
            for (int i = 1; i <= 240; i++)
            {
                var prev = s;
                s = TestEconomy.Advance(E, s, 1);
                if (i <= 24) meal += TestEconomy.Mkt(s, "meal").DemandThisTick;
                foreach (var m in s.Markets)
                {
                    TestEconomy.Catalog.TryGetMarket(m.MarketId, out var def);
                    Assert.Equal(def.Ceiling, m.Price);
                    if (def.Kind != MarketKind.Item) continue;
                    Assert.Equal(m.Last.Demand, m.Last.NpcFilled);
                    Assert.Equal(0, m.Last.PlayerFilled);
                    expectedRevenue += (long)m.Last.NpcFilled * def.Ceiling;
                }
            }
            Assert.InRange(meal, 162, 198); // 180 +- 10 %
            Assert.Equal(expectedRevenue, s.Totals.NpcBusinessRevenue);
            Assert.True(s.Totals.NpcBusinessRevenue > 0);
            Assert.Equal(0, s.Totals.PaidToPlayers);
        }

        [Fact]
        public void ClosedHourReportsPlayerSalesAndNpcRemainder()
        {
            var s = TestEconomy.Open(E, TestEconomy.Advance(E, E.Initial(), 12), "alice", 0);
            int d = TestEconomy.Mkt(s, "snack").DemandThisTick;
            long openRevenue = s.Markets.Where(m => m.MarketId != "food_scraps" && m.MarketId != "used_cooking_oil")
                .Sum(m => m.OpenDemand * (m.MarketId == "meal" ? 1200L : m.MarketId == "snack" ? 400L : 250L));
            var sold = E.TrySellItem(s, "alice", 1, TestEconomy.PlatedFries()).State;
            long revenueBefore = sold.Totals.NpcBusinessRevenue;
            var next = TestEconomy.Advance(E, sold, 1);
            var last = TestEconomy.Mkt(next, "snack").Last;
            Assert.Equal(new MarketReport(d, 1, 0, d - 1, 478), last);
            // the player's snack took one unit away from the NPC businesses
            Assert.Equal(revenueBefore + openRevenue - 400, next.Totals.NpcBusinessRevenue);
            Assert.Equal(0, TestEconomy.Mkt(next, "snack").PlayerFilledThisTick);
        }

        [Fact]
        public void PriceRecoversTowardCeilingWhenNobodySupplies()
        {
            var s = TestEconomy.Advance(E, E.Initial(), 12);
            Assert.True(TestEconomy.Mkt(s, "snack").DemandThisTick > 0);
            s = TestEconomy.SetMarket(s, "snack", m => m with { Price = 100 });
            s = TestEconomy.Advance(E, s, 1);
            // D > 0, S = 0: target 400; step = (400 - 100) * 25 / 100 = 75
            Assert.Equal(175, TestEconomy.Mkt(s, "snack").Price);
        }

        [Fact]
        public void FloodingDrivesPriceDownToFloorAndNoLower() // AC5
        {
            var s = TestEconomy.Open(E, E.Initial(), "alice", 0);
            long id = 1;
            long prev = TestEconomy.Mkt(s, "snack").Price;
            bool sawWholesaler = false, reachedFloor = false;
            for (int t = 0; t < 48; t++)
            {
                s = TestEconomy.Advance(E, s, 1);
                int sales = Math.Max(1, TestEconomy.Mkt(s, "snack").OpenDemand * 3);
                int open = TestEconomy.Mkt(s, "snack").OpenDemand;
                for (int i = 0; i < sales; i++)
                {
                    var r = E.TrySellItem(s, "alice", id++, TestEconomy.PlatedFries());
                    Assert.True(r.Ok, r.Error.ToString());
                    if (i >= open) { Assert.Equal(SaleChannel.Wholesaler, r.Channel); sawWholesaler = true; }
                    s = r.State;
                }
                long price = TestEconomy.Mkt(s, "snack").Price;
                Assert.True(price <= prev, $"price rose at tick {t}");
                Assert.True(price >= 100);
                if (price == 100) reachedFloor = true;
                prev = price;
            }
            Assert.True(sawWholesaler);
            Assert.True(reachedFloor);
            Assert.Equal(100, TestEconomy.Mkt(s, "snack").Price);
        }

        [Fact]
        public void ExactlyOneDrawPerMarketWithDemand()
        {
            var rng = new CountingRandom();
            E.Tick(E.Initial(), new TickInput(rng, 120));
            Assert.Equal(3, rng.IntCalls); // meal, snack, drink; commodities have no profile
            Assert.Equal(0, rng.FloatCalls);
            rng = new CountingRandom();
            E.Tick(E.Initial(), new TickInput(rng, 0));
            Assert.Equal(3, rng.IntCalls); // still one draw each at population 0
        }

        [Fact]
        public void JitterIsBoundedByThePercentage()
        {
            // snack hour 8 at population 100000: total = 100000*80*1 = 8_000_000; / 1400 = 5714 rem 400; j = 5714*10/100 = 571
            var low = TestEconomy.Advance(E, E.Initial(), 0);
            for (int i = 0; i < 8; i++) low = E.Tick(low, new TickInput(new ExtremeRandom(false), 100_000));
            var high = E.Initial();
            for (int i = 0; i < 8; i++) high = E.Tick(high, new TickInput(new ExtremeRandom(true), 100_000));
            Assert.Equal(5714 - 571, TestEconomy.Mkt(low, "snack").DemandThisTick);
            Assert.Equal(5714 + 571, TestEconomy.Mkt(high, "snack").DemandThisTick);
            Assert.Equal(400, TestEconomy.Mkt(low, "snack").DemandCarry);
        }

        [Fact]
        public void DemandIsCappedAtMaxDemandPerTick()
        {
            var markets = TestEconomy.WithMarket("snack", m => m with
            {
                Demand = new DemandProfile(100_000, Enumerable.Repeat(1, 24).ToList(), 0),
            });
            var e = new EconomyEngine(TestEconomy.Build(markets: markets));
            // total = 100000 * 100000 * 1 = 1e10; / (100 * 24) = 4_166_666 -> capped
            var s = e.Tick(e.Initial(), new TickInput(EconomyRng.ForTick(1, 0), EconomyLimits.MaxPopulation));
            Assert.Equal(EconomyLimits.MaxDemandPerTick, TestEconomy.Mkt(s, "snack").DemandThisTick);
        }

        [Fact]
        public void PopulationZeroMeansNoDemand()
        {
            var s = TestEconomy.Advance(E, E.Initial(), 30, pop: 0);
            Assert.All(s.Markets, m => Assert.Equal(0, m.OpenDemand));
            Assert.Equal(0, s.Totals.NpcBusinessRevenue);
        }

        [Fact]
        public void ServerErrorsThrow()
        {
            var s = E.Initial();
            var rng = EconomyRng.ForTick(1, 0);
            Assert.Throws<ArgumentNullException>(() => E.Tick(null!, new TickInput(rng, 1)));
            Assert.Throws<ArgumentNullException>(() => E.Tick(s, null!));
            Assert.Throws<ArgumentNullException>(() => E.Tick(s, new TickInput(null!, 1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => E.Tick(s, new TickInput(rng, -1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => E.Tick(s, new TickInput(rng, EconomyLimits.MaxPopulation + 1)));
            E.Tick(s, new TickInput(rng, EconomyLimits.MaxPopulation));
            var broken = s with { Markets = s.Markets.Take(2).ToList().AsReadOnly() };
            Assert.Throws<InvalidOperationException>(() => E.Tick(broken, new TickInput(rng, 1)));
        }

        [Fact]
        public void SnapshotAfterManyTicksStaysConsistent()
        {
            var s = TestEconomy.Advance(E, E.Initial(), 500);
            Assert.Equal(500, s.Tick);
            Assert.All(s.Markets, m => Assert.InRange(m.Price, 2L, 1200L));
        }
    }
}
