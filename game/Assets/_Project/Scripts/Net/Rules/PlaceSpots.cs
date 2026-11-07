using System.Collections.Generic;
using UnityEngine;

namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Spec 020, pure: the spot grid on top of a place surface and, on the server, which spots are taken by which
    /// object. Spots lie <see cref="Spacing"/> apart and at least <see cref="EdgeMargin"/> from the edges, the grid
    /// centred on the top. Positions are in the surface's top plane: x along its width, y along its depth (local
    /// Z), relative to the top's centre. Spot index = row * columns + column.
    /// </summary>
    public sealed class PlaceSpots
    {
        public const float Spacing = 0.25f;
        public const float EdgeMargin = 0.12f;
        /// <summary>No spot (none free, or none on a surface that small).</summary>
        public const int None = -1;

        // Server only: who lies on which spot. Key = surface and spot packed into one long.
        private readonly Dictionary<long, int> _occupant = new Dictionary<long, int>();
        private readonly Dictionary<int, long> _spotOf = new Dictionary<int, long>();

        /// <summary>Spots along one side of <paramref name="length"/> metres (0 when it is shorter than both margins).</summary>
        public static int CountAlong(float length)
        {
            if (float.IsNaN(length) || length < 2f * EdgeMargin) return 0;
            return Mathf.FloorToInt((length - 2f * EdgeMargin) / Spacing + 0.0001f) + 1;
        }

        public static int Columns(Vector2 top) => CountAlong(top.x);
        public static int Rows(Vector2 top) => CountAlong(top.y);
        public static int Count(Vector2 top) => Columns(top) * Rows(top);

        public static bool IsValid(Vector2 top, int spot) => spot >= 0 && spot < Count(top);

        /// <summary>Position of the spot in the top plane, relative to the top's centre. Zero for an invalid index.</summary>
        public static Vector2 Position(Vector2 top, int spot)
        {
            if (!IsValid(top, spot)) return Vector2.zero;
            int columns = Columns(top);
            int rows = Rows(top);
            int column = spot % columns;
            int row = spot / columns;
            return new Vector2(Offset(column, columns), Offset(row, rows));
        }

        private static float Offset(int i, int count)
        {
            return (i - (count - 1) * 0.5f) * Spacing;
        }

        /// <summary>
        /// The free spot nearest to <paramref name="point"/> (top plane, relative to the centre), or
        /// <see cref="None"/>. <paramref name="taken"/>[i] true means spot i is taken (null or shorter: free).
        /// On a tie the lower index wins.
        /// </summary>
        public static int NearestFree(Vector2 top, Vector2 point, IReadOnlyList<bool> taken)
        {
            int count = Count(top);
            int best = None;
            float bestSqr = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                if (taken != null && i < taken.Count && taken[i]) continue;
                float sqr = (Position(top, i) - point).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                best = i;
            }
            return best;
        }

        // ---- resting rotation of a put-down item ----

        /// <summary>The yaw part of a rotation (turn about world up only). Identity when its forward points straight up or down.</summary>
        public static Quaternion YawOf(Quaternion rotation)
        {
            Vector3 forward = rotation * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) return Quaternion.identity;
            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        /// <summary>The rotation without its yaw, i.e. how the item is tilted (a knife lying on its side keeps that).</summary>
        public static Quaternion TiltOf(Quaternion rotation)
        {
            return Quaternion.Inverse(YawOf(rotation)) * rotation;
        }

        /// <summary>
        /// How a put-down item lies: its resting <paramref name="tilt"/> (from <see cref="TiltOf"/>), turned by the
        /// yaw of <paramref name="surface"/> (identity for a drop on the floor). The tilt never changes.
        /// </summary>
        public static Quaternion RestRotation(Quaternion tilt, Quaternion surface)
        {
            return YawOf(surface) * tilt;
        }

        // ---- server: which object lies on which spot ----

        public bool IsTaken(int surface, int spot) => _occupant.ContainsKey(Key(surface, spot));

        public bool TryGetOccupant(int surface, int spot, out int objectId)
        {
            return _occupant.TryGetValue(Key(surface, spot), out objectId);
        }

        /// <summary>Marks the spot as taken by the object. False if the spot is taken; an object moves off its old spot.</summary>
        public bool TryTake(int surface, int spot, int objectId)
        {
            long key = Key(surface, spot);
            if (_occupant.ContainsKey(key)) return false;
            Free(objectId);
            _occupant[key] = objectId;
            _spotOf[objectId] = key;
            return true;
        }

        /// <summary>Frees the spot the object lies on (it was picked up). False if it lay on none.</summary>
        public bool Free(int objectId)
        {
            if (!_spotOf.TryGetValue(objectId, out long key)) return false;
            _spotOf.Remove(objectId);
            _occupant.Remove(key);
            return true;
        }

        public void Clear()
        {
            _occupant.Clear();
            _spotOf.Clear();
        }

        /// <summary>
        /// The server's place check, in this order: the player holds something (NothingHeld), the surface exists
        /// (NotFound), the spot index is valid for its <paramref name="spotCount"/> (NotFound), the spot is in reach
        /// (TooFar), the spot is free (Taken). Ok means the caller may place the item and call <see cref="TryTake"/>.
        /// </summary>
        public PickupOutcome CheckPlace(bool holding, bool surfaceExists, int spotCount, int surface, int spot, bool inReach)
        {
            if (!holding) return PickupOutcome.NothingHeld;
            if (!surfaceExists) return PickupOutcome.NotFound;
            if (spot < 0 || spot >= spotCount) return PickupOutcome.NotFound;
            if (!inReach) return PickupOutcome.TooFar;
            if (IsTaken(surface, spot)) return PickupOutcome.Taken;
            return PickupOutcome.Ok;
        }

        private static long Key(int surface, int spot) => ((long)surface << 32) | (uint)spot;
    }
}
