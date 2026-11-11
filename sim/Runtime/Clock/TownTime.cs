namespace Worktown.Sim.Clock
{
    /// <summary>The town time at one moment. PhasePermille is the progress through the phase (0..999).</summary>
    public sealed record TownTime(long Day, int DaySecond, DayPhase Phase, int PhasePermille, int MinuteOfDay, int Hour);
}
