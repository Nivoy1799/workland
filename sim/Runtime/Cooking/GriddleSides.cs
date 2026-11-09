using System;

namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 030: the two sides of one item on the griddle: sim seconds side A (the side that went down first) and side B have
    /// cooked, and which side is down now. Immutable; <see cref="Cook"/> adds time to the down side, <see cref="Flip"/> turns it.
    /// A side only cooks while it is down, so the order of the flips does not matter, only each side's total time.
    /// </summary>
    public readonly struct GriddleSides : IEquatable<GriddleSides>
    {
        public GriddleSides(float sideA, float sideB, bool bDown)
        {
            SideA = Math.Max(0f, Sanitize(sideA));
            SideB = Math.Max(0f, Sanitize(sideB));
            BDown = bDown;
        }

        /// <summary>A fresh item: nothing cooked, side A down.</summary>
        public static GriddleSides Fresh => new GriddleSides(0f, 0f, false);

        public float SideA { get; }
        public float SideB { get; }
        /// <summary>False: side A is down (on the plate). True: side B is down.</summary>
        public bool BDown { get; }
        /// <summary>0 for side A, 1 for side B.</summary>
        public int DownSide => BDown ? 1 : 0;
        public float Down => BDown ? SideB : SideA;
        public float Up => BDown ? SideA : SideB;

        /// <summary>The down side cooks <paramref name="seconds"/> more (negative or NaN counts as 0).</summary>
        public GriddleSides Cook(float seconds)
        {
            float s = Math.Max(0f, Sanitize(seconds));
            return BDown ? new GriddleSides(SideA, SideB + s, true) : new GriddleSides(SideA + s, SideB, false);
        }

        /// <summary>Turned over: the other side is down. Allowed at any time (also a raw side).</summary>
        public GriddleSides Flip() => new GriddleSides(SideA, SideB, !BDown);

        private static float Sanitize(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 0f : v;

        public bool Equals(GriddleSides other) => SideA.Equals(other.SideA) && SideB.Equals(other.SideB) && BDown == other.BDown;
        public override bool Equals(object? obj) => obj is GriddleSides other && Equals(other);
        public override int GetHashCode() => SideA.GetHashCode() ^ (SideB.GetHashCode() * 31) ^ (BDown ? 1 : 0);
        public override string ToString() => "GriddleSides(A " + SideA + ", B " + SideB + ", " + (BDown ? "B" : "A") + " down)";
    }
}
