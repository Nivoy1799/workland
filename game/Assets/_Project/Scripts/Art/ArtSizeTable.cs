using System;
using System.Collections.Generic;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 026 (D29): the size of every art key with a catalog model, mirroring the reference table of
    /// <c>docs/art-direction.md</c> (section 2). <c>ArtCatalogBuilder</c> fits each model to <see cref="TargetSize"/>,
    /// never to a free scale factor; <c>ArtSizeTests</c> checks the built models against it and the guide against it.
    /// Rows without a <see cref="ArtSize.GuideRow"/> are things the guide has no row for (road tiles, generic pieces).
    /// </summary>
    public static class ArtSizeTable
    {
        /// <summary>Kitchen worktops, stove tops and every place surface on them (m above the floor).</summary>
        public const float WorktopHeight = 0.90f;
        /// <summary>The serving counter (m).</summary>
        public const float ServingCounterHeight = 1.05f;
        /// <summary>Width of one worktop cabinet module along the run (m).</summary>
        public const float WorktopModuleWidth = 0.60f;
        /// <summary>
        /// How far a module may be wider or narrower than <see cref="WorktopModuleWidth"/> (m). Equal modules always stay
        /// within it for runs over 0.9 m (two or more modules); a shorter run is one module of its own length.
        /// </summary>
        public const float WorktopModuleSlack = 0.15f;
        /// <summary>Allowed difference between a built model and its target size (fraction).</summary>
        public const float Tolerance = 0.15f;

        private const string Worktop = "Kitchen worktop / stove top";
        private const string House = "Small house (2 storeys, to the ridge)";
        private const string AppleOnion = "Apple / onion";
        private const string Person = "Person (player)";

        private static readonly ArtSize[] Rows =
        {
            // Kitchen furniture: the stations keep their 1.2 m footprint, the tops are at the worktop height.
            World(ArtKey.StationCrate, WorktopHeight, SizeAxis.Height, Worktop),
            World(ArtKey.StationBoard, WorktopHeight, SizeAxis.Height, Worktop),
            World(ArtKey.StationPan, WorktopHeight, SizeAxis.Height, Worktop),
            World(ArtKey.StationPot, WorktopHeight, SizeAxis.Height, Worktop),
            World(ArtKey.StationCounter, ServingCounterHeight, SizeAxis.Height, "Serving counter"),
            World(ArtKey.StationPlatingTable, WorktopHeight, SizeAxis.Height, Worktop),
            World(ArtKey.StationBowl, WorktopHeight, SizeAxis.Height, Worktop),
            World(ArtKey.Worktop, WorktopHeight, SizeAxis.Height, Worktop),

            // Street. Road tiles keep the 5 m strip of the street layout (two 2.5 m lanes; the guide's 3.0 m lane is the
            // target for a later street layout spec, so the road has no guide row here).
            World(ArtKey.Road, 5f, SizeAxis.Largest),
            World(ArtKey.RoadCrossroad, 5f, SizeAxis.Largest),
            World(ArtKey.BuildingShop, 7.5f, SizeAxis.Height, "Shop (ground floor 4 m + 1 storey)"),
            World(ArtKey.BuildingHouseA, 7.5f, SizeAxis.Height, House),
            World(ArtKey.BuildingHouseC, 7.5f, SizeAxis.Height, House),
            World(ArtKey.BuildingHouseF, 7.5f, SizeAxis.Height, House),
            World(ArtKey.BuildingHouseK, 7.5f, SizeAxis.Height, House),
            World(ArtKey.PropTreeSmall, 4f, SizeAxis.Height, "Tree small / large", 0),
            World(ArtKey.PropTreeLarge, 8f, SizeAxis.Height, "Tree small / large", 1),
            World(ArtKey.PropPlanter, 1.6f, SizeAxis.Largest, "Planter"),
            World(ArtKey.PropLamp, 4.5f, SizeAxis.Height, "Street lamp"),
            World(ArtKey.PropDumpster, 1.8f, SizeAxis.Largest, "Dumpster"),
            World(ArtKey.PropTrashcan, 0.9f, SizeAxis.Height, "Trash can (height)"),
            World(ArtKey.PropFence, 2.4f, SizeAxis.Largest, "Fence section"),
            World(ArtKey.PropAwning, 2.4f, SizeAxis.Largest, "Awning"),

            // Food (small items x1.5). A half's largest dimension is the whole fruit's diameter.
            Small(ArtKey.FoodPotato, 0.09f, "Potato"),
            Small(ArtKey.FoodApple, 0.08f, AppleOnion),
            Small(ArtKey.FoodTomato, 0.07f, "Tomato"),
            Small(ArtKey.FoodOnion, 0.08f, AppleOnion),
            Small(ArtKey.FoodAppleHalf, 0.08f, AppleOnion),
            Small(ArtKey.FoodOnionHalf, 0.08f, AppleOnion),
            Small(ArtKey.FoodTomatoSlice, 0.07f, "Tomato slice"),
            Small(ArtKey.FoodFries, 0.14f, "Fries (portion)"),
            Small(ArtKey.FoodPieceHalf, 0.08f),
            Small(ArtKey.FoodPieceSlice, 0.07f),
            Small(ArtKey.FoodPieceStick, 0.09f),
            Small(ArtKey.FoodPieceCube, 0.03f),

            // Spec 028: the street-food stock. Small things are x1.5; the bigger ones (over 15 cm) stay at their real size, because
            // a held item must fit the 0.25 m hand footprint (FoodArtCatalogTests).
            Food(ArtKey.FoodFlatbread, 0.22f, "Flatbread"),
            Food(ArtKey.FoodWrap, 0.25f, "Wrap"),
            Small(ArtKey.FoodSeitan, 0.12f, "Seitan block"),
            Small(ArtKey.FoodFalafelMix, 0.10f, "Falafel mix"),
            Food(ArtKey.FoodLettuce, 0.18f, "Lettuce"),
            Food(ArtKey.FoodCucumber, 0.20f, "Cucumber"),
            Food(ArtKey.FoodRedCabbage, 0.16f, "Red cabbage"),
            Small(ArtKey.FoodLemon, 0.07f, "Lemon"),
            Small(ArtKey.FoodLemonHalf, 0.07f, "Lemon"),
            Food(ArtKey.FoodSauceGarlicHerb, 0.20f, "Sauce bottle"),
            Food(ArtKey.FoodSauceChili, 0.20f, "Sauce bottle"),

            // Cookware and tools (x1.15).
            Cookware(ArtKey.FoodPlate, 0.26f, "Plate"),
            Cookware(ArtKey.FoodBowl, 0.18f, "Bowl"),
            Cookware(ArtKey.FoodPan, 0.45f, "Frying pan (with handle)"),
            Cookware(ArtKey.FoodPot, 0.30f, "Pot"),
            Cookware(ArtKey.ToolCuttingBoard, 0.40f, "Cutting board"),
            Cookware(ArtKey.ToolKnife, 0.30f, "Chef's knife"),

            // People.
            World(ArtKey.CharacterPlayerMale, 1.75f, SizeAxis.Height, Person),
            World(ArtKey.CharacterPlayerFemale, 1.75f, SizeAxis.Height, Person),
            World(ArtKey.CharacterCustomerMale, 1.75f, SizeAxis.Height, Person),
            World(ArtKey.CharacterCustomerFemale, 1.75f, SizeAxis.Height, Person),
            World(ArtKey.CharacterCustomerMale2, 1.75f, SizeAxis.Height, Person),
            World(ArtKey.CharacterCustomerFemale2, 1.75f, SizeAxis.Height, Person),
            World(ArtKey.CharacterCustomerMale3, 1.75f, SizeAxis.Height, Person),
            World(ArtKey.CharacterCustomerFemale3, 1.75f, SizeAxis.Height, Person)
        };

        /// <summary>Every row, one per key.</summary>
        public static IReadOnlyList<ArtSize> All => Rows;

        /// <summary>The readability factor of a size class: 1.0 / 1.15 / 1.5.</summary>
        public static float Factor(SizeClass sizeClass)
        {
            switch (sizeClass)
            {
                case SizeClass.World: return 1f;
                case SizeClass.Cookware: return 1.15f;
                case SizeClass.Small: return 1.5f;
                default: throw new ArgumentOutOfRangeException(nameof(sizeClass), sizeClass, "unknown size class");
            }
        }

        /// <summary>The row of a key; false when the table has none.</summary>
        public static bool TryGet(ArtKey key, out ArtSize size)
        {
            foreach (ArtSize row in Rows)
            {
                if (row.Key != key) continue;
                size = row;
                return true;
            }
            size = default;
            return false;
        }

        /// <summary>The row of a key; throws for a key without a row (a model the guide does not size yet).</summary>
        public static ArtSize Of(ArtKey key)
        {
            if (TryGet(key, out ArtSize size)) return size;
            throw new ArgumentOutOfRangeException(nameof(key), key, "no row in ArtSizeTable; add one (and one in the guide)");
        }

        /// <summary>The size in the game in metres: real size x the class factor.</summary>
        public static float TargetSize(ArtKey key) => Of(key).Target;

        /// <summary>True when <paramref name="built"/> is within <see cref="Tolerance"/> of <paramref name="target"/>.</summary>
        public static bool Fits(float built, float target) => target > 0f && Math.Abs(built - target) <= target * Tolerance;

        /// <summary>
        /// Spec 026: how a worktop run of <paramref name="length"/> m is split into cabinet modules: n = max(1, round(length /
        /// <see cref="WorktopModuleWidth"/>)) modules of equal width length / n, so every run length works.
        /// </summary>
        public static (int Count, float Width) WorktopModules(float length)
        {
            if (!(length > 0f) || float.IsInfinity(length)) throw new ArgumentOutOfRangeException(nameof(length), length, "a worktop needs a length");
            int count = Math.Max(1, (int)Math.Round(length / WorktopModuleWidth, MidpointRounding.AwayFromZero));
            return (count, length / count);
        }

        private static ArtSize World(ArtKey key, float real, SizeAxis axis, string guideRow = null, int guideIndex = 0) =>
            new ArtSize(key, SizeClass.World, real, axis, guideRow, guideIndex);

        private static ArtSize Food(ArtKey key, float real, string guideRow) =>
            new ArtSize(key, SizeClass.World, real, SizeAxis.Largest, guideRow);

        private static ArtSize Cookware(ArtKey key, float real, string guideRow) =>
            new ArtSize(key, SizeClass.Cookware, real, SizeAxis.Largest, guideRow);

        private static ArtSize Small(ArtKey key, float real, string guideRow = null) =>
            new ArtSize(key, SizeClass.Small, real, SizeAxis.Largest, guideRow);
    }
}
