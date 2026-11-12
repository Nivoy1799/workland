using System;
using System.Collections.Generic;

namespace Worktown.Sim.Layout
{
    /// <summary>
    /// Spec 062a: a shortest walk on a <see cref="WalkGrid"/> (A*, 4-neighbour, one step per cell, the Manhattan distance as the estimate;
    /// among open cells of the same estimate the lower cell index goes first, and a cell keeps the first parent that reached it at its best
    /// cost, so the same grid and ends give the same path). <see cref="Cells"/>: every cell's centre from the start's cell to the goal's;
    /// <see cref="Corners"/>: the start, each centre where the direction changes, and the goal. Empty when there is no path (the start's or
    /// the goal's cell outside the grid or taken, or no way between). Immutable.
    /// </summary>
    public sealed class GridPath
    {
        private GridPath(IReadOnlyList<PointMm> cells)
        {
            Cells = cells;
            Corners = Reduce(cells);
        }

        public bool Found => Cells.Count > 0;
        public IReadOnlyList<PointMm> Cells { get; }
        public IReadOnlyList<PointMm> Corners { get; }

        public static GridPath Find(WalkGrid grid, PointMm from, PointMm to)
        {
            if (grid == null) throw new ArgumentException("a grid is required", nameof(grid));
            var none = new GridPath(Array.Empty<PointMm>());
            if (!grid.TryCell(from, out int start) || !grid.TryCell(to, out int goal) || !grid.IsFree(start) || !grid.IsFree(goal)) return none;

            int n = grid.CellCount;
            var cost = new int[n];
            var parent = new int[n];
            var closed = new bool[n];
            for (int i = 0; i < n; i++)
            {
                cost[i] = int.MaxValue;
                parent[i] = -1;
            }
            int goalColumn = goal % grid.Columns, goalRow = goal / grid.Columns;
            int Estimate(int i) => Math.Abs(i % grid.Columns - goalColumn) + Math.Abs(i / grid.Columns - goalRow);
            long Key(int i) => (long)(cost[i] + Estimate(i)) * n + i;

            var open = new SortedSet<long>();
            cost[start] = 0;
            open.Add(Key(start));
            while (open.Count > 0)
            {
                long key = open.Min;
                open.Remove(key);
                int current = (int)(key % n);
                if (closed[current]) continue;
                closed[current] = true;
                if (current == goal) break;
                foreach (int next in grid.Neighbours(current))
                {
                    if (closed[next] || !grid.IsFree(next)) continue;
                    int g = cost[current] + 1;
                    if (g >= cost[next]) continue;
                    if (cost[next] != int.MaxValue) open.Remove(Key(next));
                    cost[next] = g;
                    parent[next] = current;
                    open.Add(Key(next));
                }
            }
            if (!closed[goal]) return none;

            var cells = new List<PointMm>();
            for (int i = goal; i != -1; i = parent[i]) cells.Add(grid.Centre(i));
            cells.Reverse();
            return new GridPath(cells.AsReadOnly());
        }

        /// <summary>The first and last point and every point where the walk turns.</summary>
        public static IReadOnlyList<PointMm> Reduce(IReadOnlyList<PointMm> cells)
        {
            if (cells == null || cells.Count == 0) return Array.Empty<PointMm>();
            var corners = new List<PointMm> { cells[0] };
            for (int i = 1; i + 1 < cells.Count; i++)
            {
                int dx0 = Math.Sign(cells[i].X - cells[i - 1].X), dz0 = Math.Sign(cells[i].Z - cells[i - 1].Z);
                int dx1 = Math.Sign(cells[i + 1].X - cells[i].X), dz1 = Math.Sign(cells[i + 1].Z - cells[i].Z);
                if (dx0 != dx1 || dz0 != dz1) corners.Add(cells[i]);
            }
            if (cells.Count > 1) corners.Add(cells[cells.Count - 1]);
            return corners.AsReadOnly();
        }
    }
}
