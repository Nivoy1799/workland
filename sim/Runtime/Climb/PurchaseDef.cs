using System;

namespace Worktown.Sim.Climb
{
    /// <summary>
    /// Spec 052: something to buy in a stage, its one-off cost, its upkeep (or wage) per service and its effect.
    /// Spec 052b: <see cref="MaxCount"/> copies may be owned (1 = buy once, 0 = no limit); copy k costs
    /// <see cref="CostOf"/>(k), each copy <see cref="CostGrowthPermille"/>/1000 of the one before (1000 = flat). Upkeep and
    /// effects count per owned copy.
    /// </summary>
    public sealed record PurchaseDef(string Id, string Name, ClimbStage Stage, PurchaseKind Kind, long Cost,
        long UpkeepPerService, PurchaseEffect Effect, int MaxCount = 1, int CostGrowthPermille = 1000)
    {
        /// <summary>
        /// The cost of the next copy when <paramref name="ownedCount"/> are owned: <c>cost_k = cost_(k-1) * growth / 1000</c>,
        /// floored each step to whole <paramref name="unit"/>s (the catalog's MoneyScale, so cents give the dollar result).
        /// </summary>
        public long CostOf(int ownedCount, long unit = 1)
        {
            if (ownedCount < 0) throw new ArgumentOutOfRangeException(nameof(ownedCount), "ownedCount must be >= 0");
            long u = Math.Max(1L, unit);
            long cost = Cost;
            for (int k = 0; k < ownedCount; k++)
            {
                long next = cost * CostGrowthPermille / (1000 * u) * u;
                cost = next;
                if (cost > long.MaxValue / 8) return long.MaxValue / 8;
            }
            return cost;
        }

        /// <summary>True when one more copy may be bought after <paramref name="ownedCount"/>.</summary>
        public bool CanBuyMore(int ownedCount) => MaxCount == 0 || ownedCount < MaxCount;
    }
}
