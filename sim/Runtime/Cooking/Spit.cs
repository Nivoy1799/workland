using System;
using System.Collections.Generic;

namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 036: the seitan spit's sim rules, pure and deterministic. A cone of seitan turns in front of a heat panel; only its outer
    /// layer cooks, along <see cref="OuterCurve"/> (high heat; low heat cooks at <see cref="LowRate"/>). The heat op <c>shave</c> at the
    /// station "spit" takes the cone's meat (a base seitan item) and the outer layer's heat time (<c>time</c>, sim seconds at high heat)
    /// and gives one portion of "shaved seitan" with that doneness. A shave of the server-measured hold gives
    /// <see cref="PortionsFor"/> portions (one per second held, in steps); after it the new outer layer starts at
    /// <see cref="ResetShareOfGood"/> of the good level (never more cooked than the layer it replaced, so shaving again never helps).
    /// The cone holds <see cref="PortionsPerCone"/> portions and then runs out (<see cref="SpitCone"/>).
    /// </summary>
    public static class Spit
    {
        public const string OperationId = "shave";
        public const string Station = "spit";
        /// <summary>The meat on the cone: the base seitan item (the only input the op takes).</summary>
        public const string MeatId = "seitan";
        /// <summary>The fixed temperature of the heat panel's face (C; never shown, D21).</summary>
        public const float PanelTempC = 230f;

        // The spit timings (kitchen seconds at high heat: Good, Perfect, Burnt) live in the one pace table, CookingPace (spec 043).
        public static readonly FryCurve OuterCurve = CookingPace.SpitOuter;

        /// <summary>Portions on a fresh cone (spec 036 default to review: about 30, 60 g each).</summary>
        public const int PortionsPerCone = 30;
        /// <summary>The most portions one hold shaves (the catch tray holds this many).</summary>
        public const int MaxPortionsPerShave = 3;
        /// <summary>A hold shorter than this shaves nothing; from here each second held is one more portion (rounded).</summary>
        public const float MinShaveSeconds = 0.5f;
        /// <summary>One portion per this many seconds held.</summary>
        public const float SecondsPerPortion = 1f;
        /// <summary>The server stops a hold here (the most portions).</summary>
        public const float MaxShaveSeconds = MaxPortionsPerShave * SecondsPerPortion;
        /// <summary>After a shave the new outer layer starts at this share of the good level's heat time.</summary>
        public const float ResetShareOfGood = 0.5f;
        /// <summary>The longest heat time an outer layer may have (sim seconds; the op's range).</summary>
        public const float MaxLayerSeconds = 1800f;

        /// <summary>The heat knob: off, low, high.</summary>
        public const int Off = 0;
        public const int Low = 1;
        public const int High = 2;
        public const int Levels = 3;
        /// <summary>Low heat cooks the outer layer at this share of high.</summary>
        public const float LowRate = 0.5f;

        /// <summary>The outer layer's sim seconds after a shave (<see cref="ResetShareOfGood"/> × the good level).</summary>
        public static float ResetSeconds => ResetShareOfGood * OuterCurve.WindowMinAt;

        /// <summary>How fast heat level <paramref name="level"/> cooks the outer layer (0 off, <see cref="LowRate"/>, 1 high).</summary>
        public static float RateOf(int level) => level == High ? 1f : level == Low ? LowRate : 0f;

        /// <summary>The knob's next level (off, low, high, off).</summary>
        public static int NextLevel(int level) => level == Off ? Low : level == Low ? High : Off;

        /// <summary>
        /// Portions a hold of <paramref name="seconds"/> (server-measured) shaves: 0 below <see cref="MinShaveSeconds"/>, else one per
        /// second rounded (0.5–1.5 s one, 1.5–2.5 s two, from 2.5 s three), at most <see cref="MaxPortionsPerShave"/>.
        /// </summary>
        public static int PortionsFor(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < MinShaveSeconds - 1e-6) return 0;
            int n = (int)Math.Floor(seconds / SecondsPerPortion + 0.5 + 1e-6);
            return Math.Max(1, Math.Min(MaxPortionsPerShave, n));
        }

        /// <summary>The op's parameters for an outer layer that has had <paramref name="seconds"/> sim seconds of high heat.</summary>
        public static IReadOnlyDictionary<string, ParamValue> ShaveParams(float seconds)
        {
            float s = float.IsNaN(seconds) || seconds < 0f ? 0f : Math.Min(seconds, MaxLayerSeconds);
            return new Dictionary<string, ParamValue> { ["time"] = ParamValue.Number(s) };
        }

        /// <summary>The op's only input: the cone's meat, a base seitan item (never shaved seitan again, nothing cut or cooked).</summary>
        public static bool IsConeMeat(Item? input) =>
            input != null && input.OperationId == null && string.Equals(input.BaseId, MeatId, StringComparison.Ordinal);

        /// <summary>
        /// Spec 036: what a guest's review says about the seitan in its build: shaved off the spit or grilled strips from the griddle (both
        /// fit the seitan wrap and the seitan flatbread); null for a build without seitan, or anything else.
        /// </summary>
        public static string? ReviewNote(Item? served)
        {
            foreach (Item layer in Assembly.LayersOf(served))
            {
                if (!string.Equals(Assembly.BaseOf(layer), MeatId, StringComparison.Ordinal)) continue;
                string? op = Assembly.HeatOpOf(layer);
                if (string.Equals(op, OperationId, StringComparison.Ordinal)) return "The seitan was shaved fresh off the spit.";
                if (string.Equals(op, Griddle.OperationId, StringComparison.Ordinal)) return "The seitan came as grilled strips.";
            }
            return null;
        }
    }
}
