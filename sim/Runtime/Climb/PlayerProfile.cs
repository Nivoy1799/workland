namespace Worktown.Sim.Climb
{
    /// <summary>Spec 052: how fast (actions per minute) and how well (skill, permille) a simulated player works.</summary>
    public sealed record PlayerProfile(string Id, int ActionsPerMinute, int SkillPermille);
}
