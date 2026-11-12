using System;
using Worktown.Sim.Town;
using Xunit;
using T = Worktown.Sim.Tests.Town.TestTown;

namespace Worktown.Sim.Tests.Town
{
    public class TownPathsTests
    {
        // Fixture lengths (mm, floor): 1-2 = 1-4 = 2-3 = 4-3 = 53851; 2-5 = 64031; 3-5 = 60000 (a 1 s lift).

        [Fact]
        public void StraightRoute() // AC5
        {
            var paths = new TownPaths(T.Catalog());
            WalkRoute? r = paths.Route(1, 2);
            Assert.NotNull(r);
            Assert.Equal(new[] { 1, 2 }, r!.Nodes);
            Assert.Equal(53851, r.LengthMm);
            Assert.Equal(53851L * 2 / 5, r.Ms);
        }

        [Fact]
        public void RouteAroundAGap() // AC5: no edge 1-3
        {
            var paths = new TownPaths(T.Catalog());
            WalkRoute r = paths.Route(1, 3)!;
            Assert.Equal(3, r.Nodes.Count);
            Assert.Equal(107702, r.LengthMm);
            Assert.Equal(r.LengthMm * 2 / 5, r.Ms);
        }

        [Fact]
        public void TieResolvesToTheSmallerNodeSequenceEveryRun() // AC5: 1-2-3 and 1-4-3 cost the same
        {
            for (int run = 0; run < 3; run++)
            {
                var paths = new TownPaths(T.Catalog());
                Assert.Equal(new[] { 1, 2, 3 }, paths.Route(1, 3)!.Nodes);
                Assert.Equal(new[] { 3, 2, 1 }, paths.Route(3, 1)!.Nodes);
            }
        }

        [Fact]
        public void TieBreaksOnFewerNodesFirst()
        {
            // 1-6-3 through the midpoint (0, 0) and 1-3 directly are both 100 m; the one-edge route wins.
            var parts = T.Small();
            parts.Nodes.Add(new PathNode(6, TownPoint.Metres(0, 0), 0, "alpha"));
            parts.Edges.Add(new PathEdge(1, 6, PathEdgeKind.Path, 5000, 0));
            parts.Edges.Add(new PathEdge(6, 3, PathEdgeKind.Path, 5000, 0));
            parts.Edges.Add(new PathEdge(3, 1, PathEdgeKind.Path, 5000, 0));
            var paths = new TownPaths(parts.Build());
            WalkRoute r = paths.Route(1, 3)!;
            Assert.Equal(new[] { 1, 3 }, r.Nodes);
            Assert.Equal(100000, r.LengthMm);
        }

        [Fact]
        public void LiftUsedOnlyWithAllowLifts() // AC5
        {
            var paths = new TownPaths(T.Catalog());
            WalkRoute walk = paths.Route(1, 5)!;
            Assert.Equal(new[] { 1, 2, 5 }, walk.Nodes);
            Assert.Equal((53851L + 64031) * 2 / 5, walk.Ms);
            WalkRoute lift = paths.Route(1, 5, allowLifts: true)!;
            Assert.Equal(new[] { 1, 2, 3, 5 }, lift.Nodes);
            Assert.Equal(107702L * 2 / 5 + 1000, lift.Ms);
            Assert.Equal(167702, lift.LengthMm); // horizontal length of every edge, the lift's too
            Assert.True(lift.Ms < walk.Ms);
        }

        [Fact]
        public void UnreachableGivesMinusOne() // AC5
        {
            var paths = new TownPaths(T.Small().RemoveEdge(2, 5).Build());
            Assert.Null(paths.Route(1, 5));
            Assert.Equal(-1, paths.WalkMs(1, 5));
            Assert.True(paths.WalkMs(1, 5, allowLifts: true) > 0);
        }

        [Fact]
        public void WalkMsAndPlaceWalkMs()
        {
            var paths = new TownPaths(T.Catalog());
            Assert.Equal(0, paths.WalkMs(1, 1));
            Assert.Equal(new[] { 1 }, paths.Route(1, 1)!.Nodes);
            Assert.Equal(107702L * 2 / 5, paths.WalkMs(1, 3));
            Assert.Equal(paths.WalkMs(1, 3), paths.PlaceWalkMs("p_one", "p_two"));
            Assert.Equal(paths.WalkMs(3, 1), paths.PlaceWalkMs("p_two", "p_one"));
            Assert.Throws<ArgumentException>(() => paths.WalkMs(1, 99));
            Assert.Throws<ArgumentException>(() => paths.PlaceWalkMs("p_one", "nope"));
            Assert.Throws<ArgumentException>(() => new TownPaths(null!));
        }

        [Fact]
        public void ISqrtIsTheExactFloor()
        {
            Assert.Equal(0, TownPaths.ISqrt(0));
            Assert.Equal(1, TownPaths.ISqrt(1));
            Assert.Equal(1, TownPaths.ISqrt(3));
            Assert.Equal(2, TownPaths.ISqrt(4));
            Assert.Equal(53851, TownPaths.ISqrt(2_900_000_000));
            Assert.Equal(3037000499, TownPaths.ISqrt(long.MaxValue));
            for (long v = 0; v < 5000; v++)
            {
                long r = TownPaths.ISqrt(v);
                Assert.True(r * r <= v && (r + 1) * (r + 1) > v);
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => TownPaths.ISqrt(-1));
        }

        [Fact]
        public void DistanceIsHorizontal()
        {
            Assert.Equal(5000, TownPaths.Distance(TownPoint.Metres(0, 0), TownPoint.Metres(3, 4)));
            Assert.Equal(53851, TownPaths.Distance(TownPoint.Metres(-50, 0), TownPoint.Metres(0, 20)));
        }

        [Fact]
        public void DefaultTownWalks()
        {
            TownCatalog c = TownCatalog.CreateDefault();
            var paths = new TownPaths(c);
            Assert.Equal(32000, paths.PlaceWalkMs("fountain", "s1")); // 80 m along Main Street
            long walk = paths.PlaceWalkMs("fountain", "ingredient_lab");
            long ride = paths.PlaceWalkMs("fountain", "ingredient_lab", allowLifts: true);
            Assert.True(ride < walk); // the funicular is a shortcut, the ramps are always there
            long tower = paths.PlaceWalkMs("station", "sky_deck");
            Assert.True(paths.PlaceWalkMs("station", "sky_deck", allowLifts: true) <= tower);
        }
    }
}
