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
        CannotSell,
        /// <summary>Spec 013: the plate or bowl is full.</summary>
        StationFull,
        /// <summary>Spec 013: a plated dish cannot go onto another plate or into a bowl.</summary>
        AlreadyPlated,
        /// <summary>Spec 013: nothing on the plate or in the bowl to take.</summary>
        StationEmpty,
        /// <summary>Spec 016: taken out before it started cooking; the food came back unchanged.</summary>
        TooShort,
        /// <summary>Spec 016: fry/saute can only change while the pan is empty.</summary>
        PanBusy,
        /// <summary>Spec 019: a cut stroke or turn needs a knife in the hands.</summary>
        NoKnife,
        /// <summary>Spec 019: a cut stroke or turn on a board with no food on it.</summary>
        NothingToCut,
        /// <summary>Spec 021: the board, pan, pot, plate or bowl has no room for more.</summary>
        VesselFull,
        /// <summary>Spec 021: the target of a use request is no cookware (or someone holds it).</summary>
        NotAVessel,
        /// <summary>Spec 021: this cookware cannot be poured into that one (e.g. a plate into a pan).</summary>
        CannotPour,
        /// <summary>Spec 021: a pot is poured onto a plate or into a bowl, not emptied by hand.</summary>
        PourInstead,
        /// <summary>Spec 021: the plate shelf gives no more plates while the world has the maximum.</summary>
        NoPlatesLeft
    }
}
