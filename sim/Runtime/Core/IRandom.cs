namespace Worktown.Sim.Core
{
    /// <summary>Injected source of randomness. Sim code never creates its own RNG.</summary>
    public interface IRandom
    {
        /// <summary>Next float in [0, 1).</summary>
        float NextFloat();

        /// <summary>Next int in [minInclusive, maxExclusive).</summary>
        int NextInt(int minInclusive, int maxExclusive);
    }
}
