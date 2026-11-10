namespace Worktown.Sim.Cooking
{
    /// <summary>Ideal doneness range, inclusive on both ends.</summary>
    public sealed record DonenessWindow(float Min, float Max)
    {
        /// <summary>Spec 043: where the window's Perfect half starts (its middle; Good before it, Perfect from it to <see cref="Max"/>).</summary>
        public float PerfectFrom => Min + (Max - Min) * 0.5f;
    }
}
