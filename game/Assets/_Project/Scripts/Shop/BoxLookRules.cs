using UnityEngine;

namespace Worktown.Game.Shop
{
    /// <summary>Spec 034, pure: the delivery box's unpack look (SupplyBox plays it at the click).</summary>
    public static class BoxLookRules
    {
        /// <summary>s: the unpack look (the box tips towards the slot, shakes, and comes back).</summary>
        public const float UnpackSeconds = 0.36f;
        /// <summary>The most the box tips forward while it is unpacked (degrees).</summary>
        public const float UnpackTilt = 32f;

        /// <summary>The box's tilt (degrees) <paramref name="t"/> seconds into the unpack look: up to the tilt, a shake at the top, back.</summary>
        public static float UnpackAngle(float t)
        {
            if (t <= 0f || t >= UnpackSeconds) return 0f;
            float u = t / UnpackSeconds;
            float lift = Mathf.Sin(u * Mathf.PI); // up and back down
            float shake = Mathf.Sin(u * Mathf.PI * 6f) * 0.18f * lift;
            return UnpackTilt * (lift + shake);
        }
    }
}
