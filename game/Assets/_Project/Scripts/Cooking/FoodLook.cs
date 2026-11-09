using System;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 017: the coarse look of a food that clients may know: which ingredient, which cut, how long
    /// it was heated (bucket only), plated or mixed. Bytes only (D21): no stats, quality or exact times.
    /// Pure value type; synced on Food and (spec 021) Vessel.
    /// </summary>
    public struct FoodLook : IEquatable<FoodLook>
    {
        public const byte UnknownIngredient = 255;

        public const byte ShapeWhole = 0;
        public const byte ShapeHalves = 1;
        public const byte ShapeSlices = 2;
        public const byte ShapeSticks = 3;
        public const byte ShapeCubes = 4;

        public const byte DonenessRaw = 0;
        public const byte DonenessShort = 1;
        public const byte DonenessMedium = 2;
        public const byte DonenessLong = 3;

        public const byte HeatNone = 0;
        public const byte HeatFried = 1;
        public const byte HeatBoiled = 2;
        /// <summary>Spec 029: from the fryer's basket (fries, falafel balls); golden like fried.</summary>
        public const byte HeatDeepFried = 3;

        public const byte KindSingle = 0;
        public const byte KindPlated = 1;
        public const byte KindMixed = 2;

        /// <summary>Parts drawn on a plate or in a bowl; the rest are not drawn.</summary>
        public const int MaxParts = 4;

        /// <summary>Index into DemoIngredients (first component), 255 = unknown.</summary>
        public byte Ingredient;
        /// <summary>0 whole, 1 halves, 2 slices, 3 sticks, 4 cubes (from the last cut).</summary>
        public byte Shape;
        /// <summary>0 raw, 1 short, 2 medium, 3 long (from the last heat op's time bucket).</summary>
        public byte Doneness;
        /// <summary>0 none, 1 fried (fry or saute), 2 boiled, 3 deep-fried (spec 029): picks the golden shift (spec 017 open question 3).</summary>
        public byte Heat;
        /// <summary>0 single, 1 plated, 2 mixed.</summary>
        public byte Kind;
        /// <summary>Plated or mixed: number of parts shown (max 4), else 0.</summary>
        public byte PartCount;

        public FoodLook(byte ingredient, byte shape, byte doneness, byte heat, byte kind, byte partCount)
        {
            Ingredient = ingredient;
            Shape = shape;
            Doneness = doneness;
            Heat = heat;
            Kind = kind;
            PartCount = partCount;
        }

        public static FoodLook Unknown => new FoodLook(UnknownIngredient, 0, 0, 0, 0, 0);

        public bool Equals(FoodLook other)
        {
            return Ingredient == other.Ingredient && Shape == other.Shape && Doneness == other.Doneness
                && Heat == other.Heat && Kind == other.Kind && PartCount == other.PartCount;
        }

        public override bool Equals(object obj) => obj is FoodLook other && Equals(other);

        public override int GetHashCode()
        {
            return Ingredient | (Shape << 8) | (Doneness << 12) | (Heat << 16) | (Kind << 20) | (PartCount << 24);
        }

        public static bool operator ==(FoodLook a, FoodLook b) => a.Equals(b);
        public static bool operator !=(FoodLook a, FoodLook b) => !a.Equals(b);

        public override string ToString()
        {
            return "FoodLook(" + Ingredient + ", shape " + Shape + ", done " + Doneness + ", heat " + Heat
                + ", kind " + Kind + ", parts " + PartCount + ")";
        }
    }
}
