namespace Worktown.Game.Net
{
    /// <summary>Server answer to a pickup or drop request.</summary>
    public enum PickupOutcome : byte
    {
        Ok,
        NotFound,
        TooFar,
        Taken,
        AlreadyHolding,
        NothingHeld,
        /// <summary>Review of spec 027b: rate-limited, not carried out (see CookOutcome.TooFast).</summary>
        TooFast
    }
}
