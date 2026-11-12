using System;
using System.Globalization;

namespace Worktown.Sim.Layout
{
    /// <summary>
    /// Spec 062a: a point on the floor in world millimetres (x, z). A readonly struct with value equality (the sim is C# 9, so no record
    /// struct). Also a direction when its parts are -1, 0 or 1 (<see cref="LayoutRules.CookDirection"/>).
    /// </summary>
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
        public override int GetHashCode() => unchecked((X * 397) ^ Z);
        public override string ToString() =>
            "(" + X.ToString(CultureInfo.InvariantCulture) + ", " + Z.ToString(CultureInfo.InvariantCulture) + ")";
        public static bool operator ==(PointMm a, PointMm b) => a.Equals(b);
        public static bool operator !=(PointMm a, PointMm b) => !a.Equals(b);
    }
}
