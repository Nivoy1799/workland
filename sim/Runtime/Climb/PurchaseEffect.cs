namespace Worktown.Sim.Climb
{
    /// <summary>
    /// Spec 052: what owning a purchase does. <see cref="SavedOn"/> is a dish id or <see cref="ClimbCatalog.CleaningTarget"/>;
    /// <see cref="CeilingChannel"/> null means every channel. Spec 052b: <see cref="WorkSpots"/> adds work spots (per copy).
    /// </summary>
    public sealed record PurchaseEffect(int ActionsSaved, string? SavedOn, int QualityBonus, int CostSavedPermille,
        int CeilingBonusPermille, string? CeilingChannel, int ReputationBonus, int StaffActionsPerMinute,
        int StaffSkillPermille, string? UnlocksChannel, string? UnlocksDish, int WorkSpots = 0)
    {
        /// <summary>No effect (a gate, a status item without a bonus).</summary>
        public static PurchaseEffect None { get; } = new PurchaseEffect(0, null, 0, 0, 0, null, 0, 0, 0, null, null);
    }
}
