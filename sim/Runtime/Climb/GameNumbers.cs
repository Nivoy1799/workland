using System;
using System.Collections.Generic;
using System.Linq;

namespace Worktown.Sim.Climb
{
    /// <summary>
    /// Spec 052b: the game-side numbers per crew size (the GAME NUMBERS table). Amounts come from the catalog through
    /// <see cref="CrewScaling.For"/> (D40), times <c>gameScale</c>; services and hours from the reference runs (average profile,
    /// Balanced+Match, seeds 1..n) at the game's pace (<see cref="GameBetweenSeconds"/>, the 15 s shift summary). Pure and
    /// deterministic: the same inputs give the same row.
    /// </summary>
    public static class GameNumbers
    {
        /// <summary>The game's pause between services (its shift summary, seconds).</summary>
        public const int GameBetweenSeconds = 15;
        /// <summary>The crew sizes of the report's table.</summary>
        public static IReadOnlyList<int> ReportPlayers { get; } = Array.AsReadOnly(new[] { 1, 2, 3, 4, 6, 8, 12, 16 });

        public static GameNumbersRow For(ClimbCatalog catalog, int players, int gameScale, int seeds = 5, int serviceSeconds = 360,
            string profileId = "average", long horizonSeconds = 8 * 3600)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (gameScale < 1) throw new ArgumentOutOfRangeException(nameof(gameScale), "gameScale must be at least 1");
            if (seeds < 1) throw new ArgumentOutOfRangeException(nameof(seeds), "seeds must be at least 1");
            CrewFactors crew = CrewScaling.For(catalog, players);
            long unit = catalog.MoneyScale;
            StageDef s1 = catalog.StageOf(ClimbStage.FoodCorner), s2 = catalog.StageOf(ClimbStage.FirstRestaurant);
            long goal = s1.GatePurchaseId == null ? 0 : CrewScaling.ToReal(catalog.Purchase(s1.GatePurchaseId).Cost, crew.GoalPermille, unit);
            long license = s2.GatePurchaseId == null ? 0 : CrewScaling.ToReal(catalog.Purchase(s2.GatePurchaseId).Cost, crew.GoalPermille, unit);
            long pitch = CrewScaling.ScaleMoney(s1.Upkeep, crew.DemandPermille, unit);
            long rent = CrewScaling.ScaleMoney(s2.Upkeep, crew.DemandPermille, unit);
            long start = CrewScaling.ToReal(catalog.StartingMoney, crew.GoalPermille, unit);

            var engine = new ClimbEngine(catalog);
            var runs = new List<ClimbRun>();
            for (int seed = 1; seed <= seeds; seed++)
                runs.Add(engine.Run(new CrewSetup(players, profileId, ClimbStrategy.Reference, (ulong)seed, serviceSeconds, GameBetweenSeconds),
                    horizonSeconds));
            long services = ClimbEvaluation.Median(runs.Select(r => r.Completed(ClimbStage.FoodCorner)
                ? r.ServicesIn(ClimbStage.FoodCorner) : ClimbEvaluation.Dnf).ToList());
            long entered = ClimbEvaluation.Median(runs.Select(r => ClimbEvaluation.EnteredOrDnf(r, ClimbStage.FirstRestaurant)).ToList());
            int tenths = entered == ClimbEvaluation.Dnf ? -1 : (int)((entered * 10 + 1800) / 3600);
            return new GameNumbersRow(players, crew.DifficultyPermille, crew.DemandPermille, crew.GoalPermille, goal * gameScale,
                crew.StockPerService, pitch * gameScale, start * gameScale, rent * gameScale, license * gameScale,
                services == ClimbEvaluation.Dnf ? -1 : (int)services, tenths);
        }
    }
}
