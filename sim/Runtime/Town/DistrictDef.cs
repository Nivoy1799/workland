using System.Collections.Generic;

namespace Worktown.Sim.Town
{
    /// <summary>
    /// Spec 064: one district of Haven Nine. <see cref="Areas"/> are its rectangles in the plan; <see cref="HeightMm"/> is a
    /// representative height above the lake (path nodes carry the real heights); <see cref="DayFootfall"/> is relative points
    /// per day, spread over the hours by the curve <see cref="CurveId"/>; <see cref="CeilingPermille"/> caps boosted footfall
    /// (revival and conditions) at that share of the base. <see cref="Neighbours"/> are district ids (symmetric).
    /// </summary>
    public sealed record DistrictDef(string Id, string Name, IReadOnlyList<TownRect> Areas, int HeightMm, bool InCore,
        int DayFootfall, string CurveId, int CeilingPermille, IReadOnlyList<string> Neighbours);
}
