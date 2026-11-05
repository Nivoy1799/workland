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

        /// <summary>Sum of grocer prices of the item's components; null if any component is unpriced.</summary>
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
