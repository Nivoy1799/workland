namespace Worktown.Sim.Revival
{
    /// <summary>
    /// Spec 065 (D40): the revival goal factor of a crew of up to <paramref name="MaxPlayers"/> players (permille of the
    /// 4-player goal). The first row with players &lt;= MaxPlayers applies.
    /// </summary>
    public sealed record RevivalCrewRow(int MaxPlayers, int GoalPermille);
}
