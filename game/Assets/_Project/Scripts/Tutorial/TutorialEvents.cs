namespace Worktown.Game.Tutorial
{
    /// <summary>
    /// Spec 038: things the local player did that only show as a moment, not as a lasting state (the server's "ok" for a sale, a
    /// payment, an order; the sink's or the spit's byte moving right after the player's own hold). <see cref="TutorialWatch"/> finds
    /// them; the steps read them from the snapshot.
    /// </summary>
    [System.Flags]
    public enum TutorialEvents : byte
    {
        None = 0,
        Served = 1,
        Paid = 2,
        Scrubbed = 4,
        Ordered = 8,
        Shaved = 16
    }
}
