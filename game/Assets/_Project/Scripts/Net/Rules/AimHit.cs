using UnityEngine;

namespace Worktown.Game.Net.Rules
{
    /// <summary>Spec 018: one ray hit as plain data for <see cref="AimRules.Pick(System.Collections.Generic.IReadOnlyList{AimHit}, Vector3)"/>.</summary>
    public readonly struct AimHit
    {
        /// <summary>Distance along the ray from the camera, in metres.</summary>
        public readonly float Distance;
        public readonly AimKind Kind;
        /// <summary>Position of the station or pickup (its root), the point the server measures reach to.
        /// For a button: its station's position. For a spot: the spot's world position (the hit point when the
        /// surface is full).</summary>
        public readonly Vector3 ObjectPosition;
        /// <summary>Spec 019, buttons only: the station the button belongs to (-1 otherwise).</summary>
        public readonly int StationIndex;
        /// <summary>Spec 019, buttons only: the station option it sends (0 otherwise).</summary>
        public readonly int Option;
        /// <summary>Spec 020, spots only: the place surface's index (-1 otherwise).</summary>
        public readonly int SurfaceIndex;
        /// <summary>Spec 020, spots only: the nearest free spot, or <see cref="PlaceSpots.None"/> when the surface is full.</summary>
        public readonly int SpotIndex;

        public AimHit(float distance, AimKind kind, Vector3 objectPosition)
            : this(distance, kind, objectPosition, -1, 0, -1, PlaceSpots.None)
        {
        }

        private AimHit(float distance, AimKind kind, Vector3 objectPosition, int stationIndex, int option, int surfaceIndex, int spotIndex)
        {
            Distance = distance;
            Kind = kind;
            ObjectPosition = objectPosition;
            StationIndex = stationIndex;
            Option = option;
            SurfaceIndex = surfaceIndex;
            SpotIndex = spotIndex;
        }

        /// <summary>Spec 019: a hit on a station button; reach is measured to the station, as the server does.</summary>
        public static AimHit Button(float distance, Vector3 stationPosition, int stationIndex, int option)
        {
            return new AimHit(distance, AimKind.Button, stationPosition, stationIndex, option, -1, PlaceSpots.None);
        }

        /// <summary>
        /// Spec 020: a hit on the top of a place surface. <paramref name="position"/> is the chosen spot's world
        /// position (the server measures reach to it), or the hit point when <paramref name="spotIndex"/> is
        /// <see cref="PlaceSpots.None"/> (the surface is full).
        /// </summary>
        public static AimHit Spot(float distance, Vector3 position, int surfaceIndex, int spotIndex)
        {
            return new AimHit(distance, AimKind.Spot, position, -1, 0, surfaceIndex, spotIndex);
        }
    }
}
