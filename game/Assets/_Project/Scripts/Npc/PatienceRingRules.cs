using System.Collections.Generic;
using UnityEngine;
using Worktown.Game.Art;
using Worktown.Sim.Economy;

namespace Worktown.Game.Npc
{
    /// <summary>
    /// Spec 032, pure: the patience ring over a waiting guest. A Panel track and a fill from the top clockwise to the share of
    /// patience left; the fill is Sage while more than half is left, Mustard down to a quarter, then Terracotta
    /// (<see cref="GuestPatience.LowShare"/>, "near the end"), and pulses a little there. Palette colours only.
    /// </summary>
    public static class PatienceRingRules
    {
        /// <summary>The ring's diameter in the world (m): readable at the counter, smaller than a head.</summary>
        public const float WorldSize = 0.2f;
        /// <summary>Beside the guest's head (m), below its bubble and the rail.</summary>
        public const float Height = 1.72f; // beside the head, under the ticket rail board (its bottom is at 1.925 m)
        public const float SideOffset = 0.3f;
        public const float PulseHz = 3f;

        public static Color FillColor(float shareLeft)
        {
            // Spec 048: the colours of RingPalette.Current (the colour-blind set when the setting is on).
            return RingPalette.Current.Patience(GuestPatience.IsLow(shareLeft), shareLeft);
        }

        /// <summary>Shown only while the guest waits in the queue (not while it pays or leaves).</summary>
        public static bool IsShown(bool waiting) => waiting;

        /// <summary>The scale pulse in the last quarter (1 elsewhere).</summary>
        public static float Pulse(float shareLeft, float time)
        {
            if (!GuestPatience.IsLow(shareLeft)) return 1f;
            return 1f + 0.08f * Mathf.Sin(time * PulseHz * 2f * Mathf.PI);
        }

        /// <summary>The ring for <paramref name="shareLeft"/> into the lists (cleared first): the track, then the fill.</summary>
        public static void Build(float shareLeft, List<Vector3> vertices, List<Color> colors, List<int> triangles)
        {
            vertices.Clear();
            colors.Clear();
            triangles.Clear();
            float s = Mathf.Clamp01(float.IsNaN(shareLeft) ? 0f : shareLeft);
            HeatRingRules.Arc(0f, 1f, HeatRingRules.TrackInner, HeatRingRules.TrackOuter, HeatRingRules.Track, vertices, colors, triangles);
            if (s > 0f)
                HeatRingRules.Arc(0f, s, HeatRingRules.FillInner, HeatRingRules.FillOuter, FillColor(s), vertices, colors, triangles);
        }
    }
}
