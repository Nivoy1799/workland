namespace Worktown.Game.Net.Rules
{
    /// <summary>Spec 018: what an aim ray hit. "Other" (wall, floor, another player) blocks the view.
    /// Spec 019: "Button" is a switch or mode button on a station (it carries its station and option).
    /// Spec 020: "Spot" is the top of a place surface (it carries the surface and the chosen free spot).</summary>
    public enum AimKind : byte
    {
        Other = 0,
        Station = 1,
        Pickup = 2,
        Button = 3,
        Spot = 4
    }
}
