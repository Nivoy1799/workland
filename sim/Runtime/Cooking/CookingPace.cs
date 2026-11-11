namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 043: THE cooking pace, one table for every heat station (docs/cooking-roadmap.md, "Cooking pace"). The curve stations
    /// list Good, Perfect and Burnt in kitchen (real) seconds per ingredient; the pan and the pot cook by their dose formula
    /// (ItemFactory) at <see cref="PanPotSpeed"/>. All numbers follow the inbox's "about 2x faster" default (question 3, option A)
    /// and are defaults to review; changing one is one line here.
    /// </summary>
    public static class CookingPace
    {
        /// <summary>
        /// The pan and the pot cook this many times faster than the spec 005 dose formula (a whole potato in the pan: Good at about
        /// 22 s instead of 44 s; in the pot about 70 s instead of 141 s).
        /// </summary>
        public const float PanPotSpeed = 2f;

        // ---- the fryer (spec 029), at 180 C ----
        public static readonly FryCurve Fries = FryCurve.FromKitchenSeconds("potato", 25f, 30f, 45f);
        public static readonly FryCurve Falafel = FryCurve.FromKitchenSeconds("falafel_mix", 20f, 25f, 38f);

        // ---- the griddle (spec 030), per side ----
        public static readonly FryCurve SeitanSide = FryCurve.FromKitchenSeconds("seitan", 12f, 14f, 22f);
        public static readonly FryCurve PaprikaSide = FryCurve.FromKitchenSeconds("paprika", 10f, 12f, 20f);
        public static readonly FryCurve EggplantSide = FryCurve.FromKitchenSeconds("eggplant", 10f, 12f, 20f);
        public static readonly FryCurve MushroomSide = FryCurve.FromKitchenSeconds("mushroom", 10f, 12f, 20f);
        public static readonly FryCurve FlatbreadSide = FryCurve.FromKitchenSeconds("flatbread", 4f, 5f, 9f);
        public static readonly FryCurve WrapSide = FryCurve.FromKitchenSeconds("wrap", 4f, 5f, 9f);

        // ---- the spit (spec 036), the outer layer at high heat ----
        public static readonly FryCurve SpitOuter = FryCurve.FromKitchenSeconds("seitan", 40f, 55f, 90f);
    }
}
