using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Worktown.Sim.Town
{
    /// <summary>
    /// Spec 064: the town in numbers: districts (footfall per day, peak hour, neighbours), places (kind, district, Daily,
    /// LocationPermille, rent, walk seconds from the fountain), one line per check (PASS, or FAIL per problem) and the last
    /// line <c>RESULT: PASS (12/12)</c> or <c>RESULT: FAIL (k/12)</c> (k = checks that pass). The same inputs give the same string.
    /// </summary>
    public static class TownReport
    {
        public static string Format(TownCatalog c, IReadOnlyList<TownProblem> problems)
        {
            if (c == null) throw new ArgumentException("A catalog is required.", nameof(c));
            if (problems == null) throw new ArgumentException("Problems are required.", nameof(problems));
            var paths = new TownPaths(c);
            var sb = new StringBuilder();
            void Line(string s) => sb.Append(s).Append('\n');

            Line("TOWN REPORT (spec 064): " + c.Districts.Count + " districts, " + c.Places.Count + " places, " + c.Nodes.Count + " nodes, "
                + c.Edges.Count + " edges; reference " + c.ReferencePlaceId + ", start " + c.StartPlaceId);
            Line("");
            Line("DISTRICTS");
            Line(Row("id", 18, "core", 5, "per day", 8, "peak", 5) + "neighbours");
            foreach (DistrictDef d in c.Districts)
            {
                int perDay = Footfall.DistrictSum(c, d.Id, 0, 23);
                int peak = 0;
                for (int h = 1; h < TownCatalog.Hours; h++)
                    if (Footfall.DistrictHourly(c, d.Id, h) > Footfall.DistrictHourly(c, d.Id, peak)) peak = h;
                Line(Row(d.Id, 18, d.InCore ? "yes" : "no", 5, N(perDay), 8, peak.ToString("00", CultureInfo.InvariantCulture), 5)
                    + string.Join(", ", d.Neighbours));
            }

            Line("");
            Line("PLACES");
            Line(Row("id", 18, "kind", 11, "district", 18, "daily", 7, "location", 9, "rent", 6) + "walk_s");
            foreach (PlaceDef p in c.Places)
            {
                long ms = paths.WalkMs(c.FountainNodeId, p.NodeId);
                string walk = ms < 0 ? "-" : (ms / 1000).ToString(CultureInfo.InvariantCulture) + "." + ((ms % 1000) / 100).ToString(CultureInfo.InvariantCulture);
                Line(Row(p.Id, 18, p.Kind.ToString(), 11, p.DistrictId, 18, N(Footfall.Daily(c, p.Id)), 7,
                    N(Footfall.LocationPermille(c, p.Id)), 9, N(p.RentPermille), 6) + walk);
            }

            Line("");
            Line("CHECKS");
            int passed = 0;
            foreach (string id in TownCheck.CheckIds)
            {
                var mine = problems.Where(p => p.Check == id).ToList();
                if (mine.Count == 0)
                {
                    passed++;
                    Line("PASS " + id + " " + TownCheck.Describe(id));
                }
                else
                    foreach (TownProblem p in mine) Line("FAIL " + id + " " + TownCheck.Describe(id) + ": " + p.Detail);
            }
            int total = TownCheck.CheckIds.Count;
            Line("RESULT: " + (passed == total ? "PASS" : "FAIL") + " (" + passed + "/" + total + ")");
            return sb.ToString();
        }

        private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Row(params object[] cells)
        {
            var sb = new StringBuilder();
            for (int i = 0; i + 1 < cells.Length; i += 2) sb.Append(((string)cells[i]).PadRight((int)cells[i + 1]));
            return sb.ToString();
        }
    }
}
