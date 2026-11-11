namespace Worktown.Sim.Town
{
    /// <summary>Spec 064: a spot the server puts a player back to (outside the play area or below the fall height).</summary>
    public sealed record SafeSpot(int Id, int NodeId);
}
