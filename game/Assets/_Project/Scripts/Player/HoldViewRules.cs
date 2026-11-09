using UnityEngine;

namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 031b item 4, pure: where held food (and builds, cookware, tools other than the knife) is drawn in your first-person
    /// view. The item stands whole in the lower right: a wrap (0.25 m) fills about <see cref="ReferenceShare"/> of the view height,
    /// smaller food less, anything that would fill more than <see cref="MaxShare"/> is scaled down to it, so nothing large ever
    /// reaches the view centre. It is tilted a little towards the eye, so the top of a build or a plate shows.
    /// Screen positions are fractions of the view (0..1 from the bottom left).
    /// </summary>
    public static class HoldViewRules
    {
        /// <summary>The reference item: a wrap, 0.25 m (docs/art-direction.md).</summary>
        public const float ReferenceSize = 0.25f;
        /// <summary>The reference item fills this share of the view height.</summary>
        public const float ReferenceShare = 0.25f;
        /// <summary>No held item fills more of the view height than this (larger ones are scaled down).</summary>
        public const float MaxShare = 0.32f;
        /// <summary>The item's centre on screen, as a fraction of the view width from the left.</summary>
        public const float CentreX = 0.75f;
        /// <summary>The item's centre on screen, as a fraction of the view height from the bottom.</summary>
        public const float CentreY = 0.21f;
        /// <summary>Degrees the item's top is tilted towards the eye.</summary>
        public const float TiltDegrees = 28f;

        /// <summary>The tilt of a held item in camera space (its top towards the eye).</summary>
        public static Quaternion Tilt => Quaternion.Euler(-TiltDegrees, 0f, 0f);

        private static float HalfTan(float verticalFov)
        {
            float fov = float.IsNaN(verticalFov) || verticalFov < 10f ? 70f : Mathf.Min(verticalFov, 150f);
            return Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
        }

        /// <summary>How far in front of the eye the item's centre stands (m): the reference item fills its share there.</summary>
        public static float Depth(float verticalFov) => ReferenceSize / (ReferenceShare * 2f * HalfTan(verticalFov));

        /// <summary>The share of the view height an item of <paramref name="size"/> m fills at <see cref="Depth"/>, unscaled.</summary>
        public static float Share(float size, float verticalFov)
        {
            if (float.IsNaN(size) || size <= 0f) return 0f;
            return size / (2f * Depth(verticalFov) * HalfTan(verticalFov));
        }

        /// <summary>The scale an item of <paramref name="size"/> m (its largest side) is drawn at: 1, or less so it fills at most
        /// <see cref="MaxShare"/>.</summary>
        public static float FitScale(float size, float verticalFov)
        {
            float share = Share(size, verticalFov);
            return share > MaxShare ? MaxShare / share : 1f;
        }

        /// <summary>The item's centre in camera space (right, up, forward) for the view's <paramref name="verticalFov"/> and
        /// <paramref name="aspect"/> (width / height).</summary>
        public static Vector3 CentreInView(float verticalFov, float aspect)
        {
            float depth = Depth(verticalFov);
            float halfHeight = depth * HalfTan(verticalFov);
            float a = float.IsNaN(aspect) || aspect <= 0.1f ? 16f / 9f : aspect;
            return new Vector3((CentreX * 2f - 1f) * halfHeight * a, (CentreY * 2f - 1f) * halfHeight, depth);
        }

        /// <summary>
        /// The screen rectangle (fractions of the view) an item of <paramref name="size"/> m covers at most, drawn at
        /// <see cref="FitScale"/>: a square of its largest side around <see cref="CentreX"/>, <see cref="CentreY"/> (a bound).
        /// </summary>
        public static Rect ScreenBound(float size, float verticalFov, float aspect)
        {
            float a = float.IsNaN(aspect) || aspect <= 0.1f ? 16f / 9f : aspect;
            float h = Share(size, verticalFov) * FitScale(size, verticalFov);
            float w = h / a;
            return new Rect(CentreX - w * 0.5f, CentreY - h * 0.5f, w, h);
        }
    }
}
