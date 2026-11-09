using System;

namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 029: how fast one ingredient fries in the deep fryer at <see cref="DeepFry.ReferenceTempC"/>, as three sim times:
    /// when it reaches its ideal window's minimum, the window's maximum, and the window's maximum plus
    /// <see cref="DeepFry.BurnMargin"/> (burnt). The doneness is piecewise linear between them (<see cref="DeepFry.Doneness"/>).
    /// </summary>
    public sealed record FryCurve(string BaseId, float WindowMinAt, float WindowMaxAt, float BurntAt)
    {
        /// <summary>
        /// From the kitchen's zone timings in real seconds (the 027c ring: Good from the window's minimum, Perfect from the
        /// middle of the window, Burnt at the burn margin), at <see cref="DeepFry.KitchenTimeScale"/> sim seconds per second.
        /// </summary>
        public static FryCurve FromKitchenSeconds(string baseId, float good, float perfect, float burnt)
        {
            if (!(good > 0f && perfect > good && burnt > 2f * perfect - good))
                throw new ArgumentException("Fry timings need 0 < good < perfect and burnt after the window's end.", nameof(burnt));
            float scale = DeepFry.KitchenTimeScale;
            return new FryCurve(baseId, good * scale, (2f * perfect - good) * scale, burnt * scale);
        }
    }
}
