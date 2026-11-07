using UnityEngine;

namespace Worktown.Game.Social
{
    /// <summary>Pure rules for non-verbal communication (spec 012).</summary>
    public static class SocialRules
    {
        public const int WheelSlots = 8;
        /// <summary>Stick/mouse offsets shorter than this pick nothing (centre of the wheel).</summary>
        public const float DeadZone = 0.35f;
        /// <summary>Pings reach as far as network interest (40 m), measured from the sender.</summary>
        public const float PingRange = 40f;
        public const float PingSeconds = 6f;
        public const float BubbleSeconds = 3f;
        /// <summary>Rate limit: a burst of this many actions, then one per <see cref="RefillSeconds"/>.</summary>
        public const int Burst = 3;
        public const float RefillSeconds = 1f;

        /// <summary>Wheel slot for a direction (x right, y up); 0 is the top, counting clockwise.
        /// -1 inside the dead zone or for non-finite input.</summary>
        public static int WheelSlot(Vector2 direction)
        {
            if (float.IsNaN(direction.x) || float.IsNaN(direction.y) || float.IsInfinity(direction.x) || float.IsInfinity(direction.y))
                return -1;
            if (direction.magnitude < DeadZone) return -1;
            float angle = Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg; // 0 = up, 90 = right
            if (angle < 0f) angle += 360f;
            float size = 360f / WheelSlots;
            return (int)((angle + size * 0.5f) / size) % WheelSlots;
        }

        /// <summary>A ping must be a finite point within <see cref="PingRange"/> of the sender.</summary>
        public static bool IsValidPing(Vector3 sender, Vector3 point)
        {
            if (!IsFinite(point.x) || !IsFinite(point.y) || !IsFinite(point.z)) return false;
            return (point - sender).sqrMagnitude <= PingRange * PingRange;
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
