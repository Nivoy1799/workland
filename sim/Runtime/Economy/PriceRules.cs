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
        /// the bottle's price. 032 review: rounding can put a "lots" layer made of merged squeezes up to $1 above what they cost
        /// (three 10 % taps of a $14 bottle cost 3 x $1, a 30 % portion is $4), so each lots portion counts
        /// <see cref="LotsRoundingMargin"/> less; with any number of merged lots layers the cost is never above what was spent, and
        /// the wholesaler's cap (<see cref="SurplusCap"/>) never pays more than that. Items without a squeeze are unchanged.
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

        /// <summary>032 review: a lots portion counts this much less than a 30 % portion (merged squeezes round down by up to $1).</summary>
        public const long LotsRoundingMargin = 1;

        /// <summary>What a portion of a <paramref name="price"/> bottle in <paramref name="amountBucket"/> counts in <see cref="ItemCost"/>.</summary>
        public static long PortionCounted(long price, string? amountBucket)
        {
            long cost = PortionCost(price, PortionPercentOf(amountBucket));
            return amountBucket == Assembly.Lots ? Math.Max(0L, cost - LotsRoundingMargin) : cost;
        }

        // ---- Spec 033: a cup of a drink ----

        /// <summary>
        /// The share (%) of a drink's grocer price (one full cup, <see cref="Drinks.CupMl"/>) a pour of <paramref name="ml"/> costs:
        /// a started percent per 4 ml, at least 1 %, at most 100 % (a spill pours more than the cup, the cup is the most charged).
        /// NaN or negative counts as nothing poured (1 %).
        /// </summary>
        public static int FillPercent(float ml)
        {
            if (float.IsNaN(ml) || ml < 0f) ml = 0f;
            int percent = (int)Math.Ceiling(ml * 100f / Drinks.CupMl - 1e-4);
            return Math.Clamp(percent, 1, 100);
        }

        /// <summary>
        /// What one pour costs, charged once (spec 033): the cup's price when it was empty (<paramref name="withCup"/>; a top-up of the
        /// same drink pays no second cup) plus <paramref name="percent"/> % of the drink's price (<see cref="PortionCost"/>).
        /// </summary>
        public static long FillCost(long cupPrice, long drinkPrice, int percent, bool withCup) =>
            (withCup ? Math.Max(0L, cupPrice) : 0L) + PortionCost(drinkPrice, Math.Clamp(percent, 1, 100));

        /// <summary>The least share (%) any pour that made the fill bucket <paramref name="bucket"/> was charged (its lower bound).</summary>
        public static int FillPercentOf(string? bucket)
        {
            switch (bucket)
            {
                case Drinks.Right: return FillPercent(Drinks.RightFrom);
                case Drinks.Full: return FillPercent(Drinks.FullFrom);
                case Drinks.Spilled: return 100;
                default: return 1;
            }
        }

        private static long PortionDiscount(EconomyCatalog c, Item item)
        {
            if (item.Kind == OperationKind.Fill)
            {
                // Spec 033: a cup of a drink counts its drink at the least share its bucket was charged, $1 less (top-ups round each
                // pour; the cup's own price, always paid with the first pour, is not counted at all), never above what was spent.
                item.Buckets.TryGetValue(Drinks.MlParam, out string? level);
                long discount = 0;
                foreach (var comp in item.Components)
                    if (c.TryGetGrocerPrice(comp.BaseId, out var p))
                        discount += p - Math.Max(0L, PortionCost(p, FillPercentOf(level)) - LotsRoundingMargin);
                return discount;
            }
            if (item.Kind == OperationKind.Portion)
            {
                item.Buckets.TryGetValue(Assembly.AmountParam, out string? bucket);
                long discount = 0;
                foreach (var comp in item.Components)
                    if (c.TryGetGrocerPrice(comp.BaseId, out var p)) discount += p - PortionCounted(p, bucket);
                return discount;
            }
            long sum = 0;
            foreach (var input in item.Inputs) sum += PortionDiscount(c, input);
            return sum;
        }

        /// <summary>
        /// Spec 034: what the item's parts cost the shop when its stock came from the supplier: every component at
        /// <see cref="Supplier.BulkUnitPrice"/> (never above what a unit cost in any order); a squeezed portion at the least share of a
        /// bottle any squeeze of its amount bucket draws, a pour's drink at the least share of a cup its fill bucket draws, both rounded
        /// down (a draw takes exactly that share of a bought unit, so the count is never above it, merged squeezes and top-ups too); the
        /// cup itself is not counted (as in 033). Null if any component is unpriced. Always at most <see cref="ItemCost"/>.
        /// </summary>
        public static long? StockCost(EconomyCatalog c, Item item)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            if (item == null) throw new ArgumentNullException(nameof(item));
            long sum = 0;
            foreach (var comp in item.Components)
            {
                if (!c.TryGetGrocerPrice(comp.BaseId, out var p)) return null;
                sum += Supplier.BulkUnitPrice(c, p);
            }
            return sum - StockDiscount(c, item);
        }

        private static long StockDiscount(EconomyCatalog c, Item item)
        {
            if (item.Kind == OperationKind.Fill || item.Kind == OperationKind.Portion)
            {
                int percent;
                if (item.Kind == OperationKind.Fill)
                {
                    item.Buckets.TryGetValue(Drinks.MlParam, out string? level);
                    percent = FillPercentOf(level);
                }
                else
                {
                    item.Buckets.TryGetValue(Assembly.AmountParam, out string? bucket);
                    percent = PortionPercentOf(bucket);
                }
                long discount = 0;
                foreach (var comp in item.Components)
                    if (c.TryGetGrocerPrice(comp.BaseId, out var p))
                    {
                        long unit = Supplier.BulkUnitPrice(c, p);
                        discount += unit - unit * percent / 100;
                    }
                return discount;
            }
            long sum = 0;
            foreach (var input in item.Inputs) sum += StockDiscount(c, input);
            return sum;
        }

        /// <summary>
        /// StockCost - 1 - Weight * B, may be negative; 0 if the item is unpriced. Spec 034: the cost counts the supplier's bulk unit
        /// price (<see cref="StockCost"/>), so nothing sells to the wholesaler for more than its stock cost.
        /// </summary>
        public static long SurplusCap(EconomyCatalog c, Item item)
        {
            var cost = StockCost(c, item);
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
