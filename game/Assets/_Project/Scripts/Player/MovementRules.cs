using UnityEngine;

namespace Worktown.Game.Player
{
    /// <summary>
    /// Movement numbers and math shared by the owner (prediction) and the server (authority)
    /// (spec 010). Pure functions: same input, same result on every machine.
    /// </summary>
    public static class MovementRules
    {
        /// <summary>m/s. Spec 025 (D31): calmer speeds for people of real size (was 4 / 8 / 2).</summary>
        public const float WalkSpeed = 2.5f;
        public const float SprintSpeed = 5f;
        /// <summary>Jump apex height in metres; the take-off speed follows from gravity. Spec 025: 0.6 (was 1.2).</summary>
        public const float JumpHeight = 0.6f;
        public const float CrouchSpeed = 1.25f;
        /// <summary>m/s² towards a higher target speed. Spec 025: scaled with the speeds (was 30), so starting takes as long.</summary>
        public const float Acceleration = 19f;
        /// <summary>m/s² towards a lower target speed (stopping). Spec 025: scaled with the speeds (was 40).</summary>
        public const float Deceleration = 25f;
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

        /// <summary>Gravity while rising (m/s², positive). Stronger than real gravity: real gravity
        /// makes game jumps feel floaty (play-test 2026-11-07).</summary>
        public const float RiseGravity = 9.81f * 2f;
        /// <summary>Gravity while falling: stronger again, so landings are quick (common genre tuning).</summary>
        public const float FallGravity = 9.81f * 3f;
        public const float MaxFallSpeed = 30f;

        /// <summary>Take-off speed that reaches <see cref="JumpHeight"/> under <see cref="RiseGravity"/>.</summary>
        public static float JumpSpeed()
        {
            return Mathf.Sqrt(2f * RiseGravity * JumpHeight);
        }

        /// <summary>Next vertical velocity after one step: rise gravity going up, fall gravity coming down,
        /// capped at <see cref="MaxFallSpeed"/>.</summary>
        public static float ApplyGravity(float verticalVelocity, float delta)
        {
            float gravity = verticalVelocity > 0f ? RiseGravity : FallGravity;
            return Mathf.Max(verticalVelocity - gravity * delta, -MaxFallSpeed);
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

        /// <summary>Spec 018: the body yaw after one step. First person faces the (sanitized) camera yaw, so the
        /// player strafes and walks backwards; third person turns towards the move direction
        /// (<see cref="TurnTowards"/>).</summary>
        public static float FacingYaw(bool firstPerson, float currentYaw, float cameraYaw, Vector3 direction, float delta)
        {
            return firstPerson ? SanitizeYaw(cameraYaw) : TurnTowards(currentYaw, direction, delta);
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
