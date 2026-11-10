namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 043: where a doneness stands against its ideal window, the sim side of the kitchen's 027c zones. The window's first
    /// half is Good, its second half (from <see cref="DonenessWindow.PerfectFrom"/> to its end) Perfect; below it Undercooked, past it
    /// Overdone. <see cref="DonenessZones"/> has the rules.
    /// </summary>
    public enum DonenessZone
    {
        Undercooked = 0,
        Good = 1,
        Perfect = 2,
        Overdone = 3,
    }
}
