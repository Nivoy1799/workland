using System;

namespace Worktown.Sim.Town
{
    /// <summary>
    /// Spec 064: a point in the town plan in integer millimetres (x east, z north, origin at the centre of Fountain Cross).
    /// A readonly struct with value equality (the sim is C# 9, so no record struct).
    /// </summary>
    public readonly struct TownPoint : IEquatable<TownPoint>
    {
        public TownPoint(int xMm, int zMm)
        {
            XMm = xMm;
            ZMm = zMm;
        }

        public int XMm { get; }
        public int ZMm { get; }

        /// <summary>A point from whole metres (the default data is written in metres).</summary>
        public static TownPoint Metres(int x, int z) => new TownPoint(x * 1000, z * 1000);

        public bool Equals(TownPoint other) => XMm == other.XMm && ZMm == other.ZMm;
        public override bool Equals(object? obj) => obj is TownPoint p && Equals(p);
        public override int GetHashCode() => unchecked((XMm * 397) ^ ZMm);
        public override string ToString() => "(" + XMm + ", " + ZMm + ")";
        public static bool operator ==(TownPoint a, TownPoint b) => a.Equals(b);
        public static bool operator !=(TownPoint a, TownPoint b) => !a.Equals(b);
    }
}
