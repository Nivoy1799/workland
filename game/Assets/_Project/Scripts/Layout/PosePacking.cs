namespace Worktown.Game.Layout
{
    /// <summary>
    /// Spec 062a, pure: a piece's pose in one long (its PosePacked SyncVar): x and z in millimetres (24 bits each, signed: about +-8.4 km),
    /// the quarter turns (2 bits) and a counter's length in millimetres (14 bits: 0 to 16383). Bits from the lowest: x 0-23, z 24-47,
    /// turns 48-49, length 50-63. Out-of-range values clamp (turns wrap), so every packed value unpacks to a pose.
    /// </summary>
    public static class PosePacking
    {
        public const int CoordinateBits = 24;
        public const int MaxCoordinate = (1 << (CoordinateBits - 1)) - 1;
        public const int MinCoordinate = -(1 << (CoordinateBits - 1));
        public const int LengthBits = 14;
        public const int MaxLength = (1 << LengthBits) - 1;
        private const ulong CoordinateMask = (1UL << CoordinateBits) - 1;

        public static long Pack(int xMm, int zMm, int turns, int lengthMm)
        {
            ulong x = (ulong)(uint)Clamp(xMm, MinCoordinate, MaxCoordinate) & CoordinateMask;
            ulong z = (ulong)(uint)Clamp(zMm, MinCoordinate, MaxCoordinate) & CoordinateMask;
            ulong t = (ulong)(((turns % 4) + 4) % 4);
            ulong l = (ulong)Clamp(lengthMm, 0, MaxLength);
            return (long)(x | (z << CoordinateBits) | (t << (2 * CoordinateBits)) | (l << (2 * CoordinateBits + 2)));
        }

        public static void Unpack(long packed, out int xMm, out int zMm, out int turns, out int lengthMm)
        {
            ulong p = (ulong)packed;
            xMm = SignExtend(p & CoordinateMask);
            zMm = SignExtend((p >> CoordinateBits) & CoordinateMask);
            turns = (int)((p >> (2 * CoordinateBits)) & 3UL);
            lengthMm = (int)((p >> (2 * CoordinateBits + 2)) & MaxLength);
        }

        /// <summary>The yaw in degrees of <paramref name="turns"/> quarter turns (0, 90, 180, 270).</summary>
        public static float Yaw(int turns) => (((turns % 4) + 4) % 4) * 90f;

        private static int SignExtend(ulong value)
        {
            int v = (int)value;
            return (v & (1 << (CoordinateBits - 1))) != 0 ? v - (1 << CoordinateBits) : v;
        }

        private static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;
    }
}
