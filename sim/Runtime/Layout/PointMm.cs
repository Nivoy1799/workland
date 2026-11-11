using System;

namespace Worktown.Sim.Layout
{
    /// <summary>Spec 062a: a point on the floor in world millimetres.</summary>
    public readonly struct PointMm : IEquatable<PointMm>
    {
        public PointMm(int x, int z)
        {
            X = x;
            Z = z;
        }

        public int X { get; }
        public int Z { get; }

        public bool Equals(PointMm other) => X == other.X && Z == other.Z;
        public override bool Equals(object? obj) => obj is PointMm p && Equals(p);
        public override int GetHashCode() => unchecked(X * 397 ^ Z);
        public override string ToString() => "(" + X + ", " + Z + ")";
    }
}
