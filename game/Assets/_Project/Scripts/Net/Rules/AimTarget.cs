using Worktown.Game.Cooking;

namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Spec 020: what the dot points at, as plain data for <see cref="InteractionRules"/>: the kind of target and
    /// what clients can see of it (the stove flags, the item's name, the chosen spot; spec 021: a piece of cookware's
    /// kind and synced contents, whether a spot is a burner). <c>default</c> is "nothing".
    /// </summary>
    public readonly struct AimTarget
    {
        /// <summary><see cref="AimKind.Other"/> = nothing to interact with.</summary>
        public readonly AimKind Kind;
        public readonly StationKind Station;
        /// <summary>Spec 021, cookware: board, pan, pot, plate or bowl.</summary>
        public readonly VesselKind Vessel;
        /// <summary>Cookware: how many things lie in it (from its synced contents).</summary>
        public readonly int ContentCount;
        /// <summary>Cookware: what taking or pouring gives ("potato slices"), null when empty.</summary>
        public readonly string ContentName;
        /// <summary>Buttons: the station option (StationRules.StoveOption).</summary>
        public readonly int Option;
        /// <summary>Buttons: the stove's synced flags (on, saute per burner).</summary>
        public readonly byte StoveFlags;
        /// <summary>Pickups: the item's name ("potato", "knife", "box"). Crates: the ingredient ("potato").</summary>
        public readonly string Name;
        /// <summary>Crates: the grocer price in dollars, -1 when unknown.</summary>
        public readonly long Price;
        /// <summary>Spots: the chosen free spot, <see cref="PlaceSpots.None"/> when the surface is full.</summary>
        public readonly int SpotIndex;
        /// <summary>Spec 021, spots: the surface is a burner.</summary>
        public readonly bool IsBurner;
        /// <summary>Spec 020: the target is beyond reach (the ray sees it, the server would refuse): no action, "Too far".</summary>
        public readonly bool IsTooFar;
        /// <summary>Spec 029, cookware: its synced mode byte (a basket's <see cref="FryerRules.Mode"/>).</summary>
        public readonly byte Mode;
        /// <summary>Spec 034: the stock line this slot shows and a delivery box unpacks into (Shop.StockCatalog), -1 for none.</summary>
        public readonly int StockLine;
        /// <summary>Spec 034: that line's synced count as this client sees it (Shop.StockCounts), -1 while unknown.</summary>
        public readonly int StockCount;
        /// <summary>Spec 035: a cleaning station's (or the plate shelf's) synced byte is known (<see cref="StoveFlags"/> holds it).</summary>
        public readonly bool Known;

        private AimTarget(AimKind kind, StationKind station, VesselKind vessel, int contentCount, string contentName, int option,
            byte stoveFlags, string name, long price, int spotIndex, bool burner, bool tooFar, byte mode = 0, int stockLine = -1,
            int stockCount = -1, bool known = false)
        {
            Known = known;
            StockLine = stockLine < 0 ? -1 : stockLine;
            StockCount = stockCount < 0 ? -1 : stockCount;
            Mode = mode;
            Kind = kind;
            Station = station;
            Vessel = vessel;
            ContentCount = contentCount < 0 ? 0 : contentCount;
            ContentName = ContentCount > 0 && !string.IsNullOrEmpty(contentName) ? contentName : null;
            Option = option;
            StoveFlags = stoveFlags;
            Name = name;
            Price = price;
            SpotIndex = spotIndex;
            IsBurner = burner;
            IsTooFar = tooFar;
        }

        public static AimTarget Nothing => default;

        /// <summary>The same target, but beyond reach.</summary>
        public AimTarget TooFar() => With(true);

        /// <summary>The same target within reach (what a step closer would give).</summary>
        public AimTarget InReach() => With(false);

        private AimTarget With(bool tooFar)
        {
            return new AimTarget(Kind, Station, Vessel, ContentCount, ContentName, Option, StoveFlags, Name, Price, SpotIndex, IsBurner, tooFar, Mode,
                StockLine, StockCount, Known);
        }

        /// <summary>
        /// Spec 035: a cleaning station (the dish return, the sink or its basin, the trash can, the bin) or the plate shelf, with its synced
        /// byte (Shop.CleaningRules: the dirty count, the basin and scrubbing bit, the fill, the clean plates) when <paramref name="known"/>.
        /// </summary>
        public static AimTarget CleaningStation(StationKind kind, int option, byte flags, bool known)
        {
            return new AimTarget(AimKind.Station, kind, default, 0, null, option, flags, null, -1, PlaceSpots.None, false, false, 0, -1, -1, known);
        }

        /// <summary>
        /// Spec 036: a slot of the seitan spit (SpitRules: the cone): <see cref="StoveFlags"/> the spit's byte as this client sees it (the
        /// level, someone shaving: the interactor clears its own, a cone), <see cref="Mode"/> the cone's mode byte (portions left),
        /// <see cref="ContentCount"/> the portions in the catch tray, <see cref="ContentName"/> and <see cref="Name"/> what a shave gives now.
        /// </summary>
        public static AimTarget SpitTool(int slot, byte flags, byte coneMode, int tray, string takeName)
        {
            return new AimTarget(AimKind.Station, StationKind.Spit, VesselKind.SpitCone, tray, takeName, slot, flags, takeName, -1,
                PlaceSpots.None, false, false, coneMode);
        }

        /// <summary>Spec 034: the same target showing stock line <paramref name="line"/> at <paramref name="count"/> (-1: unknown).</summary>
        public AimTarget WithStock(int line, int count)
        {
            return new AimTarget(Kind, Station, Vessel, ContentCount, ContentName, Option, StoveFlags, Name, Price, SpotIndex, IsBurner, IsTooFar,
                Mode, line, count, Known);
        }

        /// <summary>
        /// Spec 034: a slot of the ordering terminal (Shop.TerminalRules): <see cref="Option"/> the slot (a row or a button), <see cref="Name"/>
        /// the words of its click ("order 1 pack of tomato ($336, 1 h)", "next page (2/4)"), null when it does nothing (a row past the list's
        /// end), <see cref="Price"/> a row's order price.
        /// </summary>
        public static AimTarget TerminalSlot(int slot, string words, long price)
        {
            return new AimTarget(AimKind.Station, StationKind.Terminal, default, 0, null, slot, 0, words, price, PlaceSpots.None, false, false);
        }

        public static AimTarget Button(int option, byte stoveFlags) => Button(StationKind.Stove, option, stoveFlags);

        /// <summary>Spec 029: a button of any station (the stove's or the fryer's), with that station's synced flags.</summary>
        public static AimTarget Button(StationKind station, int option, byte flags)
        {
            return new AimTarget(AimKind.Button, station, default, 0, null, option, flags, null, -1, PlaceSpots.None, false, false);
        }

        /// <summary>A station other than a crate: the counter, a stove's body, the plate shelf.</summary>
        public static AimTarget OnStation(StationKind kind)
        {
            return new AimTarget(AimKind.Station, kind, default, 0, null, 0, 0, null, -1, PlaceSpots.None, false, false);
        }

        public static AimTarget Crate(string ingredient, long price)
        {
            return new AimTarget(AimKind.Station, StationKind.Crate, default, 0, null, 0, 0, ingredient, price, PlaceSpots.None, false, false);
        }

        /// <summary>
        /// Spec 031: a tool on the assembly board (<see cref="BuildRules"/>: a dispenser or a packaging): <see cref="Option"/> the
        /// tool, <see cref="StoveFlags"/> the board's byte (who squeezes), <see cref="Name"/> the sauce or packaging, <see cref="Price"/>
        /// its price, and the board's build as clients see it: <see cref="ContentCount"/> layers, <see cref="Mode"/> its mode byte.
        /// </summary>
        public static AimTarget AssemblyTool(int tool, byte boardFlags, string name, long price, int layers, byte buildMode)
        {
            return new AimTarget(AimKind.Station, StationKind.Assembly, VesselKind.Assembly, layers, layers > 0 ? "the build" : null, tool,
                boardFlags, name, price, PlaceSpots.None, false, false, buildMode);
        }

        /// <summary>
        /// Spec 033: a tool of the drink dispenser (<see cref="DrinkRules"/>: a tap, the cup stack or the lid stack): <see cref="Option"/>
        /// the tool, <see cref="StoveFlags"/> the dispenser's byte (who pours, the spill; the interactor clears its own pour's bit),
        /// <see cref="Name"/> the tap's drink, <see cref="Price"/> the price shown (a cup with a full pour of the drink, or -1).
        /// </summary>
        public static AimTarget DrinkTool(int tool, byte flags, string name, long price) => DrinkTool(tool, flags, name, price, DrinkRules.NoSpotCup);

        /// <summary>
        /// Spec 042: <see cref="DrinkTool(int, byte, string, long)"/> with the cup on a spot as this client sees it in <see cref="Mode"/>
        /// (DrinkRules.SpotByte): for a tap, the cup on that tap's spot; for the lid stack, the first filled open cup on a spot.
        /// </summary>
        public static AimTarget DrinkTool(int tool, byte flags, string name, long price, byte spotCup)
        {
            return new AimTarget(AimKind.Station, StationKind.DrinkDispenser, default, 0, null, tool, flags, name, price, PlaceSpots.None,
                false, false, spotCup);
        }

        /// <summary>
        /// Spec 042: a tap's cup spot on the drip tray (a one-spot place surface): <see cref="Station"/> the drink dispenser,
        /// <see cref="Option"/> the tap, <see cref="SpotIndex"/> its spot (PlaceSpots.None when a cup stands there), <see cref="Name"/>
        /// the tap's drink.
        /// </summary>
        public static AimTarget CupSpot(int spotIndex, int tap)
        {
            return new AimTarget(AimKind.Spot, StationKind.DrinkDispenser, default, 0, null, tap, 0, DrinkRules.DrinkName(tap), -1,
                spotIndex < 0 ? PlaceSpots.None : spotIndex, false, false);
        }

        /// <summary>Spec 042: a cup spot target (<see cref="CupSpot"/>).</summary>
        public bool IsCupSpot => Kind == AimKind.Spot && Station == StationKind.DrinkDispenser;

        /// <summary>
        /// Spec 042: a cup standing on a tap's spot that another player pours into (<see cref="IsBusy"/>): it cannot be taken until the pour
        /// ends (the server refuses it too).
        /// </summary>
        public static AimTarget BusyCup(string name)
        {
            return new AimTarget(AimKind.Pickup, StationKind.DrinkDispenser, default, 0, null, BusyOption, 0, name, -1, PlaceSpots.None, false, false);
        }

        /// <summary>042 review: the option a <see cref="BusyCup"/> target carries (a pickup has no option otherwise).</summary>
        public const int BusyOption = 1;

        /// <summary>Spec 042: a pickup someone else is using (<see cref="BusyCup"/>).</summary>
        public bool IsBusy => Kind == AimKind.Pickup && Station == StationKind.DrinkDispenser && Option == BusyOption;

        /// <summary>
        /// Spec 032: a slot of the register (Shop.RegisterRules: <see cref="Option"/> the slot), with what clients see of the payment:
        /// the head payer's packed short in <see cref="Price"/> (RegisterRules.PackPayment) and the change tray's sum in
        /// <see cref="ContentCount"/> (as this player sees it, its own clicks first).
        /// </summary>
        public static AimTarget RegisterSlot(int option, ushort payment, int tray)
        {
            return new AimTarget(AimKind.Station, StationKind.Register, default, tray, null, option, 0, "the register", payment,
                PlaceSpots.None, false, false);
        }

        /// <summary>A free item that is no cookware (food, the knife, a box).</summary>
        public static AimTarget Item(string name)
        {
            return new AimTarget(AimKind.Pickup, default, default, 0, null, 0, 0, name, -1, PlaceSpots.None, false, false);
        }

        /// <summary>Spec 021: cookware lying somewhere, with <paramref name="contentCount"/> things in it.</summary>
        public static AimTarget Cookware(VesselKind kind, int contentCount, string contentName, byte mode = 0)
        {
            return new AimTarget(AimKind.Vessel, default, kind, contentCount, contentName, 0, 0, "the " + VesselRules.Name(kind), -1,
                PlaceSpots.None, false, false, mode);
        }

        /// <summary>A place surface; <paramref name="spotIndex"/> is the chosen free spot or PlaceSpots.None (full).</summary>
        public static AimTarget Spot(int spotIndex, bool burner = false)
        {
            return new AimTarget(AimKind.Spot, default, default, 0, null, 0, 0, null, -1, spotIndex < 0 ? PlaceSpots.None : spotIndex,
                burner, false);
        }
    }
}
