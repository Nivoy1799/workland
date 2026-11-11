using System.Collections.Generic;

namespace Worktown.Sim.Revival
{
    /// <summary>
    /// Spec 065: one district's revival data: its goal factor (permille of the level goals), the progress it starts with (ppm
    /// of the step to Patched) and its neighbours (copied from the town; they get the spill of its meals). The catalog copies
    /// <paramref name="Neighbours"/> (an <see cref="IReadOnlyList{T}"/>, as spec 064: no System.Collections.Immutable in /sim).
    /// </summary>
    public sealed record DistrictRevivalDef(string DistrictId, int GoalPermille, int StartProgressPpm, IReadOnlyList<string> Neighbours);
}
