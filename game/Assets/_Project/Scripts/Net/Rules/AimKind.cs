namespace Worktown.Game.Net.Rules
{
    /// <summary>Spec 018: what an aim ray hit. "Other" (wall, floor, another player) blocks the view.</summary>
    public enum AimKind : byte
    {
        Other = 0,
        Station = 1,
        Pickup = 2
    }
}
