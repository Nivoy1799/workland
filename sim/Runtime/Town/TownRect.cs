using System;

namespace Worktown.Sim.Town
{
    /// <summary>
    /// Spec 064: an axis-aligned rectangle of the plan in millimetres, X0 &lt; X1 and Z0 &lt; Z1 (the catalog checks it).
    /// The border is inside: a point on it is contained. Rectangles that only share a border or a corner touch, they do
    /// not overlap.
    /// </summary>
    public readonly struct TownRect : IEquatable<TownRect>
    {
        public TownRect(int x0Mm, int z0Mm, int x1Mm, int z1Mm)
        {
            X0Mm = x0Mm;
            Z0Mm = z0Mm;
            X1Mm = x1Mm;
            Z1Mm = z1Mm;
        }

        public int X0Mm { get; }
        public int Z0Mm { get; }
        public int X1Mm { get; }
        public int Z1Mm { get; }

        /// <summary>A rectangle from whole metres, in the spec's table order: x0..x1, z0..z1.</summary>
        public static TownRect Metres(int x0, int x1, int z0, int z1) => new TownRect(x0 * 1000, z0 * 1000, x1 * 1000, z1 * 1000);

        /// <summary>True when the point lies inside or on the border.</summary>
        public bool Contains(TownPoint p) => p.XMm >= X0Mm && p.XMm <= X1Mm && p.ZMm >= Z0Mm && p.ZMm <= Z1Mm;

        /// <summary>True when the interiors intersect (sharing only a border or a corner is not an overlap).</summary>
        public bool Overlaps(TownRect o) => X0Mm < o.X1Mm && o.X0Mm < X1Mm && Z0Mm < o.Z1Mm && o.Z0Mm < Z1Mm;

        /// <summary>True when the closed rectangles meet but their interiors do not (a shared border or corner).</summary>
        public bool Touches(TownRect o) =>
            !Overlaps(o) && X0Mm <= o.X1Mm && o.X0Mm <= X1Mm && Z0Mm <= o.Z1Mm && o.Z0Mm <= Z1Mm;

        /// <summary>The four corners, counter-clockwise from (X0, Z0).</summary>
        public TownPoint[] Corners() => new[]
        {
            new TownPoint(X0Mm, Z0Mm), new TownPoint(X1Mm, Z0Mm), new TownPoint(X1Mm, Z1Mm), new TownPoint(X0Mm, Z1Mm),
        };

        public bool Equals(TownRect o) => X0Mm == o.X0Mm && Z0Mm == o.Z0Mm && X1Mm == o.X1Mm && Z1Mm == o.Z1Mm;
        public override bool Equals(object? obj) => obj is TownRect r && Equals(r);
        public override int GetHashCode() => unchecked((((((X0Mm * 397) ^ Z0Mm) * 397) ^ X1Mm) * 397) ^ Z1Mm);
        public override string ToString() => "[" + X0Mm + ".." + X1Mm + ", " + Z0Mm + ".." + Z1Mm + "]";
        public static bool operator ==(TownRect a, TownRect b) => a.Equals(b);
        public static bool operator !=(TownRect a, TownRect b) => !a.Equals(b);
    }
}
