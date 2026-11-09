using System;
using System.Collections.Generic;

namespace Worktown.Game.Npc
{
    /// <summary>
    /// One ingredient a menu dish needs (spec 022), in terms the sim already produces: the base ingredient, the cut
    /// shape and the heat op with its minimum doneness (time bucket). Pure data, immutable.
    /// </summary>
    public sealed class DishNeed
    {
        /// <summary>Use as <see cref="Shapes"/>: cut in any shape.</summary>
        public static readonly IReadOnlyList<string> AnyCut = Array.Empty<string>();

        public DishNeed(string baseId, IReadOnlyList<string> shapes, IReadOnlyList<string> heatOps, string minTime)
            : this(baseId, shapes, heatOps, minTime, null)
        {
        }

        /// <summary>030 review: an "any other" need limited to <paramref name="anyOf"/> (e.g. only the grill vegetables).</summary>
        public DishNeed(string baseId, IReadOnlyList<string> shapes, IReadOnlyList<string> heatOps, string minTime,
            IReadOnlyList<string> anyOf)
        {
            AnyOf = anyOf;
            BaseId = baseId;
            Shapes = shapes;
            HeatOps = heatOps;
            MinTime = minTime ?? string.Empty;
        }

        /// <summary>Base ingredient id, or null for "any other ingredient" (a base no other need of the dish took).</summary>
        public string BaseId { get; }

        /// <summary>Allowed cut shape bucket ids; <see cref="AnyCut"/> = cut in any shape; null = no cut needed.</summary>
        public IReadOnlyList<string> Shapes { get; }

        /// <summary>Allowed heat op ids (fry, saute, boil); null = no heat needed.</summary>
        public IReadOnlyList<string> HeatOps { get; }

        /// <summary>Lowest time bucket id of the heat op ("short", "medium", "long"); empty = any.</summary>
        public string MinTime { get; }

        /// <summary>030 review: for an "any other" need, the base ingredients that may fill it; null = any.</summary>
        public IReadOnlyList<string> AnyOf { get; }

        public bool NeedsCut => Shapes != null;
        public bool NeedsHeat => HeatOps != null;
    }
}
