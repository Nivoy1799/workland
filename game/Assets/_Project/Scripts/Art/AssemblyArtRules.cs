using UnityEngine;
using Worktown.Game.Cooking;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 031, pure: the assembly board's looks and measures (shared by the art catalog, the scene builder and the views): the
    /// graybox board (worktop high, 0.9 m wide), where its build spot and tools stand on its top, how high each layer of the stack
    /// is, the drop arc of a new layer, the fold, the wrap and the box, and the sauce ribbon.
    /// </summary>
    public static class AssemblyArtRules
    {
        /// <summary>The board: worktop high and deep, 0.9 m wide (a cutting board, two dispensers and a packaging rack on top).</summary>
        public const float Width = 0.9f;
        public const float Depth = 1.2f;
        public const float Top = ArtSizeTable.WorktopHeight;
        /// <summary>The wooden board under the build (m), its thickness and its centre (station space, -z towards the aisle).</summary>
        public const float BoardWidth = 0.42f;
        public const float BoardDepth = 0.32f;
        public const float BoardThickness = 0.015f;
        public static readonly Vector2 SpotPosition = new Vector2(-0.14f, -0.32f);
        /// <summary>The spot's aim collider (m across) and height.</summary>
        public const float SpotSize = 0.34f;
        public const float SpotColliderHeight = 0.06f;

        /// <summary>The tools on the top (station x, z): the two dispensers at the back left, the rack along the right edge.</summary>
        private static readonly Vector2[] Tools =
        {
            new Vector2(-0.30f, 0.12f), // garlic-herb dispenser
            new Vector2(-0.10f, 0.12f), // chili dispenser
            new Vector2(0.30f, -0.34f), // wrap paper
            new Vector2(0.30f, -0.06f), // boxes
            new Vector2(0.30f, 0.22f), // bags
        };

        /// <summary>Where tool <paramref name="tool"/> stands on the top (station x, z), zero for an unknown tool.</summary>
        public static Vector2 ToolPosition(int tool) => tool >= 0 && tool < Tools.Length ? Tools[tool] : Vector2.zero;

        /// <summary>The power-free board has no buttons; the label sits on its front like the other stations.</summary>
        public const float LabelHeight = 0.855f;

        // ---- the stack ----

        /// <summary>The flat layers: a bread base (a disc the size of the wrap) and a sauce (a thin, smaller disc).</summary>
        public const float BaseThickness = 0.008f;
        public const float SauceThickness = 0.004f;
        public const float SauceScale = 0.62f;
        /// <summary>A piled layer is drawn at this share of its size (it sits on the bread).</summary>
        public const float PileScale = 0.8f;

        /// <summary>Spec 031: a bread base (flatbread, wrap) is drawn as a flat disc.</summary>
        public static bool IsBread(byte ingredient) =>
            ingredient == DemoIngredients.FirstStreetFood || ingredient == DemoIngredients.FirstStreetFood + 1;

        /// <summary>A sauce layer (garlic-herb, chili) is drawn as a thin disc in its colour.</summary>
        public static bool IsSauce(byte ingredient) =>
            ingredient == DemoIngredients.FirstStreetFood + 8 || ingredient == DemoIngredients.FirstStreetFood + 9;

        /// <summary>
        /// How much a layer adds to the stack's height (m): a bread disc or a sauce its thickness, salad slices a little, falafel
        /// balls, strips and fries more (they settle into each other, so less than their own size).
        /// </summary>
        public static float LayerHeight(FoodLook look)
        {
            if (IsSauce(look.Ingredient)) return SauceThickness;
            if (look.Kind == FoodLook.KindSingle && IsBread(look.Ingredient) && look.Shape == FoodLook.ShapeWhole) return BaseThickness;
            if (look.Heat == FoodLook.HeatDeepFried && look.Shape == FoodLook.ShapeWhole) return 0.035f; // falafel balls
            switch (look.Shape)
            {
                case FoodLook.ShapeSlices: return 0.012f;
                case FoodLook.ShapeSticks: return 0.02f;
                case FoodLook.ShapeCubes: return 0.025f;
                case FoodLook.ShapeHalves: return 0.03f;
                default: return 0.04f;
            }
        }

        /// <summary>
        /// Where layer <paramref name="index"/> sits sideways on the stack (m, station x and z): flat layers centred, piled ones
        /// a little to alternating sides, so a layer under them still peeks out (the falafel under the lettuce).
        /// </summary>
        public static Vector2 LayerShift(int index, FoodLook look)
        {
            if (IsFlat(look) || index <= 0) return Vector2.zero;
            return index % 2 == 1 ? new Vector2(-0.025f, 0.012f) : new Vector2(0.025f, -0.01f);
        }

        /// <summary>The flat disc look: a bread base or a sauce (else the layer is the ingredient's own model).</summary>
        public static bool IsFlat(FoodLook look) =>
            IsSauce(look.Ingredient) || (IsBread(look.Ingredient) && look.Shape == FoodLook.ShapeWhole && look.Kind == FoodLook.KindSingle);

        // ---- motion ----

        /// <summary>A new layer drops onto the stack in this long (s) from this high (m), on a short arc from the cook's side.</summary>
        public const float DropSeconds = 0.18f;
        public const float DropHeight = 0.22f;
        public const float DropReach = 0.08f;
        /// <summary>The landing squash: the layer's height scale right after the plop, back to 1 over <see cref="SquashSeconds"/>.</summary>
        public const float Squash = 0.7f;
        public const float SquashSeconds = 0.12f;

        /// <summary>
        /// The offset of a dropping layer from its rest (station space; -z is the cook's side) at <paramref name="t"/> s: it comes
        /// down from <see cref="DropHeight"/> along an ease-in, starting <see cref="DropReach"/> towards the cook, so it arcs in.
        /// Zero once landed.
        /// </summary>
        public static Vector3 DropOffset(float t)
        {
            if (!(t < DropSeconds)) return Vector3.zero;
            float x = t <= 0f ? 0f : t / DropSeconds;
            float left = 1f - x * x; // ease-in fall
            return new Vector3(0f, DropHeight * left, -DropReach * (1f - x));
        }

        /// <summary>The height scale of a layer <paramref name="t"/> s after it landed (the plop squash).</summary>
        public static float SquashAfter(float t)
        {
            if (!(t > 0f)) return Squash;
            if (t >= SquashSeconds) return 1f;
            return Mathf.Lerp(Squash, 1f, t / SquashSeconds);
        }

        /// <summary>The fold, the paper wrap and a box or bag going on take this long (s, "short animations").</summary>
        public const float StepSeconds = 0.3f;

        /// <summary>The share 0..1 of a fold or wrap after <paramref name="t"/> s (smooth start and end).</summary>
        public static float StepShare(float t)
        {
            if (!(t > 0f)) return 0f;
            if (t >= StepSeconds) return 1f;
            float x = t / StepSeconds;
            return x * x * (3f - 2f * x);
        }

        // ---- the sauce ribbon ----

        /// <summary>The bottle lifts over the build to squeeze in this long (s), and goes back in <see cref="BottleBackSeconds"/>.</summary>
        public const float BottleOverSeconds = 0.12f;
        public const float BottleBackSeconds = 0.15f;
        /// <summary>The nozzle stands this far above the build's top while squeezing (m).</summary>
        public const float NozzleAbove = 0.16f;
        /// <summary>The ribbon's width (m) and how far it sways side to side (m) at <see cref="RibbonSwayHz"/>.</summary>
        public const float RibbonWidth = 0.012f;
        public const float RibbonSway = 0.03f;
        public const float RibbonSwayHz = 1.6f;

        /// <summary>Where the ribbon lands on the build at <paramref name="t"/> s into the squeeze (a slow zigzag over the stack, m).</summary>
        public static Vector2 RibbonLanding(float t)
        {
            float phase = Mathf.Sin(t * RibbonSwayHz * 2f * Mathf.PI);
            float along = Mathf.Sin(t * RibbonSwayHz * 0.5f * 2f * Mathf.PI);
            return new Vector2(phase * RibbonSway, along * RibbonSway * 1.6f);
        }

        /// <summary>The share 0..1 of the bottle's way over the build <paramref name="t"/> s after the press (or back, after the release).</summary>
        public static float BottleShare(float t, bool over)
        {
            float length = over ? BottleOverSeconds : BottleBackSeconds;
            if (!(t > 0f)) return 0f;
            if (t >= length) return 1f;
            float x = t / length;
            return 1f - (1f - x) * (1f - x);
        }
    }
}
