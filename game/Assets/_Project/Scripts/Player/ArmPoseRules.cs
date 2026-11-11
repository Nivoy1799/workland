using UnityEngine;

namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 044 (034 and 035 feels off): the third-person arm poses the clips do not have. After the animation, an arm is bent (follow-up
    /// 2026-11-11 evening: a two-bone bend at the shoulder and the elbow, <see cref="TwoBone"/>; before, it only turned at the shoulder)
    /// so the hand reaches a target: both hands on the sides of a carried delivery box, the right hand on the cloth going
    /// round a spill. Pure math; <see cref="CharacterVisual"/> applies it to the rig's bones every frame a target is set.
    /// </summary>
    public static class ArmPoseRules
    {
        /// <summary>m: half the width between the hands on a carried box (its sides).</summary>
        public const float BoxHalfWidth = 0.17f;

        /// <summary>Where the hands hold a carried box: its left and right side at <see cref="HoldViewRules.BoxCarryHeight"/>, in front of
        /// the chest (<see cref="HoldViewRules.BoxCarryForward"/>), for a body at <paramref name="feet"/> facing <paramref name="yaw"/>.</summary>
        public static void BoxHands(Vector3 feet, float yaw, out Vector3 left, out Vector3 right)
        {
            Quaternion frame = Quaternion.Euler(0f, yaw, 0f);
            Vector3 middle = feet + Vector3.up * HoldViewRules.BoxCarryHeight + frame * (Vector3.forward * HoldViewRules.BoxCarryForward);
            left = middle + frame * (Vector3.left * BoxHalfWidth);
            right = middle + frame * (Vector3.right * BoxHalfWidth);
        }

        /// <summary>
        /// The world rotation that turns an upper arm (shoulder at <paramref name="shoulder"/>, hand now at <paramref name="hand"/>) so the
        /// hand points at <paramref name="target"/>, by <paramref name="weight"/> (0: as animated, 1: fully). Identity when a direction is
        /// degenerate. Apply as <c>upperArm.rotation = result * upperArm.rotation</c>.
        /// </summary>
        public static Quaternion Reach(Vector3 shoulder, Vector3 hand, Vector3 target, float weight)
        {
            Vector3 now = hand - shoulder, wanted = target - shoulder;
            if (now.sqrMagnitude < 1e-6f || wanted.sqrMagnitude < 1e-6f || float.IsNaN(weight) || weight <= 0f) return Quaternion.identity;
            Quaternion full = Quaternion.FromToRotation(now, wanted);
            return weight >= 1f ? full : Quaternion.Slerp(Quaternion.identity, full, weight);
        }

        /// <summary>Follow-up 2026-11-11 evening: how far out (to the arm's side) and back the elbow leans, against 1 down.</summary>
        public const float BendOut = 0.5f;
        public const float BendBack = 0.25f;

        /// <summary>
        /// Follow-up 2026-11-11 evening: the way an elbow bends on a body facing <paramref name="yaw"/>: down, out to the arm's side
        /// (<paramref name="left"/> arm: the body's left) and a little back, as a reaching or carrying arm does.
        /// </summary>
        public static Vector3 BendHint(float yaw, bool left)
        {
            Quaternion frame = Quaternion.Euler(0f, yaw, 0f);
            Vector3 side = frame * (left ? Vector3.left : Vector3.right);
            Vector3 back = frame * Vector3.back;
            return Vector3.down + side * BendOut + back * BendBack;
        }

        /// <summary>
        /// Follow-up 2026-11-11 evening (044 known limit, "no elbow bend"): where the elbow of a two-bone arm goes so the hand reaches
        /// <paramref name="target"/>: the shoulder, the target and the two lengths make a triangle, bent towards
        /// <paramref name="bendHint"/>. Out of reach the arm is straight towards the target; too close, the elbow folds as far as the
        /// lengths allow. Degenerate input (no lengths, the target on the shoulder) keeps the elbow along the hint.
        /// </summary>
        public static Vector3 Elbow(Vector3 shoulder, Vector3 target, float upperLength, float lowerLength, Vector3 bendHint)
        {
            Vector3 toTarget = target - shoulder;
            float distance = toTarget.magnitude;
            Vector3 hint = bendHint.sqrMagnitude > 1e-8f ? bendHint.normalized : Vector3.down;
            if (!(upperLength > 1e-5f) || !(lowerLength > 1e-5f) || !(distance > 1e-5f)) return shoulder + hint * Mathf.Max(0f, upperLength);
            Vector3 direction = toTarget / distance;
            float reach = upperLength + lowerLength;
            float d = Mathf.Clamp(distance, Mathf.Abs(upperLength - lowerLength) + 1e-5f, reach);
            float along = (upperLength * upperLength - lowerLength * lowerLength + d * d) / (2f * d);
            float height = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            Vector3 bend = hint - direction * Vector3.Dot(hint, direction);
            if (bend.sqrMagnitude < 1e-8f)
            {
                bend = Vector3.Cross(direction, Vector3.right);
                if (bend.sqrMagnitude < 1e-8f) bend = Vector3.Cross(direction, Vector3.forward);
            }
            return shoulder + direction * along + bend.normalized * height;
        }

        /// <summary>
        /// Follow-up 2026-11-11 evening: the two world turns that bend an animated arm (<paramref name="shoulder"/>,
        /// <paramref name="elbow"/>, <paramref name="hand"/> as animated) so the hand reaches <paramref name="target"/>, by
        /// <paramref name="weight"/> (0: as animated, 1: fully). The bone lengths come from the pose. Apply in order:
        /// <c>upperArm.rotation = upperTurn * upperArm.rotation</c>, then <c>lowerArm.rotation = lowerTurn * lowerArm.rotation</c>.
        /// False (both identity) when the weight is 0 or the pose is degenerate. No allocations.
        /// </summary>
        public static bool TwoBone(Vector3 shoulder, Vector3 elbow, Vector3 hand, Vector3 target, Vector3 bendHint, float weight,
            out Quaternion upperTurn, out Quaternion lowerTurn)
        {
            upperTurn = Quaternion.identity;
            lowerTurn = Quaternion.identity;
            if (float.IsNaN(weight) || weight <= 0f) return false;
            Vector3 upper = elbow - shoulder, lower = hand - elbow;
            float upperLength = upper.magnitude, lowerLength = lower.magnitude;
            if (upperLength < 1e-4f || lowerLength < 1e-4f || (target - shoulder).sqrMagnitude < 1e-8f) return false;
            float w = Mathf.Min(1f, weight);
            Vector3 elbowGoal = Elbow(shoulder, target, upperLength, lowerLength, bendHint);
            upperTurn = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(upper, elbowGoal - shoulder), w);
            Vector3 elbowNow = shoulder + upperTurn * upper;
            Vector3 lowerNow = upperTurn * lower;
            Vector3 wanted = target - elbowNow;
            if (wanted.sqrMagnitude > 1e-8f) lowerTurn = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(lowerNow, wanted), w);
            return true;
        }
    }
}
