namespace Worktown.Game.Cooking
{
    /// <summary>Server answer to a crate, station or serve request (spec 007).</summary>
    public enum CookOutcome : byte
    {
        Ok,
        TooFar,
        NothingHeld,
        HandsFull,
        UnknownStation,
        InvalidOption,
        NotFood,
        SimRefused
    }
}
