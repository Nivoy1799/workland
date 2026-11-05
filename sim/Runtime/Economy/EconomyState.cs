using System.Collections.Generic;

namespace Worktown.Sim.Economy
{
    /// <summary>Immutable. Lists are sorted by key (ordinal). Compare states via EconomySnapshot.</summary>
    public sealed record EconomyState(long Tick, IReadOnlyList<MarketState> Markets,
        IReadOnlyList<Account> Accounts, IReadOnlyList<ConsumedInstance> RecentConsumed, EconomyTotals Totals);
}
