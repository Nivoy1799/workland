namespace Worktown.Sim.Revival
{
    /// <summary>
    /// Spec 065: one district's revival: its level and its progress towards the next level in ppm of that step's goal
    /// (0..1,000,000; always 1,000,000 at Thriving). Progress is a fraction of the goal, so a crew change is fair (D40).
    /// </summary>
    public sealed record DistrictRevival(string DistrictId, RevivalLevel Level, int ProgressPpm);
}
