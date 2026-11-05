namespace Worktown.Sim.Economy
{
    public sealed record MarketState(string MarketId, long Price, int OpenDemand, long DemandCarry,
        int DemandThisTick, int PlayerFilledThisTick, int WholesaleThisTick, long DemandPaidThisTick, MarketReport Last);
}
