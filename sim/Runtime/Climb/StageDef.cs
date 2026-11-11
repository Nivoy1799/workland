namespace Worktown.Sim.Climb
{
    /// <summary>
    /// Spec 052: one stage of the climb. The gate fields say how you move to the next stage: buy
    /// <see cref="GatePurchaseId"/>, reach <see cref="GateStarsTenths"/> on average over the last <see cref="GateWindow"/>
    /// services, own <see cref="GateMinChannels"/> extra channels, pass the few-players check. Zero/null means no such condition.
    /// </summary>
    public sealed record StageDef(ClimbStage Stage, int WorkSpots, int OverflowPermille, int PaymentActions,
        int CleaningActions, long Upkeep, int MaxDishes, string? GatePurchaseId, int GateStarsTenths, int GateWindow,
        int GateMinChannels, bool GateFewPlayers, int ReputationFloorPermille);
}
