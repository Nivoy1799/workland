using System;
using System.Collections.Generic;

namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 029: the deep fry part of a heat op's <see cref="HeatProfile"/>: one <see cref="FryCurve"/> per ingredient (an
    /// ingredient without one uses <see cref="Fallback"/>). Immutable.
    /// </summary>
    public sealed class DeepFryProfile
    {
        public DeepFryProfile(IReadOnlyList<FryCurve> curves, FryCurve fallback)
        {
            if (curves == null) throw new ArgumentNullException(nameof(curves));
            Fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
            Curves = Array.AsReadOnly(new List<FryCurve>(curves).ToArray());
        }

        public IReadOnlyList<FryCurve> Curves { get; }
        public FryCurve Fallback { get; }

        /// <summary>The curve of <paramref name="baseId"/>, else <see cref="Fallback"/>.</summary>
        public FryCurve CurveFor(string baseId)
        {
            foreach (FryCurve c in Curves)
                if (string.Equals(c.BaseId, baseId, StringComparison.Ordinal)) return c;
            return Fallback;
        }
    }
}
