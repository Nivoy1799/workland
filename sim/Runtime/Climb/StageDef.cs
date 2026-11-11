namespace Worktown.Sim.Climb
{
    /// <summary>
    /// Spec 052: one stage of the climb. The gate fields say how you move to the next stage: buy
    /// <see cref="GatePurchaseId"/>, reach <see cref="GateStarsTenths"/> on average over the last <see cref="GateWindow"/>
    /// services, own <see cref="GateMinChannels"/> extra channels, and (spec 052b) own <see cref="GateMinStaff"/> staff and
    /// <see cref="GateMinMachines"/> stage 4 machines (copies count). Zero/null means no such condition.
    /// Spec 052b: <see cref="WorkPermille"/> scales every per-guest action cost of the stage (1000 = as written),
    /// <see cref="TipPermille"/> adds a tip on the service's revenue (floored once), <see cref="StockPerService"/> caps the stage 1
    /// dishes a 4-player crew may serve per service (the supplier's drop; <see cref="CrewScaling"/> scales it; 0 = no cap).
    /// </summary>
    public sealed record StageDef(ClimbStage Stage, int WorkSpots, int OverflowPermille, int PaymentActions,
        int CleaningActions, long Upkeep, int MaxDishes, string? GatePurchaseId, int GateStarsTenths, int GateWindow,
        int GateMinChannels, int GateMinStaff, int GateMinMachines, int ReputationFloorPermille, int WorkPermille = 1000,
        int TipPermille = 0, int StockPerService = 0);
}
