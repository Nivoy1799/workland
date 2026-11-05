namespace Worktown.Sim.Cooking
{
    /// <summary>Ideal doneness range, inclusive on both ends.</summary>
    public sealed record DonenessWindow(float Min, float Max);
}
