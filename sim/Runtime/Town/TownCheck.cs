using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Worktown.Sim.Town
{
    /// <summary>
    /// Spec 064: the twelve checks of the town (C1-C12). <see cref="Run"/> returns every problem in check order; an empty
    /// list means the town passes. Walks for C4 and C6 use no lifts or funicular. Detail strings use the invariant culture.
    /// </summary>
    public static class TownCheck
    {
        public const string ReferenceBusiestPitch = "c3";
        public const string Fountain = "fountain_cross";
        public const string LowRing = "low_ring";
        public const string MainStreet = "main_street";
        public const long CoreWalkMs = 90000;
        public const long BorderWalkMs = 300000;

        private static readonly string[] Ids = { "C1", "C2", "C3", "C4", "C5", "C6", "C7", "C8", "C9", "C10", "C11", "C12" };

        private static readonly string[] Descriptions =
        {
            "path and ramp slopes below 5 %",
            "every stair has a stair-free route within 3x its length",
            "every node reachable from the fountain by path and ramp",
            "every core place within 90 s of the fountain",
            "play area 640 x 480 m (+-5 m), core places within 235 x 165 m",
            "border walks Wreck to Water Gate and Tunnel to Wildbelt within 300 s",
            "the Dome holds the play area with 20 m to spare and its crown is above every node",
            "every district has an area, a safe spot and a node; every place's node is in its district",
            "rent follows footfall within pitches, small places and restaurants",
            "the reference place has location 1000; c3 is the busiest pitch",
            "the Low Ring is busier than Main Street at 21-03 and quieter than every core district at 10-16",
            "every footfall curve has its named shape",
        };

        /// <summary>The check ids in order: C1..C12.</summary>
        public static IReadOnlyList<string> CheckIds { get; } = Array.AsReadOnly(Ids);

        /// <summary>A one-line description of a check id (throws for an unknown id).</summary>
        public static string Describe(string checkId)
        {
            int i = Array.IndexOf(Ids, checkId);
            if (i < 0) throw new ArgumentException("Unknown check '" + checkId + "'.");
            return Descriptions[i];
        }

        /// <summary>Runs C1-C12 on the catalog.</summary>
        public static IReadOnlyList<TownProblem> Run(TownCatalog c)
        {
            if (c == null) throw new ArgumentException("A catalog is required.", nameof(c));
            var paths = new TownPaths(c);
            var problems = new List<TownProblem>();
            void Fail(string check, string detail) => problems.Add(new TownProblem(check, detail));

            // C1 slopes
            foreach (PathEdge e in c.Edges.Where(e => e.Kind == PathEdgeKind.Path || e.Kind == PathEdgeKind.Ramp))
            {
                long length = paths.LengthOf(e);
                long dh = Math.Abs((long)c.Node(e.A).HeightMm - c.Node(e.B).HeightMm);
                long permille = length > 0 ? dh * 1000 / length : (dh == 0 ? 0 : long.MaxValue);
                if (permille >= 50) Fail("C1", e.Kind + " " + I(e.A) + "-" + I(e.B) + " slope " + I(permille) + " permille");
            }

            // C2 stairs
            foreach (PathEdge e in c.Edges.Where(e => e.Kind == PathEdgeKind.Stairs))
            {
                long length = paths.LengthOf(e);
                WalkRoute? r = paths.RouteWhere(e.A, e.B, k => k == PathEdgeKind.Path || k == PathEdgeKind.Ramp);
                if (r == null) Fail("C2", "stairs " + I(e.A) + "-" + I(e.B) + " have no stair-free route");
                else if (r.LengthMm > 3 * length) Fail("C2", "stairs " + I(e.A) + "-" + I(e.B) + ": the stair-free route is " + I(r.LengthMm) + " mm, over 3x " + I(length));
            }

            // C3 reachable by path and ramp
            foreach (PathNode n in c.Nodes)
                if (paths.RouteWhere(c.FountainNodeId, n.Id, k => k == PathEdgeKind.Path || k == PathEdgeKind.Ramp) == null)
                    Fail("C3", "node " + I(n.Id) + " is not reachable from the fountain by path and ramp");

            // C4 core within 90 s
            foreach (PlaceDef p in c.Places.Where(p => c.District(p.DistrictId).InCore))
            {
                long ms = paths.WalkMs(c.FountainNodeId, p.NodeId);
                if (ms < 0 || ms > CoreWalkMs) Fail("C4", p.Id + " is " + (ms < 0 ? "unreachable" : I(ms) + " ms") + " from the fountain");
            }

            // C5 sizes
            long playW = c.PlayArea.Max(p => (long)p.XMm) - c.PlayArea.Min(p => (long)p.XMm);
            long playH = c.PlayArea.Max(p => (long)p.ZMm) - c.PlayArea.Min(p => (long)p.ZMm);
            if (Math.Abs(playW - 640000) > 5000 || Math.Abs(playH - 480000) > 5000)
                Fail("C5", "play area is " + I(playW) + " x " + I(playH) + " mm, not 640 x 480 m (+-5 m)");
            var core = c.Places.Where(p => p.Kind != PlaceKind.Address && c.District(p.DistrictId).InCore).ToList();
            if (core.Count > 0)
            {
                long coreW = core.Max(p => (long)p.Anchor.XMm) - core.Min(p => (long)p.Anchor.XMm);
                long coreH = core.Max(p => (long)p.Anchor.ZMm) - core.Min(p => (long)p.Anchor.ZMm);
                if (coreW > 235000 || coreH > 165000) Fail("C5", "core places span " + I(coreW) + " x " + I(coreH) + " mm, over 235 x 165 m");
            }

            // C6 border walks
            foreach ((string from, string to) in new[] { ("wreck_bulkhead", "water_gate_3"), ("tunnel_door", "wildbelt_lookout") })
            {
                if (!c.HasPlace(from) || !c.HasPlace(to)) { Fail("C6", "missing border place " + (c.HasPlace(from) ? to : from)); continue; }
                long ms = paths.PlaceWalkMs(from, to);
                if (ms < 0 || ms > BorderWalkMs) Fail("C6", from + " to " + to + " is " + (ms < 0 ? "unreachable" : I(ms) + " ms"));
            }

            // C7 the Dome
            decimal a2 = (decimal)c.DomeRadiusXMm * c.DomeRadiusXMm, b2 = (decimal)c.DomeRadiusZMm * c.DomeRadiusZMm;
            foreach (TownPoint p in c.PlayArea)
            {
                long dx = (long)p.XMm - c.DomeCentre.XMm, dz = (long)p.ZMm - c.DomeCentre.ZMm;
                dx += Math.Sign(dx) * 20000L;
                dz += Math.Sign(dz) * 20000L;
                if ((decimal)dx * dx * b2 + (decimal)dz * dz * a2 > a2 * b2)
                    Fail("C7", "play-area corner " + p + " has less than 20 m to spare inside the Dome");
            }
            foreach (PathNode n in c.Nodes.Where(n => n.HeightMm >= c.DomeCrownMm))
                Fail("C7", "node " + I(n.Id) + " is not below the Dome's crown");

            // C8 districts and place nodes
            foreach (DistrictDef d in c.Districts)
            {
                if (d.Areas.Count == 0) Fail("C8", d.Id + " has no area");
                if (!c.Nodes.Any(n => n.DistrictId == d.Id)) Fail("C8", d.Id + " has no node");
                if (!c.SafeSpots.Any(s => c.Node(s.NodeId).DistrictId == d.Id)) Fail("C8", d.Id + " has no safe spot");
            }
            foreach (PlaceDef p in c.Places)
                if (c.Node(p.NodeId).DistrictId != p.DistrictId) Fail("C8", p.Id + ": node " + I(p.NodeId) + " is in " + c.Node(p.NodeId).DistrictId + ", not " + p.DistrictId);

            // C9 rent follows footfall within each rentable kind
            foreach (PlaceKind kind in new[] { PlaceKind.Pitch, PlaceKind.Small, PlaceKind.Restaurant })
            {
                var list = c.Places.Where(p => p.Kind == kind && !p.EventDaysOnly).Select(p => (p, daily: Footfall.Daily(c, p.Id))).ToList();
                foreach (var hi in list)
                    foreach (var lo in list)
                        if (hi.daily > lo.daily && hi.p.RentPermille < lo.p.RentPermille)
                            Fail("C9", hi.p.Id + " (daily " + I(hi.daily) + ", rent " + I(hi.p.RentPermille) + ") rents lower than " + lo.p.Id
                                + " (daily " + I(lo.daily) + ", rent " + I(lo.p.RentPermille) + ")");
            }

            // C10 reference and busiest pitch
            int refLoc = Footfall.LocationPermille(c, c.ReferencePlaceId);
            if (refLoc != 1000) Fail("C10", c.ReferencePlaceId + " location is " + I(refLoc) + ", not 1000");
            if (!c.HasPlace(ReferenceBusiestPitch)) Fail("C10", "missing " + ReferenceBusiestPitch);
            else
            {
                int top = Footfall.Daily(c, ReferenceBusiestPitch);
                foreach (PlaceDef p in c.Places.Where(p => p.Kind == PlaceKind.Pitch && p.Id != ReferenceBusiestPitch))
                    if (Footfall.Daily(c, p.Id) >= top) Fail("C10", p.Id + " is at least as busy as " + ReferenceBusiestPitch);
            }

            // C11 the Low Ring: dim by day, busy at night
            if (!c.Districts.Any(d => d.Id == LowRing) || !c.Districts.Any(d => d.Id == MainStreet))
                Fail("C11", "missing " + LowRing + " or " + MainStreet);
            else
            {
                int lowNight = Footfall.DistrictSum(c, LowRing, 21, 3), mainNight = Footfall.DistrictSum(c, MainStreet, 21, 3);
                if (lowNight <= mainNight) Fail("C11", LowRing + " 21-03 footfall " + I(lowNight) + " is not above " + MainStreet + "'s " + I(mainNight));
                int lowDay = Footfall.DistrictSum(c, LowRing, 10, 16);
                foreach (DistrictDef d in c.Districts.Where(d => d.InCore))
                {
                    int day = Footfall.DistrictSum(c, d.Id, 10, 16);
                    if (lowDay >= day) Fail("C11", LowRing + " 10-16 footfall " + I(lowDay) + " is not below " + d.Id + "'s " + I(day));
                }
            }

            // C12 curve shapes
            foreach (FootfallCurve curve in c.Curves)
            {
                string? why = ShapeProblem(curve);
                if (why != null) Fail("C12", curve.Id + ": " + why);
            }

            return problems.AsReadOnly();
        }

        private static string I(long v) => v.ToString(CultureInfo.InvariantCulture);

        private static string? ShapeProblem(FootfallCurve curve)
        {
            IReadOnlyList<int> h = curve.HourPermille;
            int Sum(params int[] hours) => hours.Sum(x => h[x]);
            switch (curve.Id)
            {
                case "commute":
                    int[] peaks = { 7, 8, 9, 17, 18, 19 };
                    int outside = Enumerable.Range(0, TownCatalog.Hours).Where(x => !peaks.Contains(x)).Max(x => h[x]);
                    int morning = Math.Max(h[7], Math.Max(h[8], h[9])), evening = Math.Max(h[17], Math.Max(h[18], h[19]));
                    return morning > outside && evening > outside ? null : "needs peaks at 07-09 and 17-19 above every other hour";
                case "shops":
                    return Sum(10, 11, 12, 13, 14, 15, 16, 17, 18, 19) > 500 ? null : "needs most weight at 10-19";
                case "market":
                    return Sum(6, 7, 8, 9, 10, 11, 12, 13) > 500 ? null : "needs most weight at 06-13";
                case "evening":
                    return Sum(16, 17, 18, 19, 20, 21, 22, 23) > 500 ? null : "needs most weight at 16-23";
                case "night":
                    return Sum(20, 21, 22, 23, 0, 1, 2, 3) >= 500 ? null : "needs at least half its weight at 20-03";
                default:
                    return null; // no named shape
            }
        }
    }
}
