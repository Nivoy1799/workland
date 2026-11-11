using System;
using System.Collections.Generic;

namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 056a: every base item the shipped kitchen uses, in the game's order (the FoodLook ingredient byte): the four demo
    /// ingredients (potato, apple, tomato, onion; the spec 005 test fixtures), then <see cref="StreetFoodItems.All"/>, then
    /// <see cref="StreetFoodItems.GrillVegetables"/>. <see cref="Catalog"/> appends <see cref="Drinks.Items"/> (the cup and its
    /// drinks: no stock, no look index). The game's <c>DemoIngredients</c> forwards here (spec 056c), so the offline name list
    /// (<see cref="ReachableItems"/>) and the game use one list. Immutable.
    /// </summary>
    public static class KitchenItems
    {
        public static readonly BaseItemDef Potato = new BaseItemDef("potato", "potato",
            new ItemStats(0.1f, 0f, 0f, 0.1f, 0.1f, 0f, 0.5f, 0.6f, 0f, 0.8f),
            new DonenessWindow(0.55f, 0.85f), Array.Empty<string>());

        public static readonly BaseItemDef Apple = new BaseItemDef("apple", "apple",
            new ItemStats(0.6f, 0f, 0.3f, 0.05f, 0f, 0f, 0.7f, 0.3f, 0f, 0.85f),
            new DonenessWindow(0f, 0.3f), Array.Empty<string>());

        public static readonly BaseItemDef Tomato = Simple("tomato", 0.4f, 0.1f, 0.5f, 0f, 0.6f, 0.1f, 0f);
        public static readonly BaseItemDef Onion = Simple("onion", 0.5f, 0f, 0.2f, 0.3f, 0.3f, 0f, 0.6f);

        /// <summary>The index of the first street-food item in <see cref="All"/> (the four demo ingredients come before it).</summary>
        public const int FirstStreetFood = 4;

        /// <summary>The stock in look-byte order: potato, apple, tomato, onion, the ten street-food items, the three grill vegetables.</summary>
        public static readonly IReadOnlyList<BaseItemDef> All = CombineAll();

        /// <summary>Every base item the kitchen's sim knows: <see cref="All"/>, then <see cref="Drinks.Items"/>.</summary>
        public static readonly IReadOnlyList<BaseItemDef> Catalog = CombineCatalog();

        /// <summary>A validated <see cref="ItemCatalog"/> of <see cref="Catalog"/> (a new one each call).</summary>
        public static ItemCatalog CreateItemCatalog() => new ItemCatalog(Catalog);

        private static IReadOnlyList<BaseItemDef> CombineAll()
        {
            var all = new List<BaseItemDef> { Potato, Apple, Tomato, Onion };
            all.AddRange(StreetFoodItems.All);
            all.AddRange(StreetFoodItems.GrillVegetables);
            return all.AsReadOnly();
        }

        private static IReadOnlyList<BaseItemDef> CombineCatalog()
        {
            var all = new List<BaseItemDef>(All);
            all.AddRange(Drinks.Items);
            return all.AsReadOnly();
        }

        private static BaseItemDef Simple(string id, float sweet, float salty, float sour, float bitter, float umami, float fat,
            float crunch)
        {
            return new BaseItemDef(id, id,
                new ItemStats(sweet, salty, sour, bitter, umami, fat, crunch, 0.5f, 0f, 0.5f),
                new DonenessWindow(0f, 1f), Array.Empty<string>());
        }
    }
}
