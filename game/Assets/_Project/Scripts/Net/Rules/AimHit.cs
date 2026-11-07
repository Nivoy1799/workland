using UnityEngine;

namespace Worktown.Game.Net.Rules
{
    /// <summary>Spec 018: one ray hit as plain data for <see cref="AimRules.Pick"/>.</summary>
    public readonly struct AimHit
    {
        /// <summary>Distance along the ray from the camera, in metres.</summary>
        public readonly float Distance;
        public readonly AimKind Kind;
        /// <summary>Position of the station or pickup (its root), the point the server measures reach to.</summary>
        public readonly Vector3 ObjectPosition;

        public AimHit(float distance, AimKind kind, Vector3 objectPosition)
        {
            Distance = distance;
            Kind = kind;
            ObjectPosition = objectPosition;
        }
    }
}
