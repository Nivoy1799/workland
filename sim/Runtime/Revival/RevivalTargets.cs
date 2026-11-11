namespace Worktown.Sim.Revival
{
    /// <summary>
    /// Spec 065: the revival targets V1-V7 (defaults = the spec's). Times are play seconds; medians over seeds.
    /// V1: <see cref="ShopDistrict"/> reaches Patched between S2 entry + <see cref="PatchedFromS2Seconds"/> and S2 entry +
    /// <see cref="PatchedToS2Seconds"/>. V2: Lively between S3 and S4 entry, Thriving between S4 entry and S5 entry +
    /// <see cref="ThrivingAfterS5Seconds"/>. V3: other crews take <see cref="FairMinPermille"/> x h(n)..<see cref="FairMaxPermille"/>
    /// of the <see cref="ReferencePlayers"/> crew's hours to the shop district's Patched and Lively (065 review: the lower edge
    /// scales with the crew's difficulty h(n), as T1; V1 and V2 tie the levels to the crew's own stage entries). V4: every district reaches
    /// Patched before the horizon (reference crew). V5: <see cref="LastDistrict"/> comes back last. V6: the footfall bonus at
    /// its top level keeps the 052 T1 bands of the reference crew. V7: no level ever drops.
    /// </summary>
    public sealed record RevivalTargets(
        string ShopDistrict = "main_street",
        string LastDistrict = "low_ring",
        int ReferencePlayers = 4,
        long PatchedFromS2Seconds = -1800,
        long PatchedToS2Seconds = 3600,
        long ThrivingAfterS5Seconds = 18000,
        int FairMinPermille = 670,
        int FairMaxPermille = 1500)
    {
        /// <summary>The spec defaults.</summary>
        public static RevivalTargets Default { get; } = new RevivalTargets();
    }
}
