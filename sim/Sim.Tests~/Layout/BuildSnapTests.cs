using System;
using Worktown.Sim.Layout;
using Xunit;

namespace Worktown.Sim.Tests.Layout
{
    /// <summary>Spec 062b AC2: BuildSnap's grid, turns, floor edge, wall and magnet as literals.</summary>
    public class BuildSnapTests
    {
        private static readonly PieceDef Box = new PieceDef("cabinet", 600, 400, PieceMount.InLine, false, false, false, false, true, 0, 0);
        private static readonly PieceDef Panel = new PieceDef("panel", 800, 20, PieceMount.Wall, false, false, true, false, true, 0, 0);
        private static readonly PieceCatalog Catalog = new PieceCatalog(new[] { Box, Panel });

        // A 6 x 4 m floor from (0, 0); the street south of it.
        private static readonly RoomDef Room = new RoomDef(new RectMm(-2000, -3000, 8000, 6000), new RectMm(0, 0, 6000, 4000),
            new RectMm(0, 0, 6000, 4000), new RectMm(0, -3000, 8000, 0), null, null, new PointMm(3000, 2000), new PointMm(7500, -1000));

        private static LayoutPiece P(int id, string type, int x, int z, int turns = 0) => new LayoutPiece(id, type, x, z, turns, 0, 1, 0);

        /// <summary>Two cabinets touching along the back wall: x 1000-2200, back edge z 4000.</summary>
        private static ShopLayout Run() => new ShopLayout(1, new[] { P(1, "cabinet", 1300, 3800), P(2, "cabinet", 1900, 3800) }, null, null, 3);

        [Fact]
        public void Snap_PutsTheMinCornerOnTheGrid()
        {
            Assert.Equal(new PointMm(1050, 1950), BuildSnap.Snap(Box, 1130, 2010, 0, 0)); // corner (830, 1810) to (750, 1750)
            Assert.Equal(new PointMm(1200, 2050), BuildSnap.Snap(Box, 1130, 2010, 1, 0)); // turned: 400 x 600, corner (930, 1710) to (1000, 1750)
            Assert.Equal(new PointMm(1010, 1950), BuildSnap.Snap(Box, 1130, 2010, 0, 0, new PointMm(460, 0))); // the room grid from x 460
            Assert.Equal(new PointMm(-200, -50), BuildSnap.Snap(Box, -100, -100, 0, 0)); // below zero: (-400, -300) to (-500, -250)
            Assert.Equal(new PointMm(550, 450), BuildSnap.Snap(Box, 425, 325, 0, 0)); // corner (125, 125): a half rounds up to (250, 250)
            Assert.Equal(new PointMm(300, 200), BuildSnap.Snap(Box, 300, 200, 0, 0)); // already on the grid
        }

        [Fact]
        public void Turns_WrapToFourQuarterTurns()
        {
            Assert.Equal(3, BuildSnap.Turns(-1));
            Assert.Equal(1, BuildSnap.Turns(5));
            Assert.Equal(0, BuildSnap.Turns(4));
            Assert.Equal((400, 600), BuildSnap.Size(Box, 3, 0));
            Assert.Equal((600, 400), BuildSnap.Size(Box, 2, 0));
        }

        [Fact]
        public void Pose_MagnetSnapsFlushToARunsEnd()
        {
            // Aimed 50 mm past the run's end and 100 mm in front of its line: flush to x 2200 on the run's back edge.
            LayoutPiece moved = BuildSnap.Pose(Run(), Catalog, Room, P(9, "cabinet", 0, 0), 2550, 3700, 0);
            Assert.Equal(new LayoutPiece(9, "cabinet", 2500, 3800, 0, 0, 1, 0), moved);
            // Before the run's near end.
            Assert.Equal(new PointMm(700, 3800), Centre(BuildSnap.Pose(Run(), Catalog, Room, P(9, "cabinet", 0, 0), 600, 3700, 0)));
            // Far from the run: only the grid.
            Assert.Equal(new PointMm(4550, 3700), Centre(BuildSnap.Pose(Run(), Catalog, Room, P(9, "cabinet", 0, 0), 4550, 3700, 0)));
            // Other turns: no magnet.
            Assert.Equal(new PointMm(2550, 3700), Centre(BuildSnap.Pose(Run(), Catalog, Room, P(9, "cabinet", 0, 0), 2550, 3700, 2)));
        }

        [Fact]
        public void Pose_LeavesThePieceItselfOutOfTheRuns()
        {
            // Piece 2 aimed a little east: the run it leaves is piece 1 alone, so it snaps flush to piece 1's end again.
            LayoutPiece moved = BuildSnap.Pose(Run(), Catalog, Room, Run().Piece(2)!, 1950, 3800, 0);
            Assert.Equal(new PointMm(1900, 3800), Centre(moved));
        }

        [Fact]
        public void Pose_PushesAPieceBackInsideTheFloor()
        {
            // The grid puts it 100 mm past the east wall and 150 mm past the back: pushed back flush.
            Assert.Equal(new PointMm(5700, 3800), Centre(BuildSnap.Pose(Run(), Catalog, Room, P(9, "cabinet", 0, 0), 5900, 3900, 0)));
        }

        [Fact]
        public void Pose_PutsAWallPieceFlushAgainstTheWall()
        {
            Assert.Equal(new PointMm(2900, 3990), Centre(BuildSnap.Pose(Run(), Catalog, Room, P(9, "panel", 0, 0), 3000, 3900, 0)));
            Assert.Equal(new PointMm(2900, 3990), Centre(BuildSnap.Pose(Run(), Catalog, Room, P(9, "panel", 0, 0), 3000, 3700, 0)));
            // Far from the wall: it stays where the grid puts it (the rules then refuse it: off the wall).
            Assert.Equal(new PointMm(2900, 2010), Centre(BuildSnap.Pose(Run(), Catalog, Room, P(9, "panel", 0, 0), 3000, 2000, 0)));
        }

        [Fact]
        public void Pose_RejectsMissingInputs()
        {
            Assert.Throws<ArgumentException>(() => BuildSnap.Pose(null!, Catalog, Room, P(1, "cabinet", 0, 0), 0, 0, 0));
            Assert.Throws<ArgumentException>(() => BuildSnap.Size(null!, 0, 0));
        }

        private static PointMm Centre(LayoutPiece p) => new PointMm(p.XMm, p.ZMm);
    }
}
