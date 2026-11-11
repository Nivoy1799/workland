using UnityEngine;

namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Spec 041, pure: the held item's rest rotation, turned with the mouse wheel before it is put down, in steps of
    /// <see cref="StepDegrees"/>. No new RPC parameter: the existing place request's spot int carries the step above the spot index
    /// (<see cref="Pack"/>); the server unpacks it (<see cref="TryUnpack"/>; a step out of range is refused like an unknown spot) and
    /// turns its own rest rotation (ToolRest for tools, the surface's yaw for the rest) by the step about the vertical
    /// (<see cref="Apply"/>). The position, the spot checks and the tilt stay the server's.
    /// </summary>
    public static class PlaceTurn
    {
        public const int Steps = 8;
        public const float StepDegrees = 360f / Steps;
        /// <summary>The spot index lives in the low bits; a surface has far fewer spots than this.</summary>
        public const int SpotBits = 16;
        public const int SpotMask = (1 << SpotBits) - 1;

        /// <summary><paramref name="turn"/> plus <paramref name="steps"/>, wrapped into 0..<see cref="Steps"/>-1.</summary>
        public static int Add(int turn, int steps)
        {
            int t = (turn + steps) % Steps;
            return t < 0 ? t + Steps : t;
        }

        /// <summary>The spot int of a place request: the spot and the turn step. A negative or huge spot stays as it is (refused anyway).</summary>
        public static int Pack(int spot, int turn)
        {
            if (spot < 0 || spot > SpotMask) return spot;
            return spot | (Add(turn, 0) << SpotBits);
        }

        /// <summary>
        /// The spot and the turn step from a place request's spot int. False (spot -1) for a step of <see cref="Steps"/> or more: the
        /// request is refused like an unknown spot. A negative int is a spot as before (turn 0).
        /// </summary>
        public static bool TryUnpack(int packed, out int spot, out int turn)
        {
            turn = 0;
            if (packed < 0)
            {
                spot = packed;
                return true;
            }
            int t = packed >> SpotBits;
            if (t >= Steps)
            {
                spot = -1;
                return false;
            }
            spot = packed & SpotMask;
            turn = t;
            return true;
        }

        /// <summary>The rest rotation turned by <paramref name="turn"/> steps about the world's vertical (yaw only; the tilt stays).</summary>
        public static Quaternion Apply(Quaternion rest, int turn)
        {
            int t = Add(turn, 0);
            return t == 0 ? rest : Quaternion.Euler(0f, t * StepDegrees, 0f) * rest;
        }

        // ---- 041 review: a turned item may not lie over an occupied neighbour spot ----

        /// <summary>How far (m) past the half-way line to a neighbour spot an item may reach before it counts as lying over it.</summary>
        public const float OverlapSlack = 0.01f;

        /// <summary>
        /// The half size of an item's footprint on a surface: x along the surface's width, y along its depth (its local X and Z). The
        /// item has the local half extents <paramref name="halfExtents"/> and lies at the world rotation <paramref name="rest"/>; the
        /// surface is turned by <paramref name="surface"/>. It is the axis-aligned box around the turned item, seen from above.
        /// </summary>
        public static Vector2 Footprint(Vector3 halfExtents, Quaternion rest, Quaternion surface)
        {
            Quaternion q = Quaternion.Inverse(surface) * rest;
            Vector3 r = q * Vector3.right, u = q * Vector3.up, f = q * Vector3.forward;
            Vector3 h = new Vector3(Mathf.Abs(halfExtents.x), Mathf.Abs(halfExtents.y), Mathf.Abs(halfExtents.z));
            float x = Mathf.Abs(r.x) * h.x + Mathf.Abs(u.x) * h.y + Mathf.Abs(f.x) * h.z;
            float y = Mathf.Abs(r.z) * h.x + Mathf.Abs(u.z) * h.y + Mathf.Abs(f.z) * h.z;
            return new Vector2(x, y);
        }

        /// <summary>
        /// Whether a footprint of half size <paramref name="half"/> on a spot reaches over the half-way line (plus
        /// <see cref="OverlapSlack"/>) to the neighbour spot <paramref name="dColumn"/>, <paramref name="dRow"/> away (each -1, 0 or 1;
        /// a diagonal neighbour only when it reaches over both lines).
        /// </summary>
        public static bool Reaches(Vector2 half, int dColumn, int dRow)
        {
            float line = PlaceSpots.Spacing * 0.5f + OverlapSlack;
            return (dColumn == 0 || half.x > line) && (dRow == 0 || half.y > line);
        }

        /// <summary>
        /// 041 review, server (PickupService.PlanPlace): true when the turned footprint (<paramref name="turnedHalf"/>) on spot
        /// <paramref name="spot"/> of a top of size <paramref name="top"/> reaches an occupied neighbour spot (<paramref name="taken"/>)
        /// that the unturned rest (<paramref name="restHalf"/>, turn 0) does not reach. So the wheel never lays an item over a
        /// neighbour; what the item covers without the wheel stays as it was (a half turn has the same footprint and is never refused).
        /// </summary>
        public static bool TurnOverlaps(Vector2 top, int spot, Vector2 restHalf, Vector2 turnedHalf, System.Func<int, bool> taken)
        {
            if (taken == null || !PlaceSpots.IsValid(top, spot)) return false;
            int columns = PlaceSpots.Columns(top), rows = PlaceSpots.Rows(top);
            int column = spot % columns, row = spot / columns;
            for (int dRow = -1; dRow <= 1; dRow++)
            {
                for (int dColumn = -1; dColumn <= 1; dColumn++)
                {
                    if (dRow == 0 && dColumn == 0) continue;
                    int c = column + dColumn, r = row + dRow;
                    if (c < 0 || c >= columns || r < 0 || r >= rows) continue;
                    if (!Reaches(turnedHalf, dColumn, dRow) || Reaches(restHalf, dColumn, dRow)) continue;
                    if (taken(r * columns + c)) return true;
                }
            }
            return false;
        }
    }
}
