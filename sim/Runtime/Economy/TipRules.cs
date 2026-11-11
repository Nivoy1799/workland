using System;

namespace Worktown.Sim.Economy
{
    /// <summary>
    /// Spec 032: the guest's tip, speed × accuracy × quality, at most <see cref="MaxTipPercent"/> % of the price. Cents (spec 037, D39),
    /// integer math only (permille factors, rounded down), so it is the same on every machine.
    /// <para>Speed: full while the order came within half the patience, then falls linearly to 0 at the end of it. Accuracy: 1 for
    /// the order as asked, 0.5 for one step short (a missing or wrong layer, a modifier not kept), 0 for more. Quality: from the
    /// order's stars, 5 stars 1, 1 star 0. The guest's mood (a wrong change, a slow wait) shows in the review, not here.</para>
    /// </summary>
    public static class TipRules
    {
        public const int MaxTipPercent = 30;
        /// <summary>The speed factor starts to fall past this share of the patience (permille).</summary>
        public const int FullSpeedUntilPermille = 500;

        /// <summary>1000 up to half the patience, then down to 0 at the full patience. <paramref name="patienceTenths"/> ≤ 0 gives 0.</summary>
        public static int SpeedPermille(int waitedTenths, int patienceTenths)
        {
            if (patienceTenths <= 0) return 0;
            if (waitedTenths <= 0) return 1000;
            long used = Math.Min(1000L, waitedTenths * 1000L / patienceTenths);
            if (used <= FullSpeedUntilPermille) return 1000;
            return (int)((1000L - used) * 1000L / (1000L - FullSpeedUntilPermille));
        }

        /// <summary>1000 for no miss, 500 for one, 0 for two or more.</summary>
        public static int AccuracyPermille(int misses) => misses <= 0 ? 1000 : misses == 1 ? 500 : 0;

        /// <summary>From the stars (1-5): 0, 250, 500, 750, 1000.</summary>
        public static int QualityPermille(int stars) => Math.Max(0, Math.Min(4, stars - 1)) * 250;

        /// <summary>The tip in cents, rounded down (the register rounds what a guest pays to whole coins, CashRules.ToCoins); 0 for a price of 0 or less.</summary>
        public static long Tip(long price, int speedPermille, int accuracyPermille, int qualityPermille)
        {
            if (price <= 0) return 0;
            long t = price * MaxTipPercent;
            t = t * Clamp(speedPermille) / 1000;
            t = t * Clamp(accuracyPermille) / 1000;
            t = t * Clamp(qualityPermille) / 1000;
            return t / 100;
        }

        /// <summary>The tip from the raw inputs: the wait and the patience (tenths of a second), the misses, the stars.</summary>
        public static long Tip(long price, int waitedTenths, int patienceTenths, int misses, int stars) =>
            Tip(price, SpeedPermille(waitedTenths, patienceTenths), AccuracyPermille(misses), QualityPermille(stars));

        private static long Clamp(int permille) => Math.Max(0, Math.Min(1000, permille));
    }
}
