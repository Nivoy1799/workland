namespace Worktown.Sim.Clock
{
    /// <summary>Services run back to back on the town clock, so the pace per play hour does not depend on the day length.</summary>
    public static class ServiceFit
    {
        public static (int ServicesPerDay, int LeftoverSeconds, int ServicesPerPlayHourTimes100) For(
            ClockCatalog c, int serviceSeconds, int betweenSeconds)
        {
            int cycle = serviceSeconds + betweenSeconds;
            return (c.DayLengthSeconds / cycle, c.DayLengthSeconds % cycle, 360000 / cycle);
        }
    }
}
