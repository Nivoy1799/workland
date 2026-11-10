using System;
using System.Collections.Generic;

namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 043, pure: the doneness zones of the pan, pot, fryer, griddle and spit, so that the cue players see is the truth.
    /// <list type="bullet">
    /// <item>The rating's doneness term (<see cref="Factor"/>) is best (1) in the Perfect half of the window, a little less
    /// (<see cref="GoodFactor"/>) in the Good half, and falls off on both sides of the window over <see cref="FalloffWidth"/>;
    /// untouched raw food with a window from 0 stays at 1.</item>
    /// <item>A heat op's time bucket follows the zone of what it cooked (<see cref="BucketIndex"/>): undercooked is the first bucket
    /// ("pale", "lightly", half-grilled), Good and Perfect the second (golden, grilled, the plain cooked name), Overdone the third.</item>
    /// </list>
    /// The kitchen's rings read the same zones (HeatZoneRules), with the same <see cref="Epsilon"/>.
    /// </summary>
    public static class DonenessZones
    {
        /// <summary>The doneness term in the Good half of the window (the Perfect half is 1). Default to review.</summary>
        public const float GoodFactor = 0.9f;
        /// <summary>Outside the window the doneness term falls to 0 over this much doneness (as before 043).</summary>
        public const float FalloffWidth = 0.2f;
        /// <summary>Doneness tolerance at the zone boundaries (the kitchen's bisection lands within it).</summary>
        public const float Epsilon = 1e-4f;

        /// <summary>The zone of one doneness against <paramref name="window"/>.</summary>
        public static DonenessZone Of(float doneness, DonenessWindow window)
        {
            if (window == null) throw new ArgumentNullException(nameof(window));
            if (doneness > window.Max + Epsilon) return DonenessZone.Overdone;
            if (doneness < window.Min - Epsilon) return DonenessZone.Undercooked;
            return doneness < window.PerfectFrom - Epsilon ? DonenessZone.Good : DonenessZone.Perfect;
        }

        /// <summary>
        /// The zone of food made of <paramref name="parts"/>: Overdone when any part is past its window, else Undercooked when any part
        /// is below it, else Perfect when every part is in its Perfect half, else Good (the kitchen's ring order: the dish is ready when
        /// its slowest part is, and spoilt when its fastest part is). No parts: Undercooked.
        /// </summary>
        public static DonenessZone Of(IReadOnlyList<ItemComponent> parts)
        {
            if (parts == null || parts.Count == 0) return DonenessZone.Undercooked;
            bool under = false, good = false;
            foreach (ItemComponent c in parts)
            {
                DonenessZone z = Of(c.Doneness, c.Ideal);
                if (z == DonenessZone.Overdone) return DonenessZone.Overdone;
                if (z == DonenessZone.Undercooked) under = true;
                else if (z == DonenessZone.Good) good = true;
            }
            if (under) return DonenessZone.Undercooked;
            return good ? DonenessZone.Good : DonenessZone.Perfect;
        }

        /// <summary>
        /// The rating's doneness term (0..1) of one doneness: 1 in the Perfect half, <see cref="GoodFactor"/> in the Good half, and
        /// outside the window that term times clamp01(1 - distance / <see cref="FalloffWidth"/>) (below: from Good; past: from Perfect).
        /// </summary>
        public static float Factor(float doneness, DonenessWindow window)
        {
            if (window == null) throw new ArgumentNullException(nameof(window));
            // Raw food that may be eaten raw (a window from 0: lettuce, cucumber) is at its best untouched, as before 043.
            if (window.Min <= 0f && doneness <= 0f) return 1f;
            if (doneness > window.Max) return Clamp01(1f - (doneness - window.Max) / FalloffWidth);
            if (doneness < window.Min) return GoodFactor * Clamp01(1f - (window.Min - doneness) / FalloffWidth);
            return doneness < window.PerfectFrom - Epsilon ? GoodFactor : 1f;
        }

        /// <summary>
        /// The time bucket (0 under, 1 done, 2 over) a heat op gives food in <paramref name="zone"/>: Good and Perfect share the done
        /// bucket, so the menu and the name take any food in the window.
        /// </summary>
        public static int BucketIndex(DonenessZone zone) =>
            zone == DonenessZone.Undercooked ? 0 : zone == DonenessZone.Overdone ? 2 : 1;

        /// <summary>
        /// True for a layer whose heat op (the first met from the top, <see cref="Assembly.HeatOpOf"/>) left it undercooked: its time
        /// bucket is that op's first one (zone buckets only). A layer without a heat op is never undercooked.
        /// </summary>
        public static bool IsUndercooked(Item? layer)
        {
            for (Item? c = layer; c != null; c = c.Inputs.Count > 0 ? c.Inputs[0] : null)
                if (c.Kind == OperationKind.Heat)
                    return c.Buckets.TryGetValue("time", out string? bucket) && string.Equals(bucket, UnderBucket, StringComparison.Ordinal);
            return false;
        }

        /// <summary>The id of the default heat ops' undercooked time bucket.</summary>
        public const string UnderBucket = "short";

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
