using System;
using System.Collections.Generic;

namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 030: the griddle's sim rules, pure and deterministic. Two heat ops at the station "griddle": <c>griddle</c> (seitan,
    /// vegetables) and <c>toast</c> (bread, a short window). Each side of an item cooks on its own along the ingredient's curve
    /// (<see cref="SideDoneness"/>); the item's doneness is their combination (<see cref="CombineSides"/>): inside the window only
    /// when both sides are, else the worse side. The ops take the two sides' sim seconds as <c>time</c> (the shorter side) and
    /// <see cref="LongSideParam"/> (the longer side), so a flipped item is the same item whichever side went down first, and
    /// the menu's "time" bucket means "both sides at least". Build the parameters with <see cref="Params"/>.
    /// </summary>
    public static class Griddle
    {
        public const string OperationId = "griddle";
        public const string ToastOperationId = "toast";
        public const string Station = "griddle";
        /// <summary>The longer-cooked side's sim seconds (the shorter one is the op's "time").</summary>
        public const string LongSideParam = "long_side";
        /// <summary>The griddle plate's fixed temperature (C; players never see it, D21).</summary>
        public const float PlateTempC = 200f;
        /// <summary>The longest time a side may be given (sim seconds).</summary>
        public const float MaxSideSeconds = 600f;

        // ---- THE griddle timings, in kitchen seconds per side: Good, Perfect, Burnt (spec 030 default A "about 2x faster",
        // pending the human's cooking-pace answer). Changing a timing is this one line. ----
        public static readonly FryCurve SeitanSide = FryCurve.FromKitchenSeconds("seitan", 12f, 14f, 22f);
        public static readonly FryCurve PaprikaSide = FryCurve.FromKitchenSeconds("paprika", 10f, 12f, 20f);
        public static readonly FryCurve EggplantSide = FryCurve.FromKitchenSeconds("eggplant", 10f, 12f, 20f);
        public static readonly FryCurve MushroomSide = FryCurve.FromKitchenSeconds("mushroom", 10f, 12f, 20f);
        public static readonly FryCurve FlatbreadSide = FryCurve.FromKitchenSeconds("flatbread", 4f, 5f, 9f);
        public static readonly FryCurve WrapSide = FryCurve.FromKitchenSeconds("wrap", 4f, 5f, 9f);

        /// <summary>The curves of the <c>griddle</c> op (anything else grills like the vegetables).</summary>
        public static readonly GriddleProfile GrillProfile = new GriddleProfile(
            new[] { SeitanSide, PaprikaSide, EggplantSide, MushroomSide }, PaprikaSide, false);
        /// <summary>The curves of the <c>toast</c> op (anything else toasts like flatbread).</summary>
        public static readonly GriddleProfile ToastProfile = new GriddleProfile(new[] { FlatbreadSide, WrapSide }, FlatbreadSide, true);

        /// <summary>The bread the griddle toasts (the <c>toast</c> op) instead of grilling.</summary>
        public static readonly IReadOnlyList<string> Breads = Array.AsReadOnly(new[] { "flatbread", "wrap" });

        /// <summary>
        /// The griddle op for <paramref name="food"/>: <c>toast</c> for a single bread item (flatbread, wrap, also cut), else
        /// <c>griddle</c>.
        /// </summary>
        public static string OperationFor(Item food)
        {
            if (food != null && food.Components.Count == 1)
                foreach (string bread in Breads)
                    if (string.Equals(food.Components[0].BaseId, bread, StringComparison.Ordinal)) return ToastOperationId;
            return OperationId;
        }

        /// <summary>True for the two griddle ops.</summary>
        public static bool IsGriddleOp(string? opId) =>
            string.Equals(opId, OperationId, StringComparison.Ordinal) || string.Equals(opId, ToastOperationId, StringComparison.Ordinal);

        /// <summary>
        /// The op parameters for side A cooked <paramref name="sideA"/> and side B <paramref name="sideB"/> sim seconds: "time" is
        /// the shorter side, <see cref="LongSideParam"/> the longer, each clamped to 0..<see cref="MaxSideSeconds"/>.
        /// </summary>
        public static IReadOnlyDictionary<string, ParamValue> Params(float sideA, float sideB)
        {
            float a = ClampSide(sideA), b = ClampSide(sideB);
            return new Dictionary<string, ParamValue>
            {
                ["time"] = ParamValue.Number(Math.Min(a, b)),
                [LongSideParam] = ParamValue.Number(Math.Max(a, b)),
            };
        }

        /// <summary>The parameters of <paramref name="sides"/>.</summary>
        public static IReadOnlyDictionary<string, ParamValue> Params(GriddleSides sides) => Params(sides.SideA, sides.SideB);

        /// <summary>
        /// The doneness of one side after <paramref name="seconds"/> down, for a component that starts at
        /// <paramref name="start"/>: along <paramref name="curve"/> like the fryer (<see cref="DeepFry.Doneness"/>); never less
        /// than the start.
        /// </summary>
        public static float SideDoneness(FryCurve curve, DonenessWindow window, float start, float seconds) =>
            DeepFry.Doneness(curve, window, start, seconds);

        /// <summary>
        /// The doneness of an item from its two sides: the mean when both are inside the ideal window, else the side further
        /// from the window; on an outside tie the side over the window (the burnt one). So one raw side reads raw, one burnt side
        /// reads burnt, and only two good sides rate as good (ItemRater reads the item's doneness). 030 review: the mean only when
        /// both distances are 0, so a raw side and a burnt side equally far out never average back into the window.
        /// </summary>
        public static float CombineSides(float sideA, float sideB, DonenessWindow window)
        {
            float da = Distance(sideA, window), db = Distance(sideB, window);
            if (da <= 0f && db <= 0f) return ItemCanonical.Clamp01((sideA + sideB) * 0.5f);
            if (da > db) return ItemCanonical.Clamp01(sideA);
            if (db > da) return ItemCanonical.Clamp01(sideB);
            return ItemCanonical.Clamp01(Math.Max(sideA, sideB)); // an outside tie: the over side
        }

        /// <summary>How far a doneness lies outside the window (0 inside).</summary>
        public static float Distance(float doneness, DonenessWindow window) =>
            Math.Max(0f, Math.Max(window.Min - doneness, doneness - window.Max));

        /// <summary>What the griddle calls a cut it grills (first match wins): seitan sticks are "seitan strips".</summary>
        public static readonly IReadOnlyList<DeepFryShape> Shapes = Array.AsReadOnly(new[]
        {
            new DeepFryShape("seitan", "sticks", "seitan strips", 1),
        });

        /// <summary>The griddle's name of a single-ingredient input's cut ("seitan strips"); false for anything else.</summary>
        public static bool TryGetShape(Item input, out DeepFryShape shape)
        {
            shape = null!;
            if (input == null || input.Components.Count != 1) return false;
            string baseId = input.Components[0].BaseId;
            input.Buckets.TryGetValue("shape", out string? cut);
            foreach (DeepFryShape s in Shapes)
            {
                if (!string.Equals(s.BaseId, baseId, StringComparison.Ordinal)) continue;
                if (s.CutShape != null && !string.Equals(s.CutShape, cut, StringComparison.Ordinal)) continue;
                shape = s;
                return true;
            }
            return false;
        }

        /// <summary>
        /// The name template from the two sides' time buckets (ids only, so the name follows the hash): any side long is
        /// "charred {in}"; both short "warm {in}"; one short "half-grilled {in}" ("half-toasted"); else "grilled {in}"
        /// ("toasted"). <paramref name="input"/> replaces {in} when the griddle names the cut (<see cref="TryGetShape"/>).
        /// </summary>
        public static string NameTemplate(string shortSide, string longSide, bool toasts, string? input = null)
        {
            string done = toasts ? "toasted" : "grilled";
            string word;
            if (longSide == "long" || shortSide == "long") word = "charred";
            else if (shortSide == "short") word = longSide == "short" ? "warm" : "half-" + done;
            else word = done;
            return word + " " + (input ?? "{in}");
        }

        private static float ClampSide(float seconds)
        {
            if (float.IsNaN(seconds) || seconds < 0f) return 0f;
            return seconds > MaxSideSeconds ? MaxSideSeconds : seconds;
        }
    }
}
