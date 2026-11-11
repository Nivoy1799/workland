namespace Worktown.Sim.Climb
{
    /// <summary>
    /// Spec 052: the tuning targets T1-T8 (defaults = the spec's recommended open-question answers). Times are play seconds.
    /// T1 bands are cumulative hours to enter S2..S5 for the reference profile and strategy.
    /// Spec 052b (D40): the bands are the 4-player crew's (<see cref="ReferencePlayers"/>); a crew with difficulty h(n) below
    /// 1000 may be faster by that factor (the lower edge is scaled by h, the upper edge stays). Crews above the reference may
    /// spend at most <see cref="PlateauPermille"/> and at least <see cref="PlateauMinPermille"/> of the reference crew's time in each
    /// stage (T1p, the plateau). T5 uses a longer share window for crews below the reference (<see cref="ShareWindowFor"/>).
    /// C1 (calibration to the game, spec 053/037): median stage 1 services of the calibration runs, solo
    /// <see cref="C1SoloTenths"/> +- <see cref="C1SoloToleranceTenths"/>, crews <see cref="C1CrewTenths"/> +- <see cref="C1CrewToleranceTenths"/>.
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
        int MaxDebtServices = 5,
        int ReferencePlayers = CrewScaling.ReferencePlayers,
        int PlateauPermille = 1000,
        int PlateauMinPermille = 850,
        int C1SoloTenths = 190,
        int C1SoloToleranceTenths = 25,
        int C1CrewTenths = 57,
        int C1CrewToleranceTenths = 10)
    {
        /// <summary>The spec defaults.</summary>
        public static ClimbTargets Default { get; } = new ClimbTargets();

        /// <summary>
        /// 052b review: the T5 share window for a crew of <paramref name="players"/>: <see cref="ShareWindow"/> for the reference
        /// crew and above, longer below it (times reference / players), because a small crew sees few guests per service.
        /// </summary>
        public int ShareWindowFor(int players) =>
            players >= ReferencePlayers || players < 1 ? ShareWindow : ShareWindow * ReferencePlayers / players;

        /// <summary>The T1 band (from, to seconds) of entering <paramref name="stage"/> (2..5); (0, 0) for stage 1.</summary>
        public (long From, long To) BandOf(ClimbStage stage) => stage switch
        {
            ClimbStage.FirstRestaurant => (S2FromSeconds, S2ToSeconds),
            ClimbStage.ServiceMix => (S3FromSeconds, S3ToSeconds),
            ClimbStage.Automation => (S4FromSeconds, S4ToSeconds),
            ClimbStage.BeyondKitchen => (S5FromSeconds, S5ToSeconds),
            _ => (0, 0),
        };

        /// <summary>
        /// D40: the T1 band of a crew with difficulty <paramref name="difficultyPermille"/>: the lower edge times h (at most 1000,
        /// so an easier crew may be faster), the upper edge unchanged (no crew slower than the reference band).
        /// </summary>
        public (long From, long To) BandOf(ClimbStage stage, int difficultyPermille)
        {
            (long from, long to) = BandOf(stage);
            int h = System.Math.Max(0, System.Math.Min(1000, difficultyPermille));
            return (from * h / 1000, to);
        }
    }
}
