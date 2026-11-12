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
        public static readonly Color Cream = Rgb(0xF3E9D2);
        public static readonly Color Plaster = Rgb(0xE2EADD);
        public static readonly Color WallOut = Rgb(0xF1E6CF);
        public static readonly Color Mint = Rgb(0x8CC5AC);
        public static readonly Color Teal = Rgb(0x4F9A85);
        public static readonly Color TealDark = Rgb(0x2F6E5E);
        public static readonly Color Mustard = Rgb(0xE3AE45);
        public static readonly Color Orange = Rgb(0xE58436);
        public static readonly Color Salmon = Rgb(0xE8957A);
        public static readonly Color Sky = Rgb(0x9CC3D5);
        public static readonly Color DeepBlue = Rgb(0x4E6E8E);
        public static readonly Color Terracotta = Rgb(0xC9734F);
        public static readonly Color WoodLight = Rgb(0xB98B5E);
        public static readonly Color WoodDark = Rgb(0x8A5A3B);
        public static readonly Color Chrome = Rgb(0xC9CDD0);
        public static readonly Color Charcoal = Rgb(0x3A3532);
        public static readonly Color Leaf = Rgb(0x5E8F4F);
        public static readonly Color Plant = Rgb(0x3F6B3A);
        public static readonly Color Ground = Rgb(0x9DB585);
        public static readonly Color Pavement = Rgb(0xD8CDB8);
        public static readonly Color Road = Rgb(0x6F6A66);

        // UI colours (guide section 5.4, unchanged from D32): not part of the town palette.
        public static readonly Color Ink = Rgb(0x2A2624);
        /// <summary>UI panels: <c>#2E2A27</c> at 85 % opacity.</summary>
        public static readonly Color Panel = WithAlpha(Rgb(0x2E2A27), 0.85f);
        /// <summary>The interaction highlight only.</summary>
        public static readonly Color Interact = Rgb(0xF5D27A);

        /// <summary>The town colours of the guide's section 5.1 table, in its order (a row with two names gives two entries).</summary>
        public static IReadOnlyList<(string Name, Color Color)> GuideColours { get; } = new[]
        {
            ("Cream", Cream), ("Plaster", Plaster), ("WallOut", WallOut), ("Mint", Mint), ("Teal", Teal),
            ("TealDark", TealDark), ("Mustard", Mustard), ("Orange", Orange), ("Salmon", Salmon), ("Sky", Sky),
            ("DeepBlue", DeepBlue), ("Terracotta", Terracotta), ("WoodLight", WoodLight), ("WoodDark", WoodDark),
            ("Chrome", Chrome), ("Charcoal", Charcoal), ("Leaf", Leaf), ("Plant", Plant), ("Ground", Ground),
            ("Pavement", Pavement), ("Road", Road)
        };

        /// <summary>The UI colours of the guide's section 5.4.</summary>
        public static IReadOnlyList<(string Name, Color Color)> UiColours { get; } = new[]
        {
            ("Ink", Ink), ("Panel", Panel), ("Interact", Interact)
        };

        // Spec 048: the colour-blind friendly ring set (the Okabe-Ito hues): blue, yellow, orange and vermillion stay apart for every
        // common kind of colour blindness, and step down in lightness as the food gets worse. Only the rings use them (RingPalette).
        public static readonly Color SafeSkyBlue = Rgb(0x56B4E9);
        public static readonly Color SafeYellow = Rgb(0xF0E442);
        public static readonly Color SafeOrange = Rgb(0xE69F00);
        public static readonly Color SafeVermillion = Rgb(0xD55E00);

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

        private static readonly Color[] CustomerTints = { Cream, Terracotta, Mustard, Ground, Sky, DeepBlue };

        /// <summary>Number of customer tints (<see cref="Customer"/> wraps around).</summary>
        public static int CustomerTintCount => CustomerTints.Length;

        /// <summary>Customer tint <paramref name="index"/>: Cream, Terracotta, Mustard, Ground, Sky, DeepBlue, wrapping (negatives too).</summary>
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
