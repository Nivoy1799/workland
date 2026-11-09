using Worktown.Sim.Cooking;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 033, pure: the drink dispenser's controls and its synced byte, shared by the server (KitchenState, DrinkDesk) and clients
    /// (prompts, the stream, the cup's level). The dispenser has <see cref="ToolCount"/> tools, each a slot of the station
    /// (<see cref="StockSlot"/>, slot option = tool): three taps (lemonade, iced tea, soda), the cup stack and the lid stack.
    /// Requests (RequestStation, no new RPC), exactly like the 031 sauce dispenser: a tap's <see cref="PourStartOption"/> on press and
    /// <see cref="PourStopOption"/> on release (the server measures the time between their arrivals with its own ticks; the cup in the
    /// hands fills at <see cref="Drinks.PourRate"/>, the server stops it at <see cref="Drinks.MaxMl"/>); the cup stack's
    /// <see cref="CupOption"/> (empty hands take a cup) and the lid stack's <see cref="LidOption"/> (a filled cup in the hands gets
    /// its lid). The station's StationContents byte: a pouring bit per tap and the spill bit (<see cref="SpillBit"/>, set by a pour
    /// over the brim; spec 035's cleaning will clear it). A cup's coarse look: <see cref="FoodLook.KindCup"/> or
    /// <see cref="FoodLook.KindLidded"/>, the drink in the ingredient byte (<see cref="DrinkByte"/>), the level bucket in the shape
    /// byte (<see cref="LevelOf(string)"/>). Millilitres never leave the server (D21); the pouring player predicts its own.
    /// </summary>
    public static class DrinkRules
    {
        public const int TapCount = 3;
        public const int CupTool = TapCount;
        public const int LidTool = TapCount + 1;
        public const int ToolCount = TapCount + 2;

        /// <summary>Station options: press 1..3, release 4..6, take a cup 7, put a lid on 8.</summary>
        public static int PourStartOption(int tap) => 1 + tap;
        public static int PourStopOption(int tap) => 1 + TapCount + tap;
        public const int CupOption = 1 + 2 * TapCount;
        public const int LidOption = CupOption + 1;

        /// <summary>The station byte's spill bit (above the three pouring bits).</summary>
        public const int SpillBit = 1 << TapCount;

        /// <summary>The empty cups that may lie about at once (a free cup is charged with its first pour; this caps clutter).</summary>
        public const int MaxEmptyCups = 6;

        /// <summary>Cup levels in the look's shape byte.</summary>
        public const byte LevelEmpty = 0, LevelLow = 1, LevelRight = 2, LevelFull = 3, LevelSpilled = 4;

        /// <summary>A tool slot is a tap (0..2), the cup stack (3) or the lid stack (4).</summary>
        public static bool IsTap(int tool) => tool >= 0 && tool < TapCount;

        /// <summary>The drink base id of tap <paramref name="tap"/> (lemonade, iced tea, soda), or null.</summary>
        public static string DrinkOf(int tap) => IsTap(tap) ? Drinks.All[tap].Id : null;

        /// <summary>Player words for tap <paramref name="tap"/>'s drink ("iced tea"), or "drink".</summary>
        public static string DrinkName(int tap) => IsTap(tap) ? Drinks.All[tap].DisplayName : "drink";

        /// <summary>The look's drink byte: 0 none (an empty cup), 1 + the tap of the drink.</summary>
        public static byte DrinkByte(string drinkId)
        {
            int tap = Drinks.IndexOf(drinkId);
            return tap < 0 ? (byte)0 : (byte)(1 + tap);
        }

        /// <summary>The tap a look's drink byte names, or -1 (none).</summary>
        public static int TapOfDrinkByte(byte drink) => drink >= 1 && drink <= TapCount ? drink - 1 : -1;

        /// <summary>Reads a station option back: a pour start or stop of tap <paramref name="tap"/>.</summary>
        public static bool TryParsePour(int option, out int tap, out bool start)
        {
            start = option >= 1 && option < 1 + TapCount;
            tap = start ? option - 1 : option - 1 - TapCount;
            if (option >= 1 && tap >= 0 && tap < TapCount) return true;
            tap = -1;
            start = false;
            return false;
        }

        /// <summary>The station option a click on the cup stack or the lid stack sends; -1 for a tap (held, not clicked).</summary>
        public static int ClickOption(int tool) => tool == CupTool ? CupOption : tool == LidTool ? LidOption : -1;

        // ---- the station byte ----

        public static byte WithPouring(byte flags, int tap, bool on)
        {
            if (!IsTap(tap)) return flags;
            int bit = 1 << tap;
            return (byte)(on ? flags | bit : flags & ~bit);
        }

        public static bool IsPouring(byte flags, int tap) => IsTap(tap) && (flags & (1 << tap)) != 0;

        public static byte WithSpill(byte flags, bool on) => (byte)(on ? flags | SpillBit : flags & ~SpillBit);

        public static bool HasSpill(byte flags) => (flags & SpillBit) != 0;

        // ---- levels ----

        /// <summary>The look's level of a fill bucket (low, right, full, spilled); empty for anything else.</summary>
        public static byte LevelOf(string bucket)
        {
            switch (bucket)
            {
                case Drinks.Low: return LevelLow;
                case Drinks.Right: return LevelRight;
                case Drinks.Full: return LevelFull;
                case Drinks.Spilled: return LevelSpilled;
                default: return LevelEmpty;
            }
        }

        /// <summary>The look's level of <paramref name="ml"/> in a cup (0 is empty).</summary>
        public static byte LevelOfMl(float ml) => ml <= 0f ? LevelEmpty : LevelOf(Drinks.BucketOf(ml));

        /// <summary>The drawn height of the liquid (fraction of the cup) for exactly <paramref name="ml"/> (the pouring player's own).</summary>
        public static float DrawnFill(float ml)
        {
            float x = ml / Drinks.CupMl;
            return x <= 0f ? 0f : x >= 1f ? 1f : x;
        }

        /// <summary>The drawn height for a level bucket, as everyone else sees it (a low cup is drawn half full).</summary>
        public static float DrawnFill(byte level)
        {
            switch (level)
            {
                case LevelLow: return 0.55f;
                case LevelRight: return DrawnFill((Drinks.RightFrom + Drinks.FullFrom) * 0.5f);
                case LevelFull: return 0.97f;
                case LevelSpilled: return 1f;
                default: return 0f;
            }
        }

        /// <summary>Where the fill line is drawn (fraction of the cup's height): the middle of the right window.</summary>
        public static float FillLine => DrawnFill(LevelRight);

        // ---- pours ----

        /// <summary>The ml a tap pours between two ticks at <paramref name="tickRate"/> (<see cref="Drinks.PourRate"/>), never below 0.</summary>
        public static float PourMl(uint startTick, uint stopTick, double tickRate)
        {
            if (!(tickRate > 0d)) return 0f;
            double s = CookingTime.ElapsedTicks(startTick, stopTick) / tickRate;
            return (float)System.Math.Max(0d, s * Drinks.PourRate);
        }

        /// <summary>The ml in the cup after pouring <paramref name="poured"/> onto <paramref name="before"/>: capped at the server's stop.</summary>
        public static float Total(float before, float poured) => Drinks.ClampMl(System.Math.Max(0f, before) + System.Math.Max(0f, poured));

        /// <summary>The pouring player's predicted ml <paramref name="seconds"/> after its press, onto <paramref name="before"/> (capped).</summary>
        public static float PredictedMl(float before, double seconds) =>
            Total(before, seconds > 0d ? (float)(seconds * Drinks.PourRate) : 0f);

        /// <summary>
        /// What the pouring player assumes is already in its cup before a top-up, from the level it sees (it never knows the exact
        /// ml): the bucket's lower bound (an empty cup 0, a low cup a third).
        /// </summary>
        public static float SeenMl(byte level)
        {
            switch (level)
            {
                case LevelLow: return Drinks.CupMl / 3f;
                case LevelRight: return Drinks.RightFrom;
                case LevelFull: return Drinks.FullFrom;
                case LevelSpilled: return Drinks.MaxMl;
                default: return 0f;
            }
        }

        // ---- what a client may offer (the server checks again) ----

        /// <summary>
        /// Whether tap <paramref name="tap"/> can pour into what the hands hold, as clients see it: a cup (empty, or open with the same
        /// drink and room left), and the tap free (<paramref name="flags"/>; the interactor clears its own pour's bit).
        /// </summary>
        public static CookOutcome CanPour(bool holdsCup, bool lidded, int cupTap, byte level, int tap, byte flags)
        {
            if (!IsTap(tap)) return CookOutcome.InvalidOption;
            if (!holdsCup) return CookOutcome.NeedsCup;
            if (lidded) return CookOutcome.CupClosed;
            if (level != LevelEmpty && cupTap != tap) return CookOutcome.OtherDrink;
            if (level >= LevelSpilled) return CookOutcome.CupFull;
            return IsPouring(flags, tap) ? CookOutcome.DispenserBusy : CookOutcome.Ok;
        }

        /// <summary>Whether a lid fits what the hands hold: an open cup with a drink in it.</summary>
        public static CookOutcome CanLid(bool holdsCup, bool lidded, byte level)
        {
            if (!holdsCup) return CookOutcome.NeedsCup;
            if (lidded) return CookOutcome.CupClosed;
            return level == LevelEmpty ? CookOutcome.NotFilled : CookOutcome.Ok;
        }

        /// <summary>Whether empty hands can take a cup (anything held: HandsFull).</summary>
        public static CookOutcome CanTakeCup(bool holding) => holding ? CookOutcome.HandsFull : CookOutcome.Ok;
    }
}
