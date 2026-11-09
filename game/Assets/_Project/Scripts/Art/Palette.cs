using System.Collections.Generic;
using UnityEngine;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 026 (D32): the one warm muted palette of <c>docs/art-direction.md</c>, section 5. Every colour in the game
    /// code comes from here (the UI uses the same names as <c>--wt-*</c> variables in <c>UI/Palette.uss</c>);
    /// <c>PaletteTests</c> fails on a <c>new Color(</c> anywhere else under <c>Scripts</c>. Field names and hex values
    /// are those of the guide's table.
    /// </summary>
    public static class Palette
    {
        public static readonly Color Cream = Rgb(0xF2E6D0);
        public static readonly Color Plaster = Rgb(0xE4D5BC);
        public static readonly Color WoodLight = Rgb(0xC8A27A);
        public static readonly Color WoodDark = Rgb(0x8A5A3B);
        public static readonly Color Terracotta = Rgb(0xC8664A);
        public static readonly Color Brick = Rgb(0xA4503C);
        public static readonly Color Mustard = Rgb(0xD9A441);
        public static readonly Color Sage = Rgb(0x8FA77E);
        public static readonly Color Leaf = Rgb(0x5E7F4F);
        public static readonly Color SoftBlue = Rgb(0x7FA3B8);
        public static readonly Color DeepBlue = Rgb(0x3F5F78);
        public static readonly Color Steel = Rgb(0x9AA0A3);
        public static readonly Color Charcoal = Rgb(0x3A3532);
        public static readonly Color Ink = Rgb(0x2A2624);
        /// <summary>UI panels: <c>#2E2A27</c> at 85 % opacity.</summary>
        public static readonly Color Panel = WithAlpha(Rgb(0x2E2A27), 0.85f);
        /// <summary>The interaction highlight only.</summary>
        public static readonly Color Interact = Rgb(0xF5D27A);

        /// <summary>The 16 colours of the guide's table, in its order (names as the fields).</summary>
        public static IReadOnlyList<(string Name, Color Color)> GuideColours { get; } = new[]
        {
            ("Cream", Cream), ("Plaster", Plaster), ("WoodLight", WoodLight), ("WoodDark", WoodDark),
            ("Terracotta", Terracotta), ("Brick", Brick), ("Mustard", Mustard), ("Sage", Sage), ("Leaf", Leaf),
            ("SoftBlue", SoftBlue), ("DeepBlue", DeepBlue), ("Steel", Steel), ("Charcoal", Charcoal), ("Ink", Ink),
            ("Panel", Panel), ("Interact", Interact)
        };

        // Food keeps natural colours, muted to sit in the palette (HSV saturation at most 70 %).
        public static readonly Color FoodPotato = Rgb(0xD4B27A);
        public static readonly Color FoodApple = Rgb(0xB8483E);
        public static readonly Color FoodTomato = Rgb(0xD0563F);
        public static readonly Color FoodOnion = Rgb(0xD9CCDA);
        // Spec 028: the street-food stock (muted, saturation at most 70 %).
        public static readonly Color FoodFlatbread = Rgb(0xE0C294);
        public static readonly Color FoodWrap = Rgb(0xE6D3AE);
        public static readonly Color FoodSeitan = Rgb(0xB88A5C);
        public static readonly Color FoodFalafel = Rgb(0x9C8B4F);
        public static readonly Color FoodLettuce = Rgb(0x9DBB7E);
        public static readonly Color FoodCucumber = Rgb(0x6F9A58);
        public static readonly Color FoodRedCabbage = Rgb(0x9A5F86);
        public static readonly Color FoodLemon = Rgb(0xE6CB5A);
        public static readonly Color FoodHerbSauce = Rgb(0xE3E0B8);
        public static readonly Color FoodChiliSauce = Rgb(0xC4513F);
        // Spec 030: the grill vegetables.
        public static readonly Color FoodPaprika = Rgb(0xC4503C);
        public static readonly Color FoodEggplant = Rgb(0x6E4A6E);
        public static readonly Color FoodMushroom = Rgb(0xC8B496);
        // Spec 033: the drinks (muted like the food).
        public static readonly Color FoodLemonade = Rgb(0xE9DD9A);
        public static readonly Color FoodIcedTea = Rgb(0xA86A3C);
        public static readonly Color FoodSoda = Rgb(0x6E3F30);
        /// <summary>What fried food shifts towards.</summary>
        public static readonly Color FoodGolden = Rgb(0xE8BE6A);

        /// <summary>The food colours (for the saturation check).</summary>
        public static IReadOnlyList<(string Name, Color Color)> FoodColours { get; } = new[]
        {
            ("FoodPotato", FoodPotato), ("FoodApple", FoodApple), ("FoodTomato", FoodTomato), ("FoodOnion", FoodOnion),
            ("FoodGolden", FoodGolden),
            ("FoodFlatbread", FoodFlatbread), ("FoodWrap", FoodWrap), ("FoodSeitan", FoodSeitan), ("FoodFalafel", FoodFalafel),
            ("FoodLettuce", FoodLettuce), ("FoodCucumber", FoodCucumber), ("FoodRedCabbage", FoodRedCabbage),
            ("FoodLemon", FoodLemon), ("FoodHerbSauce", FoodHerbSauce), ("FoodChiliSauce", FoodChiliSauce),
            ("FoodPaprika", FoodPaprika), ("FoodEggplant", FoodEggplant), ("FoodMushroom", FoodMushroom),
            ("FoodLemonade", FoodLemonade), ("FoodIcedTea", FoodIcedTea), ("FoodSoda", FoodSoda)
        };

        /// <summary>Highest HSV saturation a food colour may have.</summary>
        public const float MaxFoodSaturation = 0.70f;

        private static readonly Color[] CustomerTints = { Cream, Terracotta, Mustard, Sage, SoftBlue, DeepBlue };

        /// <summary>Number of customer tints (<see cref="Customer"/> wraps around).</summary>
        public static int CustomerTintCount => CustomerTints.Length;

        /// <summary>Customer tint <paramref name="index"/>: Cream, Terracotta, Mustard, Sage, SoftBlue, DeepBlue, wrapping (negatives too).</summary>
        public static Color Customer(int index)
        {
            int i = index % CustomerTints.Length;
            return CustomerTints[i < 0 ? i + CustomerTints.Length : i];
        }

        /// <summary>The same colour with another opacity (0..1).</summary>
        public static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        /// <summary>The colour with its RGB multiplied by <paramref name="factor"/> (darker below 1), opaque.</summary>
        public static Color Shade(Color color, float factor)
        {
            Color c = color * factor;
            c.a = 1f;
            return c;
        }

        /// <summary>Upper-case <c>#RRGGBB</c> of a colour (alpha ignored), as written in the guide.</summary>
        public static string Hex(Color color) => "#" + ColorUtility.ToHtmlStringRGB(color);

        private static Color Rgb(int hex) =>
            new Color(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f, 1f);
    }
}
