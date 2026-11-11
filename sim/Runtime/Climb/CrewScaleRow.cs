namespace Worktown.Sim.Climb
{
    /// <summary>
    /// Spec 052b, D40: one row of the crew table (<see cref="ClimbCatalog.CrewTable"/>, sorted by <see cref="MaxPlayers"/>):
    /// the per-player difficulty h(n) (<see cref="DifficultyPermille"/>, 1000 = the 4-player reference) and extra guest patience
    /// for crews of up to <see cref="MaxPlayers"/>. The Fixed fields override the D40 rule (0 = use it): a fixed guest demand,
    /// goal factor or stage 1 stock, for calibration runs (C1) and <c>--crew-scaling off</c>. Read only through
    /// <see cref="CrewScaling.For"/>.
    /// </summary>
    public sealed record CrewScaleRow(int MaxPlayers, int DifficultyPermille, int PatienceSeconds = 0, int FixedDemandPermille = 0,
        int FixedGoalPermille = 0, int FixedStock = 0);
}
