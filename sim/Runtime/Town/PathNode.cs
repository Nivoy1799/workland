namespace Worktown.Sim.Town
{
    /// <summary>Spec 064: a node of the walking graph: plan position, height above the lake (mm) and its district.</summary>
    public sealed record PathNode(int Id, TownPoint At, int HeightMm, string DistrictId);
}
