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

        private AimTarget(AimKind kind, StationKind station, VesselKind vessel, int contentCount, string contentName, int option,
            byte stoveFlags, string name, long price, int spotIndex, bool burner, bool tooFar, byte mode = 0)
        {
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
            return new AimTarget(Kind, Station, Vessel, ContentCount, ContentName, Option, StoveFlags, Name, Price, SpotIndex, IsBurner, tooFar, Mode);
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
        public static AimTarget DrinkTool(int tool, byte flags, string name, long price)
        {
            return new AimTarget(AimKind.Station, StationKind.DrinkDispenser, default, 0, null, tool, flags, name, price, PlaceSpots.None,
                false, false);
        }

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
