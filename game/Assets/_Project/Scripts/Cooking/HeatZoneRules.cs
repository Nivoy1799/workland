using System;
using System.Collections.Generic;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 027c: pure rules for the cooking feedback. The server builds a <see cref="HeatTimeline"/> per food from the sim
    /// (<see cref="Build"/>: the times at which the cooked item's components enter and leave their ideal doneness window,
    /// found by applying the real sim op, so no cooking formula is copied here), then turns the time on the heat into the
    /// two presentation bytes clients get: <see cref="Progress"/> (0-255, the share of the way to the end of the good
    /// window) and the zone byte (<see cref="Encode"/>: the <see cref="HeatZone"/> plus a warning bit set
    /// <see cref="WarnRealSeconds"/> before Overdone). Cooking rules and timings are unchanged.
    /// </summary>
    public static class HeatZoneRules
    {
        /// <summary>Burnt: this much doneness past the window's maximum (the sim's doneness factor falls to zero over 0.2,
        /// so it is at half here), capped at fully done; never for a window that ends at 1.</summary>
        public const float BurnMargin = 0.1f;
        /// <summary>The second half of the good window is Perfect.</summary>
        public const double PerfectShareOfWindow = 0.5;
        /// <summary>Where the good window starts on the ring and in <see cref="Progress"/> (fixed, so the client can mark it).</summary>
        public const float GoodShare = 0.6f;
        /// <summary>Where Perfect starts on the ring (halfway through the good arc).</summary>
        public const float PerfectShare = GoodShare + (1f - GoodShare) * (float)PerfectShareOfWindow;
        /// <summary>The warning cue: this many real seconds before Overdone.</summary>
        public const double WarnRealSeconds = 3.0;
        /// <summary>The warning in sim seconds (<see cref="CookingTime.TimeScale"/> sim seconds per real second).</summary>
        public const double WarnSimSeconds = WarnRealSeconds * CookingTime.TimeScale;

        /// <summary>Bit 7 of the zone byte: Overdone comes within <see cref="WarnRealSeconds"/>.</summary>
        public const byte WarnBit = 0x80;
        private const byte ZoneMask = 0x0F;
        private const int SearchSteps = 30;
        private const float Epsilon = 1e-4f;

        // ---- the timeline (server) ----

        /// <summary>
        /// The timeline of one food in a heat op with time parameter <paramref name="time"/>. <paramref name="cookedAt"/>
        /// returns the components of the sim's result for a time on the heat (clamped to the op's range by the caller's
        /// sim op), or null when the sim refuses. Doneness only grows with time, so each threshold is found by bisection.
        /// Null arguments or a refusing sim give a timeline that never leaves Raw.
        /// </summary>
        public static HeatTimeline Build(ParamDef time, Func<float, IReadOnlyList<ItemComponent>> cookedAt)
        {
            const double never = double.PositiveInfinity;
            if (time == null || cookedAt == null || !(time.Max >= time.Min) || time.Min < 0f)
                return new HeatTimeline(never, never, never, never, never);
            float min = time.Min, max = time.Max;
            if (cookedAt(max) == null) return new HeatTimeline(never, never, never, never, never);

            double good = FirstTime(min, max, cookedAt, IsGood);
            double overdone = FirstTime(min, max, cookedAt, IsOverdone);
            double burnt = FirstTime(min, max, cookedAt, IsBurnt);
            double end = overdone;
            if (double.IsInfinity(end))
            {
                double full = FirstTime(min, max, cookedAt, IsFullyDone);
                end = double.IsInfinity(full) ? max : full;
            }
            return new HeatTimeline(min, good, end, overdone, burnt);
        }

        /// <summary>Every component is at least at its window's minimum.</summary>
        public static bool IsGood(IReadOnlyList<ItemComponent> parts)
        {
            if (parts == null || parts.Count == 0) return false;
            foreach (ItemComponent c in parts)
                if (c.Doneness < c.Ideal.Min - Epsilon) return false;
            return true;
        }

        /// <summary>A component is past its window's maximum.</summary>
        public static bool IsOverdone(IReadOnlyList<ItemComponent> parts)
        {
            if (parts == null) return false;
            foreach (ItemComponent c in parts)
                if (c.Doneness > c.Ideal.Max + Epsilon) return true;
            return false;
        }

        /// <summary>A component whose window ends below 1 is <see cref="BurnMargin"/> past it (or fully done).</summary>
        public static bool IsBurnt(IReadOnlyList<ItemComponent> parts)
        {
            if (parts == null) return false;
            foreach (ItemComponent c in parts)
            {
                if (c.Ideal.Max >= 1f - Epsilon) continue;
                float limit = Math.Min(1f, c.Ideal.Max + BurnMargin);
                if (c.Doneness >= limit - Epsilon) return true;
            }
            return false;
        }

        private static bool IsFullyDone(IReadOnlyList<ItemComponent> parts)
        {
            if (parts == null || parts.Count == 0) return false;
            foreach (ItemComponent c in parts)
                if (c.Doneness < 1f - Epsilon) return false;
            return true;
        }

        /// <summary>The first time in [min, max] where the (monotonic) test holds; infinity when it never does.</summary>
        private static double FirstTime(float min, float max, Func<float, IReadOnlyList<ItemComponent>> cookedAt,
            Func<IReadOnlyList<ItemComponent>, bool> test)
        {
            if (test(cookedAt(min))) return min;
            if (!test(cookedAt(max))) return double.PositiveInfinity;
            float lo = min, hi = max;
            for (int i = 0; i < SearchSteps && hi - lo > 1e-3f; i++)
            {
                float mid = lo + (hi - lo) * 0.5f;
                if (test(cookedAt(mid))) hi = mid;
                else lo = mid;
            }
            return hi;
        }

        // ---- zone, progress, warning (pure, per food) ----

        /// <summary>The zone after <paramref name="simSeconds"/> on the heat. NaN or negative counts as 0 (Raw).</summary>
        public static HeatZone Zone(double simSeconds, HeatTimeline timeline)
        {
            double t = Sanitize(simSeconds);
            if (t >= timeline.BurntFrom) return HeatZone.Burnt;
            if (t >= timeline.OverdoneFrom) return HeatZone.Overdone;
            if (t < timeline.CookFrom) return HeatZone.Raw;
            if (t < timeline.GoodFrom) return HeatZone.Cooking;
            return t < timeline.PerfectFrom ? HeatZone.Good : HeatZone.Perfect;
        }

        /// <summary>
        /// Progress 0-255: from 0 to <see cref="GoodShare"/> up to the start of the good window, then to 255 at its end;
        /// 255 after it. Never decreases with time. NaN or negative counts as 0.
        /// </summary>
        public static byte Progress(double simSeconds, HeatTimeline timeline)
        {
            double t = Sanitize(simSeconds);
            double share;
            double good = timeline.GoodFrom, end = timeline.End;
            if (t >= end) share = 1.0;
            else if (double.IsInfinity(good) || good >= end)
                share = double.IsInfinity(end) || end <= 0.0 ? 0.0 : GoodShare * t / end;
            else if (t < good) share = good <= 0.0 ? GoodShare : GoodShare * t / good;
            else share = GoodShare + (1.0 - GoodShare) * (t - good) / (end - good);
            if (share < 0.0) share = 0.0;
            if (share > 1.0) share = 1.0;
            return (byte)Math.Round(share * 255.0);
        }

        /// <summary>True from exactly <see cref="WarnSimSeconds"/> before Overdone until Overdone (never for food that does
        /// not overcook).</summary>
        public static bool Warn(double simSeconds, HeatTimeline timeline)
        {
            double t = Sanitize(simSeconds);
            if (double.IsInfinity(timeline.OverdoneFrom)) return false;
            return t >= timeline.OverdoneFrom - WarnSimSeconds && t < timeline.OverdoneFrom;
        }

        private static double Sanitize(double simSeconds) => double.IsNaN(simSeconds) || simSeconds < 0.0 ? 0.0 : simSeconds;

        // ---- the zone byte ----

        public static byte Encode(HeatZone zone, bool warn)
        {
            byte z = (byte)zone;
            if (z > (byte)HeatZone.Burnt) z = (byte)HeatZone.None;
            return (byte)(z | (warn ? WarnBit : 0));
        }

        /// <summary>The zone in a zone byte; None for an unknown value.</summary>
        public static HeatZone ZoneOf(byte encoded)
        {
            int z = encoded & ZoneMask;
            return z <= (int)HeatZone.Burnt ? (HeatZone)z : HeatZone.None;
        }

        public static bool WarnOf(byte encoded) => (encoded & WarnBit) != 0;

        // ---- one vessel ----

        /// <summary>Zone, progress and warning of one food after <paramref name="simSeconds"/> on the heat.</summary>
        public static HeatReading Read(double simSeconds, HeatTimeline timeline)
        {
            return new HeatReading(Zone(simSeconds, timeline), Progress(simSeconds, timeline), Warn(simSeconds, timeline));
        }

        /// <summary>
        /// Several foods in one pot: any Overdone or Burnt food wins (the furthest one, progress full); otherwise the least
        /// advanced food decides (the dish is ready when its slowest part is). The warning is set when any food is about to
        /// overcook. <see cref="HeatReading.Empty"/> is the neutral start.
        /// </summary>
        public static HeatReading Combine(HeatReading a, HeatReading b)
        {
            if (a.Zone == HeatZone.None) return b;
            if (b.Zone == HeatZone.None) return a;
            bool warn = a.Warn || b.Warn;
            if (a.Zone >= HeatZone.Overdone || b.Zone >= HeatZone.Overdone)
                return new HeatReading(a.Zone > b.Zone ? a.Zone : b.Zone, 255, warn);
            return new HeatReading(a.Zone < b.Zone ? a.Zone : b.Zone, a.Progress < b.Progress ? a.Progress : b.Progress, warn);
        }

        /// <summary>The zone byte of a reading.</summary>
        public static byte Encode(HeatReading reading) => Encode(reading.Zone, reading.Warn);
    }
}
