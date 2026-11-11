namespace Worktown.Sim.Clock
{
    /// <summary>Real Seconds spent inside one clock hour of one day.</summary>
    public sealed record HourSpan(long Day, int Hour, int Seconds);
}
