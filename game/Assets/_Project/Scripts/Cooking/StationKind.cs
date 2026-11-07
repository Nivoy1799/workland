namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Kitchen furniture that does something when clicked (spec 007). Spec 021: the board, pan, pot, plate and
    /// bowl are cookware you carry (<see cref="VesselKind"/>), no longer stations; the stoves (burners with knobs)
    /// and the plate shelf are new.
    /// </summary>
    public enum StationKind : byte
    {
        Crate,
        Counter,
        /// <summary>Spec 021: two burners, each with an on/off knob and a fry/sauté button.</summary>
        Stove,
        /// <summary>Spec 021: empty hands take a clean plate.</summary>
        PlateShelf
    }
}
