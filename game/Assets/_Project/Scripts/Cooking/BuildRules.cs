using System.Collections.Generic;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 031, pure: the assembly board's controls and its synced bytes, shared by the server (KitchenState, KitchenService) and
    /// clients (prompts, the stack, the ribbon). The board has <see cref="ToolCount"/> tools on its top, each a slot of the station
    /// (<see cref="StockSlot"/>, slot option = tool): the two sauce dispensers and the packaging rack (wrap paper, box, bag).
    /// Requests (RequestStation, no new RPC): a dispenser's <see cref="SqueezeStartOption"/> on press and <see cref="SqueezeStopOption"/>
    /// on release (the server measures the time between their arrivals with its own ticks); a packaging's <see cref="PackagingOption"/>.
    /// The station's StationContents byte: a squeezing bit per dispenser. The build spot's Vessel.Mode byte: how it is closed and
    /// packed and whether it lies on bread (<see cref="Mode"/>). Times and amounts never leave the server.
    /// </summary>
    public static class BuildRules
    {
        public const int DispenserCount = 2;
        public const int PackagingCount = 3;
        public const int ToolCount = DispenserCount + PackagingCount;
        /// <summary>Packagings (tool index minus the dispensers).</summary>
        public const int Paper = 0, Box = 1, Bag = 2;
        /// <summary>The server stops a squeeze held this long (s), so a lost release never squeezes for ever.</summary>
        public const float AutoStopSeconds = 3f;

        public const int CloseOpen = 0, CloseFolded = 1, CloseWrapped = 2;
        public const int PackNone = 0, PackBox = 1, PackBag = 2;
        private const int CloseMask = 0x03;
        private const int PackShift = 2;
        private const int PackMask = 0x03;
        private const int BreadBit = 0x10;

        /// <summary>The sauce base id of dispenser <paramref name="d"/> (garlic-herb, chili), or null.</summary>
        public static string SauceOf(int d) => d >= 0 && d < DispenserCount ? Assembly.Sauces[d] : null;

        /// <summary>A tool slot is a dispenser (0, 1) or a packaging (2..4).</summary>
        public static bool IsDispenser(int tool) => tool >= 0 && tool < DispenserCount;
        public static bool IsPackaging(int tool) => tool >= DispenserCount && tool < ToolCount;
        public static int PackagingOf(int tool) => IsPackaging(tool) ? tool - DispenserCount : -1;

        /// <summary>Station options: press 1, 2; release 3, 4; packaging 5, 6, 7.</summary>
        public static int SqueezeStartOption(int d) => 1 + d;
        public static int SqueezeStopOption(int d) => 1 + DispenserCount + d;
        public static int PackagingOption(int p) => 1 + 2 * DispenserCount + p;

        /// <summary>Reads a station option back: a squeeze start or stop of dispenser <paramref name="d"/>.</summary>
        public static bool TryParseSqueeze(int option, out int d, out bool start)
        {
            start = option >= 1 && option < 1 + DispenserCount;
            d = start ? option - 1 : option - 1 - DispenserCount;
            if (option >= 1 && d >= 0 && d < DispenserCount) return true;
            d = -1;
            start = false;
            return false;
        }

        /// <summary>Reads a packaging option back (paper, box, bag).</summary>
        public static bool TryParsePackaging(int option, out int p)
        {
            p = option - 1 - 2 * DispenserCount;
            if (p >= 0 && p < PackagingCount) return true;
            p = -1;
            return false;
        }

        /// <summary>The grocer id of a packaging (what it costs; spec 031 defaults to review).</summary>
        public static string PackagingId(int p)
        {
            switch (p)
            {
                case Paper: return "wrap_paper";
                case Box: return "takeaway_box";
                case Bag: return "paper_bag";
                default: return null;
            }
        }

        /// <summary>Player words: "wrap paper", "box", "bag".</summary>
        public static string PackagingName(int p) => p == Paper ? "wrap paper" : p == Box ? "box" : p == Bag ? "bag" : "it";

        /// <summary>The sim step a packaging applies: paper is the close "wrapped", box and bag a pack.</summary>
        public static bool TryStep(int p, out string opId, out IReadOnlyDictionary<string, ParamValue> parameters)
        {
            opId = null;
            parameters = null;
            switch (p)
            {
                case Paper: opId = Assembly.CloseOperationId; parameters = Assembly.CloseParams(Assembly.Wrapped); return true;
                case Box: opId = Assembly.PackOperationId; parameters = Assembly.PackParams(Assembly.Box); return true;
                case Bag: opId = Assembly.PackOperationId; parameters = Assembly.PackParams(Assembly.Bag); return true;
                default: return false;
            }
        }

        // ---- the station byte: who squeezes ----

        public static byte WithSqueezing(byte flags, int d, bool on)
        {
            if (d < 0 || d >= DispenserCount) return flags;
            int bit = 1 << d;
            return (byte)(on ? flags | bit : flags & ~bit);
        }

        public static bool IsSqueezing(byte flags, int d) => d >= 0 && d < DispenserCount && (flags & (1 << d)) != 0;

        // ---- the build spot's mode byte ----

        /// <summary>The build's mode byte: its close state, pack state, and whether its first layer is bread.</summary>
        public static byte Mode(int close, int pack, bool bread) =>
            (byte)((close & CloseMask) | ((pack & PackMask) << PackShift) | (bread ? BreadBit : 0));

        /// <summary>The mode byte of a sim build (0 for none).</summary>
        public static byte ModeOf(Item build)
        {
            if (build == null) return 0;
            string close = Assembly.CloseOf(build), pack = Assembly.PackOf(build);
            return Mode(close == Assembly.Wrapped ? CloseWrapped : close == Assembly.Folded ? CloseFolded : CloseOpen,
                pack == Assembly.Box ? PackBox : pack == Assembly.Bag ? PackBag : PackNone, Assembly.HasBreadBase(build));
        }

        public static int CloseOf(byte mode) => mode & CloseMask;
        public static int PackOf(byte mode) => (mode >> PackShift) & PackMask;
        public static bool OnBread(byte mode) => (mode & BreadBit) != 0;
        /// <summary>Open: more layers can go on and sauce can be squeezed.</summary>
        public static bool IsOpen(byte mode) => CloseOf(mode) == CloseOpen && PackOf(mode) == PackNone;
        /// <summary>Finished: wrapped in paper or packed (sold as it is).</summary>
        public static bool IsFinished(byte mode) => CloseOf(mode) == CloseWrapped || PackOf(mode) != PackNone;

        /// <summary>Empty hands on the build: fold an open build on bread (with something on the bread); else take it.</summary>
        public static bool FoldsByHand(int layers, byte mode) => layers >= 2 && IsOpen(mode) && OnBread(mode);

        /// <summary>
        /// Whether a packaging fits the build as clients see it (the server checks again in the sim): paper on a folded wrap; a bag
        /// or a box on a folded build on bread or on an open build without bread. <see cref="CookOutcome.Ok"/> or why not.
        /// </summary>
        public static CookOutcome CanPackage(int p, int layers, byte mode)
        {
            if (layers <= 0) return CookOutcome.StationEmpty;
            if (IsFinished(mode)) return CookOutcome.BuildClosed;
            bool folded = CloseOf(mode) == CloseFolded;
            bool bread = OnBread(mode);
            switch (p)
            {
                case Paper: return folded ? CookOutcome.Ok : bread ? CookOutcome.FoldFirst : CookOutcome.NothingToFold;
                case Bag:
                case Box: return bread && !folded ? CookOutcome.FoldFirst : CookOutcome.Ok;
                default: return CookOutcome.InvalidOption;
            }
        }

        /// <summary>Whether a squeeze can start as clients see it: a build that is open.</summary>
        public static CookOutcome CanSqueeze(int layers, byte mode)
        {
            if (layers <= 0) return CookOutcome.StationEmpty;
            return IsOpen(mode) ? CookOutcome.Ok : CookOutcome.BuildClosed;
        }

        /// <summary>The seconds between two ticks at <paramref name="tickRate"/>, capped at <see cref="Assembly.MaxSqueezeSeconds"/>.</summary>
        public static float SqueezeSeconds(uint startTick, uint stopTick, double tickRate)
        {
            if (!(tickRate > 0d)) return 0f;
            double s = CookingTime.ElapsedTicks(startTick, stopTick) / tickRate;
            return (float)System.Math.Min(Assembly.MaxSqueezeSeconds, System.Math.Max(0d, s));
        }
    }
}
