namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 027c: where food on the heat stands, as clients may know it (a presentation byte, D21 amendment pending in
    /// docs/human-inbox.md). Computed by the server from the sim's doneness window (<see cref="HeatZoneRules"/>); clients
    /// only draw the ring, the colour, the smoke and play the cues. The order is the cooking order.
    /// </summary>
    public enum HeatZone : byte
    {
        /// <summary>Nothing on the heat (empty vessel): no ring.</summary>
        None = 0,
        /// <summary>Below the op's minimum time: taking it out gives it back unchanged.</summary>
        Raw = 1,
        /// <summary>Cooking, not yet inside the ingredient's ideal doneness window.</summary>
        Cooking = 2,
        /// <summary>Inside the window, first half.</summary>
        Good = 3,
        /// <summary>Inside the window, second half, up to its end.</summary>
        Perfect = 4,
        /// <summary>Past the window: the sim's doneness factor is falling.</summary>
        Overdone = 5,
        /// <summary>At least <see cref="HeatZoneRules.BurnMargin"/> doneness past the window.</summary>
        Burnt = 6
    }
}
