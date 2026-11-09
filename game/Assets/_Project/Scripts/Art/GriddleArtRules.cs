using UnityEngine;
using Worktown.Game.Cooking;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 030, pure: the griddle's looks. The flip is a <see cref="FlipSeconds"/> arc (up and down by <see cref="FlipHeight"/>)
    /// with a half turn about the spot's x axis; grill marks show on a side once it reached the good window, darker as it goes
    /// on. Also the graybox griddle's measures (shared by the art catalog and the scene builder).
    /// </summary>
    public static class GriddleArtRules
    {
        /// <summary>The flip arc's length (s, spec 030: 0.25 s).</summary>
        public const float FlipSeconds = 0.25f;
        /// <summary>The top of the arc above the plate (m).</summary>
        public const float FlipHeight = 0.12f;

        /// <summary>The graybox griddle: a steel plate on a cabinet, as wide as the plate plus a rim, worktop deep, worktop high.</summary>
        public const float Width = 1.02f;
        /// <summary>The plate's top above the floor (m): on the 0.90 m worktop line.</summary>
        public const float PlateTop = ArtSizeTable.WorktopHeight;
        /// <summary>The steel plate's thickness (m).</summary>
        public const float PlateThickness = 0.025f;
        /// <summary>The station's depth (the worktop depth).</summary>
        public const float Depth = 1.2f;
        /// <summary>
        /// The plate's centre in the station's space (x right as seen from the aisle, z from the station's centre, -z towards the
        /// aisle): the 0.9 × 0.6 m plate (GriddleRules) sits at the front, behind it a steel deck (a place surface for the spatula
        /// and a plate).
        /// </summary>
        public const float PlateCenterZ = -0.27f;
        public const float DeckCenterZ = 0.31f;
        public const float DeckDepth = 0.5f;
        /// <summary>The six spots: three across (x), two deep (z, row 0 at the front).</summary>
        public static readonly float[] SpotX = { -0.3f, 0f, 0.3f };
        public static readonly float[] SpotZ = { -0.42f, -0.12f };
        /// <summary>A spot's aim collider (m across) and the height of its contents above the plate.</summary>
        public const float SpotSize = 0.28f;
        public const float SpotColliderHeight = 0.05f;
        /// <summary>The power knob on the front face, right of the station sign (m), as on the fryer.</summary>
        public const float KnobX = 0.36f;
        public const float ButtonHeight = 0.72f;
        public const float LabelHeight = 0.855f;

        /// <summary>Where spot <paramref name="spot"/> lies in the station's space (x, z).</summary>
        public static Vector2 SpotPosition(int spot)
        {
            int columns = SpotX.Length;
            if (spot < 0 || spot >= columns * SpotZ.Length) return Vector2.zero;
            return new Vector2(SpotX[spot % columns], SpotZ[spot / columns]);
        }

        /// <summary>The share 0..1 of the flip done after <paramref name="t"/> s (smooth start and end).</summary>
        public static float FlipShare(float t)
        {
            if (!(t > 0f)) return 0f;
            if (t >= FlipSeconds) return 1f;
            float x = t / FlipSeconds;
            return x * x * (3f - 2f * x);
        }

        /// <summary>The height above the rest pose after <paramref name="t"/> s (a parabola, 0 at both ends).</summary>
        public static float Lift(float t)
        {
            if (!(t > 0f) || t >= FlipSeconds) return 0f;
            float x = t / FlipSeconds;
            return 4f * FlipHeight * x * (1f - x);
        }

        /// <summary>The turn (degrees about x) after <paramref name="t"/> s: 0 to 180.</summary>
        public static float Turn(float t) => 180f * FlipShare(t);

        /// <summary>True while a flip that started <paramref name="t"/> s ago is still in the air.</summary>
        public static bool IsFlipping(float t) => t >= 0f && t < FlipSeconds;

        /// <summary>Grill marks show on a side from the good window on.</summary>
        public static bool MarksShown(HeatZone zone) => zone >= HeatZone.Good;

        /// <summary>The marks' colour: golden brown when good, darker when perfect, charcoal past the window.</summary>
        public static Color MarkColor(HeatZone zone)
        {
            switch (zone)
            {
                case HeatZone.Good: return Palette.WoodDark;
                case HeatZone.Perfect: return Color.Lerp(Palette.WoodDark, Palette.Charcoal, 0.6f);
                default: return Palette.Charcoal;
            }
        }
    }
}
