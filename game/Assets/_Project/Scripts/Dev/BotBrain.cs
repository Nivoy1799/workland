using System;
using System.Collections.Generic;
using UnityEngine;

namespace Worktown.Game.Dev
{
    /// <summary>
    /// Spec 045 (the spec 004 load test), pure: the seeded choices of one load-test bot. A bot walks between random waypoints, every
    /// <see cref="MinPickupWait"/> to <see cref="MaxPickupWait"/> s it tries to pick up the nearest free item, carries it
    /// <see cref="MinCarry"/> to <see cref="MaxCarry"/> s and drops it. Same seed, same choices (AGENTS rule 7: an injected seed).
    /// </summary>
    public sealed class BotBrain
    {
        public const float MinPickupWait = 5f;
        public const float MaxPickupWait = 15f;
        public const float MinCarry = 10f;
        public const float MaxCarry = 30f;
        /// <summary>m: items farther away than this are not worth a walk.</summary>
        public const float MaxPickupDistance = 12f;

        private readonly System.Random _random;

        public BotBrain(int seed)
        {
            _random = new System.Random(seed);
        }

        /// <summary>The next waypoint, an index into <paramref name="count"/> waypoints (-1 for none).</summary>
        public int NextWaypoint(int count) => count > 0 ? _random.Next(count) : -1;

        /// <summary>Seconds of walking before the next pick-up try.</summary>
        public float NextPickupWait() => Between(MinPickupWait, MaxPickupWait);

        /// <summary>Seconds to carry a picked-up item before dropping it.</summary>
        public float NextCarry() => Between(MinCarry, MaxCarry);

        private float Between(float min, float max) => min + (float)_random.NextDouble() * (max - min);

        /// <summary>
        /// The candidate nearest to <paramref name="from"/> (flat distance) within <paramref name="maxDistance"/>, that
        /// <paramref name="usable"/> allows (null: all); -1 when there is none.
        /// </summary>
        public static int Nearest(Vector3 from, IReadOnlyList<Vector3> candidates, float maxDistance, Func<int, bool> usable = null)
        {
            if (candidates == null) return -1;
            int best = -1;
            float bestSq = maxDistance * maxDistance;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (usable != null && !usable(i)) continue;
                Vector3 d = candidates[i] - from;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq > bestSq) continue;
                bestSq = sq;
                best = i;
            }
            return best;
        }
    }
}
