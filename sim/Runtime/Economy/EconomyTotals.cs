namespace Worktown.Sim.Economy
{
    /// <summary>Money flow counters, saturating at long.MaxValue.</summary>
    public sealed record EconomyTotals(long PaidToPlayers, long DebitedFromPlayers, long NpcBusinessRevenue, long GrocerRevenue);
}
