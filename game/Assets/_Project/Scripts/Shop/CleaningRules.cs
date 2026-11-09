using Worktown.Game.Cooking;
using Worktown.Sim.Economy;

namespace Worktown.Game.Shop
{
    /// <summary>
    /// Spec 035, pure: the cleaning stations' controls and their synced bytes, shared by the server (CleaningRoom, CleaningService) and
    /// clients (prompts, the looks). Requests go through the existing RequestStation (no new RPC):
    /// - the dish return: <see cref="TakeDirtyOption"/> (empty hands take the oldest dirty dish);
    /// - the sink: <see cref="SinkPutOption"/> (the dirty dish in the hands goes into the basin), <see cref="ScrubStartOption"/> on
    ///   press and <see cref="ScrubStopOption"/> on release (the server measures the time between their arrivals in its own ticks; a
    ///   scrub of <see cref="ScrubSeconds"/> washes the oldest dish in the basin, the server stops it there);
    /// - the trash can: <see cref="TrashOption"/> (what the hands hold goes in, or empty hands take the bag out);
    /// - the bin outside: <see cref="BinOption"/> (the bag in the hands goes in);
    /// - the drink dispenser's spill: <see cref="WipeStartOption"/> / <see cref="WipeStopOption"/> with the cloth in the hands (a wipe of
    ///   <see cref="WipeSeconds"/> clears it, DrinkRules.SpillBit).
    /// The stations' StationContents byte: the dish return's dirty count, the sink's basin count and its scrubbing bit, the trash can's
    /// fill, the plate shelf's clean plates. The shop's score is one more byte (ShopHygiene).
    /// </summary>
    public static class CleaningRules
    {
        public const int TakeDirtyOption = 0;
        public const int SinkPutOption = 0;
        public const int ScrubStartOption = 1;
        public const int ScrubStopOption = 2;
        public const int TrashOption = 0;
        public const int BinOption = 0;
        /// <summary>The drink dispenser's options after its own (taps 1-6, cups 7, lids 8).</summary>
        public const int WipeStartOption = DrinkRules.LidOption + 1;
        public const int WipeStopOption = DrinkRules.LidOption + 2;
        /// <summary>The drink dispenser's spill slot (after its five tools): the puddle the cloth is held on.</summary>
        public const int SpillTool = DrinkRules.ToolCount;
        /// <summary>The sink's one slot: the basin.</summary>
        public const int BasinTool = 0;

        /// <summary>Spec 035: one dish is scrubbed in this many seconds of holding (server-timed, like the dispensers).</summary>
        public const float ScrubSeconds = 1.5f;
        /// <summary>Spec 035 default to review (the spec says "holding it on the spill clears it"): a wipe takes this long.</summary>
        public const float WipeSeconds = 1.0f;
        /// <summary>Dirty dishes the sink holds at once.</summary>
        public const int BasinCapacity = 3;
        /// <summary>
        /// Spec 035: the shop owns this many plates (finite: spec 034 made no stock line of them, so the plates are counted here): the
        /// two on the plating worktop at the start, the rest clean on the plate shelf. Without washing they run out.
        /// </summary>
        public const int TotalPlates = VesselRules.MaxPlatesInWorld;
        /// <summary>Plates lying on the plating worktop when the scene starts (GrayboxSceneBuilder: "Plate 1", "Plate 2").</summary>
        public const int ScenePlates = 2;
        /// <summary>How long (game minutes) an eat-in guest eats before its plate comes back dirty to the dish return.</summary>
        public const int EatMinutes = 20;

        /// <summary>The hold kinds (part of the server's hold key).</summary>
        public const int HoldScrub = 1;
        public const int HoldWipe = 2;

        public static float HoldSeconds(int kind) => kind == HoldWipe ? WipeSeconds : ScrubSeconds;

        public static bool IsWipeOption(int option) => option == WipeStartOption || option == WipeStopOption;
        public static bool IsScrubOption(int option) => option == ScrubStartOption || option == ScrubStopOption;

        /// <summary>
        /// Whether option <paramref name="option"/> on a station of <paramref name="kind"/> needs the player in reach: everything but the
        /// release of a scrub or a wipe (the press was in reach).
        /// </summary>
        public static bool NeedsReach(StationKind kind, int option) =>
            !((kind == StationKind.Sink && option == ScrubStopOption) || (kind == StationKind.DrinkDispenser && option == WipeStopOption));

        /// <summary>Whether <paramref name="kind"/> is one of the spec 035 stations the cleaning service answers.</summary>
        public static bool IsCleaningStation(StationKind kind) =>
            kind == StationKind.DishReturn || kind == StationKind.Sink || kind == StationKind.TrashCan || kind == StationKind.Bin;

        // ---- the bytes ----

        public const int ScrubbingBit = 1 << 3;

        /// <summary>The drink dispenser's byte: someone wipes its spill (above DrinkRules' pouring and spill bits; onlookers' cloth look).</summary>
        public const int WipingBit = DrinkRules.SpillBit << 1;

        public static bool IsWiping(byte dispenserFlags) => (dispenserFlags & WipingBit) != 0;

        /// <summary>The sink's byte: the dishes in the basin (low 3 bits) and whether someone scrubs.</summary>
        public static byte SinkByte(int basin, bool scrubbing) =>
            (byte)((basin < 0 ? 0 : basin > 7 ? 7 : basin) | (scrubbing ? ScrubbingBit : 0));

        public static int BasinOf(byte sink) => sink & 0x07;
        public static bool IsScrubbing(byte sink) => (sink & ScrubbingBit) != 0;

        /// <summary>A count as a byte (clamped).</summary>
        public static byte CountByte(int count) => (byte)(count < 0 ? 0 : count > 255 ? 255 : count);

        // ---- what a client may offer (the server checks again) ----

        public static CookOutcome CanTakeDirty(bool holding, int atReturn)
        {
            if (holding) return CookOutcome.HandsFull;
            return atReturn > 0 ? CookOutcome.Ok : CookOutcome.NoDirtyDishes;
        }

        public static CookOutcome CanPutInSink(bool holdsDirty, int basin)
        {
            if (!holdsDirty) return CookOutcome.NotDirty;
            return basin >= BasinCapacity ? CookOutcome.SinkFull : CookOutcome.Ok;
        }

        /// <summary>Empty hands scrub a dish in the basin; <paramref name="busy"/>: someone else scrubs (the bit, not our own).</summary>
        public static CookOutcome CanScrub(bool holding, int basin, bool busy)
        {
            if (holding) return CookOutcome.HandsFull;
            if (basin <= 0) return CookOutcome.SinkEmpty;
            return busy ? CookOutcome.DispenserBusy : CookOutcome.Ok;
        }

        /// <summary>The trash takes what is in the hands, while it is not full.</summary>
        public static CookOutcome CanDiscard(bool discardable, int fill)
        {
            if (!discardable) return CookOutcome.CannotDiscard;
            return HygieneRules.IsTrashFull(fill) ? CookOutcome.TrashFull : CookOutcome.Ok;
        }

        public static CookOutcome CanTakeBag(bool holding, int fill)
        {
            if (holding) return CookOutcome.HandsFull;
            return fill > 0 ? CookOutcome.Ok : CookOutcome.TrashEmpty;
        }

        public static CookOutcome CanBin(bool holdsBag) => holdsBag ? CookOutcome.Ok : CookOutcome.NotABag;

        public static CookOutcome CanWipe(bool holdsCloth, bool spill, bool busy)
        {
            if (!spill) return CookOutcome.NoSpill;
            if (!holdsCloth) return CookOutcome.NoCloth;
            return busy ? CookOutcome.DispenserBusy : CookOutcome.Ok;
        }

        /// <summary>The trash's fill as a share 0..1 (the drawn level, the bag's size).</summary>
        public static float FillShare(int fill) =>
            fill <= 0 ? 0f : fill >= HygieneRules.TrashCapacity ? 1f : fill / (float)HygieneRules.TrashCapacity;

        /// <summary>The scrub's or wipe's progress 0..1 after <paramref name="seconds"/> of holding.</summary>
        public static float HoldShare(int kind, double seconds)
        {
            float total = HoldSeconds(kind);
            if (!(seconds > 0d) || total <= 0f) return 0f;
            return seconds >= total ? 1f : (float)(seconds / total);
        }
    }
}
