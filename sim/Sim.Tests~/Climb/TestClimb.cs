using System;
using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Climb;

namespace Worktown.Sim.Tests.Climb
{
    /// <summary>
    /// Spec 052 test fixture: a tiny catalog (one base channel "counter" with one rival, a "booth" channel unlocked in stage 2,
    /// two dishes, stage 1 and 2 with gates; stages 3-5 are plain) and a hand-built run builder for the evaluation tests.
    /// </summary>
    internal static class TestClimb
    {
        public static readonly PlayerProfile Worker = new PlayerProfile("worker", 10, 500);
        public static readonly PlayerProfile Clumsy = new PlayerProfile("clumsy", 10, 0);

        public static IReadOnlyList<PlayerProfile> Profiles => new[] { Worker, Clumsy, new PlayerProfile("expert", 20, 900) };

        public static IReadOnlyList<DishDef> Dishes => new[]
        {
            new DishDef("chips", "Chips", 4, 1, 5, 600),
            new DishDef("soup", "Soup", 6, 2, 8, 700),
        };

        public static IReadOnlyList<ChannelDef> Channels => new[]
        {
            new ChannelDef("counter", "Counter", ClimbStage.FoodCorner, 20, 1000, 1000, 0, 0),
            new ChannelDef("booth", "Booth", ClimbStage.FirstRestaurant, 10, 1000, 1000, 1, 2),
        };

        public static IReadOnlyList<RivalDef> Rivals => new[]
        {
            new RivalDef("rival_stand", "Rival Stand", "counter", 500, 0, 60, 500, 1, 2),
        };

        public static IReadOnlyList<PurchaseDef> Purchases => new[]
        {
            new PurchaseDef("peeler", "Peeler", ClimbStage.FoodCorner, PurchaseKind.Productive, 10, 0, PurchaseEffect.None with { ActionsSaved = 2, SavedOn = "chips" }),
            new PurchaseDef("sign", "Sign", ClimbStage.FoodCorner, PurchaseKind.Productive, 20, 0, PurchaseEffect.None with { CeilingBonusPermille = 500 }),
            new PurchaseDef("lease", "Lease", ClimbStage.FoodCorner, PurchaseKind.Gate, 50, 0, PurchaseEffect.None),
            new PurchaseDef("soup_recipe", "Soup Recipe", ClimbStage.FirstRestaurant, PurchaseKind.Productive, 30, 0, PurchaseEffect.None with { UnlocksDish = "soup" }),
            new PurchaseDef("booth_kit", "Booth Kit", ClimbStage.FirstRestaurant, PurchaseKind.Productive, 40, 0, PurchaseEffect.None with { UnlocksChannel = "booth" }),
            new PurchaseDef("helper", "Helper", ClimbStage.FirstRestaurant, PurchaseKind.Productive, 30, 3, PurchaseEffect.None with { StaffActionsPerMinute = 10, StaffSkillPermille = 1000 }),
            new PurchaseDef("lab", "Lab", ClimbStage.BeyondKitchen, PurchaseKind.Productive, 40, 1, PurchaseEffect.None with { QualityBonus = 50, CostSavedPermille = 500, ReputationBonus = 5 }),
            new PurchaseDef("trophy_case", "Trophy Case", ClimbStage.BeyondKitchen, PurchaseKind.Status, 100, 0, PurchaseEffect.None with { ReputationBonus = 10 }),
        };

        public static IReadOnlyList<StageDef> Stages => new[]
        {
            new StageDef(ClimbStage.FoodCorner, 1, 500, 1, 0, 1, 1, "lease", 0, 0, 0, 0, 0, 300),
            new StageDef(ClimbStage.FirstRestaurant, 2, 500, 1, 1, 2, 2, null, 30, 2, 1, 0, 0, 400),
            new StageDef(ClimbStage.ServiceMix, 2, 500, 1, 1, 2, 2, null, 30, 1, 0, 0, 0, 400),
            new StageDef(ClimbStage.Automation, 2, 500, 1, 1, 2, 2, null, 30, 1, 0, 0, 0, 400),
            new StageDef(ClimbStage.BeyondKitchen, 2, 500, 1, 1, 2, 2, null, 0, 0, 0, 0, 0, 400),
        };

        public static IReadOnlyList<BigJobDef> BigJobs => new[]
        {
            new BigJobDef("contest", "Contest", BigJobKind.Competition, 10, 0, 1000, 0, 100, null, 3),
            new BigJobDef("fair", "Fair", BigJobKind.Event, 20, 5, 2000, 0, 0, "lab", 2),
        };

        public static readonly ChoiceWeights Weights = new ChoiceWeights(1000, 500, 1000, 300, 0, 500, 10);

        public static ClimbCatalog Build(IEnumerable<PlayerProfile>? profiles = null, IEnumerable<DishDef>? dishes = null,
            IEnumerable<ChannelDef>? channels = null, IEnumerable<RivalDef>? rivals = null, IEnumerable<PurchaseDef>? purchases = null,
            IEnumerable<StageDef>? stages = null, IEnumerable<BigJobDef>? bigJobs = null, ChoiceWeights? weights = null,
            string stage1Dish = "chips", long startingMoney = 0, long moneyScale = 1) =>
            new ClimbCatalog(profiles ?? Profiles, dishes ?? Dishes, channels ?? Channels, rivals ?? Rivals, purchases ?? Purchases,
                stages ?? Stages, bigJobs ?? BigJobs, weights ?? Weights, stage1Dish, startingMoney, moneyScale);

        public static readonly ClimbCatalog Catalog = Build();
        public static readonly ClimbEngine Engine = new ClimbEngine(Catalog);

        public static CrewSetup Setup(int players = 1, string profile = "worker", ClimbStrategy? strategy = null, ulong seed = 1) =>
            new CrewSetup(players, profile, strategy ?? ClimbStrategy.Reference, seed);

        /// <summary>The start state with a different stage, balance, owned list (and the stage entered now).</summary>
        public static ClimbState State(ClimbStage stage = ClimbStage.FoodCorner, long balance = 0, params string[] owned)
        {
            ClimbState s = Engine.Start(Setup());
            var entered = s.StageEnteredSeconds.ToArray();
            for (int i = 2; i <= (int)stage; i++) entered[i] = 0;
            return s with { Stage = stage, Balance = balance, Owned = owned, StageEnteredSeconds = entered };
        }

        public static ClimbState WithPrices(ClimbState s, PricePolicy policy = PricePolicy.Match) =>
            s with { Prices = ClimbPolicy.Prices(Catalog, s, policy) };

        // ---------- hand-built runs (evaluation tests) ----------

        public const int Step = 600;

        /// <summary>One hand-built service. Revenue = net + upkeep; one "counter" channel with a rival.</summary>
        public static ServiceResult Svc(int index, ClimbStage stage, long net = 100, long upkeep = 10, long balance = 100,
            int you = 5, int rival = 5, IReadOnlyList<long>? goals = null, int chosen = 10, int missed = 0) =>
            new ServiceResult(index, stage, null, (long)index * Step, Step, chosen, chosen - missed, missed, 0, net + upkeep, 0, upkeep, 0, 0,
                balance, 700, 80, 40, 800, new[] { "counter" }, new[] { you }, new[] { rival }, Array.Empty<string>(),
                goals ?? new[] { net * 5 });

        /// <summary>
        /// A run with <paramref name="perStage"/>[i] services in stage i+1 (600 s each); <paramref name="custom"/> may replace any
        /// service (index, stage, default).
        /// </summary>
        public static ClimbRun Run(int players, string profile, ClimbStrategy strategy, ulong seed, int[] perStage,
            Func<int, ClimbStage, ServiceResult, ServiceResult>? custom = null, long horizon = 40 * 3600, CrewFactors? crew = null)
        {
            var list = new List<ServiceResult>();
            int index = 0;
            for (int st = 0; st < perStage.Length; st++)
                for (int k = 0; k < perStage[st]; k++, index++)
                {
                    ClimbStage stage = (ClimbStage)(st + 1);
                    ServiceResult r = Svc(index, stage);
                    list.Add(custom != null ? custom(index, stage, r) : r);
                }
            var setup = new CrewSetup(players, profile, strategy, seed);
            return new ClimbRun(setup, horizon, list, Engine.Start(Setup()), 1, crew);
        }

        /// <summary>Default pacing (600 s services): S2 at 1 h, S3 at 4 h, S4 at 9 h, S5 at 15 h.</summary>
        public static readonly int[] Good = { 6, 18, 30, 36, 20 };
    }
}
