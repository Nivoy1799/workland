using System;
using Worktown.Sim.Core;
using Worktown.Sim.Economy;

namespace Worktown.Sim.Clock
{
    /// <summary>
    /// The planned weather of a day, pure in (catalog, world seed, day). Exactly three draws per day, in this order:
    /// roll = NextInt(0, 1000), rain hour = NextInt(RainFromHour, RainToHour + 1), lightning seed = NextInt(0, 65536).
    /// </summary>
    public static class WeatherPlan
    {
        public const ulong WeatherSalt = 0x066;

        public static DayWeather For(WeatherCatalog w, ulong worldSeed, long day)
        {
            if (day < 1) throw new ArgumentOutOfRangeException(nameof(day));
            ulong seed = worldSeed ^ WeatherSalt;
            bool previousStorm = day > 1 && EconomyRng.ForTick(seed, day - 1).NextInt(0, 1000) < w.StormPermille;
            return For(w, EconomyRng.ForTick(seed, day), day, previousStorm);
        }

        /// <summary>Same as the seeded overload, with the day RNG and the previous day storm roll given.</summary>
        public static DayWeather For(WeatherCatalog w, IRandom rng, long day, bool previousRolledStorm)
        {
            int roll = rng.NextInt(0, 1000);
            int rainHour = rng.NextInt(w.RainFromHour, w.RainToHour + 1);
            int lightningSeed = rng.NextInt(0, 65536);

            DayKind kind;
            if (roll < w.StormPermille) kind = day == 1 || previousRolledStorm ? DayKind.Normal : DayKind.Storm;
            else if (roll < w.StormPermille + w.OpenPanelPermille) kind = DayKind.OpenPanel;
            else kind = DayKind.Normal;

            bool panels = kind == DayKind.OpenPanel;
            bool storm = kind == DayKind.Storm;
            return new DayWeather(
                day, kind, rainHour * 60, w.RainMinutes,
                panels ? w.PanelsFromMinute : -1, panels ? w.PanelsToMinute : -1,
                storm ? w.StormFromMinute : -1, storm ? w.StormToMinute : -1,
                lightningSeed);
        }

        public static bool InRainHour(DayWeather w, TownTime t) =>
            t.MinuteOfDay >= w.RainStartMinute && t.MinuteOfDay < w.RainStartMinute + w.RainMinutes;

        /// <summary>Panels are open in the panel window but closed during the Rain Hour.</summary>
        public static bool PanelsOpen(DayWeather w, TownTime t) =>
            w.Kind == DayKind.OpenPanel && t.MinuteOfDay >= w.PanelsFromMinute && t.MinuteOfDay < w.PanelsToMinute
            && !InRainHour(w, t);

        public static bool InStorm(DayWeather w, TownTime t) =>
            w.Kind == DayKind.Storm && t.MinuteOfDay >= w.StormFromMinute && t.MinuteOfDay < w.StormToMinute;
    }
}
