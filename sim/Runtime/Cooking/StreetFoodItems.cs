using System;
using System.Collections.Generic;

namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 028: the base items of the vegan street-food shop (docs/cooking-roadmap.md). Flavour stats in the existing
    /// stat space, one doneness window each. No animal products anywhere (D27, see <see cref="ContentRules"/>).
    /// </summary>
    public static class StreetFoodItems
    {
        public static readonly BaseItemDef Flatbread = Make("flatbread", "flatbread", 0.10f, 0.10f, 0f, 0f, 0.10f, 0.10f, 0.30f, 0.6f, 0.3f, 0.40f, 0.80f);
        public static readonly BaseItemDef Wrap = Make("wrap", "wrap", 0.10f, 0.10f, 0f, 0f, 0.10f, 0.05f, 0.10f, 0.5f, 0.4f, 0.20f, 0.60f);
        public static readonly BaseItemDef Seitan = Make("seitan", "seitan", 0f, 0.30f, 0f, 0.05f, 0.80f, 0.10f, 0.20f, 0.6f, 0.5f, 0.50f, 0.85f);
        public static readonly BaseItemDef FalafelMix = Make("falafel_mix", "falafel mix", 0.10f, 0.30f, 0.05f, 0.20f, 0.50f, 0.20f, 0.10f, 0.7f, 0.6f, 0.50f, 0.90f);
        public static readonly BaseItemDef Lettuce = Make("lettuce", "lettuce", 0.10f, 0f, 0.05f, 0.20f, 0.05f, 0f, 0.90f, 0.2f, 0.9f, 0f, 0.20f);
        public static readonly BaseItemDef Cucumber = Make("cucumber", "cucumber", 0.10f, 0f, 0.10f, 0.10f, 0f, 0f, 0.80f, 0.2f, 0.95f, 0f, 0.30f);
        public static readonly BaseItemDef RedCabbage = Make("red_cabbage", "red cabbage", 0.20f, 0f, 0.10f, 0.30f, 0.10f, 0f, 0.80f, 0.4f, 0.8f, 0f, 0.50f);
        public static readonly BaseItemDef Lemon = Make("lemon", "lemon", 0.10f, 0f, 0.90f, 0.20f, 0f, 0f, 0.10f, 0.3f, 0.8f, 0f, 0.20f);
        public static readonly BaseItemDef GarlicHerbSauce = Make("garlic_herb_sauce", "garlic-herb sauce", 0.05f, 0.30f, 0.10f, 0.15f, 0.30f, 0.40f, 0f, 0.4f, 0.6f, 0f, 0.40f);
        public static readonly BaseItemDef ChiliSauce = Make("chili_sauce", "chili sauce", 0.20f, 0.30f, 0.30f, 0.10f, 0.20f, 0.10f, 0f, 0.5f, 0.7f, 0f, 0.40f);

        // Spec 030: the grill vegetables (the griddle op times them in Griddle). Not in All: GrillVegetables lists them.
        public static readonly BaseItemDef Paprika = Make("paprika", "paprika", 0.40f, 0f, 0.10f, 0.10f, 0.10f, 0f, 0.70f, 0.4f, 0.85f, 0.30f, 0.70f);
        public static readonly BaseItemDef Eggplant = Make("eggplant", "eggplant", 0.10f, 0f, 0f, 0.20f, 0.20f, 0.05f, 0.30f, 0.5f, 0.85f, 0.40f, 0.80f);
        public static readonly BaseItemDef Mushroom = Make("mushroom", "mushroom", 0.05f, 0.05f, 0f, 0.05f, 0.60f, 0f, 0.20f, 0.5f, 0.85f, 0.30f, 0.70f);

        /// <summary>The ten new crate items in crate order (after the four demo ingredients).</summary>
        public static readonly IReadOnlyList<BaseItemDef> All = Array.AsReadOnly(new[]
        {
            Flatbread, Wrap, Seitan, FalafelMix, Lettuce, Cucumber, RedCabbage, Lemon, GarlicHerbSauce, ChiliSauce
        });

        /// <summary>Spec 030: the three grill vegetables, in stock order after <see cref="All"/>: paprika, eggplant, mushroom.</summary>
        public static readonly IReadOnlyList<BaseItemDef> GrillVegetables = Array.AsReadOnly(new[] { Paprika, Eggplant, Mushroom });

        /// <summary>
        /// The cut bucket id ("halves", "slices", "sticks") the item is cut into, or null when it is not cut (wrap,
        /// falafel mix, sauces). The sim has no "shredded" or "strips" bucket yet: shredding is thin slices, strips are sticks.
        /// </summary>
        public static string? CutShape(string baseId)
        {
            switch (baseId)
            {
                case "flatbread":
                case "lemon":
                case "mushroom": // spec 030
                    return "halves";
                case "seitan":
                case "paprika": // spec 030: strips
                    return "sticks";
                case "lettuce":
                case "cucumber":
                case "red_cabbage":
                case "eggplant": // spec 030
                    return "slices";
                default:
                    return null;
            }
        }

        private static BaseItemDef Make(string id, string name, float sweet, float salty, float sour, float bitter, float umami,
            float fat, float crunch, float heatTolerance, float moisture, float windowMin, float windowMax)
            => new BaseItemDef(id, name, new ItemStats(sweet, salty, sour, bitter, umami, fat, crunch, heatTolerance, 0f, moisture),
                new DonenessWindow(windowMin, windowMax), Array.Empty<string>());
    }
}
