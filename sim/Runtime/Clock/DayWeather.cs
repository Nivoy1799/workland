namespace Worktown.Sim.Clock
{
    /// <summary>
    /// The planned weather of one day. Minutes are clock minutes of the day (0..1440); a window that does not
    /// apply is -1..-1. LightningSeed seeds the strike schedule.
    /// </summary>
    public sealed record DayWeather(
        long Day, DayKind Kind, int RainStartMinute, int RainMinutes, int PanelsFromMinute, int PanelsToMinute,
        int StormFromMinute, int StormToMinute, int LightningSeed);
}
