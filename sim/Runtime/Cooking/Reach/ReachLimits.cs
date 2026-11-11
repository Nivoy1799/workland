namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 056a: the limits of <see cref="ReachableItems.Enumerate(ItemFactory, System.Collections.Generic.IReadOnlyList{RouteDef}, ReachLimits)"/>.
    /// A Number param is swept at <see cref="SweepSteps"/> + 1 evenly spaced values from its Min to its Max (plus each bucket
    /// threshold); more than <see cref="MaxItems"/> items throws.
    /// </summary>
    public sealed record ReachLimits(int SweepSteps, int MaxItems)
    {
        public const int DefaultSweepSteps = 48;
        public const int DefaultMaxItems = 2500;

        public static ReachLimits Default { get; } = new ReachLimits(DefaultSweepSteps, DefaultMaxItems);
    }
}
