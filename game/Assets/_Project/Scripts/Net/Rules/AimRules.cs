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

        /// <summary>Same as <see cref="Pick(IReadOnlyList{AimHit}, Vector3, bool)"/> with pickups solid.</summary>
        public static int Pick(IReadOnlyList<AimHit> hits, Vector3 playerPosition)
        {
            return Pick(hits, playerPosition, false);
        }

        /// <summary>
        /// Index of the target hit, or <see cref="None"/>. The closest hit wins; if it is
        /// <see cref="AimKind.Other"/> there is no target (no aiming through walls). A station, pickup or spot is a
        /// target only within <see cref="PickupRules.MaxReach"/> of the player, the rule the server checks.
        /// Hits with a NaN or negative distance are ignored.
        /// Spec 019: a <see cref="AimKind.Button"/> is a target like a station (reach to its station); it sits on
        /// the station's front, so the ray hits it first, and it also wins a tie with any other hit.
        /// Spec 020: a <see cref="AimKind.Spot"/> (the top of a place surface) is a target, reach measured to the
        /// chosen spot. With <paramref name="pickupsTransparent"/> (the hands are full, so nothing can be picked
        /// up) pickups neither are targets nor block: the ray finds the spot or station behind them.
        /// Spec 021: a <see cref="AimKind.Vessel"/> (cookware) is never transparent: with full hands it is where food
        /// goes in or a pan pours into; reach is measured to the vessel, as the server does.
        /// </summary>
        public static int Pick(IReadOnlyList<AimHit> hits, Vector3 playerPosition, bool pickupsTransparent)
        {
            return Pick(hits, playerPosition, pickupsTransparent, out _);
        }

        /// <summary>
        /// Same as <see cref="Pick(IReadOnlyList{AimHit}, Vector3, bool)"/>; when there is no target because the closest
        /// hit is a station, button, item or spot beyond <see cref="PickupRules.MaxReach"/>, <paramref name="outOfReach"/>
        /// is its index (spec 020: shown grey with "Too far"), else <see cref="None"/>.
        /// </summary>
        public static int Pick(IReadOnlyList<AimHit> hits, Vector3 playerPosition, bool pickupsTransparent, out int outOfReach)
        {
            outOfReach = None;
            if (hits == null) return None;
            int best = None;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < hits.Count; i++)
            {
                if (pickupsTransparent && hits[i].Kind == AimKind.Pickup) continue;
                float distance = hits[i].Distance;
                if (float.IsNaN(distance) || distance < 0f || distance > bestDistance) continue;
                if (distance == bestDistance && hits[i].Kind != AimKind.Button) continue;
                bestDistance = distance;
                best = i;
            }
            if (best == None) return None;

            AimHit hit = hits[best];
            if (hit.Kind == AimKind.Other) return None;
            if (PickupRules.IsInReach(playerPosition, hit.ObjectPosition)) return best;
            outOfReach = best;
            return None;
        }
    }
}
