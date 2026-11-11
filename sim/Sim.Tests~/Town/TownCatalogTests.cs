using System;
using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Climb;
using Worktown.Sim.Cooking;
using Worktown.Sim.Town;
using Xunit;
using T = Worktown.Sim.Tests.Town.TestTown;

namespace Worktown.Sim.Tests.Town
{
    public class TownCatalogTests
    {
        private static void Throws(T.Parts parts, string fragment)
        {
            var e = Assert.Throws<ArgumentException>(() => parts.Build());
            Assert.Contains(fragment, e.Message);
        }

        [Fact]
        public void CreateDefaultDoesNotThrowAndIsVegan() // AC3, D27
        {
            TownCatalog c = TownCatalog.CreateDefault();
            Assert.Equal(12, c.Districts.Count);
            Assert.All(c.Districts.Select(d => d.Name).Concat(c.Places.Select(p => p.Name)), n => Assert.Null(ContentRules.FindForbidden(n)));
        }

        [Fact]
        public void FixtureBuilds()
        {
            TownCatalog c = T.Catalog();
            Assert.Equal(2, c.Districts.Count);
            Assert.Equal(5, c.Nodes.Count);
            Assert.Equal(3, c.Places.Count);
        }

        // AC3: one literal per validation rule named by the spec.
        [Fact] public void DuplicateIdThrows() { var p = T.Small(); p.Places.Add(p.Places[0]); Throws(p, "Duplicate place id p_one"); }
        [Fact] public void BadIdThrows() => Throws(T.Small().Place("p_two", x => x with { Id = "P-Two" }), "Bad place id 'P-Two'");
        [Fact] public void CurveOf23ValuesThrows() => Throws(T.Small().Curve("flat", x => x with { HourPermille = x.HourPermille.Take(23).ToArray() }), "needs 24 hourly values");
        [Fact] public void CurveSum999Throws() => Throws(T.Small().Curve("flat", x => x with { HourPermille = x.HourPermille.Select((v, h) => h == 0 ? v - 1 : v).ToArray() }), "sum to 1000, not 999");
        [Fact] public void AnchorOutsideItsDistrictThrows() => Throws(T.Small().Place("p_one", x => x with { Anchor = TownPoint.Metres(-30, 0) } with { DistrictId = "beta", NodeId = 3 }), "outside its district beta");
        [Fact] public void UnknownNodeThrows() => Throws(T.Small().Place("p_one", x => x with { NodeId = 99 }), "unknown node 99");
        [Fact] public void AsymmetricNeighbourThrows() => Throws(T.Small().District("beta", d => d with { Neighbours = new string[0] }), "not the other way round");
        [Fact] public void EdgeToMissingNodeThrows() { var p = T.Small(); p.Edges.Add(new PathEdge(1, 42, PathEdgeKind.Path, 5000, 0)); Throws(p, "unknown node 42"); }
        [Fact] public void ForbiddenWordInNameThrows() => Throws(T.Small().Place("p_two", x => x with { Name = "Cheese Corner" }), "forbidden word 'cheese'");
        [Fact] public void DistrictWithoutSafeSpotThrows() { var p = T.Small(); p.SafeSpots.RemoveAll(s => s.NodeId == 3); Throws(p, "beta: a district needs at least one safe spot"); }

        // The other rules of the constructor.
        [Fact] public void OverlappingAreasThrow() => Throws(T.Small().District("beta", d => d with { Areas = new[] { TownRect.Metres(-10, 100, -100, 100) } }), "areas overlap");
        [Fact] public void AreaOutsidePlayAreaThrows() => Throws(T.Small().District("beta", d => d with { Areas = new[] { TownRect.Metres(0, 120, -100, 100) } }), "outside the play area");
        [Fact] public void EmptyAreaThrows() => Throws(T.Small().District("beta", d => d with { Areas = new[] { TownRect.Metres(10, 10, -100, 100) } }), "needs X0 < X1");
        [Fact] public void NodeFarFromAnchorThrows() => Throws(T.Small().Place("p_one", x => x with { NodeId = 2 } with { Anchor = TownPoint.Metres(-90, 0) }), "more than 30 m");
        [Fact] public void BorderOffTheEdgeThrows() => Throws(T.Small().Place("p_two", x => x with { Kind = PlaceKind.Border }), "play-area edge");
        [Fact] public void PermilleOutOfRangeThrows() => Throws(T.Small().Place("p_two", x => x with { RentPermille = 5001 }), "permille must be 0..5000");
        [Fact] public void FromStageOutOfRangeThrows() => Throws(T.Small().Place("p_two", x => x with { FromStage = 6 }), "FromStage must be 1..5");
        [Fact] public void WidthOutOfRangeThrows() { var p = T.Small(); p.Edges[0] = p.Edges[0] with { WidthMm = 1499 }; Throws(p, "width must be 1500..20000"); }
        [Fact] public void SelfEdgeThrows() { var p = T.Small(); p.Edges.Add(new PathEdge(2, 2, PathEdgeKind.Path, 5000, 0)); Throws(p, "two different nodes"); }
        [Fact] public void LiftWithoutSecondsThrows() { var p = T.Small(); p.Edges[5] = p.Edges[5] with { FixedSeconds = 0 }; Throws(p, "FixedSeconds > 0"); }
        [Fact] public void UnknownReferenceThrows() { var p = T.Small(); p.Reference = "nope"; Throws(p, "Unknown reference place"); }
        [Fact] public void FallHeightAboveANodeThrows() { var p = T.Small(); p.FallHeight = 0; Throws(p, "fall height"); }
        [Fact] public void ClockwisePlayAreaThrows() { var p = T.Small(); p.PlayArea.Reverse(); Throws(p, "counter-clockwise"); }
        [Fact] public void UnknownCurveThrows() => Throws(T.Small().District("beta", d => d with { CurveId = "none" }), "unknown curve 'none'");

        [Fact]
        public void CatalogCopiesItsInputs()
        {
            var parts = T.Small();
            var areas = new List<TownRect> { TownRect.Metres(0, 100, -100, 100) };
            parts.District("beta", d => d with { Areas = areas });
            TownCatalog c = parts.Build();
            areas.Add(TownRect.Metres(-100, 0, -100, 100));
            Assert.Single(c.District("beta").Areas);
        }

        [Fact]
        public void LookupsFindAndThrow()
        {
            TownCatalog c = T.Catalog();
            Assert.Equal("Alpha", c.District("alpha").Name);
            Assert.Equal(24, c.Curve("flat").HourPermille.Count);
            Assert.Equal(PlaceKind.Pitch, c.Place("p_two").Kind);
            Assert.Equal(2000, c.Node(5).HeightMm);
            Assert.True(c.HasPlace("p_one"));
            Assert.False(c.HasPlace("p_nine"));
            Assert.Throws<ArgumentException>(() => c.District("x"));
            Assert.Throws<ArgumentException>(() => c.Curve("x"));
            Assert.Throws<ArgumentException>(() => c.Place("x"));
            Assert.Throws<ArgumentException>(() => c.Node(77));
        }

        [Fact]
        public void PlayAreaContainsAndEdge()
        {
            TownCatalog c = T.Catalog();
            Assert.True(c.InPlayArea(TownPoint.Metres(0, 0)));
            Assert.True(c.InPlayArea(TownPoint.Metres(100, 50))); // on the edge counts
            Assert.False(c.InPlayArea(TownPoint.Metres(101, 0)));
            Assert.True(c.OnPlayAreaEdge(TownPoint.Metres(100, 50)));
            Assert.True(c.OnPlayAreaEdge(TownPoint.Metres(-100, -100)));
            Assert.False(c.OnPlayAreaEdge(TownPoint.Metres(0, 0)));

            // A concave (L-shaped) play area: the notch is outside.
            var parts = T.Small();
            parts.PlayArea.Clear();
            parts.PlayArea.AddRange(new[]
            {
                TownPoint.Metres(-100, -100), TownPoint.Metres(100, -100), TownPoint.Metres(100, 100), TownPoint.Metres(0, 100),
                TownPoint.Metres(0, 70), TownPoint.Metres(-100, 70),
            });
            parts.District("alpha", d => d with { Areas = new[] { TownRect.Metres(-100, 0, -100, 70) } });
            TownCatalog l = parts.Build();
            Assert.False(l.InPlayArea(TownPoint.Metres(-50, 90)));
            Assert.True(l.InPlayArea(TownPoint.Metres(50, 90)));
            Assert.True(l.OnPlayAreaEdge(TownPoint.Metres(-50, 70)));
        }

        [Fact]
        public void PointAndRectValues()
        {
            Assert.Equal(new TownPoint(-80000, 2800), new TownPoint(-80000, 2800));
            Assert.True(TownPoint.Metres(1, 2) == new TownPoint(1000, 2000));
            Assert.True(TownPoint.Metres(1, 2) != TownPoint.Metres(2, 1));
            Assert.Equal(TownPoint.Metres(3, 4).GetHashCode(), new TownPoint(3000, 4000).GetHashCode());
            Assert.Equal("(1000, -2000)", TownPoint.Metres(1, -2).ToString());
            Assert.False(TownPoint.Metres(1, 2).Equals((object)"x"));

            TownRect a = TownRect.Metres(0, 10, 0, 10);
            Assert.Equal(new TownRect(0, 0, 10000, 10000), a);
            Assert.True(a == TownRect.Metres(0, 10, 0, 10));
            Assert.True(a != TownRect.Metres(0, 11, 0, 10));
            Assert.Equal(a.GetHashCode(), new TownRect(0, 0, 10000, 10000).GetHashCode());
            Assert.Equal("[0..10000, 0..10000]", a.ToString());
            Assert.True(a.Contains(TownPoint.Metres(10, 5)));
            Assert.False(a.Contains(TownPoint.Metres(11, 5)));
            Assert.True(a.Overlaps(TownRect.Metres(5, 15, 5, 15)));
            Assert.False(a.Overlaps(TownRect.Metres(10, 20, 0, 10)));
            Assert.True(a.Touches(TownRect.Metres(10, 20, 0, 10)));  // a shared border
            Assert.True(a.Touches(TownRect.Metres(10, 20, 10, 20))); // a shared corner
            Assert.False(a.Touches(TownRect.Metres(11, 20, 0, 10)));
            Assert.False(a.Touches(TownRect.Metres(5, 15, 5, 15)));  // overlapping is not touching
            Assert.Equal(new[] { TownPoint.Metres(0, 0), TownPoint.Metres(10, 0), TownPoint.Metres(10, 10), TownPoint.Metres(0, 10) }, a.Corners());
        }

        [Fact]
        public void DefaultChannelsExistInTheClimbCatalog() // AC7
        {
            var channels = new HashSet<string>(ClimbCatalog.CreateDefault().Channels.Select(ch => ch.Id));
            TownCatalog c = TownCatalog.CreateDefault();
            Assert.All(c.Places.SelectMany(p => p.Channels), id => Assert.Contains(id, channels));
            Assert.All(c.Places.Where(p => p.Kind == PlaceKind.Restaurant), p => Assert.Contains("tables", p.Channels));
            Assert.All(c.Places.Where(p => p.Kind != PlaceKind.Pitch && p.Kind != PlaceKind.Small && p.Kind != PlaceKind.Restaurant),
                p => Assert.Empty(p.Channels));
        }

        [Fact]
        public void DefaultDistrictsAreTheTable() // AC7
        {
            string[] expected =
            {
                "station_square", "main_street", "fountain_cross", "market_hall", "garden_promenade", "lakefront", "homes",
                "workshop_lane", "greenhouse_hill", "festival_meadow", "tower_rise", "low_ring",
            };
            TownCatalog c = TownCatalog.CreateDefault();
            Assert.Equal(expected, c.Districts.Select(d => d.Id).ToArray());
            Assert.Equal(new[] { "station_square", "main_street", "fountain_cross", "market_hall" }, c.Districts.Where(d => d.InCore).Select(d => d.Id).ToArray());
            Assert.Equal(1600, c.District("fountain_cross").DayFootfall);
            Assert.Equal("night", c.District("low_ring").CurveId);
        }

        [Fact]
        public void S1IsTheStartAndReferencePlace() // AC7, question 2 and 3
        {
            TownCatalog c = TownCatalog.CreateDefault();
            Assert.Equal("s1", c.StartPlaceId);
            Assert.Equal("s1", c.ReferencePlaceId);
            PlaceDef s1 = c.Place("s1");
            Assert.Equal(new TownPoint(-80000, 2800), s1.Anchor);
            Assert.Equal(75, s1.AreaM2);
            Assert.Equal("main_street", s1.DistrictId);
            Assert.Equal(new TownPoint(0, 0), c.Node(c.FountainNodeId).At);
        }

        [Fact]
        public void MainStreetNodesNearTheShopShareItsHeight() // 067 keeps the shop on a flat pad
        {
            TownCatalog c = TownCatalog.CreateDefault();
            PlaceDef s1 = c.Place("s1");
            int h = c.Node(s1.NodeId).HeightMm;
            var near = c.Nodes.Where(n => n.DistrictId == "main_street" && TownPaths.Distance(n.At, s1.Anchor) <= 40000).ToList();
            Assert.True(near.Count >= 3);
            Assert.All(near, n => Assert.Equal(h, n.HeightMm));
        }

        [Fact]
        public void AxisNodesAreAtMost40MetresApart() // Main Street (z = 0) and the Promenade (x = 0)
        {
            TownCatalog c = TownCatalog.CreateDefault();
            var street = c.Nodes.Where(n => n.At.ZMm == 0 && (n.DistrictId == "main_street" || n.Id == c.FountainNodeId)).Select(n => n.At.XMm).OrderBy(x => x).ToList();
            var promenade = c.Nodes.Where(n => n.At.XMm == 0 && (n.DistrictId == "garden_promenade" || n.Id == c.FountainNodeId)).Select(n => n.At.ZMm).OrderBy(z => z).ToList();
            Assert.Equal(-150000, street.First());
            Assert.Equal(148000, street.Last());
            Assert.Equal(-138000, promenade.First());
            Assert.Equal(168000, promenade.Last());
            foreach (var axis in new[] { street, promenade })
                for (int i = 1; i < axis.Count; i++) Assert.InRange(axis[i] - axis[i - 1], 1, 40000);
        }

        [Fact]
        public void NeighboursAreTouchingOrEdgeJoinedDistricts() // "the builder lists them; symmetric"
        {
            TownCatalog c = TownCatalog.CreateDefault();
            foreach (DistrictDef d in c.Districts)
            {
                var derived = new SortedSet<string>(StringComparer.Ordinal);
                foreach (DistrictDef o in c.Districts.Where(o => o.Id != d.Id))
                    if (d.Areas.Any(a => o.Areas.Any(b => a.Touches(b)))) derived.Add(o.Id);
                foreach (PathEdge e in c.Edges)
                {
                    string da = c.Node(e.A).DistrictId, db = c.Node(e.B).DistrictId;
                    if (da == d.Id && db != d.Id) derived.Add(db);
                    if (db == d.Id && da != d.Id) derived.Add(da);
                }
                Assert.Equal(derived.ToArray(), d.Neighbours.OrderBy(n => n, StringComparer.Ordinal).ToArray());
            }
        }

        [Fact]
        public void DefaultPlacesFollowTheSpecLists()
        {
            TownCatalog c = TownCatalog.CreateDefault();
            int Count(PlaceKind k) => c.Places.Count(p => p.Kind == k);
            Assert.Equal(8, Count(PlaceKind.Pitch));
            Assert.Equal(6, Count(PlaceKind.Small));
            Assert.Equal(4, Count(PlaceKind.Restaurant));
            Assert.Equal(5, Count(PlaceKind.StageFive));
            Assert.Equal(10, Count(PlaceKind.Civic));
            Assert.Equal(4, Count(PlaceKind.Stop));
            Assert.Equal(10, Count(PlaceKind.Address));
            Assert.Equal(7, Count(PlaceKind.Border));
            Assert.True(c.Place("c8").EventDaysOnly);
            Assert.Single(c.Places, p => p.EventDaysOnly);
            Assert.All(c.Places.Where(p => p.Kind == PlaceKind.Pitch), p => Assert.True(p.OutdoorSeats));
            Assert.All(c.Places.Where(p => p.Kind == PlaceKind.Small), p => Assert.InRange(p.AreaM2, 60, 90));
            Assert.All(c.Places.Where(p => p.Kind == PlaceKind.Restaurant), p => Assert.InRange(p.AreaM2, 150, 300));
            Assert.Equal(1, c.Edges.Count(e => e.Kind == PathEdgeKind.Lift && e.FixedSeconds == 20));
            Assert.Equal(1, c.Edges.Count(e => e.Kind == PathEdgeKind.Funicular && e.FixedSeconds == 60));
            Assert.Equal(c.Nodes.Min(n => n.HeightMm) - 10000, c.FallHeightMm);
        }
    }
}
