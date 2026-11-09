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
        NoPlatesLeft,
        /// <summary>Review of spec 027b: the request came faster than the server's rate limit allows and was not carried out.
        /// Answered anyway, so the client's request numbering stays in step; the client shows nothing for it.</summary>
        TooFast,
        /// <summary>Spec 029: a basket goes into the oil only while the fryer is on.</summary>
        FryerOff,
        /// <summary>Spec 029: the basket is in the oil; raise it first to put food in or take it out.</summary>
        BasketLowered,
        /// <summary>Spec 029: a shake needs a lowered basket with food.</summary>
        NotLowered,
        /// <summary>Spec 030: a flip needs the spatula in the hands ("Use the spatula").</summary>
        NoSpatula,
        /// <summary>Spec 031: the build is closed (folded, wrapped or packed): nothing more goes on, it is closed once.</summary>
        BuildClosed,
        /// <summary>Spec 031: a wrap or flatbread is folded before it goes in paper or a bag.</summary>
        FoldFirst,
        /// <summary>Spec 031: someone else is squeezing that dispenser.</summary>
        DispenserBusy,
        /// <summary>Spec 031: only a build on a wrap or flatbread folds.</summary>
        NothingToFold,
        /// <summary>Spec 032: nobody waits at the register to pay.</summary>
        NoPayment,
        /// <summary>Spec 032: the guest pays by card (no cash, no change).</summary>
        PaysByCard,
        /// <summary>Spec 032: the guest pays in cash (the terminal is not for them).</summary>
        PaysCash,
        /// <summary>Spec 032: the change tray holds no more (it is synced as a byte).</summary>
        TrayFull,
        /// <summary>Spec 032: there is no change in the tray to take back.</summary>
        TrayEmpty,
        /// <summary>Spec 033: a tap pours into a cup in the hands, the lid stack lids one: take a cup first.</summary>
        NeedsCup,
        /// <summary>Spec 033: the cup has its lid (nothing more goes in, it is lidded once).</summary>
        CupClosed,
        /// <summary>Spec 033: the cup holds another drink (a top-up is the same drink only).</summary>
        OtherDrink,
        /// <summary>Spec 033: the cup ran over (or the server stopped it): nothing more goes in.</summary>
        CupFull,
        /// <summary>Spec 033: a lid goes only on a filled cup.</summary>
        NotFilled,
        /// <summary>Spec 033: enough empty cups lie about already (DrinkRules.MaxEmptyCups).</summary>
        NoCupsLeft
    }
}
