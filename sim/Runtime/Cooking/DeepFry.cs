using System;
using System.Collections.Generic;

namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 029: the deep fryer's sim rules, pure and deterministic. The <c>deep_fry</c> heat op cooks each component along its
    /// <see cref="FryCurve"/> (time scaled by the oil temperature), shapes the input (potato sticks to fries, a falafel mix
    /// portion to four balls), and carries the shake bonus and the greasy penalty in the item's quality factor.
    /// </summary>
    public static class DeepFry
    {
        public const string OperationId = "deep_fry";

        /// <summary>The fryer's oil temperature (C); the curves are timed at it.</summary>
        public const float ReferenceTempC = 180f;
        /// <summary>Sim seconds per kitchen second (the game's CookingTime.TimeScale; a game test keeps them equal).</summary>
        public const float KitchenTimeScale = 6f;
        /// <summary>Burnt: this much doneness past the window's maximum (the game's 027c burn margin).</summary>
        public const float BurnMargin = 0.1f;

        // The fry timings (kitchen seconds: Good, Perfect, Burnt) live in the one pace table, CookingPace (spec 043).
        public static readonly FryCurve Fries = CookingPace.Fries;
        public static readonly FryCurve Falafel = CookingPace.Falafel;

        /// <summary>Spec 029 defaults to review: the even-fry bonus of one shake in the good window, and the greasy penalty.</summary>
        public const float ShakeBonus = 0.10f;
        public const float GreasyPenalty = 0.10f;

        public const string ShakeParam = "shake";
        public const string NotShaken = "none";
        public const string Shaken = "shaken";
        public const string DrainParam = "drain";
        public const string Drained = "drained";
        public const string Greasy = "greasy";

        /// <summary>The curves of the default <c>deep_fry</c> op (anything else fries like potato).</summary>
        public static readonly DeepFryProfile DefaultProfile = new DeepFryProfile(new[] { Fries, Falafel }, Fries);

        /// <summary>What the basket shapes an input into, in order (first match wins).</summary>
        public static readonly IReadOnlyList<DeepFryShape> Shapes = Array.AsReadOnly(new[]
        {
            new DeepFryShape("potato", "sticks", "fries", 1),
            new DeepFryShape("falafel_mix", null, "falafel balls", 4),
        });

        /// <summary>
        /// The shape of a single-ingredient input (its one component's base, its last cut); false for anything else (it keeps
        /// its own name, "deep-fried ...").
        /// </summary>
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

        /// <summary>Seconds at <see cref="ReferenceTempC"/> that <paramref name="seconds"/> at <paramref name="tempC"/> are worth.</summary>
        public static float EffectiveSeconds(float seconds, float tempC)
        {
            float rate = Math.Max(0f, tempC - 60f) / (ReferenceTempC - 60f);
            return Math.Max(0f, seconds) * rate;
        }

        /// <summary>
        /// The doneness along <paramref name="curve"/> after <paramref name="seconds"/> (at the reference temperature): 0 to the
        /// window's minimum at <see cref="FryCurve.WindowMinAt"/>, to its maximum at <see cref="FryCurve.WindowMaxAt"/>, to
        /// the maximum plus <see cref="BurnMargin"/> at <see cref="FryCurve.BurntAt"/>, then on at that rate; clamped to 0..1.
        /// </summary>
        public static float DonenessAt(FryCurve curve, DonenessWindow window, float seconds)
        {
            float t = Math.Max(0f, seconds);
            float d;
            if (t <= curve.WindowMinAt) d = Lerp(0f, window.Min, t / curve.WindowMinAt);
            else if (t <= curve.WindowMaxAt) d = Lerp(window.Min, window.Max, (t - curve.WindowMinAt) / (curve.WindowMaxAt - curve.WindowMinAt));
            else d = window.Max + BurnMargin * (t - curve.WindowMaxAt) / (curve.BurntAt - curve.WindowMaxAt);
            return ItemCanonical.Clamp01(d);
        }

        /// <summary>
        /// A component that starts at <paramref name="start"/> doneness (food that was cooked before) and fries for
        /// <paramref name="seconds"/> more: it continues along the curve from the time that gives its start. Never decreases.
        /// </summary>
        public static float Doneness(FryCurve curve, DonenessWindow window, float start, float seconds)
        {
            float from = TimeOf(curve, window, ItemCanonical.Clamp01(start));
            return Math.Max(ItemCanonical.Clamp01(start), DonenessAt(curve, window, from + Math.Max(0f, seconds)));
        }

        /// <summary>The first time on the curve with at least <paramref name="doneness"/> (0 for 0 or a flat start).</summary>
        private static float TimeOf(FryCurve curve, DonenessWindow window, float doneness)
        {
            if (doneness <= 0f) return 0f;
            if (doneness <= window.Min)
                return window.Min <= 0f ? 0f : curve.WindowMinAt * doneness / window.Min;
            if (doneness <= window.Max)
                return window.Max <= window.Min
                    ? curve.WindowMinAt
                    : curve.WindowMinAt + (curve.WindowMaxAt - curve.WindowMinAt) * (doneness - window.Min) / (window.Max - window.Min);
            return curve.WindowMaxAt + (curve.BurntAt - curve.WindowMaxAt) * (doneness - window.Max) / BurnMargin;
        }

        /// <summary>The quality factor the shake and drain choices give (1.1 shaken, 0.9 greasy, 0.99 both).</summary>
        public static float FactorOf(bool shaken, bool greasy)
        {
            return (shaken ? 1f + ShakeBonus : 1f) * (greasy ? 1f - GreasyPenalty : 1f);
        }

        /// <summary>
        /// Spec 029b: the quality factor of food fried once more. The input's factor times this fry's choices, capped to
        /// 1 - <see cref="GreasyPenalty"/> .. 1 + <see cref="ShakeBonus"/>, so re-frying and shaking again never stacks the
        /// bonus (and greasy twice never stacks the penalty). Every single fry is inside the cap, so it rates as before.
        /// </summary>
        public static float FactorAfter(float inputFactor, bool shaken, bool greasy)
        {
            float f = inputFactor * FactorOf(shaken, greasy);
            if (f > 1f + ShakeBonus) return 1f + ShakeBonus;
            if (f < 1f - GreasyPenalty) return 1f - GreasyPenalty;
            return f;
        }

        private static float Lerp(float a, float b, float t)
        {
            if (!(t > 0f)) return a;
            if (t >= 1f) return b;
            return a + (b - a) * t;
        }
    }
}
