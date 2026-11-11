using System;
using System.Collections.Generic;

namespace Worktown.Sim.Climb
{
    /// <summary>Spec 052: one cell of the strategy grid (5 price x 3 spend policies). Id "balanced+match".</summary>
    public sealed record ClimbStrategy(PricePolicy Price, SpendPolicy Spend)
    {
        /// <summary>The reference strategy (Balanced spend, Match price).</summary>
        public static ClimbStrategy Reference { get; } = new ClimbStrategy(PricePolicy.Match, SpendPolicy.Balanced);

        /// <summary>All 15 strategies, spend-major (rush, balanced, builder) then price order.</summary>
        public static IReadOnlyList<ClimbStrategy> All { get; } = BuildAll();

        public string Id => Spend.ToString().ToLowerInvariant() + "+" + Price.ToString().ToLowerInvariant();

        /// <summary>Parses an <see cref="Id"/> ("rush+plus2"); throws <see cref="ArgumentException"/> when unknown.</summary>
        public static ClimbStrategy Parse(string id)
        {
            foreach (ClimbStrategy s in All)
                if (string.Equals(s.Id, id, StringComparison.Ordinal)) return s;
            throw new ArgumentException("Unknown strategy: " + id, nameof(id));
        }

        private static IReadOnlyList<ClimbStrategy> BuildAll()
        {
            var list = new List<ClimbStrategy>();
            foreach (SpendPolicy spend in new[] { SpendPolicy.Rush, SpendPolicy.Balanced, SpendPolicy.Builder })
            foreach (PricePolicy price in new[] { PricePolicy.Minus1, PricePolicy.Match, PricePolicy.Plus1, PricePolicy.Plus2, PricePolicy.Undercut })
                list.Add(new ClimbStrategy(price, spend));
            return list.AsReadOnly();
        }
    }
}
