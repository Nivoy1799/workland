using UnityEngine;

namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Spec 047, server: where a held item was picked up from, so it goes back there when its holder leaves the game. On a place
    /// surface's spot (<see cref="OnSpot"/>: <see cref="Surface"/>, <see cref="Spot"/>) or anywhere else (the floor, a scene start
    /// spot outside the spot table); the pose is where it lay.
    /// </summary>
    public readonly struct PickupOrigin
    {
        public readonly bool OnSpot;
        public readonly int Surface;
        public readonly int Spot;
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;

        public PickupOrigin(bool onSpot, int surface, int spot, Vector3 position, Quaternion rotation)
        {
            OnSpot = onSpot;
            Surface = onSpot ? surface : PlaceSpots.None;
            Spot = onSpot ? spot : PlaceSpots.None;
            Position = position;
            Rotation = rotation;
        }

        /// <summary>
        /// Spec 047, pure: the leaving holder's item goes back to its origin (true) when it has one and the place is still free: its
        /// spot not taken in the server's table (<paramref name="spotTaken"/>) and no other free item lying there
        /// (<paramref name="occupied"/>). Else (no origin: the server handed it over fresh; or the place is used) it drops where it is.
        /// </summary>
        public static bool GoesBack(bool hasOrigin, bool spotTaken, bool occupied) => hasOrigin && !spotTaken && !occupied;
    }
}
