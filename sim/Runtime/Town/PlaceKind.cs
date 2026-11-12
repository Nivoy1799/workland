namespace Worktown.Sim.Town
{
    /// <summary>
    /// Spec 064: what a place is. Pitch (cart pitches C1-C8), Small (S1-S6), Restaurant (R1-R4), StageFive (big jobs),
    /// Civic, Address (delivery doors), Stop (monorail, no edges), Border (anchor on the play-area edge).
    /// </summary>
    public enum PlaceKind
    {
        Pitch,
        Small,
        Restaurant,
        StageFive,
        Civic,
        Address,
        Stop,
        Border,
    }
}
