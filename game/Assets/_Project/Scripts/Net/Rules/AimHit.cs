using UnityEngine;

namespace Worktown.Game.Net.Rules
{
    /// <summary>Spec 018: one ray hit as plain data for <see cref="AimRules.Pick"/>.</summary>
    public readonly struct AimHit
    {
        /// <summary>Distance along the ray from the camera, in metres.</summary>
        public readonly float Distance;
        public readonly AimKind Kind;
        /// <summary>Position of the station or pickup (its root), the point the server measures reach to.
        /// For a button: its station's position.</summary>
        public readonly Vector3 ObjectPosition;
        /// <summary>Spec 019, buttons only: the station the button belongs to (-1 otherwise).</summary>
        public readonly int StationIndex;
        /// <summary>Spec 019, buttons only: the station option it sends (0 otherwise).</summary>
        public readonly int Option;

        public AimHit(float distance, AimKind kind, Vector3 objectPosition)
            : this(distance, kind, objectPosition, -1, 0)
        {
        }

        private AimHit(float distance, AimKind kind, Vector3 objectPosition, int stationIndex, int option)
        {
            Distance = distance;
            Kind = kind;
            ObjectPosition = objectPosition;
            StationIndex = stationIndex;
            Option = option;
        }

        /// <summary>Spec 019: a hit on a station button; reach is measured to the station, as the server does.</summary>
        public static AimHit Button(float distance, Vector3 stationPosition, int stationIndex, int option)
        {
            return new AimHit(distance, AimKind.Button, stationPosition, stationIndex, option);
        }
    }
}
