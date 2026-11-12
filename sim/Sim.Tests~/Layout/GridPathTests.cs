using System;
using System.Linq;
using Worktown.Sim.Layout;
using Xunit;

namespace Worktown.Sim.Tests.Layout
{
    /// <summary>Spec 062a AC2: GridPath literals (straight, around a block, no path) and its determinism.</summary>
    public class GridPathTests
    {
        private static readonly RectMm Bounds = new RectMm(0, 0, 4000, 2000);

        [Fact]
        public void AnOpenGrid_GivesAStraightLine()
        {
            var grid = new WalkGrid(Bounds, null);
            GridPath path = GridPath.Find(grid, new PointMm(3600, 1000), new PointMm(400, 1000));
            Assert.True(path.Found);
            Assert.Equal(33, path.Cells.Count);
            Assert.Equal(new[] { new PointMm(3600, 1000), new PointMm(400, 1000) }, path.Corners);
            Assert.All(path.Cells, c => Assert.Equal(1000, c.Z));
        }

        [Fact]
        public void ABlock_IsWalkedAround()
        {
            var block = new RectMm(1900, 600, 2100, 2000); // from the top edge down to z 0.6 m
            var grid = new WalkGrid(Bounds, new[] { block });
            GridPath path = GridPath.Find(grid, new PointMm(3600, 1000), new PointMm(400, 1000));
            Assert.True(path.Found);
            Assert.All(path.Cells, c => Assert.True(WalkGrid.DistanceSquared(block, c.X, c.Z) >= 400L * 400L, c.ToString()));
            Assert.Contains(path.Cells, c => c.Z <= 200); // under the block
            Assert.Equal(new PointMm(3600, 1000), path.Corners.First());
            Assert.Equal(new PointMm(400, 1000), path.Corners.Last());
            Assert.True(path.Corners.Count >= 4, string.Join(" ", path.Corners));
            for (int i = 1; i < path.Cells.Count; i++) // one step at a time, 4-neighbour
                Assert.Equal(100, Math.Abs(path.Cells[i].X - path.Cells[i - 1].X) + Math.Abs(path.Cells[i].Z - path.Cells[i - 1].Z));
            // Shortest: 32 steps west plus down and up again by 0.8 m each (from 1.0 m to 0.2 m).
            Assert.Equal(32 + 16 + 1, path.Cells.Count);
        }

        [Fact]
        public void AWallAcross_GivesNoPath()
        {
            var grid = new WalkGrid(Bounds, new[] { new RectMm(1900, -500, 2100, 2500) });
            GridPath path = GridPath.Find(grid, new PointMm(3600, 1000), new PointMm(400, 1000));
            Assert.False(path.Found);
            Assert.Empty(path.Cells);
            Assert.Empty(path.Corners);
            Assert.False(GridPath.Find(grid, new PointMm(2000, 1000), new PointMm(400, 1000)).Found, "a start in the wall");
            Assert.False(GridPath.Find(grid, new PointMm(-100, 1000), new PointMm(400, 1000)).Found, "a start outside the grid");
            Assert.Throws<ArgumentException>(() => GridPath.Find(null!, new PointMm(0, 0), new PointMm(0, 0)));
        }

        [Fact]
        public void TheSameGrid_GivesTheSamePath()
        {
            var block = new RectMm(1500, 500, 2500, 1500);
            GridPath a = GridPath.Find(new WalkGrid(Bounds, new[] { block }), new PointMm(3600, 1000), new PointMm(400, 1000));
            GridPath b = GridPath.Find(new WalkGrid(Bounds, new[] { block }), new PointMm(3600, 1000), new PointMm(400, 1000));
            Assert.True(a.Found);
            Assert.Equal(a.Cells, b.Cells);
            Assert.Equal(a.Corners, b.Corners);
        }

        [Fact]
        public void StartEqualsGoal_IsOneCell()
        {
            GridPath path = GridPath.Find(new WalkGrid(Bounds, null), new PointMm(1000, 1000), new PointMm(1020, 990));
            Assert.Equal(new[] { new PointMm(1000, 1000) }, path.Cells);
            Assert.Equal(new[] { new PointMm(1000, 1000) }, path.Corners);
        }

        [Fact]
        public void Reduce_KeepsTheEndsAndTheTurns()
        {
            var cells = new[] { new PointMm(0, 0), new PointMm(100, 0), new PointMm(200, 0), new PointMm(200, 100), new PointMm(200, 200), new PointMm(300, 200) };
            Assert.Equal(new[] { new PointMm(0, 0), new PointMm(200, 0), new PointMm(200, 200), new PointMm(300, 200) }, GridPath.Reduce(cells));
            Assert.Empty(GridPath.Reduce(Array.Empty<PointMm>()));
            Assert.Equal(new[] { new PointMm(5, 5) }, GridPath.Reduce(new[] { new PointMm(5, 5) }));
        }
    }
}
