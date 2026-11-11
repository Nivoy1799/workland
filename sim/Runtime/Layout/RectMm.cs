using System;

namespace Worktown.Sim.Layout
{
    /// <summary>Spec 062a: an axis-aligned rectangle on the floor in world millimetres (x and z), min inclusive, max exclusive.</summary>
    public readonly struct RectMm : IEquatable<RectMm>
    {
        public RectMm(int minX, int minZ, int maxX, int maxZ)
        {
            MinX = Math.Min(minX, maxX);
            MinZ = Math.Min(minZ, maxZ);
            MaxX = Math.Max(minX, maxX);
            MaxZ = Math.Max(minZ, maxZ);
        }

        public int MinX { get; }
        public int MinZ { get; }
        public int MaxX { get; }
        public int MaxZ { get; }
        public int Width => MaxX - MinX;
        public int Depth => MaxZ - MinZ;
        public bool IsEmpty => Width <= 0 || Depth <= 0;

        /// <summary>A rectangle <paramref name="width"/> by <paramref name="depth"/> around a centre (the half sizes rounded down).</summary>
        public static RectMm Around(int x, int z, int width, int depth) => new RectMm(x - width / 2, z - depth / 2, x - width / 2 + width, z - depth / 2 + depth);

        /// <summary>True when the two overlap with an area; touching edges do not.</summary>
        public bool Intersects(RectMm other) => !IsEmpty && !other.IsEmpty
            && MinX < other.MaxX && other.MinX < MaxX && MinZ < other.MaxZ && other.MinZ < MaxZ;

        /// <summary>True when <paramref name="other"/> lies wholly inside this one (edges may touch).</summary>
        public bool Contains(RectMm other) => other.MinX >= MinX && other.MaxX <= MaxX && other.MinZ >= MinZ && other.MaxZ <= MaxZ;

        public bool Contains(int x, int z) => x >= MinX && x < MaxX && z >= MinZ && z < MaxZ;

        public bool Equals(RectMm other) => MinX == other.MinX && MinZ == other.MinZ && MaxX == other.MaxX && MaxZ == other.MaxZ;
        public override bool Equals(object? obj) => obj is RectMm r && Equals(r);
        public override int GetHashCode() => unchecked(((MinX * 397 ^ MinZ) * 397 ^ MaxX) * 397 ^ MaxZ);
        public override string ToString() => "(" + MinX + ", " + MinZ + ")-(" + MaxX + ", " + MaxZ + ")";
    }
}
