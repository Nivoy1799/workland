using System;
using System.Collections.Generic;

namespace Worktown.Sim.Layout
{
    /// <summary>
    /// Spec 062a: the floor as walkable cells for the layout rules and the guests' routes. Cells are <see cref="CellMm"/> apart; their
    /// centres are the lattice points <c>Bounds.MinX + column * CellMm</c>, <c>Bounds.MinZ + row * CellMm</c> (column 0 to
    /// <see cref="Columns"/> - 1, so both edges of the bounds are centres). A cell is free when its centre is at least
    /// <see cref="ClearanceMm"/> from every block (straight-line distance to the rect; exactly the clearance counts as free) and, for a
    /// grid made <see cref="Within"/> an area, inside it. Cell index: <c>row * Columns + column</c>. Immutable.
    /// </summary>
    public sealed class WalkGrid
    {
        public const int CellMm = 100;
        /// <summary>A body's half width plus a margin: how far a walker's centre stays from furniture and walls.</summary>
        public const int ClearanceMm = 400;
        /// <summary>Spec 062b: the most cells a grid may have (1,000,000: a 100 m square site).</summary>
        public const long MaxCells = 1_000_000;

        private readonly bool[] _free;

        /// <summary>A grid over <paramref name="bounds"/> with every cell near one of <paramref name="blocks"/> taken.</summary>
        public WalkGrid(RectMm bounds, IEnumerable<RectMm>? blocks)
        {
            if (bounds.IsEmpty) throw new ArgumentException("the bounds must have an area", nameof(bounds));
            // Spec 062b (062a Part B review): a site too large for the grid (over a million cells, 100 x 100 m) is refused, not allocated.
            long columns = bounds.Width / CellMm + 1L, rows = bounds.Depth / CellMm + 1L;
            if (columns * rows > MaxCells)
                throw new ArgumentException("the bounds " + bounds + " need " + columns * rows + " cells, more than " + MaxCells, nameof(bounds));
            Bounds = bounds;
            Columns = (int)columns;
            Rows = (int)rows;
            _free = new bool[Columns * Rows];
            for (int i = 0; i < _free.Length; i++) _free[i] = true;
            if (blocks == null) return;
            const long reach = (long)ClearanceMm * ClearanceMm;
            foreach (RectMm block in blocks)
            {
                if (block.IsEmpty) continue;
                int c0 = Math.Max(0, CeilDiv(block.MinX - ClearanceMm - bounds.MinX, CellMm));
                int c1 = Math.Min(Columns - 1, FloorDiv(block.MaxX + ClearanceMm - bounds.MinX, CellMm));
                int r0 = Math.Max(0, CeilDiv(block.MinZ - ClearanceMm - bounds.MinZ, CellMm));
                int r1 = Math.Min(Rows - 1, FloorDiv(block.MaxZ + ClearanceMm - bounds.MinZ, CellMm));
                for (int r = r0; r <= r1; r++)
                    for (int c = c0; c <= c1; c++)
                        if (DistanceSquared(block, bounds.MinX + c * CellMm, bounds.MinZ + r * CellMm) < reach) _free[r * Columns + c] = false;
            }
        }

        private WalkGrid(WalkGrid source, bool[] free)
        {
            Bounds = source.Bounds;
            Columns = source.Columns;
            Rows = source.Rows;
            _free = free;
        }

        public RectMm Bounds { get; }
        public int Columns { get; }
        public int Rows { get; }
        public int CellCount => _free.Length;

        /// <summary>
        /// Spec 062a: the grid of <paramref name="layout"/> in <paramref name="room"/>: over the room's site, blocked by the room's walls and
        /// by the footprint of every floor-band piece (InLine, Floor and Yard pieces of a known type; wall and overhead pieces hang above
        /// the floor and block nothing).
        /// </summary>
        public static WalkGrid ForLayout(ShopLayout layout, RoomDef room, PieceCatalog catalog)
        {
            if (layout == null || room == null || catalog == null) throw new ArgumentException("a layout, a room and a catalog are required");
            var blocks = new List<RectMm>(room.Walls);
            foreach (LayoutPiece p in layout.Pieces)
                if (catalog.TryGet(p.TypeId, out PieceDef def) && LayoutRules.Band(def.Mount) == 0) blocks.Add(def.Footprint(p));
            return new WalkGrid(room.Site, blocks);
        }

        /// <summary>The same grid with every cell whose centre is outside <paramref name="area"/> taken (the guests' grid).</summary>
        public WalkGrid Within(RectMm area)
        {
            var free = new bool[_free.Length];
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Columns; c++)
                {
                    int i = r * Columns + c;
                    PointMm centre = Centre(i);
                    free[i] = _free[i] && centre.X >= area.MinX && centre.X <= area.MaxX && centre.Z >= area.MinZ && centre.Z <= area.MaxZ;
                }
            return new WalkGrid(this, free);
        }

        public int Index(int column, int row) => row * Columns + column;

        public PointMm Centre(int index) => new PointMm(Bounds.MinX + index % Columns * CellMm, Bounds.MinZ + index / Columns * CellMm);

        public bool IsFree(int index) => index >= 0 && index < _free.Length && _free[index];

        /// <summary>The cell whose centre is nearest <paramref name="point"/> (a half rounds up); false when the point is outside the bounds.</summary>
        public bool TryCell(PointMm point, out int index)
        {
            index = -1;
            if (point.X < Bounds.MinX || point.X > Bounds.MaxX || point.Z < Bounds.MinZ || point.Z > Bounds.MaxZ) return false;
            int c = Math.Min(Columns - 1, (point.X - Bounds.MinX + CellMm / 2) / CellMm);
            int r = Math.Min(Rows - 1, (point.Z - Bounds.MinZ + CellMm / 2) / CellMm);
            index = Index(c, r);
            return true;
        }

        /// <summary>The (up to four) neighbours of a cell in a fixed order: west, east, south, north.</summary>
        public IEnumerable<int> Neighbours(int index)
        {
            int c = index % Columns, r = index / Columns;
            if (c > 0) yield return index - 1;
            if (c < Columns - 1) yield return index + 1;
            if (r > 0) yield return index - Columns;
            if (r < Rows - 1) yield return index + Columns;
        }

        /// <summary>
        /// Every cell a walker at <paramref name="start"/> reaches through free cells (4-neighbour), as a flag per cell index; all false
        /// when the start's cell is outside the grid or taken.
        /// </summary>
        public IReadOnlyList<bool> ReachableFrom(PointMm start)
        {
            var seen = new bool[_free.Length];
            if (!TryCell(start, out int first) || !_free[first]) return Array.AsReadOnly(seen);
            var open = new Queue<int>();
            seen[first] = true;
            open.Enqueue(first);
            while (open.Count > 0)
            {
                int i = open.Dequeue();
                foreach (int n in Neighbours(i))
                {
                    if (seen[n] || !_free[n]) continue;
                    seen[n] = true;
                    open.Enqueue(n);
                }
            }
            return Array.AsReadOnly(seen);
        }

        /// <summary>The cell indexes whose centres lie in <paramref name="area"/> (edges included), ascending.</summary>
        public IReadOnlyList<int> CellsIn(RectMm area)
        {
            var cells = new List<int>();
            int c0 = Math.Max(0, CeilDiv(area.MinX - Bounds.MinX, CellMm)), c1 = Math.Min(Columns - 1, FloorDiv(area.MaxX - Bounds.MinX, CellMm));
            int r0 = Math.Max(0, CeilDiv(area.MinZ - Bounds.MinZ, CellMm)), r1 = Math.Min(Rows - 1, FloorDiv(area.MaxZ - Bounds.MinZ, CellMm));
            for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++)
                    cells.Add(Index(c, r));
            return cells.AsReadOnly();
        }

        /// <summary>The squared straight-line distance from a point to a rect (0 inside or on its edge).</summary>
        public static long DistanceSquared(RectMm rect, int x, int z)
        {
            long dx = x < rect.MinX ? rect.MinX - x : x > rect.MaxX ? x - rect.MaxX : 0;
            long dz = z < rect.MinZ ? rect.MinZ - z : z > rect.MaxZ ? z - rect.MaxZ : 0;
            return dx * dx + dz * dz;
        }

        private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
        private static int CeilDiv(int a, int b) => -FloorDiv(-a, b);
    }
}
