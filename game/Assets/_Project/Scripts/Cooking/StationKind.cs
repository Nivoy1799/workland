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
        Fryer,
        /// <summary>Spec 030: a flat griddle with six spots (<see cref="VesselKind.Griddle"/>) and a power knob on its front
        /// (<see cref="GriddleRules"/>).</summary>
        Griddle,
        /// <summary>Spec 031: the assembly board: one build spot (<see cref="VesselKind.Assembly"/>), two sauce dispensers held to
        /// squeeze and a packaging rack (wrap paper, box, bag) on its top (<see cref="BuildRules"/>; the tools are its slots).</summary>
        Assembly,
        /// <summary>Spec 032: the register on the counter: the change tray's coins and notes, take back, hand over and the card
        /// terminal are its slots (Shop.RegisterRules).</summary>
        Register,
        /// <summary>Spec 033: the drink dispenser on the front counter: three taps held to pour into the cup in the hands, the cup
        /// stack and the lid stack are its slots (<see cref="DrinkRules"/>).</summary>
        DrinkDispenser,
        /// <summary>Spec 034: the ordering terminal on the back wall: its screen's rows order a good, its buttons page and pick the pack
        /// count (Shop.TerminalRules; the rows and buttons are its slots).</summary>
        Terminal,
        /// <summary>Spec 035: the dish return on the front counter: eat-in guests' dirty plates come back here; empty hands take one
        /// (Shop.CleaningRules).</summary>
        DishReturn,
        /// <summary>Spec 035: the sink at the end of the back line: a dirty dish goes in, holding scrubs it (server-timed), and it goes to
        /// the clean plate shelf (Shop.CleaningRules; the basin is its slot).</summary>
        Sink,
        /// <summary>Spec 035: the trash can by the staff gap: discard food and empty packaging; empty hands take the bag out.</summary>
        TrashCan,
        /// <summary>Spec 035: the bin outside the staff gap, where the trash bag goes.</summary>
        Bin,
        /// <summary>Spec 036: the seitan spit on the front counter opposite the heat block: a heat knob, the turning cone (the knife is
        /// held on it to shave; empty hands load a new cone from stock) and the catch tray (<see cref="SpitRules"/>).</summary>
        Spit,
        /// <summary>Spec 037: the menu board above the counter: four buttons pick a line and move its price $0.50 down or up
        /// (Shop.MenuBoardRules; the buttons are its options).</summary>
        MenuBoard,
        /// <summary>Spec 057: the goal board on the wall: its rows are its options: the stage's step (rent the restaurant, the gate) and the
        /// stage 2 recipes (buy, on or off the menu); Climb.GoalBoardRules.</summary>
        GoalBoard
    }
}
