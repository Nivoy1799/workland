namespace Worktown.Sim.Economy
{
    public sealed record MarketDef(string Id, MarketKind Kind, long Floor, long Ceiling,
        int NpcQualityPermille, int MinQualityPermille, DemandProfile? Demand, int SmoothingPercent);
}
