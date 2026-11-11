using System;
using System.Collections.Generic;
using Worktown.Sim.Climb;

namespace Worktown.Sim.Economy
{
    /// <summary>
    /// Spec 037, pure: the menu board's prices and how guests react to them. The cook sets each dish's price in steps of
    /// <see cref="StepCents"/> from its reference price (the market reference, which is also the start price). Guests compare the
    /// price with the reference through spec 052's guest choice (<see cref="GuestChoice.Attraction"/>, the same weights as the climb):
    /// a shop at an average quality and reputation, no wait, one dish, against the outside option. Its share of the guests
    /// A / (A + Outside), relative to the share at the reference, scales the NPC demand of the dish's market
    /// (<see cref="DemandPermille"/>, into <see cref="TickInput.DemandPermille"/>): too expensive means fewer guests, cheap more.
    /// Above the reference the tip falls with the attraction too (<see cref="TipPermille"/>); at or below it the tip is unchanged.
    /// Integers only, no draws: the same prices give the same demand on every machine. Money is integer cents (D39).
    /// </summary>
    public static class MenuPricing
    {
        /// <summary>One step on the board: $0.50.</summary>
        public const long StepCents = 50;
        /// <summary>The board moves at most this many steps from the reference either way ($4.00).</summary>
        public const int MaxSteps = 8;
        /// <summary>The lowest price on the board ($0.50).</summary>
        public const long MinPrice = 50;
        /// <summary>The most a market's demand may grow (a very cheap menu draws at most this permille of its usual guests).</summary>
        public const int MaxDemandPermille = 3000;

        /// <summary>The lowest price the board allows for <paramref name="reference"/>.</summary>
        public static long LowestPrice(long reference) => Math.Max(MinPrice, reference - MaxSteps * StepCents);

        /// <summary>The highest price the board allows for <paramref name="reference"/>.</summary>
        public static long HighestPrice(long reference) => Math.Max(MinPrice, reference + MaxSteps * StepCents);

        /// <summary>
        /// The board's price after <paramref name="steps"/> presses (+ dearer, - cheaper) from <paramref name="price"/>, kept between
        /// <see cref="LowestPrice"/> and <see cref="HighestPrice"/>. A price off the reference's step grid (an old save) snaps to it.
        /// </summary>
        public static long Step(long reference, long price, int steps)
        {
            long low = LowestPrice(reference), high = HighestPrice(reference);
            long snapped = Clamp(price, low, high);
            long offset = GuestChoice.FloorDiv(snapped - reference, StepCents);
            long next = reference + (offset + Math.Clamp(steps, -2 * MaxSteps, 2 * MaxSteps)) * StepCents;
            return Clamp(next, low, high);
        }

        /// <summary>A price as the board keeps it: on the reference's step grid, within its bounds (a load or a bad value).</summary>
        public static long Normalize(long reference, long price) => Step(reference, price, 0);

        /// <summary>
        /// Spec 052's attraction of a shop at <paramref name="price"/> against <paramref name="reference"/>: quality and reputation at
        /// 500 (their terms are 0), no wait, one dish; only the price term counts. 1000 at the reference.
        /// </summary>
        public static int Attraction(ChoiceWeights weights, long price, long reference)
        {
            if (weights == null) throw new ArgumentNullException(nameof(weights));
            return GuestChoice.Attraction(weights, 500, 500, price - reference, Math.Max(1L, reference), 0, 1);
        }

        /// <summary>
        /// The market's demand at <paramref name="price"/> relative to the reference (permille, 1000 at the reference): the shop's share
        /// A / (A + Outside) over the share at the reference, at most <see cref="MaxDemandPermille"/>.
        /// </summary>
        public static int DemandPermille(ChoiceWeights weights, long price, long reference)
        {
            long at = Share(weights, Attraction(weights, price, reference));
            long atReference = Share(weights, Attraction(weights, reference, reference));
            if (atReference <= 0) return 1000;
            return (int)Math.Min(MaxDemandPermille, at * 1000 / atReference);
        }

        /// <summary>The tip's share (permille): 1000 at or below the reference, above it the attraction over the reference's.</summary>
        public static int TipPermille(ChoiceWeights weights, long price, long reference)
        {
            if (price <= reference) return 1000;
            int atReference = Attraction(weights, reference, reference);
            if (atReference <= 0) return 1000;
            return (int)Math.Min(1000L, (long)Attraction(weights, price, reference) * 1000 / atReference);
        }

        /// <summary>
        /// One market's demand permille for a menu: the mean (rounded down) of <see cref="DemandPermille"/> over its lines (price and
        /// reference); 1000 for none.
        /// </summary>
        public static int MarketDemandPermille(ChoiceWeights weights, IEnumerable<(long Price, long Reference)> lines)
        {
            if (lines == null) throw new ArgumentNullException(nameof(lines));
            long sum = 0;
            int n = 0;
            foreach ((long price, long reference) in lines)
            {
                sum += DemandPermille(weights, price, reference);
                n++;
            }
            return n == 0 ? 1000 : (int)(sum / n);
        }

        /// <summary>The share of guests (parts per million) a shop of attraction <paramref name="a"/> gets against the outside option.</summary>
        private static long Share(ChoiceWeights weights, int a)
        {
            long total = (long)Math.Max(0, a) + Math.Max(0, weights.Outside);
            return total <= 0 ? 0 : Math.Max(0, a) * 1_000_000L / total;
        }

        private static long Clamp(long value, long low, long high) => value < low ? low : value > high ? high : value;
    }
}
