namespace Worktown.Sim.Clock
{
    /// <summary>Weather row x phase row / 1000 per field (floor). Weather rows: first match wins.</summary>
    public sealed record ConditionsTable(
        Conditions StormRainHour, Conditions RainHour, Conditions StormWindow, Conditions PanelsOpen, Conditions Normal,
        Conditions DawnDay, Conditions Dusk, Conditions Night)
    {
        public static ConditionsTable CreateDefault() => new ConditionsTable(
            new Conditions(500, 0, 1200, 1300, 1300),
            new Conditions(700, 0, 1150, 1300, 1000),
            new Conditions(600, 300, 1200, 1200, 1300),
            new Conditions(1100, 1400, 950, 900, 1000),
            new Conditions(1000, 1000, 1000, 1000, 1000),
            new Conditions(1000, 1000, 1000, 1000, 1000),
            new Conditions(1000, 1100, 1000, 1000, 1000),
            new Conditions(1000, 800, 1000, 1250, 1000));

        public Conditions At(DayWeather w, TownTime t)
        {
            bool rain = WeatherPlan.InRainHour(w, t);
            Conditions weather;
            if (rain && w.Kind == DayKind.Storm) weather = StormRainHour;
            else if (rain) weather = RainHour;
            else if (WeatherPlan.InStorm(w, t)) weather = StormWindow;
            else if (WeatherPlan.PanelsOpen(w, t)) weather = PanelsOpen;
            else weather = Normal;

            Conditions phase = t.Phase == DayPhase.Dusk ? Dusk : t.Phase == DayPhase.Night ? Night : DawnDay;
            return new Conditions(
                weather.StreetPermille * phase.StreetPermille / 1000,
                weather.OutdoorSeatsPermille * phase.OutdoorSeatsPermille / 1000,
                weather.IndoorPermille * phase.IndoorPermille / 1000,
                weather.DeliveryPermille * phase.DeliveryPermille / 1000,
                weather.DeliveryTimePermille * phase.DeliveryTimePermille / 1000);
        }
    }
}
