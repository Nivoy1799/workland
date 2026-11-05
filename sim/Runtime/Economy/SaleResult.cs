using System.Collections.Generic;

namespace Worktown.Sim.Economy
{
    public sealed record SaleResult(EconomyState State, EconomyError Error, long Payout, string? MarketId,
        SaleChannel Channel, IReadOnlyList<GoodAmount> Byproducts)
    {
        public bool Ok => Error == EconomyError.None;
    }
}
