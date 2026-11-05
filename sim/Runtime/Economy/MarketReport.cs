namespace Worktown.Sim.Economy
{
    /// <summary>Closed hour of a market.</summary>
    public sealed record MarketReport(int Demand, int PlayerFilled, int Wholesale, int NpcFilled, long DemandPaid);
}
