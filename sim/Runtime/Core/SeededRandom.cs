using System;

namespace Worktown.Sim.Core
{
    /// <summary>
    /// Deterministic RNG using SplitMix64. The algorithm is fixed here, so the same seed
    /// gives the same sequence on every platform and runtime (unlike System.Random).
    /// </summary>
    public sealed class SeededRandom : IRandom
    {
        private ulong _state;

        public SeededRandom(ulong seed)
        {
            _state = seed;
        }

        /// <summary>Next raw 64-bit value.</summary>
        public ulong NextUInt64()
        {
            unchecked
            {
                _state += 0x9E3779B97F4A7C15UL;
                ulong z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        public float NextFloat()
        {
            // Top 24 bits give an exact float in [0, 1).
            return (NextUInt64() >> 40) / 16777216f;
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive must be greater than minInclusive.");
            }
            // Modulo bias (<= ~2^-32 for int ranges) is intentional and acceptable here.
            ulong range = (ulong)((long)maxExclusive - minInclusive);
            return (int)((long)minInclusive + (long)(NextUInt64() % range));
        }
    }
}
