using UnityEngine;

namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Spec 039 part 3, pure: how a tool with a handle (the knife, the spatula, the wipe cloth, any later tool) lies on a place
    /// surface: the way a cook leaves it, the handle towards the cook. A tool model's handle points along its local
    /// <see cref="HandleAxis"/> (+Z: the knife's and the spatula's grip; the cloth's striped hem), the blade or head the other way.
    /// Every place surface's cook side is its local -Z (the back line's tops face the aisle, the front counter is turned
    /// round). Only the yaw is decided here; the tool keeps its resting tilt (the knife on its edge, PlaceSpots.TiltOf).
    /// </summary>
    public static class ToolRest
    {
        /// <summary>A placed tool's handle points to the placer, snapped to this many degrees from the surface's facing.</summary>
        public const float SnapDegrees = 45f;

        /// <summary>The handle's direction in a tool model's own space.</summary>
        public static readonly Vector3 HandleAxis = Vector3.forward;

        /// <summary>The home spot (scene start and respawn): the handle straight into the aisle, the surface's local -Z.</summary>
        public static Quaternion Home(Quaternion surfaceFacing)
        {
            return Quaternion.Euler(0f, YawDegrees(surfaceFacing) + 180f, 0f);
        }

        /// <summary>
        /// A tool put down by a player: the handle points from <paramref name="spotPosition"/> towards
        /// <paramref name="placerPosition"/> (where the server has the player), snapped to the nearest
        /// <see cref="SnapDegrees"/> measured from the surface's facing. When the placer stands right over the spot the
        /// handle points into the aisle (<see cref="Home"/>). Only the horizontal direction counts.
        /// </summary>
        public static Quaternion Rotation(Quaternion surfaceFacing, Vector3 placerPosition, Vector3 spotPosition)
        {
            Vector3 to = placerPosition - spotPosition;
            to.y = 0f;
            if (float.IsNaN(to.x) || float.IsNaN(to.z) || to.sqrMagnitude < 1e-6f) return Home(surfaceFacing);
            float facing = YawDegrees(surfaceFacing);
            float towards = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float snapped = Mathf.Round(Mathf.DeltaAngle(facing, towards) / SnapDegrees) * SnapDegrees;
            return Quaternion.Euler(0f, facing + snapped, 0f);
        }

        /// <summary>
        /// The whole resting rotation of a put-down item: a tool (<paramref name="handleTool"/>) by <see cref="Rotation"/>, anything
        /// else by the surface's yaw as before (PlaceSpots.RestRotation: cookware keeps its spec 021 rule); the tilt stays.
        /// The server (PickupService.TryPlace) and the predicted look (PickupInteractor) both use this.
        /// </summary>
        public static Quaternion RestRotation(bool handleTool, Quaternion tilt, Quaternion surfaceFacing, Vector3 placerPosition,
            Vector3 spotPosition)
        {
            if (!handleTool) return PlaceSpots.RestRotation(tilt, surfaceFacing);
            return Rotation(surfaceFacing, placerPosition, spotPosition) * tilt;
        }

        /// <summary>The world direction a tool's handle points in when it lies with <paramref name="rotation"/>, flattened.</summary>
        public static Vector3 HandleDirection(Quaternion rotation)
        {
            Vector3 d = rotation * HandleAxis;
            d.y = 0f;
            return d.sqrMagnitude < 1e-8f ? Vector3.zero : d.normalized;
        }

        private static float YawDegrees(Quaternion rotation) => PlaceSpots.YawOf(rotation).eulerAngles.y;
    }
}
