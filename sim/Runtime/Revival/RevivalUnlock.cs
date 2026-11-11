namespace Worktown.Sim.Revival
{
    /// <summary>Spec 065: something that opens once <paramref name="DistrictId"/> reaches <paramref name="MinLevel"/> (a supplier, a place).</summary>
    public sealed record RevivalUnlock(string Id, string DistrictId, RevivalLevel MinLevel);
}
