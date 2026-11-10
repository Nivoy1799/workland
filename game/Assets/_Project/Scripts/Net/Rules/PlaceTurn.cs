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
    }
}
