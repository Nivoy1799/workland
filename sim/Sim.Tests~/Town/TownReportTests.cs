using System;
using System.Diagnostics;
using System.Linq;
using Worktown.Sim.Town;
using Xunit;

namespace Worktown.Sim.Tests.Town
{
    public class TownReportTests
    {
        [Fact]
        public void DefaultReportHasEverySectionAndPasses() // AC8
        {
            TownCatalog c = TownCatalog.CreateDefault();
            string report = TownReport.Format(c, TownCheck.Run(c));
            string[] lines = report.TrimEnd('\n').Split('\n');
            Assert.Contains("DISTRICTS", lines);
            Assert.Contains("PLACES", lines);
            Assert.Contains("CHECKS", lines);
            Assert.Equal("RESULT: PASS (12/12)", lines.Last());
            Assert.Equal(12, lines.Count(l => l.StartsWith("PASS C")));
            Assert.Contains(lines, l => l.StartsWith("low_ring ") && l.Contains("22") && l.Contains("station_square, tower_rise"));
            Assert.Contains(lines, l => l.StartsWith("s1 ") && l.Contains("Small") && l.Contains(" 1000 ") && l.EndsWith("32.0"));
            Assert.Equal(c.Districts.Count + c.Places.Count + 2, lines.Count(l => c.Districts.Any(d => l.StartsWith(d.Id + " ")) || c.Places.Any(p => l.StartsWith(p.Id + " ")) || l.StartsWith("id ")));
        }

        [Fact]
        public void FailingChecksPrintFailLinesAndTheCount() // AC8
        {
            TownCatalog c = TownCatalog.CreateDefault();
            var problems = new[] { new TownProblem("C3", "node 9 is lost"), new TownProblem("C3", "node 10 is lost"), new TownProblem("C9", "rent") };
            string report = TownReport.Format(c, problems);
            Assert.Contains("FAIL C3 " + TownCheck.Describe("C3") + ": node 9 is lost\n", report);
            Assert.Contains("FAIL C3 " + TownCheck.Describe("C3") + ": node 10 is lost\n", report);
            Assert.Contains("FAIL C9 ", report);
            Assert.Contains("PASS C1 ", report);
            Assert.EndsWith("RESULT: FAIL (10/12)\n", report);
        }

        [Fact]
        public void SameInputsGiveTheSameReport() // AC2
        {
            string a = TownReport.Format(TownCatalog.CreateDefault(), TownCheck.Run(TownCatalog.CreateDefault()));
            string b = TownReport.Format(TownCatalog.CreateDefault(), TownCheck.Run(TownCatalog.CreateDefault()));
            Assert.Equal(a, b);
        }

        [Fact]
        public void UnreachablePlaceShowsADash()
        {
            var parts = TestTown.Small().RemoveEdge(2, 3).RemoveEdge(4, 3);
            TownCatalog c = parts.Build();
            string report = TownReport.Format(c, TownCheck.Run(c));
            Assert.Contains(report.Split('\n'), l => l.StartsWith("p_two ") && l.EndsWith(" -"));
        }

        [Fact]
        public void BadArgumentsThrow()
        {
            Assert.Throws<ArgumentException>(() => TownReport.Format(null!, new TownProblem[0]));
            Assert.Throws<ArgumentException>(() => TownReport.Format(TestTown.Catalog(), null!));
        }

        [Fact]
        public void ChecksAndReportAreFast() // AC9: the Town tests stay well under 5 s
        {
            var watch = Stopwatch.StartNew();
            TownCatalog c = TownCatalog.CreateDefault();
            TownReport.Format(c, TownCheck.Run(c));
            Assert.True(watch.ElapsedMilliseconds < 2000, "report took " + watch.ElapsedMilliseconds + " ms");
        }
    }
}
