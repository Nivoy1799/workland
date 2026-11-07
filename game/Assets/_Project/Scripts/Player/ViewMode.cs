namespace Worktown.Game.Player
{
    /// <summary>Spec 018: which camera the local player looks through. Client only, never sent as a rule input.</summary>
    public enum ViewMode : byte
    {
        FirstPerson = 0,
        ThirdPerson = 1
    }
}
