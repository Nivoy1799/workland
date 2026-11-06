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
        SimRefused,
        /// <summary>Spec 009: the grocer price is more than the wallet holds.</summary>
        NotEnoughMoney,
        /// <summary>Spec 009: no market wants it (e.g. quality too low, raw potato).</summary>
        CannotSell
    }
}
