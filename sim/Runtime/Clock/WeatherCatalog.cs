using System;

namespace Worktown.Sim.Clock
{
    /// <summary>Weather mix and windows (provisional numbers). Windows are clock minutes of the day.</summary>
    public sealed record WeatherCatalog(
        int StormPermille, int OpenPanelPermille, int RainFromHour, int RainToHour, int RainMinutes,
        int PanelsFromMinute, int PanelsToMinute, int StormFromMinute, int StormToMinute)
    {
        public static WeatherCatalog CreateDefault() =>
            new WeatherCatalog(100, 300, 10, 16, 60, 9 * 60, 18 * 60, 7 * 60, 21 * 60);

        public void Validate()
        {
            if (StormPermille < 0 || OpenPanelPermille < 0 || StormPermille + OpenPanelPermille > 1000)
                throw new ArgumentException("Weather permille out of range.");
            if (RainFromHour < 0 || RainToHour < RainFromHour || RainMinutes <= 0 || RainToHour * 60 + RainMinutes > 1440)
                throw new ArgumentException("Rain Hour window outside the day.");
            if (!Inside(PanelsFromMinute, PanelsToMinute)) throw new ArgumentException("Panel window outside the day.");
            if (!Inside(StormFromMinute, StormToMinute)) throw new ArgumentException("Storm window outside the day.");
        }

        private static bool Inside(int from, int to) => from >= 0 && to <= 1440 && from < to;
    }
}
