using System;
using Worktown.Sim.Cooking;

namespace Worktown.Sim.Economy
{
    /// <summary>Pure price and payout rules, whole dollars, integer math only.</summary>
    public static class PriceRules
    {
        /// <summary>Quality (0..1 float from the rater) to permille, truncated. NaN gives 0.</summary>
        public static int QualityPermille(float quality)
        {
            if (float.IsNaN(quality)) return 0;
            return (int)(Math.Clamp(quality, 0f, 1f) * 1000f);
        }

        /// <summary>max(1, price * (500 + q) / 1000), q in 0..1000.</summary>
        public static long DemandPayout(long price, int q)
        {
            q = Math.Clamp(q, 0, 1000);
            return Math.Max(1L, price * (500 + q) / 1000);
        }

        /// <summary>max(1, floor * (500 + q / 2) / 1000), q in 0..1000.</summary>
        public static long WholesalePayout(long floor, int q)
        {
            q = Math.Clamp(q, 0, 1000);
            return Math.Max(1L, floor * (500 + q / 2) / 1000);
        }

        public static long NextPrice(MarketDef m, long price, int demand, int playerUnits)
        {
            if (m == null) throw new ArgumentNullException(nameof(m));
            long target;
            if (demand > 0)
            {
                long twoD = 2L * demand;
                long s = Math.Min((long)playerUnits, twoD);
                target = m.Ceiling - (m.Ceiling - m.Floor) * s / twoD;
            }
            else
            {
                target = playerUnits > 0 ? m.Floor : price;
            }
            long diff = target - price;
            long step = diff * m.SmoothingPercent / 100; // truncates toward zero
            if (step == 0 && diff != 0) step = diff > 0 ? 1 : -1;
            long next = price + step;
            if (next < m.Floor) next = m.Floor;
            if (next > m.Ceiling) next = m.Ceiling;
            return next;
        }

        /// <summary>
        /// Sum of grocer prices of the item's components; null if any component is unpriced. Spec 031b: a squeezed portion of
        /// sauce counts at the least share any squeeze of its amount bucket was charged (<see cref="PortionPercentOf"/>), not at
        /// the bottle's price. This is a ceiling, not the exact amount paid: rounding can put it $1 above what several merged
        /// squeezes cost (three 10 % taps of a $14 bottle cost 3 x $1, a "lots" portion counts $4); the -1 in
        /// <see cref="SurplusCap"/> absorbs that, so the wholesaler's cap stays below what was spent. Items without a squeeze
        /// are unchanged.
        /// </summary>
        public static long? ItemCost(EconomyCatalog c, Item item)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            if (item == null) throw new ArgumentNullException(nameof(item));
            long sum = 0;
            foreach (var comp in item.Components)
            {
                if (!c.TryGetGrocerPrice(comp.BaseId, out var p)) return null;
                sum += p;
            }
            return sum - PortionDiscount(c, item);
        }

        // ---- Spec 031b: portion prices (a squeeze of sauce from a dispenser) ----

        /// <summary>A started half-second of a squeeze costs this share of the bottle's grocer price (%).</summary>
        public const int PortionStepPercent = 10;
        /// <summary>A squeeze costs at most this share of the bottle's grocer price (%).</summary>
        public const int PortionMaxPercent = 30;
        /// <summary>s per portion step.</summary>
        public const float PortionStepSeconds = 0.5f;

        /// <summary>
        /// The share (%) of the bottle's grocer price a squeeze of <paramref name="seconds"/> costs: 10 % per started 0.5 s, at
        /// least one step (a tap is a dash), at most 30 %. NaN or negative counts as a tap.
        /// </summary>
        public static int PortionPercent(float seconds)
        {
            if (float.IsNaN(seconds) || seconds < 0f) seconds = 0f;
            // A tiny tolerance, so a squeeze measured as exactly 1.0 s (30 ticks) is two steps, not three.
            int steps = (int)Math.Ceiling(seconds / PortionStepSeconds - 1e-4);
            if (steps < 1) steps = 1;
            return Math.Min(PortionMaxPercent, steps * PortionStepPercent);
        }

        /// <summary><paramref name="percent"/> % of <paramref name="price"/>, rounded half up, at least 1 for a priced item.</summary>
        public static long PortionCost(long price, int percent)
        {
            if (price <= 0) return 0;
            percent = Math.Clamp(percent, 0, 100);
            return Math.Max(1L, (price * percent + 50) / 100);
        }

        /// <summary>
        /// The least share (%) any squeeze that made the amount bucket <paramref name="amountBucket"/> was charged: a dash or the
        /// right amount at least one step, lots (1.5 s or more, one squeeze or several added up) the most.
        /// </summary>
        public static int PortionPercentOf(string? amountBucket) =>
            amountBucket == Assembly.Lots ? PortionPercent(Assembly.LotsFrom) : PortionStepPercent;

        private static long PortionDiscount(EconomyCatalog c, Item item)
        {
            if (item.Kind == OperationKind.Portion)
            {
                item.Buckets.TryGetValue(Assembly.AmountParam, out string? bucket);
                int percent = PortionPercentOf(bucket);
                long discount = 0;
                foreach (var comp in item.Components)
                    if (c.TryGetGrocerPrice(comp.BaseId, out var p)) discount += p - PortionCost(p, percent);
                return discount;
            }
            long sum = 0;
            foreach (var input in item.Inputs) sum += PortionDiscount(c, input);
            return sum;
        }

        /// <summary>ItemCost - 1 - Weight * B, may be negative; 0 if the item is unpriced.</summary>
        public static long SurplusCap(EconomyCatalog c, Item item)
        {
            var cost = ItemCost(c, item);
            if (cost == null) return 0;
            return cost.Value - 1 - item.Weight * c.ByproductMaxPerComponent;
        }

        /// <summary>min(WholesalePayout(Floor, q), SurplusCap). Below 1 means the wholesaler refuses.</summary>
        public static long ItemWholesalePayout(EconomyCatalog c, MarketDef m, Item item, int q)
        {
            if (m == null) throw new ArgumentNullException(nameof(m));
            return Math.Min(WholesalePayout(m.Floor, q), SurplusCap(c, item));
        }
    }
}
