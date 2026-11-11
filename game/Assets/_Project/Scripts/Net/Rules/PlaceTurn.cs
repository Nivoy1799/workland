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
        /// 054 round (041 review nit): the footprint of a box whose centre sits <paramref name="center"/> (local, scaled) off the item's
        /// pivot. How far it reaches from the pivot on the spot, seen from above: <paramref name="positive"/> towards the surface's +X
        /// (x) and +Z (y), <paramref name="negative"/> towards -X and -Z (both as distances; a centre far off the pivot can make one
        /// negative: the box does not reach that side at all).
        /// </summary>
        public static void Footprint(Vector3 halfExtents, Vector3 center, Quaternion rest, Quaternion surface,
            out Vector2 positive, out Vector2 negative)
        {
            Vector2 half = Footprint(halfExtents, rest, surface);
            Vector3 c = Quaternion.Inverse(surface) * rest * center;
            positive = new Vector2(half.x + c.x, half.y + c.z);
            negative = new Vector2(half.x - c.x, half.y - c.z);
        }

        /// <summary>
        /// Whether a footprint of half size <paramref name="half"/> on a spot reaches over the half-way line (plus
        /// <see cref="OverlapSlack"/>) to the neighbour spot <paramref name="dColumn"/>, <paramref name="dRow"/> away (each -1, 0 or 1;
        /// a diagonal neighbour only when it reaches over both lines).
        /// </summary>
        public static bool Reaches(Vector2 half, int dColumn, int dRow) => Reaches(half, half, dColumn, dRow);

        /// <summary>As above for a footprint off the pivot (<see cref="Footprint(Vector3, Vector3, Quaternion, Quaternion, out Vector2, out Vector2)"/>):
        /// a neighbour on the + side (column or row +1) is reached by <paramref name="positive"/>, one on the - side by <paramref name="negative"/>.</summary>
        public static bool Reaches(Vector2 positive, Vector2 negative, int dColumn, int dRow)
        {
            float line = PlaceSpots.Spacing * 0.5f + OverlapSlack;
            float x = dColumn > 0 ? positive.x : negative.x;
            float y = dRow > 0 ? positive.y : negative.y;
            return (dColumn == 0 || x > line) && (dRow == 0 || y > line);
        }

        /// <summary>
        /// 041 review, server (PickupService.PlanPlace): true when the turned footprint (<paramref name="turnedHalf"/>) on spot
        /// <paramref name="spot"/> of a top of size <paramref name="top"/> reaches an occupied neighbour spot (<paramref name="taken"/>)
        /// that the unturned rest (<paramref name="restHalf"/>, turn 0) does not reach. So the wheel never lays an item over a
        /// neighbour; what the item covers without the wheel stays as it was. (PickupService.PlanPlace never asks for a half turn: it is
        /// never refused, see <see cref="IsHalfTurn"/>.)
        /// </summary>
        public static bool TurnOverlaps(Vector2 top, int spot, Vector2 restHalf, Vector2 turnedHalf, System.Func<int, bool> taken) =>
            TurnOverlaps(top, spot, restHalf, restHalf, turnedHalf, turnedHalf, taken, (t, n) => t(n));

        /// <summary>
        /// 054 round (041 review nits): as above for footprints off the pivot (positive and negative reach, see
        /// <see cref="Footprint(Vector3, Vector3, Quaternion, Quaternion, out Vector2, out Vector2)"/>), with the occupied test as a
        /// state and a non-capturing function, so the server's place check allocates no closure per request.
        /// </summary>
        public static bool TurnOverlaps<TState>(Vector2 top, int spot, Vector2 restPositive, Vector2 restNegative, Vector2 turnedPositive,
            Vector2 turnedNegative, TState state, System.Func<TState, int, bool> taken)
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
                    if (!Reaches(turnedPositive, turnedNegative, dColumn, dRow) || Reaches(restPositive, restNegative, dColumn, dRow)) continue;
                    if (taken(state, r * columns + c)) return true;
                }
            }
            return false;
        }

        /// <summary>054 round (041 review nit): a half turn (step <see cref="Steps"/> / 2). PlanPlace skips the overlap check for it, so a
        /// half turn is never refused by construction (with a footprint off the pivot it is not symmetric).</summary>
        public static bool IsHalfTurn(int turn) => Add(turn, 0) == Steps / 2;
    }
}
