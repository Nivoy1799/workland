namespace Worktown.Sim.Town
{
    /// <summary>
    /// Spec 064: Path and Ramp stay below 5 % (check C1); Stairs walk like paths and always have a ramp route next to them
    /// (C2); Lift and Funicular cost a fixed time and are used only when lifts are allowed.
    /// </summary>
    public enum PathEdgeKind
    {
        Path,
        Ramp,
        Stairs,
        Lift,
        Funicular,
    }
}
