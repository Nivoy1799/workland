using System.Collections.Generic;

namespace Worktown.Sim.Economy
{
    /// <summary>NPC demand per market. UnitsPerNpcPerDayCenti is units per NPC per day times 100 (integer, no float).</summary>
    public sealed record DemandProfile(int UnitsPerNpcPerDayCenti, IReadOnlyList<int> HourWeights, int JitterPercent);
}
