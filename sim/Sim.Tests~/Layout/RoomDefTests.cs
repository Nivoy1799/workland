using System;
using System.Linq;
using Worktown.Sim.Layout;
using Xunit;

namespace Worktown.Sim.Tests.Layout
{
    public class RoomDefTests
    {
        [Fact]
        public void CreateDefault_IsTodaysShell()
        {
            RoomDef room = RoomDef.CreateDefault();
            Assert.Equal(new RectMm(460, 11500, 14800, 14920), room.Floor);
            Assert.True(room.Site.Contains(room.Floor));
            Assert.True(room.Site.Contains(room.GuestArea));
            Assert.Equal(4, room.Walls.Count);
            Assert.All(room.Walls, w => Assert.False(w.Intersects(room.Floor)));
            Assert.Equal(new PointMm(1200, 12950), room.StaffSpawn);
            Assert.Equal(new PointMm(24000, 11100), room.GuestEntry);
            Assert.True(room.GuestArea.Contains(room.GuestEntry.X, room.GuestEntry.Z));
            Assert.Single(room.KeepOut);
            Assert.False(room.KeepOut.Any(k => room.Walls.Any(w => w.Intersects(k))), "the keep-out is the door's gap");
        }

        [Fact]
        public void TheConstructor_CopiesItsLists_AndChecksTheFloor()
        {
            var walls = new[] { new RectMm(0, 0, 10, 10) };
            var room = new RoomDef(new RectMm(0, 0, 100, 100), new RectMm(10, 10, 90, 90), default, default, walls, null, default, default);
            walls[0] = new RectMm(1, 1, 2, 2);
            Assert.Equal(new RectMm(0, 0, 10, 10), room.Walls[0]);
            Assert.Empty(room.KeepOut);
            Assert.Throws<ArgumentException>(() => new RoomDef(new RectMm(0, 0, 100, 100), new RectMm(50, 50, 150, 90), default, default, null, null, default, default));
            Assert.Throws<ArgumentException>(() => new RoomDef(default, default, default, default, null, null, default, default));
        }

        [Fact]
        public void PointMm_HasValueEquality()
        {
            Assert.Equal(new PointMm(1, 2), new PointMm(1, 2));
            Assert.True(new PointMm(1, 2) == new PointMm(1, 2));
            Assert.True(new PointMm(1, 2) != new PointMm(2, 1));
            Assert.Equal(new PointMm(1, 2).GetHashCode(), new PointMm(1, 2).GetHashCode());
            Assert.Equal("(1, -2)", new PointMm(1, -2).ToString());
        }

        [Fact]
        public void LayoutReport_CountsAndLines()
        {
            var r = new LayoutReport(new[]
            {
                new LayoutProblem(LayoutProblemKind.Aisle, 7, 8, 0, 0, "Too close to the griddle: aisles need 0.9 m"),
                new LayoutProblem(LayoutProblemKind.Missing, 0, 0, 0, 0, "The shop needs a sink"),
            });
            Assert.False(r.IsValid);
            Assert.Equal(1, r.Count(LayoutProblemKind.Aisle));
            Assert.Equal(0, r.Count(LayoutProblemKind.Overlap));
            Assert.Equal(new[] { "Aisle 7/8: Too close to the griddle: aisles need 0.9 m", "Missing 0/0: The shop needs a sink" }, r.Lines());
            Assert.True(new LayoutReport(null).IsValid);
            Assert.Equal(new LayoutReport(r.Problems), r);
            Assert.NotEqual(new LayoutReport(r.Problems.Take(1)), r);
        }
    }
}
