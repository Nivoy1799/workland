using System;
using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Town;

namespace Worktown.Sim.Tests.Town
{
    /// <summary>
    /// Spec 064 test fixture: a small town (2 districts, 5 nodes, 3 places) and editable catalog parts, so a test can change
    /// one thing and rebuild. <see cref="Parts.From"/> copies any catalog (used to break the default town one rule at a time).
    /// Nodes (metres): 1 (-50, 0), 2 (0, 20), 4 (0, -20) in alpha; 3 (50, 0), 5 (50, 60) in beta. Edges: 1-2, 2-3, 1-4,
    /// 4-3 (two equal routes 1 to 3), 2-5 (a path), 3-5 (a 1 s lift). There is no edge 1-3 (the gap).
    /// </summary>
    public static class TestTown
    {
        /// <summary>24 values summing to 1000: 42 for hours 0..15, 41 for 16..23.</summary>
        public static int[] Flat() => Enumerable.Range(0, 24).Select(h => h < 16 ? 42 : 41).ToArray();

        public static Parts Small() => new Parts
        {
            Curves = { new FootfallCurve("flat", Flat()) },
            Districts =
            {
                new DistrictDef("alpha", "Alpha", new[] { TownRect.Metres(-100, 0, -100, 100) }, 0, true, 1000, "flat", 1200, new[] { "beta" }),
                new DistrictDef("beta", "Beta", new[] { TownRect.Metres(0, 100, -100, 100) }, 0, false, 500, "flat", 1300, new[] { "alpha" }),
            },
            Nodes =
            {
                new PathNode(1, TownPoint.Metres(-50, 0), 0, "alpha"),
                new PathNode(2, TownPoint.Metres(0, 20), 0, "alpha"),
                new PathNode(3, TownPoint.Metres(50, 0), 0, "beta"),
                new PathNode(4, TownPoint.Metres(0, -20), 0, "alpha"),
                new PathNode(5, TownPoint.Metres(50, 60), 2000, "beta"),
            },
            Edges =
            {
                new PathEdge(1, 2, PathEdgeKind.Path, 5000, 0),
                new PathEdge(2, 3, PathEdgeKind.Path, 5000, 0),
                new PathEdge(1, 4, PathEdgeKind.Path, 5000, 0),
                new PathEdge(4, 3, PathEdgeKind.Path, 5000, 0),
                new PathEdge(2, 5, PathEdgeKind.Path, 5000, 0),
                new PathEdge(3, 5, PathEdgeKind.Lift, 3000, 1),
            },
            Places =
            {
                new PlaceDef("p_one", "Place One", PlaceKind.Small, "alpha", TownPoint.Metres(-50, 5), 1, 2, 70, 1000, 1000, false, false, new[] { "corner_counter" }),
                new PlaceDef("p_two", "Place Two", PlaceKind.Pitch, "beta", TownPoint.Metres(50, 5), 3, 1, 6, 1200, 1100, true, false, new[] { "corner_counter" }),
                new PlaceDef("p_event", "Event Pitch", PlaceKind.Pitch, "beta", TownPoint.Metres(55, 0), 3, 1, 6, 1000, 500, true, true, new string[0]),
            },
            PlayArea = { TownPoint.Metres(-100, -100), TownPoint.Metres(100, -100), TownPoint.Metres(100, 100), TownPoint.Metres(-100, 100) },
            SafeSpots = { new SafeSpot(1, 1), new SafeSpot(2, 3) },
            Reference = "p_one",
            Start = "p_one",
            Fountain = 1,
            DomeCentre = TownPoint.Metres(0, 0),
            DomeRadiusX = 200000,
            DomeRadiusZ = 200000,
            DomeCrown = 100000,
            FallHeight = -10000,
        };

        public static TownCatalog Catalog() => Small().Build();

        /// <summary>Editable catalog parts.</summary>
        public sealed class Parts
        {
            public List<DistrictDef> Districts { get; } = new List<DistrictDef>();
            public List<FootfallCurve> Curves { get; } = new List<FootfallCurve>();
            public List<PlaceDef> Places { get; } = new List<PlaceDef>();
            public List<PathNode> Nodes { get; } = new List<PathNode>();
            public List<PathEdge> Edges { get; } = new List<PathEdge>();
            public List<TownPoint> PlayArea { get; } = new List<TownPoint>();
            public List<SafeSpot> SafeSpots { get; } = new List<SafeSpot>();
            public string Reference { get; set; } = "";
            public string Start { get; set; } = "";
            public int Fountain { get; set; }
            public TownPoint DomeCentre { get; set; }
            public int DomeRadiusX { get; set; }
            public int DomeRadiusZ { get; set; }
            public int DomeCrown { get; set; }
            public int FallHeight { get; set; }

            public static Parts From(TownCatalog c)
            {
                var p = new Parts
                {
                    Reference = c.ReferencePlaceId, Start = c.StartPlaceId, Fountain = c.FountainNodeId, DomeCentre = c.DomeCentre,
                    DomeRadiusX = c.DomeRadiusXMm, DomeRadiusZ = c.DomeRadiusZMm, DomeCrown = c.DomeCrownMm, FallHeight = c.FallHeightMm,
                };
                p.Districts.AddRange(c.Districts);
                p.Curves.AddRange(c.Curves);
                p.Places.AddRange(c.Places);
                p.Nodes.AddRange(c.Nodes);
                p.Edges.AddRange(c.Edges);
                p.PlayArea.AddRange(c.PlayArea);
                p.SafeSpots.AddRange(c.SafeSpots);
                return p;
            }

            public static Parts Default() => From(TownCatalog.CreateDefault());

            public Parts District(string id, Func<DistrictDef, DistrictDef> change) { Replace(Districts, d => d.Id == id, change); return this; }
            public Parts Place(string id, Func<PlaceDef, PlaceDef> change) { Replace(Places, x => x.Id == id, change); return this; }
            public Parts Node(int id, Func<PathNode, PathNode> change) { Replace(Nodes, n => n.Id == id, change); return this; }
            public Parts Curve(string id, Func<FootfallCurve, FootfallCurve> change) { Replace(Curves, x => x.Id == id, change); return this; }

            public Parts RemoveEdge(int a, int b)
            {
                int removed = Edges.RemoveAll(e => (e.A == a && e.B == b) || (e.A == b && e.B == a));
                if (removed == 0) throw new InvalidOperationException("No edge " + a + "-" + b + ".");
                return this;
            }

            public TownCatalog Build() => new TownCatalog(Districts, Curves, Places, Nodes, Edges, PlayArea, SafeSpots, Reference, Start,
                Fountain, DomeCentre, DomeRadiusX, DomeRadiusZ, DomeCrown, FallHeight);

            private static void Replace<T>(List<T> list, Predicate<T> match, Func<T, T> change)
            {
                int i = list.FindIndex(match);
                if (i < 0) throw new InvalidOperationException("Nothing to change.");
                list[i] = change(list[i]);
            }
        }
    }
}
