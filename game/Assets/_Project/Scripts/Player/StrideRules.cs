using System.Collections.Generic;
using UnityEngine;

namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 025: the ground speed a walk or run clip shows at playback rate 1, measured from its feet. While a foot is
    /// planted it moves backwards relative to the body as fast as the body moves over the ground, so the clip speed is
    /// the planted foot's backward travel per second of contact. Planted is the lower foot that is close to the lowest
    /// foot height of the clip and does not move forwards; the intervals where a foot lands or lifts and the phases with
    /// both feet in the air (a run) do not count.
    /// Pure: the art builder samples the clip in the editor and passes the foot positions in.
    /// </summary>
    public static class StrideRules
    {
        /// <summary>m: a foot up to this far above the lowest foot point of the clip still counts as on the ground.</summary>
        public const float PlantedHeight = 0.05f;

        /// <summary>
        /// m/s the clip covers at playback rate 1. <paramref name="times"/> in seconds (increasing), the feet in model
        /// space in metres for the model at its in-game height, the model facing +Z. Returns 0 for a clip that stands
        /// still and for bad input (fewer than two samples, different lengths, NaN or infinite values, times that do not
        /// increase, no ground contact).
        /// </summary>
        public static float ClipSpeed(IReadOnlyList<float> times, IReadOnlyList<Vector3> left, IReadOnlyList<Vector3> right)
        {
            if (times == null || left == null || right == null) return 0f;
            int count = times.Count;
            if (count < 2 || left.Count != count || right.Count != count) return 0f;

            float ground = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                if (!IsFinite(times[i]) || !IsFinite(left[i]) || !IsFinite(right[i])) return 0f;
                if (i > 0 && times[i] <= times[i - 1]) return 0f;
                ground = Mathf.Min(ground, Mathf.Min(left[i].y, right[i].y));
            }

            int intervals = count - 1;
            bool[] leftDown = new bool[intervals];
            bool[] rightDown = new bool[intervals];
            for (int i = 0; i < intervals; i++)
            {
                leftDown[i] = Planted(left[i], left[i + 1], ground);
                rightDown[i] = Planted(right[i], right[i + 1], ground);
            }

            double travel = 0;
            double contact = 0;
            for (int i = 0; i < intervals; i++)
            {
                // Only steady contact counts: the intervals where a foot lands or lifts are half swing, half stance.
                bool l = Steady(leftDown, i);
                bool r = Steady(rightDown, i);
                if (!l && !r) continue; // both feet in the air, landing or lifting
                bool useLeft = l && (!r || left[i].y + left[i + 1].y <= right[i].y + right[i + 1].y);
                IReadOnlyList<Vector3> foot = useLeft ? left : right;
                travel += foot[i].z - foot[i + 1].z; // backwards is -Z
                contact += times[i + 1] - times[i];
            }
            if (contact <= 0) return 0f;
            return Mathf.Max(0f, (float)(travel / contact));
        }

        /// <summary>
        /// Spec 025 amendment: the normalized time (0..1 of the loop) at which the foot is furthest forward (+Z), about where
        /// it lands. Two clips blended on the same normalized phase step together when these match. 0 for bad input.
        /// </summary>
        public static float FootForwardPhase(IReadOnlyList<float> times, IReadOnlyList<Vector3> foot)
        {
            if (times == null || foot == null || times.Count < 2 || foot.Count != times.Count) return 0f;
            float start = times[0];
            float length = times[times.Count - 1] - start;
            if (!IsFinite(length) || length <= 0f) return 0f;
            int best = -1;
            for (int i = 0; i < times.Count; i++)
            {
                if (!IsFinite(times[i]) || !IsFinite(foot[i])) return 0f;
                if (best < 0 || foot[i].z > foot[best].z) best = i;
            }
            return Mathf.Repeat((times[best] - start) / length, 1f);
        }

        /// <summary>On the ground over one interval: low, and not moving forwards (a foot moving forwards swings or slides).</summary>
        private static bool Planted(Vector3 from, Vector3 to, float ground) =>
            (from.y + to.y) * 0.5f <= ground + PlantedHeight && to.z <= from.z;

        private static bool Steady(bool[] down, int i) =>
            down[i] && (i == 0 || down[i - 1]) && (i == down.Length - 1 || down[i + 1]);

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool IsFinite(Vector3 v) => IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
    }
}
