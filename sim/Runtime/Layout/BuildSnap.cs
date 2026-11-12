using System;
using System.Linq;

namespace Worktown.Sim.Layout
{
    /// <summary>
    /// Spec 062b, pure: where a piece lands when a player aims at a point with it in build mode. <see cref="Snap"/>: its footprint's min
    /// corner snaps to the <see cref="GridMm"/> grid (from an origin: the room's floor corner) and it turns in quarter turns.
    /// <see cref="Pose"/> is the whole placement the server and the ghost share: the grid; then a footprint that pokes out of the floor by
    /// at most one grid step is pushed back flush with it; a wall piece within a step of the floor's edge behind it goes flush against it;
    /// and an InLine piece within <see cref="MagnetMm"/> of a counter run's end (same turns, its back edge within <see cref="MagnetMm"/> of
    /// the run's) snaps flush to that end, on the run's line (the magnet). Deterministic: the nearest end wins; on a tie the earlier run, and
    /// a run's far end before its near end.
    /// </summary>
    public static class BuildSnap
    {
        /// <summary>The build grid: 0.25 m.</summary>
        public const int GridMm = 250;
        /// <summary>How near a counter run's end a piece must come to snap flush to it.</summary>
        public const int MagnetMm = 250;

        /// <summary>Quarter turns wrapped to 0 to 3.</summary>
        public static int Turns(int turns) => ((turns % 4) + 4) % 4;

        /// <summary>The footprint's size (x, z) after <paramref name="turns"/>: an odd number of turns swaps width and depth.</summary>
        public static (int X, int Z) Size(PieceDef def, int turns, int lengthMm)
        {
            if (def == null) throw new ArgumentException("a row is required");
            int w = def.HasLength ? Math.Max(def.MinLengthMm, Math.Min(def.MaxLengthMm, lengthMm > 0 ? lengthMm : def.WidthMm)) : def.WidthMm;
            return (Turns(turns) & 1) == 1 ? (def.DepthMm, w) : (w, def.DepthMm);
        }

        /// <summary>The centre on the world grid (origin 0, 0): see <see cref="Snap(PieceDef, int, int, int, int, PointMm)"/>.</summary>
        public static PointMm Snap(PieceDef def, int aimXMm, int aimZMm, int turns, int lengthMm) =>
            Snap(def, aimXMm, aimZMm, turns, lengthMm, new PointMm(0, 0));

        /// <summary>
        /// The centre of a piece aimed at (<paramref name="aimXMm"/>, <paramref name="aimZMm"/>) with <paramref name="turns"/>: its footprint,
        /// centred on the aim, has its min corner moved to the nearest grid point (<see cref="GridMm"/> apart from <paramref name="origin"/>; a
        /// half rounds up). The centre is that corner plus half the size (RectMm.Around's rounding), so the footprint starts on the grid.
        /// </summary>
        public static PointMm Snap(PieceDef def, int aimXMm, int aimZMm, int turns, int lengthMm, PointMm origin)
        {
            (int w, int d) = Size(def, turns, lengthMm);
            int minX = aimXMm - w / 2, minZ = aimZMm - d / 2;
            int gx = origin.X + RoundToGrid(minX - origin.X), gz = origin.Z + RoundToGrid(minZ - origin.Z);
            return new PointMm(gx + w / 2, gz + d / 2);
        }

        /// <summary>
        /// The pose <paramref name="piece"/> (placed or not) takes when aimed at (<paramref name="aimXMm"/>, <paramref name="aimZMm"/>) with
        /// <paramref name="turns"/> in <paramref name="layout"/> (the piece itself is left out of the magnet's runs): the grid from the room's
        /// floor corner, pushed inside the floor, flush to a wall (wall pieces), the magnet (InLine pieces). A yard piece only snaps to the
        /// grid. Returns the piece with the new centre and turns (its id, type and length kept).
        /// </summary>
        public static LayoutPiece Pose(ShopLayout layout, PieceCatalog catalog, RoomDef room, LayoutPiece piece, int aimXMm, int aimZMm, int turns)
        {
            if (layout == null || catalog == null || room == null || piece == null)
                throw new ArgumentException("a layout, a catalog, a room and a piece are required");
            int t = Turns(turns);
            if (!catalog.TryGet(piece.TypeId, out PieceDef def)) return piece with { XMm = aimXMm, ZMm = aimZMm, Turns = t };
            PointMm c = Snap(def, aimXMm, aimZMm, t, piece.LengthMm, new PointMm(room.Floor.MinX, room.Floor.MinZ));
            LayoutPiece at = piece with { XMm = c.X, ZMm = c.Z, Turns = t };
            if (def.Mount == PieceMount.Yard) return at;
            at = Inside(at, def, room.Floor);
            if (def.Mount == PieceMount.Wall) at = ToWall(at, def, room.Floor);
            if (def.Mount == PieceMount.InLine) at = Magnet(at, def, layout.WithPieces(layout.Pieces.Where(p => p.Id != piece.Id)), catalog);
            return at;
        }

        /// <summary>A footprint that pokes out of <paramref name="floor"/> by at most <see cref="GridMm"/> on a side moves back flush with it.</summary>
        public static LayoutPiece Inside(LayoutPiece piece, PieceDef def, RectMm floor)
        {
            if (piece == null || def == null) throw new ArgumentException("a piece and its row are required");
            RectMm r = def.Footprint(piece);
            int dx = Push(r.MinX, r.MaxX, floor.MinX, floor.MaxX), dz = Push(r.MinZ, r.MaxZ, floor.MinZ, floor.MaxZ);
            return dx == 0 && dz == 0 ? piece : piece with { XMm = piece.XMm + dx, ZMm = piece.ZMm + dz };
        }

        /// <summary>A wall piece whose back is within <see cref="GridMm"/> of the floor's edge behind it moves flush against that edge (the wall).</summary>
        public static LayoutPiece ToWall(LayoutPiece piece, PieceDef def, RectMm floor)
        {
            if (piece == null || def == null) throw new ArgumentException("a piece and its row are required");
            RectMm r = def.Footprint(piece);
            int t = Turns(piece.Turns);
            int back = CounterRuns.BackEdge(r, t);
            int wall = t == 0 ? floor.MaxZ : t == 1 ? floor.MaxX : t == 2 ? floor.MinZ : floor.MinX;
            int shift = wall - back;
            if (shift == 0 || Math.Abs(shift) > GridMm) return piece;
            return CounterRuns.AlongX(t) ? piece with { ZMm = piece.ZMm + shift } : piece with { XMm = piece.XMm + shift };
        }

        /// <summary>
        /// The magnet: <paramref name="piece"/> (an InLine piece) snaps flush to the nearest end of a run of <paramref name="others"/> with
        /// its turns when its back edge is within <see cref="MagnetMm"/> of the run's and its near end within <see cref="MagnetMm"/> of the
        /// run's end; else it stays.
        /// </summary>
        public static LayoutPiece Magnet(LayoutPiece piece, PieceDef def, ShopLayout others, PieceCatalog catalog)
        {
            if (piece == null || def == null) throw new ArgumentException("a piece and its row are required");
            RectMm r = def.Footprint(piece);
            int t = Turns(piece.Turns);
            int back = CounterRuns.BackEdge(r, t);
            (int cMin, int cMax) = CounterRuns.Along(r, t);
            int bestScore = int.MaxValue, bestAlong = 0, bestCross = 0;
            foreach (var run in CounterRuns.Of(others, catalog))
            {
                if (Turns(run[0].Turns) != t || !catalog.TryGet(run[0].TypeId, out PieceDef firstDef)) continue;
                int cross = CounterRuns.BackEdge(firstDef.Footprint(run[0]), t) - back;
                if (Math.Abs(cross) > MagnetMm) continue;
                int rMin = int.MaxValue, rMax = int.MinValue;
                foreach (LayoutPiece p in run)
                {
                    if (!catalog.TryGet(p.TypeId, out PieceDef pd)) continue;
                    (int a, int b) = CounterRuns.Along(pd.Footprint(p), t);
                    rMin = Math.Min(rMin, a);
                    rMax = Math.Max(rMax, b);
                }
                foreach (int along in new[] { rMax - cMin, rMin - cMax }) // flush after the run's far end, then before its near end
                {
                    if (Math.Abs(along) > MagnetMm) continue;
                    int score = Math.Abs(along) + Math.Abs(cross);
                    if (score >= bestScore) continue;
                    bestScore = score;
                    bestAlong = along;
                    bestCross = cross;
                }
            }
            if (bestScore == int.MaxValue) return piece;
            return CounterRuns.AlongX(t)
                ? piece with { XMm = piece.XMm + bestAlong, ZMm = piece.ZMm + bestCross }
                : piece with { XMm = piece.XMm + bestCross, ZMm = piece.ZMm + bestAlong };
        }

        private static int Push(int min, int max, int floorMin, int floorMax)
        {
            if (min < floorMin && floorMin - min <= GridMm && max + (floorMin - min) <= floorMax) return floorMin - min;
            if (max > floorMax && max - floorMax <= GridMm && min - (max - floorMax) >= floorMin) return floorMax - max;
            return 0;
        }

        private static int RoundToGrid(int v) => FloorDiv(v + GridMm / 2, GridMm) * GridMm;

        private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
    }
}
