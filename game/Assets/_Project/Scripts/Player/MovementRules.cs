using UnityEngine;

namespace Worktown.Game.Player
{
    /// <summary>
    /// Movement numbers and math shared by the owner (prediction) and the server (authority)
    /// (spec 010). Pure functions: same input, same result on every machine.
    /// </summary>
    public static class MovementRules
    {
        public const float WalkSpeed = 4f;
        public const float SprintSpeed = 8f;
        /// <summary>Jump apex height in metres; the take-off speed follows from gravity.</summary>
        public const float JumpHeight = 1.2f;
        public const float CrouchSpeed = 2f;
        /// <summary>m/s² towards a higher target speed.</summary>
        public const float Acceleration = 30f;
        /// <summary>m/s² towards a lower target speed (stopping).</summary>
        public const float Deceleration = 40f;
        /// <summary>Degrees per second the character turns towards its move direction.</summary>
        public const float TurnRate = 720f;

        public const float StandHeight = 2f;
        public const float CrouchHeight = 1.2f;

        /// <summary>Clamps a stick/WASD vector to length 1; NaN or infinite becomes zero.</summary>
        public static Vector2 SanitizeMove(Vector2 move)
        {
            if (!IsFinite(move.x) || !IsFinite(move.y)) return Vector2.zero;
            return Vector2.ClampMagnitude(move, 1f);
        }

        /// <summary>Wraps a yaw into 0..360; NaN or infinite becomes 0.</summary>
        public static float SanitizeYaw(float yaw)
        {
            return IsFinite(yaw) ? Mathf.Repeat(yaw, 360f) : 0f;
        }

        /// <summary>World direction for a move input seen from a camera with the given yaw
        /// (x = right, y = forward). Length equals the input length (0..1).</summary>
        public static Vector3 WorldDirection(Vector2 move, float cameraYaw)
        {
            move = SanitizeMove(move);
            Quaternion yaw = Quaternion.Euler(0f, SanitizeYaw(cameraYaw), 0f);
            return yaw * new Vector3(move.x, 0f, move.y);
        }

        /// <summary>Top speed for the current stance. Crouching wins over sprinting.</summary>
        public static float MaxSpeed(bool sprint, bool crouched)
        {
            if (crouched) return CrouchSpeed;
            return sprint ? SprintSpeed : WalkSpeed;
        }

        /// <summary>Take-off speed that reaches <see cref="JumpHeight"/> under the given gravity (positive, m/s²).</summary>
        public static float JumpSpeed(float gravity)
        {
            return Mathf.Sqrt(2f * Mathf.Abs(gravity) * JumpHeight);
        }

        /// <summary>Jumping needs ground under the feet and a standing stance.</summary>
        public static bool CanJump(bool wantsJump, bool grounded, bool crouched)
        {
            return wantsJump && grounded && !crouched;
        }

        /// <summary>Moves the horizontal velocity towards the target with limited acceleration.</summary>
        public static Vector3 Accelerate(Vector3 current, Vector3 target, float delta)
        {
            current.y = 0f;
            target.y = 0f;
            float rate = target.sqrMagnitude >= current.sqrMagnitude ? Acceleration : Deceleration;
            return Vector3.MoveTowards(current, target, rate * delta);
        }

        /// <summary>Turns the yaw towards the direction, at most <see cref="TurnRate"/> per second.
        /// Without a direction the yaw stays.</summary>
        public static float TurnTowards(float currentYaw, Vector3 direction, float delta)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return SanitizeYaw(currentYaw);
            float targetYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            return SanitizeYaw(Mathf.MoveTowardsAngle(currentYaw, targetYaw, TurnRate * delta));
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
