namespace Worktown.Sim.Clock
{
    /// <summary>One phase of the day: real seconds it lasts, and the clock minutes it covers from StartMinute.</summary>
    public sealed record PhaseDef(DayPhase Phase, int Seconds, int StartMinute, int Minutes);
}
