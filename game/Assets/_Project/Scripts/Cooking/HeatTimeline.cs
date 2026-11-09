namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 027c, server only: when one food on the heat reaches each <see cref="HeatZone"/>, in sim seconds on the heat
    /// (<see cref="double.PositiveInfinity"/> = never within the op's time range). Built once per food by
    /// <see cref="HeatZoneRules.Build"/> from the sim; never sent to clients (they get the zone and progress bytes).
    /// </summary>
    public readonly struct HeatTimeline
    {
        public HeatTimeline(double cookFrom, double goodFrom, double end, double overdoneFrom, double burntFrom)
        {
            CookFrom = cookFrom;
            GoodFrom = goodFrom;
            End = end;
            OverdoneFrom = overdoneFrom;
            BurntFrom = burntFrom;
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

        /// <summary>Where the Perfect half of the good window starts.</summary>
        public double PerfectFrom => double.IsInfinity(GoodFrom) || End <= GoodFrom
            ? GoodFrom
            : GoodFrom + HeatZoneRules.PerfectShareOfWindow * (End - GoodFrom);

        public override string ToString()
        {
            return "HeatTimeline(cook " + CookFrom.ToString("0.0") + ", good " + GoodFrom.ToString("0.0") + ", perfect "
                + PerfectFrom.ToString("0.0") + ", end " + End.ToString("0.0") + ", overdone " + OverdoneFrom.ToString("0.0")
                + ", burnt " + BurntFrom.ToString("0.0") + " sim s)";
        }
    }
}
