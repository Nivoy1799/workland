using System;
using System.Collections.Generic;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Base items for the spec 007 demo. Stats are the spec 005 test fixtures, so names and
    /// ratings match the /sim unit tests. A real data file replaces this later (spec 005 open question 2).
    /// </summary>
    public static class DemoIngredients
    {
        public static readonly BaseItemDef Potato = new BaseItemDef("potato", "potato",
            new ItemStats(0.1f, 0f, 0f, 0.1f, 0.1f, 0f, 0.5f, 0.6f, 0f, 0.8f),
            new DonenessWindow(0.55f, 0.85f), Array.Empty<string>());

        public static readonly BaseItemDef Apple = new BaseItemDef("apple", "apple",
            new ItemStats(0.6f, 0f, 0.3f, 0.05f, 0f, 0f, 0.7f, 0.3f, 0f, 0.85f),
            new DonenessWindow(0f, 0.3f), Array.Empty<string>());

        public static readonly BaseItemDef Tomato = Simple("tomato", 0.4f, 0.1f, 0.5f, 0f, 0.6f, 0.1f, 0f);
        public static readonly BaseItemDef Onion = Simple("onion", 0.5f, 0f, 0.2f, 0.3f, 0.3f, 0f, 0.6f);

        /// <summary>
        /// Ingredient order (the FoodLook ingredient byte): index 0 is potato; spec 028: after the four demo ingredients come the
        /// ten street-food items of the sim (<see cref="StreetFoodItems"/>), spec 030: then the three grill vegetables. Spec 028b: which slot of the fridge or the dry shelf
        /// holds which is <see cref="StockLayout"/>.
        /// </summary>
        public static readonly IReadOnlyList<BaseItemDef> All = Combine();

        /// <summary>
        /// Spec 033: every base item the kitchen's sim knows: <see cref="All"/> (the stock, in look-byte order) and then the drink
        /// dispenser's empty cup and its three drinks (<see cref="Drinks.Items"/>; not stock, no look index of their own: a cup's look
        /// names its drink in its own byte).
        /// </summary>
        public static readonly IReadOnlyList<BaseItemDef> Catalog = CombineCatalog();

        private static IReadOnlyList<BaseItemDef> CombineCatalog()
        {
            var all = new List<BaseItemDef>(All);
            all.AddRange(Drinks.Items);
            return all.AsReadOnly();
        }

        /// <summary>The index of the first street-food item (the four demo ingredients come before it).</summary>
        public const int FirstStreetFood = 4;

        private static IReadOnlyList<BaseItemDef> Combine()
        {
            var all = new List<BaseItemDef> { Potato, Apple, Tomato, Onion };
            all.AddRange(StreetFoodItems.All);
            all.AddRange(StreetFoodItems.GrillVegetables); // spec 030: paprika, eggplant, mushroom (indices 14 to 16)
            return all.AsReadOnly();
        }

        private static BaseItemDef Simple(string id, float sweet, float salty, float sour, float bitter,
            float umami, float fat, float crunch)
        {
            return new BaseItemDef(id, id,
                new ItemStats(sweet, salty, sour, bitter, umami, fat, crunch, 0.5f, 0f, 0.5f),
                new DonenessWindow(0f, 1f), Array.Empty<string>());
        }
    }
}
