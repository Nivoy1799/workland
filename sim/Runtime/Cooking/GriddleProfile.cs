using System;
using System.Collections.Generic;

namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 030: the griddle part of a heat op's <see cref="HeatProfile"/>: one doneness curve per ingredient for ONE side (an
    /// ingredient without one uses <see cref="Fallback"/>), and whether the op toasts (bread: "toasted", else "grilled").
    /// The curves are <see cref="FryCurve"/>s (the same piecewise-linear doneness shape as the fryer's). Immutable.
    /// </summary>
    public sealed class GriddleProfile
    {
        public GriddleProfile(IReadOnlyList<FryCurve> curves, FryCurve fallback, bool toasts)
        {
            if (curves == null) throw new ArgumentNullException(nameof(curves));
            Fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
            Curves = Array.AsReadOnly(new List<FryCurve>(curves).ToArray());
            Toasts = toasts;
        }

        public IReadOnlyList<FryCurve> Curves { get; }
        public FryCurve Fallback { get; }
        /// <summary>True for the bread op ("toasted ..."), false for the grill op ("grilled ...").</summary>
        public bool Toasts { get; }

        /// <summary>The curve of <paramref name="baseId"/>, else <see cref="Fallback"/>.</summary>
        public FryCurve CurveFor(string baseId)
        {
            foreach (FryCurve c in Curves)
                if (string.Equals(c.BaseId, baseId, StringComparison.Ordinal)) return c;
            return Fallback;
        }
    }
}
