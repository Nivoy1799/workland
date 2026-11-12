using System;
using System.Collections.Generic;
using Worktown.Sim.Climb;
using Worktown.Sim.Economy;

namespace Worktown.Game.Climb
{
    /// <summary>
    /// Spec 057, pure: the amounts of stage 2, the first restaurant, read from spec 052's <see cref="ClimbCatalog"/> (no amount is written in
    /// /game) and scaled to the game's price level through <see cref="FoodCornerRules.Scale"/> (1 since spec 037: the catalog is in cents).
    /// D40 (spec 052b): the crew factors of <see cref="CrewScaling.For"/> at a service start scale the running costs (the rent and upkeep, by
    /// the guest demand n/4) and the goals and one-time costs (the rent goal of stage 1, the recipes, the service licence, by n x h(n)/4),
    /// floored to whole catalog units (dollars), as the 052b GAME NUMBERS do; prices and stock never scale. The rent reserve is the starting
    /// money unscaled (it pays for stock, whose prices do not scale).
    /// </summary>
    public static class RestaurantRules
    {
        /// <summary>D40: the crew sizes the climb knows (1 to 16 players).</summary>
        public const int MinPlayers = 1;
        public const int MaxPlayers = 16;

        /// <summary>The crew factors for <paramref name="players"/> connected at a service start (clamped to 1..16).</summary>
        public static CrewFactors Crew(ClimbCatalog climb, int players)
        {
            if (climb == null) throw new ArgumentNullException(nameof(climb));
            return CrewScaling.For(climb, Math.Max(MinPlayers, Math.Min(MaxPlayers, players)));
        }

        /// <summary>057 review: how many service starts the one-time crew remembers.</summary>
        public const int CrewMemory = 3;

        /// <summary>
        /// 057 review: the remembered service starts after one more with <paramref name="players"/> (0 or less: none added), each clamped to
        /// 1..16, the newest <see cref="CrewMemory"/> kept, newest last.
        /// </summary>
        public static int[] PushCrew(IReadOnlyList<int> starts, int players)
        {
            var list = new List<int>();
            if (starts != null) foreach (int n in starts) list.Add(Math.Max(MinPlayers, Math.Min(MaxPlayers, n)));
            if (players > 0) list.Add(Math.Min(MaxPlayers, players));
            while (list.Count > CrewMemory) list.RemoveAt(0);
            return list.ToArray();
        }

        /// <summary>
        /// 057 review (the coordinator's default, inbox question 32): the crew size of one-time costs (the rent goal, the recipes, the
        /// licence) and of the stage's goal: the largest of the last <see cref="CrewMemory"/> service starts and the players connected now,
        /// so a load (nobody joined yet) or a short drop never gives solo prices. Clamped to 1..16.
        /// </summary>
        public static int OneTimePlayers(IReadOnlyList<int> starts, int connectedNow)
        {
            int n = connectedNow;
            if (starts != null)
            {
                int from = Math.Max(0, starts.Count - CrewMemory);
                for (int i = from; i < starts.Count; i++) n = Math.Max(n, starts[i]);
            }
            return Math.Max(MinPlayers, Math.Min(MaxPlayers, n));
        }

        /// <summary>The 4-player reference crew (every factor 1000).</summary>
        public static CrewFactors Reference(ClimbCatalog climb) => Crew(climb, CrewScaling.ReferencePlayers);

        private static StageDef Stage(ClimbCatalog climb) =>
            (climb ?? throw new ArgumentNullException(nameof(climb))).StageOf(ClimbStage.FirstRestaurant);

        private static long Scaled(long money, EconomyCatalog economy, ClimbCatalog climb) =>
            FoodCornerRules.Scale(money, FoodCornerRules.GameDishPrice(economy, climb), FoodCornerRules.SimDishPrice(climb));

        /// <summary>A one-time amount for this crew (a goal, a purchase): x G = n x h(n) / 4, floored to whole catalog units, then scaled.</summary>
        public static long OneTime(long catalogMoney, EconomyCatalog economy, ClimbCatalog climb, CrewFactors crew) =>
            Scaled(CrewScaling.ToReal(catalogMoney, (crew ?? throw new ArgumentNullException(nameof(crew))).GoalPermille, climb.MoneyScale),
                economy, climb);

        /// <summary>A running cost for this crew (per service): x D = n / 4, floored to whole catalog units, then scaled.</summary>
        public static long Running(long catalogMoney, EconomyCatalog economy, ClimbCatalog climb, CrewFactors crew) =>
            Scaled(CrewScaling.ScaleMoney(catalogMoney, (crew ?? throw new ArgumentNullException(nameof(crew))).DemandPermille, climb.MoneyScale),
                economy, climb);

        /// <summary>The rent per service: the stage 2 upkeep (open question 8: the rent is the stage's upkeep).</summary>
        public static long RentPerService(EconomyCatalog economy, ClimbCatalog climb, CrewFactors crew) =>
            Running(Stage(climb).Upkeep, economy, climb, crew);

        /// <summary>The upkeep per service beyond the rent: the stage's channels and every owned purchase (0 with today's catalog).</summary>
        public static long UpkeepPerService(EconomyCatalog economy, ClimbCatalog climb, CrewFactors crew, IReadOnlyList<string> owned) =>
            Running(Math.Max(0L, climb.Upkeep(ClimbStage.FirstRestaurant, owned ?? Array.Empty<string>()) - Stage(climb).Upkeep), economy, climb, crew);

        /// <summary>What one stage 2 service end charges: the rent and the upkeep.</summary>
        public static long Due(EconomyCatalog economy, ClimbCatalog climb, CrewFactors crew, IReadOnlyList<string> owned) =>
            RentPerService(economy, climb, crew) + UpkeepPerService(economy, climb, crew, owned);

        /// <summary>The most dishes on the menu, fries included (052: 4).</summary>
        public static int MaxDishes(ClimbCatalog climb) => Stage(climb).MaxDishes;

        /// <summary>The stage 2 recipes: the catalog's stage 2 purchases that unlock a dish, in catalog order (the goal board's rows 1..n).</summary>
        public static IReadOnlyList<PurchaseDef> Recipes(ClimbCatalog climb)
        {
            if (climb == null) throw new ArgumentNullException(nameof(climb));
            var list = new List<PurchaseDef>();
            foreach (PurchaseDef p in climb.Purchases)
                if (p.Stage == ClimbStage.FirstRestaurant && p.Effect.UnlocksDish != null) list.Add(p);
            return list.AsReadOnly();
        }

        /// <summary>The cost of recipe <paramref name="purchaseId"/> for this crew.</summary>
        public static long RecipeCost(string purchaseId, EconomyCatalog economy, ClimbCatalog climb, CrewFactors crew) =>
            OneTime(climb.Purchase(purchaseId).Cost, economy, climb, crew);

        /// <summary>The gate's star average in tenths (052: 35, 3.5 stars).</summary>
        public static int GateStarsTenths(ClimbCatalog climb) => Stage(climb).GateStarsTenths;

        /// <summary>How many counted services the star average looks back over (052: 5).</summary>
        public static int GateWindow(ClimbCatalog climb) => Stage(climb).GateWindow;

        /// <summary>The 052 purchase of the gate (the service licence).</summary>
        public static string GatePurchaseId(ClimbCatalog climb) => Stage(climb).GatePurchaseId;

        /// <summary>The service licence's cost for this crew (0 when the stage has no gate purchase).</summary>
        public static long GateCost(EconomyCatalog economy, ClimbCatalog climb, CrewFactors crew)
        {
            string id = GatePurchaseId(climb);
            return id == null ? 0L : OneTime(climb.Purchase(id).Cost, economy, climb, crew);
        }

        /// <summary>Stage 1's goal for this crew: the rent purchase (052b game-side follow-up 1).</summary>
        public static long RentGoal(EconomyCatalog economy, ClimbCatalog climb, CrewFactors crew) =>
            OneTime(climb.Purchase(FoodCornerRules.GoalPurchase).Cost, economy, climb, crew);

        /// <summary>The money the rent never takes (open question 4): the starting money, so stock can always be ordered.</summary>
        public static long RentReserve(EconomyCatalog economy, ClimbCatalog climb) => FoodCornerRules.StartingMoney(economy, climb);

        /// <summary>The most rent that is ever owed (open question 4): two services' rent and upkeep.</summary>
        public static long OwedCap(long duePerService) => Math.Max(0L, duePerService) * 2;
    }
}
