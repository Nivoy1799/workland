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
            return Pick(hits, playerPosition, pickupsTransparent, false, out outOfReach);
        }

        /// <summary>
        /// 039 review (must-fix): as <see cref="Pick(IReadOnlyList{AimHit}, Vector3, bool, out int)"/>; with
        /// <paramref name="spotsTransparent"/> (the hands are empty, so nothing can be put down) a <see cref="AimKind.Spot"/>
        /// neither is a target nor blocks, like pickups with full hands: the ray reaches the station under a surface's thin aim
        /// collider (the plate shelf, the sink, the stove front, the assembly back, the fryer deck). The interactor passes
        /// <c>pickupsTransparent = holding</c> and <c>spotsTransparent = !holding</c>.
        /// </summary>
        public static int Pick(IReadOnlyList<AimHit> hits, Vector3 playerPosition, bool pickupsTransparent, bool spotsTransparent,
            out int outOfReach)
        {
            outOfReach = None;
            if (hits == null) return None;
            int best = None;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < hits.Count; i++)
            {
                if (pickupsTransparent && hits[i].Kind == AimKind.Pickup) continue;
                if (spotsTransparent && hits[i].Kind == AimKind.Spot) continue;
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

        /// <summary>
        /// 041 review: whether a hit of kind <paramref name="kind"/> at <paramref name="distance"/> on the ray stops the right click's
        /// ray put-down (the free spot where the ray meets a surface, at <paramref name="putDownDistance"/>). Only what lies in front of
        /// it counts. A wall (<see cref="AimKind.Other"/>) stops it, and so does a station's body or button, as they stop the left click.
        /// Items and cookware do not (spec 041 Q2: aiming at the pot puts it down next to the pot), nor the station the surface belongs to
        /// (<paramref name="ownsTheSurface"/>: its own top, a tap's cup spot inside the drink dispenser's aim box). The interactor passes
        /// only walls as <see cref="AimKind.Other"/> (another player's held item is no wall).
        /// </summary>
        public static bool BlocksRayPutDown(AimKind kind, float distance, float putDownDistance, bool ownsTheSurface)
        {
            if (float.IsNaN(distance) || !(distance < putDownDistance)) return false;
            switch (kind)
            {
                case AimKind.Other:
                    return true;
                case AimKind.Station:
                case AimKind.Button:
                    return !ownsTheSurface;
                default:
                    return false;
            }
        }
    }
}
