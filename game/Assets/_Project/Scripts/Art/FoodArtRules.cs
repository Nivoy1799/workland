using UnityEngine;
using Worktown.Game.Cooking;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 017: pure rules for drawing a <see cref="FoodLook"/>: which model, how many pieces, the tint for
    /// ingredient and doneness, and where pieces and parts go. No scene access, so it is unit-testable.
    /// </summary>
    public static class FoodArtRules
    {
        /// <summary>Ingredient colours in DemoIngredients order (were in Food, spec 007; spec 026: from the palette).</summary>
        private static readonly Color[] IngredientColors =
        {
            Palette.FoodPotato, Palette.FoodApple, Palette.FoodTomato, Palette.FoodOnion
        };

        /// <summary>Darkening for raw / short / medium / long (open question 3).</summary>
        private static readonly float[] Darkening = { 0f, 0.10f, 0.25f, 0.45f };

        private static readonly Color Golden = Palette.FoodGolden;
        private const float GoldenShift = 0.35f;

        private const byte Potato = 0, Apple = 1, Tomato = 2, Onion = 3;

        public static int IngredientCount => IngredientColors.Length;

        /// <summary>The ingredient colour; Steel grey for an unknown ingredient.</summary>
        public static Color IngredientColor(byte ingredient)
        {
            return ingredient < IngredientColors.Length ? IngredientColors[ingredient] : Palette.Steel;
        }

        /// <summary>
        /// The model for a look's ingredient and shape: the kit's own model where it has one, else the generic
        /// piece tinted with the ingredient colour. Potato sticks are generic sticks until they are fried to at
        /// least medium; only then do they become the fries model. False for an unknown ingredient or shape
        /// (the caller shows the cube).
        /// </summary>
        public static bool TryGetPiece(FoodLook look, out FoodPiece piece)
        {
            piece = default;
            byte ingredient = look.Ingredient;
            if (ingredient >= IngredientColors.Length) return false;
            switch (look.Shape)
            {
                case FoodLook.ShapeWhole:
                    piece = new FoodPiece(WholeKey(ingredient), 1, ingredient == Potato); // no potato in the kit
                    return true;
                case FoodLook.ShapeHalves:
                    if (ingredient == Apple) piece = new FoodPiece(ArtKey.FoodAppleHalf, 2, false);
                    else if (ingredient == Onion) piece = new FoodPiece(ArtKey.FoodOnionHalf, 2, false);
                    else piece = Generic(look.Shape, 2);
                    return true;
                case FoodLook.ShapeSlices:
                    piece = ingredient == Tomato ? new FoodPiece(ArtKey.FoodTomatoSlice, 3, false) : Generic(look.Shape, 4);
                    return true;
                case FoodLook.ShapeSticks:
                    piece = ingredient == Potato && IsFries(look) ? new FoodPiece(ArtKey.FoodFries, 1, false) : Generic(look.Shape, 5);
                    return true;
                case FoodLook.ShapeCubes:
                    piece = Generic(look.Shape, 6);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Fried (fry or saute) to the medium bucket or longer: the sticks are finished fries.</summary>
        public static bool IsFries(FoodLook look)
        {
            return look.Heat == FoodLook.HeatFried && look.Doneness >= FoodLook.DonenessMedium;
        }

        private static FoodPiece Generic(byte shape, int count) => new FoodPiece(GenericPiece(shape), count, true);

        /// <summary>The whole model of a demo ingredient (the potato is a tinted stand-in, see spec 017 notes).</summary>
        public static ArtKey WholeKey(byte ingredient)
        {
            switch (ingredient)
            {
                case Potato: return ArtKey.FoodPotato;
                case Apple: return ArtKey.FoodApple;
                case Tomato: return ArtKey.FoodTomato;
                case Onion: return ArtKey.FoodOnion;
                default: throw new System.ArgumentOutOfRangeException(nameof(ingredient), ingredient, "no whole model");
            }
        }

        /// <summary>The generic piece for a cut shape (used when the kit has no model for that ingredient).</summary>
        public static ArtKey GenericPiece(byte shape)
        {
            switch (shape)
            {
                case FoodLook.ShapeHalves: return ArtKey.FoodPieceHalf;
                case FoodLook.ShapeSlices: return ArtKey.FoodPieceSlice;
                case FoodLook.ShapeSticks: return ArtKey.FoodPieceStick;
                case FoodLook.ShapeCubes: return ArtKey.FoodPieceCube;
                default: throw new System.ArgumentOutOfRangeException(nameof(shape), shape, "no generic piece");
            }
        }

        /// <summary>Fraction the colour is darkened by: 0 / 10 / 25 / 45 percent for raw to long.</summary>
        public static float DarkeningOf(byte doneness)
        {
            return Darkening[Mathf.Min(doneness, Darkening.Length - 1)];
        }

        /// <summary>
        /// Colour multiplier for a piece: the ingredient colour (tinted pieces) or white (own colours), shifted
        /// towards golden when fried, then darkened by doneness. Boiled food is not shifted.
        /// </summary>
        public static Color Tint(FoodLook look, bool tinted)
        {
            Color c = tinted ? IngredientColor(look.Ingredient) : Color.white;
            if (look.Heat == FoodLook.HeatFried && look.Doneness > FoodLook.DonenessRaw)
                c = Color.Lerp(c, c * Golden, GoldenShift);
            float keep = 1f - DarkeningOf(look.Doneness);
            return Palette.Shade(c, keep);
        }

        // ---- Spec 027c: the colour shift on the heat ----

        /// <summary>Key 2 of the shift (brown): a mix of WoodLight and WoodDark, mostly dark (a potato is already tan raw,
        /// so a lighter brown did not read as a change in the visual check).</summary>
        public static readonly Color HeatBrown = Color.Lerp(Palette.WoodLight, Palette.WoodDark, 0.75f);
        /// <summary>How far the golden key pulls the raw colour towards Mustard.</summary>
        public const float HeatGoldenMix = 0.85f;
        /// <summary>Overdone sits this far from brown towards Charcoal.</summary>
        public const float HeatOverdoneMix = 0.45f;
        /// <summary>Boiled food shifts only this much of the way (it softens rather than browns), Burnt included.</summary>
        public const float HeatBoiledStrength = 0.5f;

        /// <summary>
        /// Spec 027c: the colour of a piece on the heat, through four palette-anchored keys by <paramref name="progress01"/>
        /// (0 raw: the piece's own colour, <see cref="HeatZoneRules.GoodShare"/> golden: towards Mustard, 1 brown:
        /// <see cref="HeatBrown"/>); Overdone goes on towards Charcoal, Burnt is Charcoal. Food in the pot
        /// (<paramref name="boiled"/>) shifts <see cref="HeatBoiledStrength"/> of the way. <see cref="HeatZone.None"/> gives
        /// the bucket colour (<see cref="Tint"/>) as before.
        /// </summary>
        public static Color HeatTint(FoodLook look, bool tinted, HeatZone zone, float progress01, bool boiled)
        {
            if (zone == HeatZone.None) return Tint(look, tinted);
            Color raw = tinted ? IngredientColor(look.Ingredient) : Color.white;
            Color golden = Color.Lerp(raw, Palette.Mustard, HeatGoldenMix);
            Color shifted;
            if (zone == HeatZone.Burnt) shifted = Palette.Charcoal;
            else if (zone == HeatZone.Overdone) shifted = Color.Lerp(HeatBrown, Palette.Charcoal, HeatOverdoneMix);
            else
            {
                float p = Mathf.Clamp01(float.IsNaN(progress01) ? 0f : progress01);
                const float g = HeatZoneRules.GoodShare;
                shifted = p <= g ? Color.Lerp(raw, golden, p / g) : Color.Lerp(golden, HeatBrown, (p - g) / (1f - g));
            }
            Color c = boiled ? Color.Lerp(raw, shifted, HeatBoiledStrength) : shifted;
            c.a = 1f;
            return c;
        }

        /// <summary>Position and yaw of piece <paramref name="index"/> of <paramref name="count"/> in a small pile.</summary>
        public static Vector3 PilePosition(int index, int count)
        {
            if (count <= 1) return Vector3.zero;
            float radius = count == 2 ? 0.045f : 0.05f;
            float angle = (index * 360f / count + 20f) * Mathf.Deg2Rad;
            float lift = count > 3 && index % 2 == 1 ? 0.012f : 0f;
            return new Vector3(Mathf.Cos(angle) * radius, lift, Mathf.Sin(angle) * radius);
        }

        public static float PileYaw(int index, int count)
        {
            return count <= 1 ? 0f : (index * 67f + 15f) % 360f;
        }

        /// <summary>Where part <paramref name="index"/> of <paramref name="count"/> sits on a plate or in a bowl.</summary>
        public static Vector3 PartPosition(int index, int count, float radius)
        {
            if (count <= 1) return Vector3.zero;
            float angle = (index * 360f / count + 45f) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }
    }
}
