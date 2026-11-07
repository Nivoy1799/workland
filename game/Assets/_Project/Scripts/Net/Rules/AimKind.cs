namespace Worktown.Game.Net.Rules
{
    /// <summary>Spec 018: what an aim ray hit. "Other" (wall, floor, another player) blocks the view.
    /// Spec 019: "Button" is a switch or mode button on a station (it carries its station and option).
    /// Spec 020: "Spot" is the top of a place surface (it carries the surface and the chosen free spot).
    /// Spec 021: "Vessel" is cookware lying somewhere (board, pan, pot, plate, bowl). It is a pickup, but unlike other
    /// pickups it stays a target with full hands (food goes into it, a pan pours into it).</summary>
    public enum AimKind : byte
    {
        Other = 0,
        Station = 1,
        Pickup = 2,
        Button = 3,
        Spot = 4,
        Vessel = 5
    }
}
