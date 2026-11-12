using System;
using System.Collections.Generic;

namespace Worktown.Sim.Layout
{
    /// <summary>
    /// Spec 062a: the rules a layout must keep (<see cref="Validate"/>), pure and deterministic, in integer millimetres. Pieces of a type
    /// the catalog does not know are skipped (the save keeps them for a later build; they are not spawned).
    /// <list type="number">
    /// <item>Inside: every footprint inside the floor; a yard piece outside it; a wall piece touches a wall.</item>
    /// <item>No overlap: footprints of the same band (floor, counter-line and yard pieces; wall pieces; overhead pieces) do not intersect
    /// (touching is allowed); no floor-band piece goes into a wall; no piece covers a keep-out rect.</item>
    /// <item>Aisles: in front of each floor-band piece's access side (the cook side; a counter with a guest side also that side) a strip
    /// <see cref="AisleMm"/> deep across its width is free of other floor-band pieces and walls. Wall and overhead pieces hang above the
    /// counters and have no aisle of their own.</item>
    /// <item>Reachable: each piece with a station (<see cref="PieceDef.HasStation"/>) has a free cell in its access strip that a cook
    /// reaches from the staff spawn on the <see cref="WalkGrid"/>. A wall or overhead piece is reached across the counter under it: its
    /// strip is <see cref="ReachOverMm"/> + <see cref="AisleMm"/> deep from its face.</item>
    /// <item>Guests: at least one serving counter has its guest strip inside the guest area with a free cell guests reach from the guest
    /// entry through free cells of the guest area only.</item>
    /// <item>Guest side: every piece with a guest side has its guest strip inside the guest area and its cook strip inside the cook area.</item>
    /// <item>Required: one of each required type is placed.</item>
    /// </list>
    /// </summary>
    public static class LayoutRules
    {
        /// <summary>The hard minimum aisle (question 2: 0.9 m; spec 062b shows a soft "tight aisle" note under 1.2 m).</summary>
        public const int AisleMm = 900;
        /// <summary>How far a cook reaches over a counter to a wall or overhead piece behind or above it (the deepest station, 1.2 m).</summary>
        public const int ReachOverMm = 1200;

        public static LayoutReport Validate(ShopLayout layout, RoomDef room, PieceCatalog catalog)
        {
            if (layout == null || room == null || catalog == null) throw new ArgumentException("a layout, a room and a catalog are required");
            var known = new List<(LayoutPiece Piece, PieceDef Def, RectMm Rect)>();
            foreach (LayoutPiece p in layout.Pieces)
                if (catalog.TryGet(p.TypeId, out PieceDef def)) known.Add((p, def, def.Footprint(p)));
            var problems = new List<LayoutProblem>();

            // 1. Inside.
            foreach (var k in known)
            {
                if (k.Def.Mount == PieceMount.Yard)
                {
                    if (k.Rect.Intersects(room.Floor)) problems.Add(At(LayoutProblemKind.Outside, k.Piece, 0, "The " + NameOf(k.Piece.TypeId) + " stays in the yard"));
                    continue;
                }
                if (!room.Floor.Contains(k.Rect))
                    problems.Add(At(LayoutProblemKind.Outside, k.Piece, 0, "The " + NameOf(k.Piece.TypeId) + " must stand inside the shop"));
                else if (k.Def.Mount == PieceMount.Wall && !TouchesWall(k.Rect, room))
                    problems.Add(At(LayoutProblemKind.OffWall, k.Piece, 0, "The " + NameOf(k.Piece.TypeId) + " must hang on a wall"));
            }

            // 2. No overlap.
            for (int i = 0; i < known.Count; i++)
            {
                var a = known[i];
                for (int j = i + 1; j < known.Count; j++)
                {
                    var b = known[j];
                    if (Band(a.Def.Mount) != Band(b.Def.Mount) || !a.Rect.Intersects(b.Rect)) continue;
                    PointMm mid = Middle(Overlap(a.Rect, b.Rect));
                    problems.Add(new LayoutProblem(LayoutProblemKind.Overlap, a.Piece.Id, b.Piece.Id, mid.X, mid.Z,
                        "The " + NameOf(a.Piece.TypeId) + " overlaps the " + NameOf(b.Piece.TypeId)));
                }
                if (Band(a.Def.Mount) == 0)
                    foreach (RectMm wall in room.Walls)
                        if (a.Rect.Intersects(wall))
                        {
                            PointMm mid = Middle(Overlap(a.Rect, wall));
                            problems.Add(new LayoutProblem(LayoutProblemKind.Overlap, a.Piece.Id, 0, mid.X, mid.Z, "The " + NameOf(a.Piece.TypeId) + " goes into a wall"));
                            break;
                        }
                foreach (RectMm keep in room.KeepOut)
                    if (a.Rect.Intersects(keep))
                    {
                        PointMm mid = Middle(Overlap(a.Rect, keep));
                        problems.Add(new LayoutProblem(LayoutProblemKind.KeepOut, a.Piece.Id, 0, mid.X, mid.Z, "The " + NameOf(a.Piece.TypeId) + " blocks a door"));
                        break;
                    }
            }

            // 3. Aisles.
            foreach (var k in known)
            {
                if (Band(k.Def.Mount) != 0) continue;
                foreach (RectMm strip in AisleStrips(k.Piece, k.Def))
                {
                    bool wallNoted = false;
                    foreach (var other in known)
                    {
                        if (other.Piece.Id == k.Piece.Id || Band(other.Def.Mount) != 0 || !strip.Intersects(other.Rect)) continue;
                        problems.Add(At(LayoutProblemKind.Aisle, k.Piece, other.Piece.Id, "Too close to the " + NameOf(other.Piece.TypeId) + ": aisles need 0.9 m"));
                    }
                    foreach (RectMm wall in room.Walls)
                        if (!wallNoted && strip.Intersects(wall))
                        {
                            problems.Add(At(LayoutProblemKind.Aisle, k.Piece, 0, "Too close to the wall: aisles need 0.9 m"));
                            wallNoted = true;
                        }
                }
            }

            // 4. Every station reachable.
            WalkGrid grid = WalkGrid.ForLayout(layout, room, catalog);
            IReadOnlyList<bool> staff = grid.ReachableFrom(room.StaffSpawn);
            foreach (var k in known)
            {
                if (!k.Def.HasStation) continue;
                if (!AnyReached(grid, staff, AccessStrip(k.Piece, k.Def, false)))
                    problems.Add(At(LayoutProblemKind.Unreachable, k.Piece, 0, "Nobody can reach the " + NameOf(k.Piece.TypeId)));
            }

            // 5. Guests reach a serving counter.
            WalkGrid guestGrid = grid.Within(room.GuestArea);
            IReadOnlyList<bool> guests = guestGrid.ReachableFrom(room.GuestEntry);
            bool served = false;
            foreach (var k in known)
            {
                if (k.Piece.TypeId != PieceCatalog.ServingCounter) continue;
                RectMm strip = AccessStrip(k.Piece, k.Def, true);
                if (room.GuestArea.Contains(strip) && AnyReached(guestGrid, guests, strip)) served = true;
            }
            if (!served)
                problems.Add(new LayoutProblem(LayoutProblemKind.NoGuestCounter, 0, 0, room.GuestEntry.X, room.GuestEntry.Z,
                    "Guests cannot reach a serving counter"));

            // 6. Counters with a guest side.
            foreach (var k in known)
            {
                if (!k.Def.HasGuestSide) continue;
                if (!room.GuestArea.Contains(AccessStrip(k.Piece, k.Def, true)) || !room.CookArea.Contains(AccessStrip(k.Piece, k.Def, false)))
                    problems.Add(At(LayoutProblemKind.NoGuestSide, k.Piece, 0,
                        "The " + NameOf(k.Piece.TypeId) + " needs the guests on one side and the kitchen on the other"));
            }

            // 7. Required pieces.
            foreach (PieceDef row in catalog.Rows)
                if (row.Required && layout.CountOf(row.TypeId) == 0)
                    problems.Add(new LayoutProblem(LayoutProblemKind.Missing, 0, 0, 0, 0, "The shop needs a " + NameOf(row.TypeId)));

            return new LayoutReport(problems);
        }

        /// <summary>Rule 2's bands: floor, counter-line and yard pieces together (0), wall pieces (1), overhead pieces (2).</summary>
        public static int Band(PieceMount mount) => mount == PieceMount.Wall ? 1 : mount == PieceMount.Overhead ? 2 : 0;

        /// <summary>
        /// The way a piece's cook side faces after <paramref name="turns"/> quarter turns (Unity's yaw, clockwise seen from above): 0 -z,
        /// 1 -x, 2 +z, 3 +x. Its guest side faces the other way.
        /// </summary>
        public static PointMm CookDirection(int turns)
        {
            switch (((turns % 4) + 4) % 4)
            {
                case 1: return new PointMm(-1, 0);
                case 2: return new PointMm(0, 1);
                case 3: return new PointMm(1, 0);
                default: return new PointMm(0, -1);
            }
        }

        /// <summary>
        /// The strip in front of a piece's cook side (<paramref name="guestSide"/>: its guest side) across its width: <see cref="AisleMm"/>
        /// deep, for a wall or overhead piece <see cref="ReachOverMm"/> + <see cref="AisleMm"/> deep (rule 4: reached across a counter).
        /// </summary>
        public static RectMm AccessStrip(LayoutPiece piece, PieceDef def, bool guestSide)
        {
            if (piece == null || def == null) throw new ArgumentException("a piece and its row are required");
            int depth = Band(def.Mount) == 0 ? AisleMm : ReachOverMm + AisleMm;
            return Strip(def.Footprint(piece), CookDirection(piece.Turns + (guestSide ? 2 : 0)), depth);
        }

        /// <summary>Rule 3's strips of a piece: its cook side's, and its guest side's when it has one.</summary>
        public static IReadOnlyList<RectMm> AisleStrips(LayoutPiece piece, PieceDef def)
        {
            if (piece == null || def == null) throw new ArgumentException("a piece and its row are required");
            return def.HasGuestSide ? new[] { AccessStrip(piece, def, false), AccessStrip(piece, def, true) } : new[] { AccessStrip(piece, def, false) };
        }

        /// <summary>A type id as a short name for the problem texts: "dish_return_module" is "dish return", "shelf_dry" "shelf dry".</summary>
        public static string NameOf(string typeId)
        {
            if (string.IsNullOrEmpty(typeId)) return "piece";
            string name = typeId.Replace('_', ' ');
            return name.EndsWith(" module", StringComparison.Ordinal) ? name.Substring(0, name.Length - " module".Length) : name;
        }

        private static RectMm Strip(RectMm f, PointMm d, int depth)
        {
            if (d.Z < 0) return new RectMm(f.MinX, f.MinZ - depth, f.MaxX, f.MinZ);
            if (d.Z > 0) return new RectMm(f.MinX, f.MaxZ, f.MaxX, f.MaxZ + depth);
            if (d.X < 0) return new RectMm(f.MinX - depth, f.MinZ, f.MinX, f.MaxZ);
            return new RectMm(f.MaxX, f.MinZ, f.MaxX + depth, f.MaxZ);
        }

        private static bool TouchesWall(RectMm rect, RoomDef room)
        {
            var grown = new RectMm(rect.MinX - 1, rect.MinZ - 1, rect.MaxX + 1, rect.MaxZ + 1);
            foreach (RectMm wall in room.Walls)
                if (grown.Intersects(wall)) return true;
            return false;
        }

        private static bool AnyReached(WalkGrid grid, IReadOnlyList<bool> reached, RectMm strip)
        {
            foreach (int cell in grid.CellsIn(strip))
                if (reached[cell]) return true;
            return false;
        }

        private static LayoutProblem At(LayoutProblemKind kind, LayoutPiece piece, int otherId, string text) =>
            new LayoutProblem(kind, piece.Id, otherId, piece.XMm, piece.ZMm, text);

        private static RectMm Overlap(RectMm a, RectMm b) =>
            new RectMm(Math.Max(a.MinX, b.MinX), Math.Max(a.MinZ, b.MinZ), Math.Min(a.MaxX, b.MaxX), Math.Min(a.MaxZ, b.MaxZ));

        private static PointMm Middle(RectMm r) => new PointMm(r.MinX + r.Width / 2, r.MinZ + r.Depth / 2);
    }
}
