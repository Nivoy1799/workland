using System;
using Worktown.Sim.Economy;

namespace Worktown.Game.Npc
{
    /// <summary>
    /// 037 review, pure: what a guest's order line pays at the menu board. The price is the one the board showed when the guest joined
    /// (stored on the guest, never the board's price at the hand-over). The bill is that price times the match's share (exact 100 %,
    /// close 60 %, wrong 25 %) times a quality factor from the served item's stars (D19: the rating decides whether the food is good;
    /// 5 stars pay the full price, 1 star half), so a bill never exceeds the board's price. A wrong dish is billed on what was served:
    /// at most its own sim payout. Whole coins (CashRules.ToCoins). Integers only.
    /// </summary>
    public static class BoardBill
    {
        /// <summary>The quality factor (permille) of an item of <paramref name="stars"/> (1-5): 500, 625, 750, 875, 1000.</summary>
        public static int QualityPermille(int stars) => 500 + 125 * (Math.Max(1, Math.Min(5, stars)) - 1);

        /// <summary>The line's share of its base (permille): the match's share times the quality factor, at most 1000.</summary>
        public static int Permille(OrderFit fit) =>
            (int)Math.Min(1000L, (long)OrderRules.GuestSharePermille(fit.Match) * QualityPermille(fit.Stars) / 1000);

        /// <summary>
        /// The base of the line's bill: the stored board price; for a wrong dish the served item's own value, the smaller of the stored
        /// price and its sim payout.
        /// </summary>
        public static long Base(long storedPrice, long simPayout, OrderMatch match)
        {
            long stored = Math.Max(0L, storedPrice);
            return match == OrderMatch.Wrong ? Math.Min(stored, Math.Max(0L, simPayout)) : stored;
        }

        /// <summary>The line's bill in whole coins: <see cref="Base"/> at <see cref="Permille"/>; never above the stored board price.</summary>
        public static long LineBill(OrderFit fit, long storedPrice, long simPayout) =>
            CashRules.ToCoins(Worktown.Game.Economy.EconomyRules.ApplyPermille(Base(storedPrice, simPayout, fit.Match), Permille(fit)));
    }
}
