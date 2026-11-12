using System;
using System.Collections.Generic;

namespace Worktown.Sim.Clock
{
    /// <summary>The season calendar as data (D46: no consumer in v1). Day 1 is the first day of First.</summary>
    public sealed record SeasonCalendar(int DaysPerSeason, Season First, IReadOnlyList<FestivalDef> Festivals)
    {
        public static SeasonCalendar CreateDefault() => new SeasonCalendar(7, Season.Spring, Array.AsReadOnly(new[]
        {
            new FestivalDef("blossom_fair", "Blossom Fair", Season.Spring, 5, "garden_promenade"),
            new FestivalDef("lake_nights", "Lake Nights", Season.Summer, 5, "lakefront"),
            new FestivalDef("landing_day", "Landing Day", Season.Autumn, 4, "station_square"),
            new FestivalDef("winter_lights", "Winter Lights", Season.Winter, 5, "wreck_market"),
        }));

        public Season SeasonOf(long day) => (Season)(((day - 1) / DaysPerSeason + (int)First) % 4);

        /// <summary>1..DaysPerSeason.</summary>
        public int DayInSeason(long day) => (int)((day - 1) % DaysPerSeason) + 1;

        public FestivalDef? FestivalOn(long day)
        {
            Season s = SeasonOf(day);
            int d = DayInSeason(day);
            foreach (FestivalDef f in Festivals)
            {
                if (f.Season == s && f.Day == d) return f;
            }
            return null;
        }
    }
}
