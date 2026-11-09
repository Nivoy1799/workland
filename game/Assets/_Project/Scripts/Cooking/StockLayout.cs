using System;
using System.Collections.Generic;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 028b, pure data: which ingredient stands in which slot of the fridge and the dry shelf (docs/feel.md 6: all
    /// stock next to prep). Slot n of a station is its station option n; slots are numbered row by row from the top, left to
    /// right as seen from the aisle. Cold stock (vegetables, seitan, falafel mix, lemon, apple) is in the fridge, dry stock
    /// (sauces, potato, onion, bread, wraps) on the shelf. Every <see cref="DemoIngredients.All"/> item stands in exactly
    /// one slot (StockRulesTests). The scene builder places the slots from this table.
    /// </summary>
    public static class StockLayout
    {
        /// <summary>Two shelves of four behind the fridge's open doors.</summary>
        public const int FridgeRows = 2;
        public const int FridgeColumns = 4;
        /// <summary>Three boards of two (six dry items; a third column would stand empty).</summary>
        public const int ShelfRows = 3;
        public const int ShelfColumns = 2;

        /// <summary>Top shelf: the vegetables; lower shelf: seitan, falafel mix, lemon, apple.</summary>
        public static readonly IReadOnlyList<string> Fridge = Array.AsReadOnly(new[]
        {
            "lettuce", "cucumber", "red_cabbage", "tomato",
            "seitan", "falafel_mix", "lemon", "apple"
        });

        /// <summary>Top board: the sauces; middle: potato and onion (used most, at hand height); bottom: flatbread and wraps.</summary>
        public static readonly IReadOnlyList<string> Shelf = Array.AsReadOnly(new[]
        {
            "garlic_herb_sauce", "chili_sauce",
            "potato", "onion",
            "flatbread", "wrap"
        });

        /// <summary>True for the stations whose option picks a slot.</summary>
        public static bool IsStock(StationKind kind) => kind == StationKind.Fridge || kind == StationKind.Shelf;

        /// <summary>The ingredient ids of a stock station's slots, in option order; empty for every other kind.</summary>
        public static IReadOnlyList<string> SlotsOf(StationKind kind)
        {
            switch (kind)
            {
                case StationKind.Fridge: return Fridge;
                case StationKind.Shelf: return Shelf;
                default: return Array.Empty<string>();
            }
        }

        /// <summary>Slots per row (columns) of a stock station; 0 for every other kind.</summary>
        public static int Columns(StationKind kind)
        {
            switch (kind)
            {
                case StationKind.Fridge: return FridgeColumns;
                case StationKind.Shelf: return ShelfColumns;
                default: return 0;
            }
        }

        /// <summary>Rows of a stock station; 0 for every other kind.</summary>
        public static int Rows(StationKind kind)
        {
            switch (kind)
            {
                case StationKind.Fridge: return FridgeRows;
                case StationKind.Shelf: return ShelfRows;
                default: return 0;
            }
        }

        /// <summary>Index of <paramref name="id"/> in <see cref="DemoIngredients.All"/>, or -1.</summary>
        public static int IngredientIndexOf(string id)
        {
            for (int i = 0; i < DemoIngredients.All.Count; i++)
                if (DemoIngredients.All[i].Id == id) return i;
            return -1;
        }
    }
}
