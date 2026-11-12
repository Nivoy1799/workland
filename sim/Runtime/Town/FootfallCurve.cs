using System.Collections.Generic;

namespace Worktown.Sim.Town
{
    /// <summary>
    /// Spec 064: how a district's day footfall spreads over the 24 clock hours: 24 permille values (index = hour 0..23),
    /// each &gt;= 0, summing to 1000 (the catalog checks it). Named shapes: commute, shops, market, evening, night (check C12).
    /// </summary>
    public sealed record FootfallCurve(string Id, IReadOnlyList<int> HourPermille);
}
