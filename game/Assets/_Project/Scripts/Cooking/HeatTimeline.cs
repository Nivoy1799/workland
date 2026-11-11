namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 027c, server only: when one food on the heat reaches each <see cref="HeatZone"/>, in sim seconds on the heat
    /// (<see cref="double.PositiveInfinity"/> = never within the op's time range). Built once per food by
    /// <see cref="HeatZoneRules.Build"/> from the sim; never sent to clients (they get the zone and progress bytes).
    /// </summary>
    public readonly struct HeatTimeline
    {
        private readonly double _perfectFrom;
        private readonly bool _hasPerfect;

        public HeatTimeline(double cookFrom, double goodFrom, double end, double overdoneFrom, double burntFrom)
            : this(cookFrom, goodFrom, end, overdoneFrom, burntFrom, double.NaN)
        {
        }

        /// <summary>
        /// Spec 043: with the measured start of Perfect (the sim's <c>DonenessZones</c>: every part in its window's Perfect half);
        /// NaN keeps the 027c rule (the middle of the good window).
        /// </summary>
        public HeatTimeline(double cookFrom, double goodFrom, double end, double overdoneFrom, double burntFrom, double perfectFrom)
        {
            CookFrom = cookFrom;
            GoodFrom = goodFrom;
            End = end;
            OverdoneFrom = overdoneFrom;
            BurntFrom = burntFrom;
            _hasPerfect = !double.IsNaN(perfectFrom);
            _perfectFrom = perfectFrom;
        }

        /// <summary>The op's minimum time: before it the food is Raw.</summary>
        public double CookFrom { get; }
        /// <summary>Every part of the food is at least at its window's minimum.</summary>
        public double GoodFrom { get; }
        /// <summary>The end of the good window (progress 255): <see cref="OverdoneFrom"/>, or where the doneness stops
        /// changing (fully done or the op's maximum time) for food that never overcooks.</summary>
        public double End { get; }
        /// <summary>A part is past its window's maximum.</summary>
        public double OverdoneFrom { get; }
        /// <summary>A part is <see cref="HeatZoneRules.BurnMargin"/> past its window's maximum.</summary>
        public double BurntFrom { get; }

        /// <summary>
        /// Where Perfect starts. Spec 043: measured from the sim (every part in its window's Perfect half, where the rating is
        /// best), never before <see cref="GoodFrom"/>; for a single food that cooks evenly it is the middle of the good window.
        /// Without a measured value (027c timelines built by hand), that middle.
        /// </summary>
        public double PerfectFrom
        {
            get
            {
                if (_hasPerfect) return _perfectFrom < GoodFrom ? GoodFrom : _perfectFrom;
                return double.IsInfinity(GoodFrom) || End <= GoodFrom
                    ? GoodFrom
                    : GoodFrom + HeatZoneRules.PerfectShareOfWindow * (End - GoodFrom);
            }
        }

        public override string ToString()
        {
            return "HeatTimeline(cook " + CookFrom.ToString("0.0") + ", good " + GoodFrom.ToString("0.0") + ", perfect "
                + PerfectFrom.ToString("0.0") + ", end " + End.ToString("0.0") + ", overdone " + OverdoneFrom.ToString("0.0")
                + ", burnt " + BurntFrom.ToString("0.0") + " sim s)";
        }
    }
}
