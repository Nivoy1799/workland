using System;
using Worktown.Sim.Core;

namespace Worktown.Sim.Economy
{
    /// <summary>
    /// Spec 032: how long a guest waits for an order. A per-guest base (<see cref="BaseSeconds"/> ± <see cref="BaseSpreadPercent"/>,
    /// drawn from the injected seeded RNG) plus <see cref="PerLayerSeconds"/> per layer of the dish (its complexity). Patience only
    /// drains while the guest waits in the queue, never while it waits at the register. Pure; time comes in from the caller.
    /// </summary>
    public static class GuestPatience
    {
        /// <summary>Spec 032 defaults to review: 60 s plus 15 s per layer.</summary>
        public const int BaseSeconds = 60;
        public const int PerLayerSeconds = 15;
        /// <summary>The per-guest base varies by this share either way (54 to 66 s).</summary>
        public const int BaseSpreadPercent = 10;
        /// <summary>The ring turns Terracotta at this share of patience left or less.</summary>
        public const float LowShare = 0.25f;

        /// <summary>A guest's own base in whole seconds (one draw).</summary>
        public static int DrawBase(IRandom random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            int spread = BaseSeconds * BaseSpreadPercent / 100;
            return random.NextInt(BaseSeconds - spread, BaseSeconds + spread + 1);
        }

        /// <summary>The whole patience in seconds for a dish of <paramref name="layers"/> layers (at least one).</summary>
        public static int Seconds(int baseSeconds, int layers) => Math.Max(1, baseSeconds) + PerLayerSeconds * Math.Max(1, layers);

        /// <summary>The share of patience left after <paramref name="waited"/> s of <paramref name="total"/> s, 0..1.</summary>
        public static float ShareLeft(float total, float waited)
        {
            if (!(total > 0f)) return 0f;
            if (float.IsNaN(waited) || waited <= 0f) return 1f;
            float left = 1f - waited / total;
            return left < 0f ? 0f : left;
        }

        /// <summary>Out of patience: the guest leaves (missed).</summary>
        public static bool RunsOut(float total, float waited) => ShareLeft(total, waited) <= 0f;

        /// <summary>The share left as a byte for the network (255 full, 0 empty), rounded.</summary>
        public static byte ToByte(float shareLeft)
        {
            if (float.IsNaN(shareLeft) || shareLeft <= 0f) return 0;
            if (shareLeft >= 1f) return 255;
            return (byte)Math.Round(shareLeft * 255f);
        }

        public static float FromByte(byte value) => value / 255f;

        /// <summary>The ring is in its last quarter (Terracotta).</summary>
        public static bool IsLow(float shareLeft) => shareLeft <= LowShare;
    }
}
