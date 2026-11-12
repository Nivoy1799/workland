using System;
using System.Collections.Generic;
using System.Text;
using Worktown.Game.Economy;
using Worktown.Game.Npc;

namespace Worktown.Game.Climb
{
    /// <summary>
    /// Spec 057, pure: the stage 2 gate (spec 052 step 5). Each counted service gives a star average in tenths, a missed guest counting one
    /// star at half weight; the window keeps the newest services (newest last, at most <see cref="Kept"/>); the gate holds when the last
    /// <c>GateWindow</c> services are there, their average reaches the gate's stars and the balance reaches the licence's cost.
    /// </summary>
    public static class GateRules
    {
        /// <summary>The most services the window (and the save) keeps.</summary>
        public const int Kept = 8;
        /// <summary>A service's value is 1.0 to 5.0 stars (a save's values are clamped to it).</summary>
        public const int MinTenths = 10;
        public const int MaxTenths = 50;
        /// <summary>No counted service (nobody served or missed).</summary>
        public const int NotCounted = -1;

        public const string ReachedText = "Licence within reach! Delivery and more services come in a later update.";

        /// <summary>
        /// A service's stars in tenths: w = 2 x served + missed; (20 x stars + 10 x missed + w / 2) / w; <see cref="NotCounted"/> when w is 0.
        /// </summary>
        public static int ServiceStarsTenths(int served, int missed, int starsTotal)
        {
            long s = Math.Max(0, served), m = Math.Max(0, missed), stars = Math.Max(0, starsTotal);
            long w = 2 * s + m;
            if (w == 0) return NotCounted;
            return (int)Math.Max(MinTenths, Math.Min(MaxTenths, (20 * stars + 10 * m + w / 2) / w));
        }

        public static int ServiceStarsTenths(ShiftSummary summary) => ServiceStarsTenths(summary.Served, summary.Missed, summary.StarsTotal);

        /// <summary>The window after a service: <paramref name="tenths"/> appended (a not counted service changes nothing), the oldest dropped past <see cref="Kept"/>.</summary>
        public static int[] Push(IReadOnlyList<int> window, int tenths)
        {
            var list = new List<int>();
            if (window != null) foreach (int v in window) list.Add(v);
            if (tenths != NotCounted) list.Add(Math.Max(MinTenths, Math.Min(MaxTenths, tenths)));
            while (list.Count > Kept) list.RemoveAt(0);
            return list.ToArray();
        }

        /// <summary>How many of the last <paramref name="gateWindow"/> services the window holds.</summary>
        public static int Counted(IReadOnlyList<int> window, int gateWindow) => window == null ? 0 : Math.Min(window.Count, Math.Max(0, gateWindow));

        /// <summary>The average of the last <paramref name="gateWindow"/> services in tenths (rounded down); 0 without one.</summary>
        public static int AverageTenths(IReadOnlyList<int> window, int gateWindow)
        {
            int n = Counted(window, gateWindow);
            if (n == 0) return 0;
            long sum = 0;
            for (int i = window.Count - n; i < window.Count; i++) sum += window[i];
            return (int)(sum / n);
        }

        /// <summary>Bit i: the i-th of the last <paramref name="gateWindow"/> services (oldest first) reached <paramref name="gateStars"/>.</summary>
        public static byte Pips(IReadOnlyList<int> window, int gateWindow, int gateStars)
        {
            int n = Math.Min(8, Counted(window, gateWindow));
            int mask = 0;
            for (int i = 0; i < n; i++)
                if (window[window.Count - n + i] >= gateStars) mask |= 1 << i;
            return (byte)mask;
        }

        public static bool WindowFull(IReadOnlyList<int> window, int gateWindow) => gateWindow > 0 && window != null && window.Count >= gateWindow;

        /// <summary>The gate holds: the window is full, its average reaches the stars and the balance reaches the cost.</summary>
        public static bool GateHolds(IReadOnlyList<int> window, int gateWindow, int gateStars, long balance, long gateCost) =>
            WindowFull(window, gateWindow) && AverageTenths(window, gateWindow) >= gateStars && balance >= gateCost;

        /// <summary>Whether the gate holds from the synced values (the client's view).</summary>
        public static bool GateHolds(int averageTenths, int count, int gateWindow, int gateStars, long balance, long gateCost) =>
            gateWindow > 0 && count >= gateWindow && averageTenths >= gateStars && balance >= gateCost;

        /// <summary>"3.2" for 32 tenths.</summary>
        public static string Stars(int tenths) => (Math.Max(0, tenths) / 10) + "." + (Math.Max(0, tenths) % 10);

        /// <summary>The middle dot between the parts of a line (U+00B7).</summary>
        public const char Dot = (char)0xB7;

        /// <summary>"Next: service licence · stars 3.2 / 3.5 (last 5) · $41.20 / $1,200.00" ("stars - / 3.5" before a counted service).</summary>
        public static string GateLine(int averageTenths, int count, int gateWindow, int gateStars, long balance, long gateCost)
        {
            var sb = new StringBuilder("Next: service licence ").Append(Dot).Append(" stars ");
            sb.Append(count > 0 ? Stars(averageTenths) : "-").Append(" / ").Append(Stars(gateStars));
            sb.Append(" (last ").Append(gateWindow).Append(") ").Append(Dot).Append(' ');
            sb.Append(EconomyRules.Money(balance)).Append(" / ").Append(EconomyRules.Money(gateCost));
            return sb.ToString();
        }

        /// <summary>The summary's pips: one per window service, oldest first, "*" filled (reached the stars), "o" not, "-" not played yet.</summary>
        public static string PipText(byte pips, int count, int gateWindow)
        {
            var sb = new StringBuilder();
            int n = Math.Min(8, Math.Max(0, gateWindow));
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(i >= count ? '-' : (pips & (1 << i)) != 0 ? '*' : 'o');
            }
            return sb.ToString();
        }
    }
}
