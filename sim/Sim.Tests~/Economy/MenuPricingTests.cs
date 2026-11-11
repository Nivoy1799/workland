using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Climb;
using Worktown.Sim.Core;
using Worktown.Sim.Economy;
using Xunit;

namespace Worktown.Sim.Tests.Economy
{
    /// <summary>Spec 037 AC2: the menu board's demand curve (spec 052's guest choice), its steps, the tips, and the demand in the tick.</summary>
    public class MenuPricingTests
    {
        private static readonly ChoiceWeights W = ClimbCatalog.CreateDefault().Weights;

        [Fact]
        public void TheBoard_StepsInFiftyCents_WithinEightStepsOfTheReference()
        {
            Assert.Equal(50, MenuPricing.StepCents);
            Assert.Equal(450, MenuPricing.Step(400, 400, 1));
            Assert.Equal(350, MenuPricing.Step(400, 400, -1));
            Assert.Equal(800, MenuPricing.Step(400, 800, 1)); // +$4.00 is the most
            Assert.Equal(800, MenuPricing.HighestPrice(400));
            Assert.Equal(50, MenuPricing.Step(400, 50, -1)); // never below $0.50
            Assert.Equal(50, MenuPricing.LowestPrice(400));
            Assert.Equal(500, MenuPricing.LowestPrice(900));
            Assert.Equal(450, MenuPricing.Normalize(400, 470)); // an off-grid price snaps down to the step
            Assert.Equal(800, MenuPricing.Normalize(400, 99999));
            Assert.Equal(50, MenuPricing.Normalize(400, -3));
        }

        [Fact]
        public void TheAttraction_IsSpec052sGuestChoice_WithOnlyThePriceTerm()
        {
            Assert.Equal(1000, MenuPricing.Attraction(W, 400, 400));
            Assert.Equal(GuestChoice.Attraction(W, 500, 500, 50, 400, 0, 1), MenuPricing.Attraction(W, 450, 400));
            Assert.Equal(675, MenuPricing.Attraction(W, 450, 400)); // 1000 - 2600 x 125 / 1000
            Assert.Equal(1325, MenuPricing.Attraction(W, 350, 400));
            Assert.Equal(W.Min, MenuPricing.Attraction(W, 800, 400)); // doubled: the floor
        }

        [Fact]
        public void TheDemandCurve_PinsForFries() // pinned: 052's default weights (Price 2600, Outside 700), fries at $4.00
        {
            Assert.Equal(1000, MenuPricing.DemandPermille(W, 400, 400));
            Assert.Equal(834, MenuPricing.DemandPermille(W, 450, 400)); // 675/1375 of 1000/1700
            Assert.Equal(566, MenuPricing.DemandPermille(W, 500, 400));
            Assert.Equal(47, MenuPricing.DemandPermille(W, 800, 400));
            Assert.Equal(1112, MenuPricing.DemandPermille(W, 350, 400));
            Assert.Equal(675, MenuPricing.TipPermille(W, 450, 400));
            Assert.Equal(1000, MenuPricing.TipPermille(W, 400, 400));
            Assert.Equal(1000, MenuPricing.TipPermille(W, 300, 400)); // cheap: the tip is not raised
        }

        [Theory] // the board's references: fries, drinks, the wraps and the dearest dish
        [InlineData(300)]
        [InlineData(400)]
        [InlineData(450)]
        [InlineData(800)]
        [InlineData(900)]
        [InlineData(1000)]
        public void TooExpensiveMeansFewerGuests_CheapMeansMoreGuestsAndLessMoney(long reference)
        {
            int previous = int.MaxValue;
            for (long price = MenuPricing.LowestPrice(reference); price <= MenuPricing.HighestPrice(reference); price += MenuPricing.StepCents)
            {
                int demand = MenuPricing.DemandPermille(W, price, reference);
                Assert.True(demand <= previous, "a dearer price never draws more guests");
                Assert.InRange(demand, 0, MenuPricing.MaxDemandPermille);
                Assert.InRange(MenuPricing.TipPermille(W, price, reference), 0, 1000);
                previous = demand;
            }
            long atReference = reference * 1000;
            foreach (int steps in new[] { -2, -1, 1, 2 })
            {
                long price = MenuPricing.Step(reference, reference, steps);
                long revenue = price * MenuPricing.DemandPermille(W, price, reference);
                Assert.True(revenue < atReference, $"{steps} steps from {reference}: {revenue} is less than {atReference}");
            }
            Assert.True(MenuPricing.DemandPermille(W, reference - MenuPricing.StepCents, reference) > 1000, "cheap: more guests");
            Assert.True(MenuPricing.DemandPermille(W, reference + MenuPricing.StepCents, reference) < 1000, "dear: fewer guests");
            Assert.True(MenuPricing.TipPermille(W, reference + MenuPricing.StepCents, reference) < 1000, "dear: lower tips");
        }

        [Fact]
        public void AMarketsDemand_IsTheMeanOfItsLines()
        {
            Assert.Equal(1000, MenuPricing.MarketDemandPermille(W, new (long, long)[0]));
            Assert.Equal(1000, MenuPricing.MarketDemandPermille(W, new[] { (400L, 400L), (900L, 900L) }));
            Assert.Equal((834 + 1000) / 2, MenuPricing.MarketDemandPermille(W, new[] { (450L, 400L), (900L, 900L) }));
        }

        [Fact]
        public void TheTick_ScalesNewDemand_WithTheSameDraws_AndAtTheReferenceNothingChanges() // spec 037 determinism
        {
            EconomyEngine e = TestEconomy.Engine;
            EconomyState plain = e.Initial(), same = e.Initial(), half = e.Initial(), none = e.Initial();
            var full = new Dictionary<string, int> { ["snack"] = 1000 };
            var halved = new Dictionary<string, int> { ["snack"] = 500 };
            var zero = new Dictionary<string, int> { ["snack"] = 0 };
            int plainUnits = 0, halfUnits = 0, noneUnits = 0;
            for (int t = 0; t < 48; t++)
            {
                plain = e.Tick(plain, new TickInput(EconomyRng.ForTick(7, plain.Tick), 120));
                same = e.Tick(same, new TickInput(EconomyRng.ForTick(7, same.Tick), 120, full));
                half = e.Tick(half, new TickInput(EconomyRng.ForTick(7, half.Tick), 120, halved));
                none = e.Tick(none, new TickInput(EconomyRng.ForTick(7, none.Tick), 120, zero));
                plainUnits += TestEconomy.Mkt(plain, "snack").DemandThisTick;
                halfUnits += TestEconomy.Mkt(half, "snack").DemandThisTick;
                noneUnits += TestEconomy.Mkt(none, "snack").DemandThisTick;
                Assert.Equal(TestEconomy.Mkt(plain, "meal").DemandThisTick, TestEconomy.Mkt(half, "meal").DemandThisTick); // other markets
            }
            Assert.Equal(EconomySnapshot.Canonical(plain), EconomySnapshot.Canonical(same));
            Assert.Equal(0, noneUnits);
            Assert.InRange(halfUnits, plainUnits * 4 / 10, plainUnits * 6 / 10);
        }

        [Fact]
        public void TheTick_DrawsTheSameNumberOfTimes_AtAnyDemand()
        {
            var a = new CountingRandom();
            var b = new CountingRandom();
            EconomyEngine e = TestEconomy.Engine;
            e.Tick(e.Initial(), new TickInput(a, 120));
            e.Tick(e.Initial(), new TickInput(b, 120, new Dictionary<string, int> { ["snack"] = 3000, ["meal"] = 0 }));
            Assert.Equal(a.IntCalls, b.IntCalls);
        }

        private sealed class CountingRandom : IRandom
        {
            public int IntCalls;
            public float NextFloat() => 0f;
            public int NextInt(int minInclusive, int maxExclusive) { IntCalls++; return minInclusive; }
        }
    }
}
