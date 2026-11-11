namespace Worktown.Sim.Climb
{
    /// <summary>Spec 052: something to buy in a stage, its one-off cost, its upkeep (or wage) per service and its effect.</summary>
    public sealed record PurchaseDef(string Id, string Name, ClimbStage Stage, PurchaseKind Kind, long Cost,
        long UpkeepPerService, PurchaseEffect Effect);
}
