namespace Worktown.Sim.Climb
{
    /// <summary>
    /// Spec 052b, D40: every crew-size factor of one service, from <see cref="CrewScaling.For"/>. Permille of the 4-player
    /// reference crew: <see cref="DemandPermille"/> scales guests, work spots, staff, events and running costs (n/4);
    /// <see cref="GoalPermille"/> = n x h(n) / 4 scales goals and one-time costs (progress per service = net / this);
    /// <see cref="PatienceSeconds"/> is added to the guests' wait thresholds; <see cref="StockPerService"/> caps the stage 1
    /// dishes per service (0 = no cap).
    /// </summary>
    public sealed record CrewFactors(int Players, int DifficultyPermille, int DemandPermille, int GoalPermille, int PatienceSeconds,
        int StockPerService);
}
