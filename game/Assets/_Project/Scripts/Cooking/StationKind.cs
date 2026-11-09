namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Kitchen furniture that does something when clicked (spec 007). Spec 021: the board, pan, pot, plate and
    /// bowl are cookware you carry (<see cref="VesselKind"/>), no longer stations; the stoves (burners with knobs)
    /// and the plate shelf are new. Spec 028b: the stock lives in a fridge and on a dry shelf, one ingredient per
    /// slot (<see cref="StockSlot"/>); a crate still works (one implicit slot) but the scene has none any more.
    /// New values are appended (scenes store the number).
    /// </summary>
    public enum StationKind : byte
    {
        Crate,
        Counter,
        /// <summary>Spec 021: two burners, each with an on/off knob and a fry/sauté button.</summary>
        Stove,
        /// <summary>Spec 021: empty hands take a clean plate.</summary>
        PlateShelf,
        /// <summary>Spec 028b: cold stock, two shelves of slots behind an open door; the station option is the slot.</summary>
        Fridge,
        /// <summary>Spec 028b: dry stock on open boards; the station option is the slot.</summary>
        Shelf,
        /// <summary>Spec 029: a deep fryer with two baskets (<see cref="VesselKind.Basket"/>): a power knob and a lower/raise button
        /// per basket on its front (<see cref="FryerRules"/>).</summary>
        Fryer
    }
}
