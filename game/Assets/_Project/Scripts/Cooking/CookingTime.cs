using System;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 016: how long food has been on the heat, in sim seconds. Elapsed time comes from the
    /// FishNet tick counter, so host and dedicated server agree and frame rate does not matter. Pure.
    /// </summary>
    public static class CookingTime
    {
        /// <summary>Sim seconds per real second: golden fries (150 sim s) take 25 real seconds.</summary>
        public const int TimeScale = 6;

        /// <summary>Ticks between start and now; 0 if the counter went backwards.</summary>
        public static uint ElapsedTicks(uint startTick, uint nowTick)
        {
            return nowTick >= startTick ? nowTick - startTick : 0u;
        }

        public static double SimSeconds(uint elapsedTicks, double tickRate)
        {
            if (tickRate <= 0d) return 0d;
            return elapsedTicks / tickRate * TimeScale;
        }

        /// <summary>The op's "time" parameter, or false if it has none (not a heat op).</summary>
        public static bool TryGetTimeParam(OperationDef op, out ParamDef time)
        {
            time = null;
            if (op == null) return false;
            foreach (ParamDef p in op.Params)
            {
                if (p.Name != "time" || p.Kind != ParamKind.Number) continue;
                time = p;
                return true;
            }
            return false;
        }

        /// <summary>Below the op's minimum time nothing happens to the food.</summary>
        public static bool IsTooShort(ParamDef time, double simSeconds)
        {
            return simSeconds < time.Min;
        }

        /// <summary>Clamped to the op's maximum time, as a sim parameter value.</summary>
        public static float Clamp(ParamDef time, double simSeconds)
        {
            return (float)Math.Min(Math.Max(simSeconds, time.Min), time.Max);
        }

        /// <summary>
        /// The doneness bucket id players see while cooking ("short", "medium", "long"), picked like the
        /// sim picks buckets (the last one whose value is reached). Empty below the first bucket.
        /// </summary>
        public static string BucketId(ParamDef time, double simSeconds)
        {
            string chosen = string.Empty;
            foreach (BucketDef b in time.Buckets)
                if (b.Value <= simSeconds) chosen = b.Id;
            return chosen;
        }
    }
}
