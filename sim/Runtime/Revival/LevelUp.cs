namespace Worktown.Sim.Revival
{
    /// <summary>Spec 065: a district reached <paramref name="NewLevel"/>.</summary>
    public sealed record LevelUp(string DistrictId, RevivalLevel NewLevel);
}
