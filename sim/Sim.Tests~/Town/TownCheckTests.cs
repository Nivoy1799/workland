using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Worktown.Sim.Town;
using Xunit;
using P = Worktown.Sim.Tests.Town.TestTown.Parts;

namespace Worktown.Sim.Tests.Town
{
    /// <summary>AC4: the default town passes; each check has a literal catalog (the default town, one thing broken) that fails it.</summary>
    public class TownCheckTests
    {
        private static IReadOnlyList<TownProblem> Run(P parts) => TownCheck.Run(parts.Build());

        private static void Fails(P parts, string check)
        {
            var problems = Run(parts);
            Assert.Contains(problems, p => p.Check == check);
        }

        [Fact]
        public void DefaultTownPassesEveryCheck() // AC4
        {
            Assert.Empty(TownCheck.Run(TownCatalog.CreateDefault()));
        }

        [Fact]
        public void DetailsIgnoreTheCurrentCulture() // 064 review: invariant numbers in every detail
        {
            P parts = TestTown.Small();
            parts.DomeRadiusX = 110000; // the play-area corners have less than 20 m to spare
            TownCatalog c = parts.Build();
            List<string> invariant = TownCheck.Run(c).Select(p => p.Detail).ToList();
            CultureInfo before = CultureInfo.CurrentCulture;
            var odd = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            odd.NumberFormat.NegativeSign = "~";
            try
            {
                CultureInfo.CurrentCulture = odd;
                Assert.Equal(invariant, TownCheck.Run(c).Select(p => p.Detail).ToList());
            }
            finally { CultureInfo.CurrentCulture = before; }
            Assert.Contains("play-area corner (-100000, -100000) has less than 20 m to spare inside the Dome", invariant);
        }

        [Fact]
        public void C1SteepPathFails()
        {
            var problems = Run(P.Default().Node(205, n => n with { HeightMm = 9600 })); // 22 m to the fountain, 3.2 m down
            Assert.Contains(problems, p => p.Check == "C1" && p.Detail.Contains("205-301"));
        }

        [Fact]
        public void C2StairsWithoutANearbyRampFail()
        {
            var parts = P.Default();
            parts.Edges.Add(new PathEdge(105, 1103, PathEdgeKind.Stairs, 4000, 0)); // beside the lift; the ramp is far longer
            var problems = Run(parts);
            Assert.Contains(problems, p => p.Check == "C2" && p.Detail.Contains("105-1103"));
            Assert.DoesNotContain(problems, p => p.Check == "C2" && p.Detail.Contains("204-401")); // the market steps pass
        }

        [Fact]
        public void C2StairsWithNoOtherRouteFail()
        {
            var parts = P.Default().RemoveEdge(103, 1101);
            parts.Edges.Add(new PathEdge(103, 1101, PathEdgeKind.Stairs, 4000, 0));
            Fails(parts, "C2");
        }

        [Fact]
        public void C3NodesOnlyReachableByLiftFail()
        {
            var problems = Run(P.Default().RemoveEdge(103, 1101).RemoveEdge(1101, 1102));
            Assert.Contains(problems, p => p.Check == "C3" && p.Detail.Contains("node 1104"));
        }

        [Fact]
        public void C4CoreFarFromTheFountainFails()
        {
            // Without Main Street's last stretch, Station Square is reached only round the Low Ring... which is cut off too.
            var problems = Run(P.Default().RemoveEdge(201, 101));
            Assert.Contains(problems, p => p.Check == "C4" && p.Detail.StartsWith("c1 "));
        }

        [Fact]
        public void C4SlowCoreWalkFails()
        {
            // Station Square moved 30 m further west by pushing its far nodes out (still inside the square's area).
            var parts = P.Default();
            parts.District("station_square", d => d with { Areas = new[] { TownRect.Metres(-218, -40, -150, 40) } });
            parts.Node(102, n => n with { At = TownPoint.Metres(-216, 0) });
            parts.Node(105, n => n with { At = TownPoint.Metres(-216, 20) });
            parts.Place("station", x => x with { Anchor = TownPoint.Metres(-217, 0) });
            parts.Place("stop_station", x => x with { Anchor = TownPoint.Metres(-217, 10) });
            parts.Edges.RemoveAll(e => e.A == 101 && e.B == 102);
            parts.Edges.Add(new PathEdge(103, 102, PathEdgeKind.Path, 5000, 0)); // the long way round
            var problems = Run(parts);
            Assert.Contains(problems, p => p.Check == "C4" && p.Detail.StartsWith("station "));
        }

        [Fact]
        public void C5WrongPlayAreaFails()
        {
            // 10 m wider in the east (650 m); the east border points move onto the new edge.
            var parts = P.Default();
            parts.PlayArea[1] = TownPoint.Metres(270, -200);
            parts.PlayArea[2] = TownPoint.Metres(270, 280);
            parts.Place("water_gate_3", x => x with { Anchor = TownPoint.Metres(270, -60) });
            parts.Place("airlock_2", x => x with { Anchor = TownPoint.Metres(270, 130) });
            var problems = Run(parts);
            Assert.Contains(problems, p => p.Check == "C5" && p.Detail.StartsWith("play area"));
        }

        [Fact]
        public void C5SpreadCoreFails()
        {
            var problems = Run(P.Default().Place("s3", x => x with { Anchor = TownPoint.Metres(40, 10), NodeId = 206 }));
            Assert.Contains(problems, p => p.Check == "C5" && p.Detail.StartsWith("core places"));
        }

        [Fact]
        public void C6LongBorderWalkFails()
        {
            var problems = Run(P.Default().RemoveEdge(601, 607)); // the jetty to Water Gate 3 is cut off
            Assert.Contains(problems, p => p.Check == "C6" && p.Detail.StartsWith("wreck_bulkhead"));
        }

        [Fact]
        public void C6MissingBorderPlaceFails()
        {
            var parts = P.Default();
            parts.Places.RemoveAll(x => x.Id == "tunnel_door");
            Assert.Contains(Run(parts), p => p.Check == "C6" && p.Detail.Contains("tunnel_door"));
        }

        [Fact]
        public void C7TheTownPlanDomeIsTooSmallForTheCorners()
        {
            // The town plan's 770 x 640 m: an ellipse that size cannot hold a 640 x 480 m rectangle's corners.
            var parts = P.Default();
            parts.DomeRadiusX = 385000;
            parts.DomeRadiusZ = 320000;
            var problems = Run(parts);
            Assert.Equal(4, problems.Count(p => p.Check == "C7"));
        }

        [Fact]
        public void C7LowCrownFails()
        {
            var parts = P.Default();
            parts.DomeCrown = 40000; // below Greenhouse Hill's top
            Assert.Contains(Run(parts), p => p.Check == "C7" && p.Detail.Contains("node 907"));
        }

        [Fact]
        public void C8PlaceNodeInAnotherDistrictFails()
        {
            var problems = Run(P.Default().Place("c4", x => x with { NodeId = 302 })); // a Fountain Cross node, 16.6 m away
            Assert.Contains(problems, p => p.Check == "C8" && p.Detail.StartsWith("c4"));
        }

        [Fact]
        public void C8DistrictWithoutAreaFails()
        {
            var parts = P.Default();
            parts.District("homes", d => d with { Areas = new TownRect[0] });
            parts.Places.RemoveAll(x => x.DistrictId == "homes");
            Assert.Contains(Run(parts), p => p.Check == "C8" && p.Detail == "homes has no area");
        }

        [Fact]
        public void C9RentAgainstFootfallFails()
        {
            var problems = Run(P.Default().Place("c1", x => x with { RentPermille = 900 })); // busier than c2, cheaper
            Assert.Contains(problems, p => p.Check == "C9" && p.Detail.StartsWith("c1 ") && p.Detail.Contains("than c2"));
        }

        [Fact]
        public void C10BusierPitchThanC3Fails()
        {
            var problems = Run(P.Default().Place("c1", x => x with { FrontagePermille = 1600 }));
            Assert.Contains(problems, p => p.Check == "C10" && p.Detail.StartsWith("c1"));
        }

        [Fact]
        public void C10ReferenceWithoutFootfallFails()
        {
            var parts = P.Default();
            parts.Reference = "c8"; // event days only: no footfall on a normal day
            Assert.Contains(Run(parts), p => p.Check == "C10" && p.Detail.StartsWith("c8 location is 0"));
        }

        [Fact]
        public void C11LowRingByDayFails()
        {
            var problems = Run(P.Default().District("low_ring", d => d with { CurveId = "shops" }));
            Assert.Contains(problems, p => p.Check == "C11" && p.Detail.Contains("21-03"));
        }

        [Fact]
        public void C11BrightLowRingByDayFails()
        {
            var problems = Run(P.Default().District("low_ring", d => d with { DayFootfall = 4000 }));
            Assert.Contains(problems, p => p.Check == "C11" && p.Detail.Contains("10-16"));
        }

        [Fact]
        public void C12FlatCurvesHaveNoShape()
        {
            var parts = P.Default();
            foreach (string id in new[] { "commute", "shops", "market", "evening", "night" })
                parts.Curve(id, x => x with { HourPermille = TestTown.Flat() });
            var problems = Run(parts);
            Assert.Equal(5, problems.Count(p => p.Check == "C12"));
        }

        [Fact]
        public void C12UnnamedCurveHasNoShapeToCheck()
        {
            Assert.DoesNotContain(TownCheck.Run(TestTown.Catalog()), p => p.Check == "C12");
        }

        [Fact]
        public void CheckIdsAndDescriptions()
        {
            Assert.Equal(Enumerable.Range(1, 12).Select(i => "C" + i).ToArray(), TownCheck.CheckIds.ToArray());
            Assert.All(TownCheck.CheckIds, id => Assert.False(string.IsNullOrWhiteSpace(TownCheck.Describe(id))));
            Assert.Throws<ArgumentException>(() => TownCheck.Describe("C13"));
            Assert.Throws<ArgumentException>(() => TownCheck.Run(null!));
        }

        [Fact]
        public void ProblemsComeInCheckOrder()
        {
            var parts = P.Default().Node(205, n => n with { HeightMm = 9600 }).Place("c1", x => x with { RentPermille = 900 });
            var checks = Run(parts).Select(p => p.Check).Distinct().ToList();
            Assert.Equal(new[] { "C1", "C9" }, checks);
        }
    }
}
