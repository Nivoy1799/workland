namespace Worktown.Sim.Climb
{
    /// <summary>
    /// Spec 052: a "service" of D34 (counter, delivery, tables...): its demand ceiling, an action and price factor, the extra
    /// work spots it brings and its upkeep per service. A channel no purchase unlocks is a base counter: the highest one of
    /// the current stage is open.
    /// </summary>
    public sealed record ChannelDef(string Id, string Name, ClimbStage Stage, int GuestsPerService, int ActionsPermille,
        int PricePermille, int WorkSpots, long Upkeep);
}
