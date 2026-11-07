using System.Collections.Generic;
using UnityEngine;

namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Spec 018: picks the first-person interaction target from the hits of one camera-centre ray.
    /// Client side only; the server still checks reach and rate for every request.
    /// </summary>
    public static class AimRules
    {
        /// <summary>No target.</summary>
        public const int None = -1;

        /// <summary>
        /// Index of the target hit, or <see cref="None"/>. The closest hit wins; if it is
        /// <see cref="AimKind.Other"/> there is no target (no aiming through walls). A station or pickup is a
        /// target only within <see cref="PickupRules.MaxReach"/> of the player, the rule the server checks.
        /// Hits with a NaN or negative distance are ignored.
        /// Spec 019: a <see cref="AimKind.Button"/> is a target like a station (reach to its station); it sits on
        /// the station's front, so the ray hits it first, and it also wins a tie with any other hit.
        /// </summary>
        public static int Pick(IReadOnlyList<AimHit> hits, Vector3 playerPosition)
        {
            if (hits == null) return None;
            int best = None;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < hits.Count; i++)
            {
                float distance = hits[i].Distance;
                if (float.IsNaN(distance) || distance < 0f || distance > bestDistance) continue;
                if (distance == bestDistance && hits[i].Kind != AimKind.Button) continue;
                bestDistance = distance;
                best = i;
            }
            if (best == None) return None;

            AimHit hit = hits[best];
            if (hit.Kind != AimKind.Station && hit.Kind != AimKind.Pickup && hit.Kind != AimKind.Button) return None;
            return PickupRules.IsInReach(playerPosition, hit.ObjectPosition) ? best : None;
        }
    }
}
