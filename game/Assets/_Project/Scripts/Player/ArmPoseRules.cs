using UnityEngine;

namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 044 (034 and 035 feels off): the third-person arm poses the clips do not have. After the animation, an arm is turned at the
    /// shoulder so the hand points at a target: both hands on the sides of a carried delivery box, the right hand on the cloth going
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
    }
}
