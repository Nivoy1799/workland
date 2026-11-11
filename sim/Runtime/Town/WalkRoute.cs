using System.Collections.Generic;

namespace Worktown.Sim.Town
{
    /// <summary>
    /// Spec 064: a route of <see cref="TownPaths"/>: the node ids from start to end, the horizontal length of every edge
    /// (mm) and the time (ms: walked length x 2 / 5 at 2.5 m/s, plus the fixed seconds of lifts and funiculars).
    /// </summary>
    public sealed record WalkRoute(IReadOnlyList<int> Nodes, long LengthMm, long Ms);
}
