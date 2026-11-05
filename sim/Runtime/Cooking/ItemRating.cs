namespace Worktown.Sim.Cooking
{
    /// <summary>Quality = Taste * (0.2 + 0.8 * DonenessFactor), all 0..1.</summary>
    public sealed record ItemRating(float Quality, float Taste, float DonenessFactor);
}
