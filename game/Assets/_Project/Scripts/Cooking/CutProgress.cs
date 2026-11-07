using System;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 019: how far the food on a cutting board has been cut. Server only; clients see the coarse
    /// shape through the board's look. Bytes only. Pure value type; <see cref="CutRules"/> changes it.
    /// </summary>
    public struct CutProgress : IEquatable<CutProgress>
    {
        /// <summary>FoodLook shape: 0 whole, 1 halves, 2 slices, 3 sticks, 4 cubes (dice).</summary>
        public byte Shape;
        /// <summary>1 when the food was turned 90 degrees since the last shape change, else 0.</summary>
        public byte Turned;

        public CutProgress(byte shape, byte turned)
        {
            Shape = shape;
            Turned = turned;
        }

        /// <summary>Freshly put on the board: whole, not turned.</summary>
        public static CutProgress Whole => new CutProgress(FoodLook.ShapeWhole, 0);

        public bool Equals(CutProgress other) => Shape == other.Shape && Turned == other.Turned;
        public override bool Equals(object obj) => obj is CutProgress other && Equals(other);
        public override int GetHashCode() => Shape | (Turned << 8);
        public static bool operator ==(CutProgress a, CutProgress b) => a.Equals(b);
        public static bool operator !=(CutProgress a, CutProgress b) => !a.Equals(b);

        public override string ToString() => "CutProgress(shape " + Shape + ", turned " + Turned + ")";
    }
}
