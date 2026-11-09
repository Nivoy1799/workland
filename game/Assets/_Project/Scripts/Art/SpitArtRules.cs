using UnityEngine;
using Worktown.Game.Cooking;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 036, pure: the graybox seitan spit's sizes and the numbers of its looks (the catalog model, the scene builder and SpitView
    /// share them). Station-local metres, the origin on the counter top under the body's middle, the cook's side (the front) on -Z:
    /// a steel base plate that reaches forward under the catch tray and the knob; at the back the heat panel (its glowing face towards
    /// the cone); the vertical skewer with the cone of stacked discs (WoodLight raw, WoodDark browned, Charcoal burnt; wider at the top,
    /// shrinking as it is used) under the motor housing; the catch tray in front. The body (the aim box) is the panel and the cone: the
    /// tray and the knob stand in front of it on the base.
    /// </summary>
    public static class SpitArtRules
    {
        /// <summary>The body (the aim box of the station): the cone, the panel and the housing.</summary>
        public const float Width = 0.56f;
        public const float Depth = 0.32f;
        public const float Height = 0.78f;
        /// <summary>The body's front face (local z): the tray and the knob stand in front of it.</summary>
        public const float FrontZ = -Depth * 0.5f;

        public static readonly Vector3 BaseCentre = new Vector3(0f, 0.015f, 0f);
        public static readonly Vector3 BaseSize = new Vector3(Width, 0.03f, Depth);
        /// <summary>The steel plate in front of the body that carries the catch tray and the knob (a scene part).</summary>
        public static readonly Vector3 FrontPlateCentre = new Vector3(0f, 0.0125f, -0.26f);
        public static readonly Vector3 FrontPlateSize = new Vector3(Width, 0.025f, 0.2f);
        public static readonly Vector3 PanelCentre = new Vector3(0f, 0.38f, 0.125f);
        public static readonly Vector3 PanelSize = new Vector3(0.46f, 0.62f, 0.05f);
        /// <summary>The glowing face of the panel, towards the cone (a scene part: its colour follows the knob).</summary>
        public static readonly Vector3 GlowCentre = new Vector3(0f, 0.37f, 0.097f);
        public static readonly Vector3 GlowSize = new Vector3(0.4f, 0.52f, 0.006f);
        public static readonly Vector3 HousingCentre = new Vector3(0f, 0.745f, 0.05f); // its top is the spit's height
        public static readonly Vector3 HousingSize = new Vector3(0.18f, 0.07f, 0.24f);
        public static readonly Vector3 SkewerCentre = new Vector3(0f, 0.39f, -0.02f);
        public static readonly Vector3 SkewerSize = new Vector3(0.018f, 0.7f, 0.018f);

        /// <summary>The cone's axis (x, z) and its height range.</summary>
        public static readonly Vector2 ConeAxis = new Vector2(0f, -0.02f);
        public const float ConeBottom = 0.1f;
        public const float ConeTop = 0.6f;
        public const int Discs = 10;
        public const float RadiusBottom = 0.085f;
        public const float RadiusTop = 0.135f;
        /// <summary>What is left round the skewer of a cone that ran out.</summary>
        public const float CoreRadius = 0.025f;
        /// <summary>The cone turns this fast while a cone is on (degrees a second).</summary>
        public const float TurnDegreesPerSecond = 40f;

        /// <summary>The catch tray in front of the cone, on the base.</summary>
        public static readonly Vector3 TrayCentre = new Vector3(0f, 0.045f, -0.25f);
        public static readonly Vector3 TraySize = new Vector3(0.3f, 0.03f, 0.18f);
        /// <summary>The heat knob on the base's front right (a StationButton), in front of the body.</summary>
        public static readonly Vector3 KnobCentre = new Vector3(0.21f, 0.06f, -0.19f);
        public static readonly Vector3 KnobSize = new Vector3(0.05f, 0.05f, 0.04f);
        /// <summary>The cone slot's anchor (the hold's target): the cone's middle on the cook's side.</summary>
        public static Vector3 ConeAnchor => new Vector3(ConeAxis.x, (ConeBottom + ConeTop) * 0.5f, ConeAxis.y);

        /// <summary>Curls in flight at most, and how long one falls from the cone into the tray (s).</summary>
        public const int MaxCurls = 12;
        public const float CurlFallSeconds = 0.35f;
        /// <summary>A new curl this often while shaving (s).</summary>
        public const float CurlEvery = 0.07f;

        public static float DiscHeight => (ConeTop - ConeBottom) / Discs;

        /// <summary>The middle height of disc <paramref name="i"/> (0 the bottom).</summary>
        public static float DiscY(int i) => ConeBottom + (Mathf.Clamp(i, 0, Discs - 1) + 0.5f) * DiscHeight;

        /// <summary>
        /// The radius of disc <paramref name="i"/> with <paramref name="shareLeft"/> (0..1) of the cone left: a full cone is wider at the
        /// top; it shrinks by area towards the core (half the seitan gone is half the area of each disc's ring).
        /// </summary>
        public static float DiscRadius(int i, float shareLeft)
        {
            float full = Mathf.Lerp(RadiusBottom, RadiusTop, Mathf.Clamp01(i / (float)(Discs - 1)));
            float s = Mathf.Clamp01(shareLeft);
            return Mathf.Sqrt(CoreRadius * CoreRadius + (full * full - CoreRadius * CoreRadius) * s);
        }

        /// <summary>
        /// The outer layer's colour from the cone vessel's zone and progress (the 027c bytes): raw WoodLight, browning to WoodDark through
        /// the cooking and the good window, darker when overdone, Charcoal when burnt. Every second disc a little lighter (the layers).
        /// </summary>
        public static Color OuterColour(HeatZone zone, float progress01, int disc)
        {
            Color c;
            switch (zone)
            {
                case HeatZone.Burnt: c = Color.Lerp(Palette.WoodDark, Palette.Charcoal, 0.85f); break;
                case HeatZone.Overdone: c = Color.Lerp(Palette.WoodDark, Palette.Charcoal, 0.45f); break;
                case HeatZone.None:
                case HeatZone.Raw: c = Palette.WoodLight; break;
                default:
                    float t = Mathf.Clamp01(progress01);
                    c = Color.Lerp(Palette.WoodLight, Palette.WoodDark, t * t * (3f - 2f * t));
                    break;
            }
            return disc % 2 == 1 ? Color.Lerp(c, Palette.WoodLight, 0.12f) : c;
        }

        /// <summary>The panel's glow (an emission colour) at heat level <paramref name="level"/>: none off, warm low, bright high.</summary>
        public static Color GlowColour(int level) =>
            level >= Spit.High ? Palette.Terracotta * 1.8f : level == Spit.Low ? Palette.Terracotta * 0.8f : Color.black;

        /// <summary>The knob's light: Charcoal off, Mustard low, Terracotta high.</summary>
        public static Color KnobColour(int level) => level >= Spit.High ? Palette.Terracotta : level == Spit.Low ? Palette.Mustard : Palette.Charcoal;

        /// <summary>The panel's own colour (lit Charcoal when off, Terracotta when on).</summary>
        public static Color PanelColour(int level) => level == Spit.Off ? Palette.Charcoal : Palette.Terracotta;

        /// <summary>
        /// Where a curl is <paramref name="t"/> seconds after it left the cone at <paramref name="from"/>, falling on an arc into the tray at
        /// <paramref name="to"/> (a little hop out, then down, <see cref="CurlFallSeconds"/>).
        /// </summary>
        public static Vector3 CurlPoint(Vector3 from, Vector3 to, float t)
        {
            float u = Mathf.Clamp01(t / CurlFallSeconds);
            Vector3 p = Vector3.Lerp(from, to, u);
            p.y = Mathf.Lerp(from.y, to.y, u * u) + 0.04f * Mathf.Sin(u * Mathf.PI);
            return p;
        }

        /// <summary>The height on the cone a shave's knife is at <paramref name="seconds"/> into the hold: top to bottom, then again.</summary>
        public static float KnifeHeight(double seconds)
        {
            float stroke = (float)(seconds / 0.8 - System.Math.Floor(seconds / 0.8));
            return Mathf.Lerp(ConeTop - 0.06f, ConeBottom + 0.06f, stroke);
        }

        /// <summary>The held knife's sawing motion in first person while shaving (0..1, about three strokes a second).</summary>
        public static float KnifeStroke(double seconds) => 0.5f + 0.5f * Mathf.Sin((float)(seconds * 2.0 * System.Math.PI * 3.0));

        /// <summary>The shave's share of the longest hold (0..1), for the rasp's pitch.</summary>
        public static float HoldShare(double seconds) => Mathf.Clamp01((float)(seconds / Spit.MaxShaveSeconds));
    }
}
