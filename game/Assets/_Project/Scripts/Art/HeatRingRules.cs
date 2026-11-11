using System.Collections.Generic;
using UnityEngine;
using Worktown.Game.Cooking;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 027c: pure rules of the heat ring over a pan or pot (and later the fryer and griddle, specs 029/030): when it
    /// shows, its colours from the palette, its fixed size in pixels, where it stands next to the vessel, and the ring
    /// mesh (a dark track, the good window marked on the rim, the fill). No scene access, so it is unit-testable.
    /// </summary>
    public static class HeatRingRules
    {
        /// <summary>Diameter on screen in pixels (spec 027c default).</summary>
        public const float RingPixels = 48f;
        /// <summary>The ring stays this many seconds after the vessel left the heat ("still warm").</summary>
        public const float WarmSeconds = 5f;
        /// <summary>Gap between the vessel's contents and the ring's edge, in pixels, to the camera's right and up.</summary>
        public const float GapPixels = 18f;
        /// <summary>In first person the ring hides when the view centre is within its radius plus this margin (pixels).</summary>
        public const float CentreMarginPixels = 10f;
        /// <summary>Segments of a full circle.</summary>
        public const int Segments = 64;

        // Radii as a share of the outer radius.
        public const float TrackInner = 0.40f;
        public const float TrackOuter = 0.86f;
        public const float FillInner = 0.46f;
        public const float FillOuter = 0.80f;
        public const float MarkInner = 0.86f;
        public const float MarkOuter = 1f;

        public static readonly Color Track = Palette.Panel;
        public static readonly Color GoodMark = Palette.Sage;
        public static readonly Color PerfectMark = Palette.Mustard;

        /// <summary>The fill's colour: Cream while cooking, Sage when good, Mustard when perfect, Terracotta overdone,
        /// Brick burnt (spec 048: from <see cref="RingPalette.Current"/>, the colour-blind set when the setting is on).</summary>
        public static Color FillColor(HeatZone zone) => RingPalette.Current.Fill(zone);

        /// <summary>
        /// Shown while something is on the heat (zone not None), not carried, and the vessel stands on a burner that is on
        /// or left it at most <see cref="WarmSeconds"/> ago.
        /// </summary>
        public static bool IsShown(HeatZone zone, bool held, bool onHeat, float secondsSinceHeat)
        {
            if (zone == HeatZone.None || held) return false;
            return onHeat || (secondsSinceHeat >= 0f && secondsSinceHeat <= WarmSeconds);
        }

        /// <summary>World size of one pixel at <paramref name="distance"/> in front of a perspective camera.</summary>
        public static float WorldPerPixel(float distance, float verticalFovDegrees, float screenHeight)
        {
            if (!(distance > 0f) || !(screenHeight > 0f) || !(verticalFovDegrees > 0f)) return 0f;
            return 2f * distance * Mathf.Tan(verticalFovDegrees * 0.5f * Mathf.Deg2Rad) / screenHeight;
        }

        /// <summary>True when the screen centre lies within the ring's radius plus <see cref="CentreMarginPixels"/>.</summary>
        public static bool CoversCentre(Vector2 ringScreen, float radiusPixels, Vector2 screenSize)
        {
            Vector2 centre = screenSize * 0.5f;
            float reach = radiusPixels + CentreMarginPixels;
            return (ringScreen - centre).sqrMagnitude <= reach * reach;
        }

        /// <summary>The direction (x right, y up) of a share 0..1 of the circle, clockwise from the top.</summary>
        public static Vector2 Direction(float share)
        {
            float angle = (90f - 360f * share) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        /// <summary>
        /// Builds the ring into the lists (cleared first), in a unit circle in the XY plane, facing -Z: the track, the good
        /// window on the rim (Sage, its Perfect half Mustard), then the fill from the top clockwise to
        /// <paramref name="progress01"/> in <see cref="FillColor"/>. Later triangles draw over earlier ones.
        /// </summary>
        public static void Build(float progress01, HeatZone zone, List<Vector3> vertices, List<Color> colors, List<int> triangles)
        {
            vertices.Clear();
            colors.Clear();
            triangles.Clear();
            float p = Mathf.Clamp01(float.IsNaN(progress01) ? 0f : progress01);
            Arc(0f, 1f, TrackInner, TrackOuter, Track, vertices, colors, triangles);
            Arc(HeatZoneRules.GoodShare, HeatZoneRules.PerfectShare, MarkInner, MarkOuter, RingPalette.Current.Good, vertices, colors, triangles);
            Arc(HeatZoneRules.PerfectShare, 1f, MarkInner, MarkOuter, RingPalette.Current.Perfect, vertices, colors, triangles);
            if (p > 0f) Arc(0f, p, FillInner, FillOuter, FillColor(zone), vertices, colors, triangles);
        }

        /// <summary>Spec 030: the gap between the two halves of a griddle ring, at the top and the bottom (share of the circle).</summary>
        public const float HalfGap = 0.008f;

        /// <summary>
        /// Spec 030: the griddle's two-half ring (one half per side of the item): side 1 on the left half, side 2 on the right,
        /// each filling from the top downwards to its own progress in its zone's colour, with the good window marked on its rim
        /// (mirrored on the left). The half whose side is down (cooking now) gets a small Cream tick on its rim. Into the
        /// lists (cleared first), like <see cref="Build"/>.
        /// </summary>
        public static void BuildHalves(float side1Progress, HeatZone side1Zone, float side2Progress, HeatZone side2Zone, int downSide,
            List<Vector3> vertices, List<Color> colors, List<int> triangles)
        {
            vertices.Clear();
            colors.Clear();
            triangles.Clear();
            Arc(0f, 1f, TrackInner, TrackOuter, Track, vertices, colors, triangles);
            float g = HeatZoneRules.GoodShare * 0.5f, p = HeatZoneRules.PerfectShare * 0.5f;
            // Side 2, right: shares 0..0.5 clockwise from the top.
            Arc(g, p, MarkInner, MarkOuter, RingPalette.Current.Good, vertices, colors, triangles);
            Arc(p, 0.5f - HalfGap, MarkInner, MarkOuter, RingPalette.Current.Perfect, vertices, colors, triangles);
            // Side 1, left: mirrored, shares 1..0.5 counter-clockwise from the top.
            Arc(1f - p, 1f - g, MarkInner, MarkOuter, RingPalette.Current.Good, vertices, colors, triangles);
            Arc(0.5f + HalfGap, 1f - p, MarkInner, MarkOuter, RingPalette.Current.Perfect, vertices, colors, triangles);
            float p1 = Mathf.Clamp01(float.IsNaN(side1Progress) ? 0f : side1Progress);
            float p2 = Mathf.Clamp01(float.IsNaN(side2Progress) ? 0f : side2Progress);
            if (p2 > 0f) Arc(HalfGap, Mathf.Max(HalfGap, 0.5f * p2 - HalfGap), FillInner, FillOuter, FillColor(side2Zone), vertices, colors, triangles);
            if (p1 > 0f) Arc(Mathf.Min(1f - HalfGap, 1f - 0.5f * p1 + HalfGap), 1f - HalfGap, FillInner, FillOuter, FillColor(side1Zone),
                vertices, colors, triangles);
            float mid = downSide == 1 ? 0.25f : 0.75f;
            Arc(mid - 0.03f, mid + 0.03f, MarkOuter, MarkOuter + 0.14f, Palette.Cream, vertices, colors, triangles);
        }

        /// <summary>
        /// The palette colours (sRGB) as a mesh's vertex colours need them: in a linear colour space project the shader
        /// takes vertex colours as linear, so they are converted (else every colour looks washed out). In place.
        /// </summary>
        public static void ToVertexColors(List<Color> colors, bool linearColorSpace)
        {
            if (!linearColorSpace) return;
            for (int i = 0; i < colors.Count; i++) colors[i] = colors[i].linear;
        }

        /// <summary>A band between two radii from share <paramref name="from"/> to <paramref name="to"/>, clockwise.</summary>
        public static void Arc(float from, float to, float inner, float outer, Color color,
            List<Vector3> vertices, List<Color> colors, List<int> triangles)
        {
            if (!(to > from)) return;
            int steps = Mathf.Max(1, Mathf.CeilToInt((to - from) * Segments));
            int first = vertices.Count;
            for (int i = 0; i <= steps; i++)
            {
                Vector2 d = Direction(from + (to - from) * i / steps);
                vertices.Add(new Vector3(d.x * inner, d.y * inner, 0f));
                vertices.Add(new Vector3(d.x * outer, d.y * outer, 0f));
                colors.Add(color);
                colors.Add(color);
            }
            for (int i = 0; i < steps; i++)
            {
                int a = first + i * 2;
                // Clockwise as seen from -Z (the camera side), so the front face looks at the camera.
                triangles.Add(a); triangles.Add(a + 1); triangles.Add(a + 3);
                triangles.Add(a); triangles.Add(a + 3); triangles.Add(a + 2);
            }
        }
    }
}
