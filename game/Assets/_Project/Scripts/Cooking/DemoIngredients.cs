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
        /// Crate order in the scene: index 0 is potato; spec 028: after the four demo ingredients come the ten street-food
        /// items of the sim (<see cref="StreetFoodItems"/>), in the narrow stock bank.
        /// </summary>
        public static readonly IReadOnlyList<BaseItemDef> All = Combine();

        /// <summary>The first crate index of the street-food bank (the four old crates come before it).</summary>
        public const int FirstStreetFood = 4;

        private static IReadOnlyList<BaseItemDef> Combine()
        {
            var all = new List<BaseItemDef> { Potato, Apple, Tomato, Onion };
            all.AddRange(StreetFoodItems.All);
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
