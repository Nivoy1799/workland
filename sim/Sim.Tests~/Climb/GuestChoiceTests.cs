using System;
using System.Linq;
using Worktown.Sim.Climb;
using Worktown.Sim.Core;
using Xunit;
using T = Worktown.Sim.Tests.Climb.TestClimb;

namespace Worktown.Sim.Tests.Climb
{
    public class GuestChoiceTests
    {
        private static readonly ChoiceWeights W = ClimbCatalog.CreateDefault().Weights;

        private static int A(int q = 600, int rep = 600, long offset = 0, int wait = 80, int dishes = 1) =>
            GuestChoice.Attraction(W, q, rep, offset, 8, wait, dishes);

        [Fact]
        public void TwoEqualShopsSplitEvenly() // AC3
        {
            var rng = new SeededRandom(52);
            int a = A(), first = 0;
            for (int g = 0; g < 10000; g++) if (GuestChoice.Pick(new[] { a, a }, rng) == 0) first++;
            Assert.InRange(first, 4700, 5300);
        }

        [Fact]
        public void BetterQualityReputationPriceAndWaitEachRaiseAttraction() // AC3
        {
            int baseline = A();
            Assert.True(A(q: 700) > baseline);
            Assert.True(A(rep: 700) > baseline);
            Assert.True(A(offset: -1) > baseline);
            Assert.True(A(wait: 60) > baseline);
            Assert.True(A(offset: 2) < baseline);
            Assert.True(A(wait: 120) < baseline);
            Assert.True(GuestChoice.Attraction(W with { Variety = 80 }, 600, 600, 0, 8, 80, 4) > baseline);
        }

        [Fact]
        public void BetterShopGetsTheLargerShare() // AC3
        {
            var rng = new SeededRandom(7);
            int good = A(q: 800), plain = A(), goodCount = 0;
            for (int g = 0; g < 10000; g++) if (GuestChoice.Pick(new[] { good, plain }, rng) == 0) goodCount++;
            Assert.True(goodCount > 5300);
        }

        [Fact]
        public void AttractionNeverBelowMin() // AC3
        {
            Assert.Equal(W.Min, GuestChoice.Attraction(W, 0, 0, 100, 4, 3600, 1));
            Assert.Equal(T.Weights.Min, GuestChoice.Attraction(T.Weights, 0, 0, 1000, 1, 100000, 1));
            Assert.True(GuestChoice.Attraction(W, 1000, 1000, -3, 4, 0, 5) > 1000);
        }

        [Fact]
        public void CeilingAddsBonusPermille()
        {
            ChannelDef ch = T.Catalog.Channel("counter");
            Assert.Equal(20, GuestChoice.Ceiling(ch, 0));
            Assert.Equal(30, GuestChoice.Ceiling(ch, 500));
            Assert.Equal(20, GuestChoice.Ceiling(ch, -100));
        }

        [Fact]
        public void PickIsDeterministicAndRejectsEmptyTotals()
        {
            int[] a = { 10, 0, 30 };
            var r1 = new SeededRandom(3);
            var r2 = new SeededRandom(3);
            var picks1 = Enumerable.Range(0, 50).Select(_ => GuestChoice.Pick(a, r1)).ToList();
            var picks2 = Enumerable.Range(0, 50).Select(_ => GuestChoice.Pick(a, r2)).ToList();
            Assert.Equal(picks1, picks2);
            Assert.DoesNotContain(1, picks1);
            Assert.Throws<ArgumentException>(() => GuestChoice.Pick(new[] { 0, 0 }, r1));
        }

        [Fact]
        public void FloorDivRoundsDown()
        {
            Assert.Equal(2, GuestChoice.FloorDiv(7, 3));
            Assert.Equal(-3, GuestChoice.FloorDiv(-7, 3));
            Assert.Equal(-2, GuestChoice.FloorDiv(-6, 3));
            Assert.Equal(0, GuestChoice.FloorDiv(0, 5));
        }

        [Fact]
        public void GuestsPerChannelNeverExceedTheCeiling() // AC3
        {
            ClimbCatalog c = ClimbCatalog.CreateDefault();
            var engine = new ClimbEngine(c);
            ClimbRun run = engine.Run(new CrewSetup(16, "expert", new ClimbStrategy(PricePolicy.Minus1, SpendPolicy.Builder), 3), 20 * 3600);
            // Spec 052b: every copy a crew may own, and the crew demand factor of 16 players (D40).
            var allOwned = c.Purchases.SelectMany(p => Enumerable.Repeat(p.Id, Math.Max(1, p.MaxCount))).ToList();
            int demand = CrewScaling.For(c, 16).DemandPermille;
            foreach (ServiceResult r in run.Services)
            {
                if (r.BigJobId != null) continue;
                for (int i = 0; i < r.ChannelIds.Count; i++)
                {
                    int all = r.YourGuests[i] + Math.Max(0, r.RivalGuests[i]);
                    int ceiling = CrewScaling.ScaleCount(GuestChoice.Ceiling(c.Channel(r.ChannelIds[i]), c.CeilingBonusPermille(allOwned, r.ChannelIds[i])), demand);
                    Assert.True(all <= ceiling, $"service {r.Index} {r.ChannelIds[i]} {all} > {ceiling}");
                }
            }
        }
    }
}
