using System;
using System.Collections.Generic;

namespace Worktown.Sim.Town
{
    /// <summary>
    /// Spec 064: walking times on the town's graph, built once from a catalog. Edge length is the horizontal distance (mm);
    /// walked edges (Path, Ramp, Stairs) take <c>lengthMm * 2 / 5</c> ms (2.5 m/s, D31); a Lift or Funicular takes
    /// <c>FixedSeconds * 1000</c> ms and is used only when lifts are allowed. Dijkstra; ties: fewer nodes, then the smaller
    /// node id sequence. Costs are kept exactly in fifths of a millisecond, so a walked route's ms is its length x 2 / 5.
    /// </summary>
    public sealed class TownPaths
    {
        private readonly TownCatalog _catalog;
        private readonly Dictionary<int, int> _index = new Dictionary<int, int>();
        private readonly int[] _ids;
        private readonly List<Link>[] _links;

        public TownPaths(TownCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentException("A catalog is required.", nameof(catalog));
            int n = catalog.Nodes.Count;
            _ids = new int[n];
            _links = new List<Link>[n];
            for (int i = 0; i < n; i++)
            {
                _ids[i] = catalog.Nodes[i].Id;
                _index[_ids[i]] = i;
                _links[i] = new List<Link>();
            }
            foreach (PathEdge e in catalog.Edges)
            {
                int a = _index[e.A], b = _index[e.B];
                long length = Distance(catalog.Nodes[a].At, catalog.Nodes[b].At);
                bool ride = e.Kind == PathEdgeKind.Lift || e.Kind == PathEdgeKind.Funicular;
                long cost = ride ? e.FixedSeconds * 5000L : length * 2;
                _links[a].Add(new Link(b, e.Kind, length, cost));
                _links[b].Add(new Link(a, e.Kind, length, cost));
            }
        }

        /// <summary>The integer square root (floor) of a non-negative number.</summary>
        public static long ISqrt(long value)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "ISqrt needs a value >= 0.");
            if (value < 2) return value;
            int bits = 0;
            for (long v = value; v > 0; v >>= 1) bits++;
            long x = 1L << ((bits + 1) / 2); // >= the root; Newton steps go down to the floor
            while (true)
            {
                long y = (x + value / x) / 2;
                if (y >= x) return x;
                x = y;
            }
        }

        /// <summary>The horizontal distance between two plan points (mm, floor).</summary>
        public static long Distance(TownPoint a, TownPoint b)
        {
            long dx = (long)a.XMm - b.XMm, dz = (long)a.ZMm - b.ZMm;
            return ISqrt(dx * dx + dz * dz);
        }

        /// <summary>The fastest route between two nodes, or null when unreachable. Throws for an unknown node.</summary>
        public WalkRoute? Route(int fromNode, int toNode, bool allowLifts = false) =>
            RouteWhere(fromNode, toNode, k => allowLifts || (k != PathEdgeKind.Lift && k != PathEdgeKind.Funicular));

        /// <summary>The walk time between two nodes in ms, or -1 when unreachable.</summary>
        public long WalkMs(int fromNode, int toNode, bool allowLifts = false) => Route(fromNode, toNode, allowLifts)?.Ms ?? -1;

        /// <summary>The walk time between the nodes of two places in ms, or -1 when unreachable.</summary>
        public long PlaceWalkMs(string fromPlace, string toPlace, bool allowLifts = false) =>
            WalkMs(_catalog.Place(fromPlace).NodeId, _catalog.Place(toPlace).NodeId, allowLifts);

        /// <summary>The fastest route using only edges whose kind passes <paramref name="allowed"/> (checks C2 and C3).</summary>
        internal WalkRoute? RouteWhere(int fromNode, int toNode, Func<PathEdgeKind, bool> allowed)
        {
            if (!_index.TryGetValue(fromNode, out int src)) throw new ArgumentException("Unknown node " + fromNode + ".");
            if (!_index.TryGetValue(toNode, out int dst)) throw new ArgumentException("Unknown node " + toNode + ".");
            int n = _ids.Length;
            var cost = new long[n];
            var length = new long[n];
            var path = new int[n][];
            var done = new bool[n];
            for (int i = 0; i < n; i++) cost[i] = -1;
            cost[src] = 0;
            path[src] = new[] { _ids[src] };
            while (true)
            {
                int best = -1;
                for (int i = 0; i < n; i++)
                    if (!done[i] && cost[i] >= 0 && (best < 0 || Better(cost[i], path[i], cost[best], path[best]))) best = i;
                if (best < 0) return null;
                if (best == dst) return new WalkRoute(Array.AsReadOnly(path[best]), length[best], cost[best] / 5);
                done[best] = true;
                foreach (Link l in _links[best])
                {
                    if (done[l.To] || !allowed(l.Kind)) continue;
                    long c = cost[best] + l.Cost;
                    int[] p = new int[path[best].Length + 1];
                    Array.Copy(path[best], p, path[best].Length);
                    p[p.Length - 1] = _ids[l.To];
                    if (cost[l.To] < 0 || Better(c, p, cost[l.To], path[l.To]))
                    {
                        cost[l.To] = c;
                        path[l.To] = p;
                        length[l.To] = length[best] + l.LengthMm;
                    }
                }
            }
        }

        /// <summary>The horizontal length of an edge of the catalog (mm).</summary>
        internal long LengthOf(PathEdge e) => Distance(_catalog.Node(e.A).At, _catalog.Node(e.B).At);

        private static bool Better(long costA, int[] pathA, long costB, int[] pathB)
        {
            if (costA != costB) return costA < costB;
            if (pathA.Length != pathB.Length) return pathA.Length < pathB.Length;
            for (int i = 0; i < pathA.Length; i++)
                if (pathA[i] != pathB[i]) return pathA[i] < pathB[i];
            return false;
        }

        private readonly struct Link
        {
            public Link(int to, PathEdgeKind kind, long lengthMm, long cost)
            {
                To = to;
                Kind = kind;
                LengthMm = lengthMm;
                Cost = cost;
            }

            public int To { get; }
            public PathEdgeKind Kind { get; }
            public long LengthMm { get; }
            public long Cost { get; }
        }
    }
}
