namespace Worktown.Sim.Climb
{
    /// <summary>Spec 052: an NPC rival on one channel and how far it may react (price cuts, quality steps).</summary>
    public sealed record RivalDef(string Id, string Name, string ChannelId, int QualityPermille, int PriceOffset,
        int WaitSeconds, int ReputationPermille, int MaxPriceCuts, int MaxQualitySteps);
}
