using System;

namespace Worktown.Game.Npc
{
    /// <summary>
    /// Spec 022: the numbers of one shift: customers served, money they paid, their stars and the customers who left
    /// unserved. Immutable; the With methods return a new value.
    /// </summary>
    public readonly struct ShiftSummary
    {
        public static readonly ShiftSummary Empty = default;

        public ShiftSummary(int served, long earned, int starsTotal, int missed)
        {
            Served = served;
            Earned = earned;
            StarsTotal = starsTotal;
            Missed = missed;
        }

        public int Served { get; }
        public long Earned { get; }
        public int StarsTotal { get; }
        public int Missed { get; }

        /// <summary>Average stars of the served dishes in tenths (42 = 4.2), rounded half up; 0 when none.</summary>
        public int AverageStarsTenths => Served <= 0 ? 0 : (int)((StarsTotal * 10L + Served / 2) / Served);

        public ShiftSummary WithServed(int stars, long pay)
        {
            return new ShiftSummary(Served + 1, Earned + Math.Max(0L, pay), StarsTotal + Math.Max(0, stars), Missed);
        }

        public ShiftSummary WithMissed(int count)
        {
            return new ShiftSummary(Served, Earned, StarsTotal, Missed + Math.Max(0, count));
        }

        /// <summary>"4.2" style text of the average.</summary>
        public static string StarsText(int tenths)
        {
            if (tenths < 0) tenths = 0;
            return (tenths / 10) + "." + (tenths % 10);
        }
    }
}
