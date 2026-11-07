namespace Worktown.Game.Net.Rules
{
    /// <summary>Spec 018: what an aim ray hit. "Other" (wall, floor, another player) blocks the view.
    /// Spec 019: "Button" is a switch or mode button on a station (it carries its station and option).</summary>
    public enum AimKind : byte
    {
        Other = 0,
        Station = 1,
        Pickup = 2,
        Button = 3
    }
}
