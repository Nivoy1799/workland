using System;
using System.Linq;
using Worktown.Sim.Layout;
using Xunit;

namespace Worktown.Sim.Tests.Layout
{
    public class WalkGridTests
    {
        private static readonly RectMm Bounds = new RectMm(0, 0, 2000, 1000); // 21 x 11 centres

        [Fact]
        public void Cells_AreTheLatticePoints_IncludingBothEdges()
        {
            var grid = new WalkGrid(Bounds, null);
            Assert.Equal(21, grid.Columns);
            Assert.Equal(11, grid.Rows);
            Assert.Equal(231, grid.CellCount);
            Assert.Equal(new PointMm(0, 0), grid.Centre(0));
            Assert.Equal(new PointMm(2000, 1000), grid.Centre(grid.CellCount - 1));
            Assert.Equal(new PointMm(300, 200), grid.Centre(grid.Index(3, 2)));
            Assert.All(Enumerable.Range(0, grid.CellCount), i => Assert.True(grid.IsFree(i)));
            Assert.False(grid.IsFree(-1));
            Assert.False(grid.IsFree(grid.CellCount));
            Assert.Throws<ArgumentException>(() => new WalkGrid(new RectMm(0, 0, 0, 100), null));
        }

        [Fact]
        public void TryCell_TakesTheNearestCentre()
        {
            var grid = new WalkGrid(Bounds, null);
            Assert.True(grid.TryCell(new PointMm(349, 151), out int i));
            Assert.Equal(new PointMm(300, 200), grid.Centre(i));
            Assert.True(grid.TryCell(new PointMm(350, 150), out i)); // a half rounds up
            Assert.Equal(new PointMm(400, 200), grid.Centre(i));
            Assert.True(grid.TryCell(new PointMm(2000, 1000), out i));
            Assert.Equal(grid.CellCount - 1, i);
            Assert.False(grid.TryCell(new PointMm(-1, 0), out i));
            Assert.Equal(-1, i);
            Assert.False(grid.TryCell(new PointMm(0, 1001), out _));
        }

        [Fact]
        public void ABlock_TakesEveryCellCloserThanTheClearance()
        {
            var block = new RectMm(1000, 400, 1200, 600);
            var grid = new WalkGrid(Bounds, new[] { block });
            bool Free(int x, int z) => grid.TryCell(new PointMm(x, z), out int i) && grid.IsFree(i);
            Assert.False(Free(1100, 500), "inside");
            Assert.False(Free(700, 500), "300 mm away");
            Assert.True(Free(600, 500), "exactly 400 mm away is free");
            Assert.True(Free(1600, 500));
            Assert.False(Free(1500, 500));
            Assert.True(Free(1100, 0), "400 mm below");
            Assert.False(Free(1100, 100));
            Assert.False(Free(800, 200), "a corner 283 mm away");
            Assert.True(Free(700, 100), "a corner 424 mm away");
        }

        [Fact]
        public void DistanceSquared_IsToTheNearestPointOfTheRect()
        {
            var r = new RectMm(0, 0, 100, 100);
            Assert.Equal(0, WalkGrid.DistanceSquared(r, 50, 50));
            Assert.Equal(0, WalkGrid.DistanceSquared(r, 100, 100));
            Assert.Equal(100, WalkGrid.DistanceSquared(r, 110, 50));
            Assert.Equal(25, WalkGrid.DistanceSquared(r, -3, -4));
        }

        [Fact]
        public void Within_TakesTheCellsOutsideTheArea()
        {
            var grid = new WalkGrid(Bounds, null).Within(new RectMm(0, 0, 1000, 500));
            Assert.True(grid.TryCell(new PointMm(1000, 500), out int edge));
            Assert.True(grid.IsFree(edge), "the edge is inside");
            Assert.True(grid.TryCell(new PointMm(1100, 500), out int outside));
            Assert.False(grid.IsFree(outside));
            Assert.Equal(66, Enumerable.Range(0, grid.CellCount).Count(grid.IsFree)); // 11 x 6
        }

        [Fact]
        public void Neighbours_ComeWestEastSouthNorth()
        {
            var grid = new WalkGrid(Bounds, null);
            int i = grid.Index(3, 2);
            Assert.Equal(new[] { grid.Index(2, 2), grid.Index(4, 2), grid.Index(3, 1), grid.Index(3, 3) }, grid.Neighbours(i));
            Assert.Equal(new[] { 1, grid.Columns }, grid.Neighbours(0));
        }

        [Fact]
        public void ReachableFrom_StopsAtAWall()
        {
            var wall = new RectMm(900, -1000, 1100, 2000); // across the whole grid
            var grid = new WalkGrid(Bounds, new[] { wall });
            var reached = grid.ReachableFrom(new PointMm(0, 0));
            Assert.True(grid.TryCell(new PointMm(500, 500), out int west));
            Assert.True(grid.TryCell(new PointMm(1500, 500), out int east));
            Assert.True(reached[west]);
            Assert.False(reached[east]);
            Assert.All(grid.ReachableFrom(new PointMm(1000, 500)), r => Assert.False(r)); // starts in the wall
            Assert.All(grid.ReachableFrom(new PointMm(-500, 0)), r => Assert.False(r)); // outside the grid
        }

        [Fact]
        public void CellsIn_ListsTheCentresInsideTheArea()
        {
            var grid = new WalkGrid(Bounds, null);
            Assert.Equal(new[] { grid.Index(1, 1), grid.Index(2, 1), grid.Index(1, 2), grid.Index(2, 2) },
                grid.CellsIn(new RectMm(50, 100, 200, 250)));
            Assert.Empty(grid.CellsIn(new RectMm(5000, 5000, 6000, 6000)));
            Assert.Equal(grid.CellCount, grid.CellsIn(new RectMm(-500, -500, 3000, 3000)).Count);
        }

        [Fact]
        public void ForLayout_BlocksTheWallsAndTheFloorPieces_NotTheWallOrOverheadOnes()
        {
            ShopLayout standard = DefaultLayoutFile.Load();
            RoomDef room = RoomDef.CreateDefault();
            WalkGrid grid = WalkGrid.ForLayout(standard, room, PieceCatalog.CreateDefault());
            Assert.Equal(room.Site, grid.Bounds);
            bool Free(int x, int z) => grid.TryCell(new PointMm(x, z), out int i) && grid.IsFree(i);
            Assert.True(Free(1200, 13000), "the aisle by spawn point 1");
            Assert.False(Free(7480, 14300), "a stove");
            Assert.False(Free(14900, 13000), "the right wall");
            Assert.True(Free(11600, 11100), "the queue head's cell");
            Assert.False(Free(11600, 11200), "closer than 0.4 m to the serving counter");
            Assert.True(Free(300, 12950), "the staff door");
            Assert.Throws<ArgumentException>(() => WalkGrid.ForLayout(null!, room, PieceCatalog.CreateDefault()));
        }
    }
}
