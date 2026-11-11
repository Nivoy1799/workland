using System.Collections.Generic;

namespace Worktown.Sim.Revival
{
    /// <summary>
    /// Spec 065: the saved form of a <see cref="RevivalState"/>: three parallel lists (district id, level 0..3, progress ppm).
    /// <see cref="RevivalState.FromSaved"/> reads it leniently; loading never rebuilds levels from points.
    /// </summary>
    public sealed record SavedRevival(IReadOnlyList<string> DistrictIds, IReadOnlyList<int> Levels, IReadOnlyList<int> ProgressPpm);
}
