namespace Worktown.Sim.Climb
{
    /// <summary>
    /// Spec 052: the tuning targets T1-T8 (defaults = the spec's recommended open-question answers). Times are play seconds.
    /// T1 bands are cumulative hours to enter S2..S5 for the reference profile and strategy.
    /// </summary>
    public sealed record ClimbTargets(
        string ReferenceProfileId = "average",
        string NoviceProfileId = "novice",
        string ExpertProfileId = "expert",
        long S2FromSeconds = 2700, long S2ToSeconds = 5400,
        long S3FromSeconds = 10800, long S3ToSeconds = 18000,
        long S4FromSeconds = 25200, long S4ToSeconds = 39600,
        long S5FromSeconds = 43200, long S5ToSeconds = 72000,
        int MinServicesPerStage = 3,
        int SkillRatioPermille = 800,
        int ShortcutPermille = 850,
        int MaxSharePermille = 750,
        int ShareWindow = 10,
        int SnowballPermille = 500,
        int MaxUpkeepPermille = 700,
        int GoalMinTimes = 3,
        int GoalMaxTimes = 20,
        int GoalWindow = 5,
        int MaxDebtServices = 5)
    {
        /// <summary>The spec defaults.</summary>
        public static ClimbTargets Default { get; } = new ClimbTargets();

        /// <summary>The T1 band (from, to seconds) of entering <paramref name="stage"/> (2..5); (0, 0) for stage 1.</summary>
        public (long From, long To) BandOf(ClimbStage stage) => stage switch
        {
            ClimbStage.FirstRestaurant => (S2FromSeconds, S2ToSeconds),
            ClimbStage.ServiceMix => (S3FromSeconds, S3ToSeconds),
            ClimbStage.Automation => (S4FromSeconds, S4ToSeconds),
            ClimbStage.BeyondKitchen => (S5FromSeconds, S5ToSeconds),
            _ => (0, 0),
        };
    }
}
