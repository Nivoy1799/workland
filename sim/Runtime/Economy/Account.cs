using System.Collections.Generic;

namespace Worktown.Sim.Economy
{
    /// <summary>
    /// Goods are sorted by GoodId (ordinal), Units &gt; 0. 034 review: <see cref="GrocerPaid"/> is what this account has paid the
    /// grocer and supplier so far, less what was refunded: a refund is never more (EconomyEngine.Refund).
    /// </summary>
    public sealed record Account(string Id, long Balance, IReadOnlyList<GoodAmount> Goods)
    {
        public long GrocerPaid { get; init; }
    }
}
