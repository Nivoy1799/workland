using Worktown.Sim.Core;

namespace Worktown.Sim.Economy
{
    public static class EconomyRng
    {
        /// <summary>Per-tick RNG derived from the world seed, so a restored state needs no RNG state.</summary>
        public static IRandom ForTick(ulong worldSeed, long tick)
        {
            unchecked
            {
                return new SeededRandom(new SeededRandom(worldSeed ^ (ulong)tick).NextUInt64());
            }
        }
    }
}
