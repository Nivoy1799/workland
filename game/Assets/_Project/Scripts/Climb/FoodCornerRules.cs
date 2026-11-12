using System;
using Worktown.Game.Economy;
using Worktown.Game.Npc;
using Worktown.Sim.Climb;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;

namespace Worktown.Game.Climb
{
    /// <summary>
    /// Spec 053, pure: stage 1, the food corner. One dish (the 052 stage 1 dish, fries), walk-up guests who take it to go and pay cash,
    /// stock without ordering (a fixed drop fills the potato slot at each service start, paid at the bulk price), a pitch fee at each
    /// service end, and one goal: save for the rent. Every amount comes from spec 052's <see cref="ClimbCatalog"/>, scaled to the game's
    /// own price level by the dish price (<see cref="Scale"/>), so it holds before and after spec 037 (which made the scale 1: cents).
    /// </summary>
    public static class FoodCornerRules
    {
        /// <summary>The 052 dish id of stage 1 (open question 1: fries).</summary>
        public const string DishId = "fries";
        /// <summary>The game's menu dish for it.</summary>
        public const byte GameDish = Menu.Fries;
        /// <summary>The one stock line that is open (the dry shelf's potato slot).</summary>
        public const string StockGood = "potato";
        /// <summary>The 052 purchase that is the stage 1 goal (renting the restaurant).</summary>
        public const string GoalPurchase = "rent_restaurant";
        /// <summary>The 052 unlock a new shop starts with.</summary>
        public const string StartUnlock = "dish_fries";
        /// <summary>The fixed shop name until O6 (moderation) is decided.</summary>
        public const string ShopName = "Your food corner";
        /// <summary>The NPC market the dish sells to (its price level is the scale).</summary>
        public const string Market = "snack";

        /// <summary>Spec 057: the restaurant is in this build; the goal board rents it.</summary>
        public const string GoalReachedText = "Rent goal reached! Rent the restaurant at the goal board.";

        /// <summary>ceil(<paramref name="simMoney"/> × <paramref name="gameDishPrice"/> / <paramref name="simDishPrice"/>); 0 for no money.</summary>
        public static long Scale(long simMoney, long gameDishPrice, long simDishPrice)
        {
            if (simMoney <= 0 || gameDishPrice <= 0) return 0;
            if (simDishPrice <= 0) throw new ArgumentOutOfRangeException(nameof(simDishPrice), "the sim dish price must be positive");
            return (simMoney * gameDishPrice + simDishPrice - 1) / simDishPrice;
        }

        /// <summary>
        /// The game's price of the dish. Spec 037: the menu board's reference (start) price, which guests pay (MenuPrices: the climb's own
        /// $4.00 in cents), so the scale is 1 and the corner's amounts are the climb's in cents. It was the snack market's ceiling payout at
        /// the dish's base quality ($460 of the old scale, x 115). The economy catalog must still have the dish's market.
        /// </summary>
        public static long GameDishPrice(EconomyCatalog economy, ClimbCatalog climb)
        {
            if (economy == null) throw new ArgumentNullException(nameof(economy));
            if (climb == null) throw new ArgumentNullException(nameof(climb));
            if (!economy.TryGetMarket(Market, out MarketDef _)) throw new ArgumentException("no " + Market + " market", nameof(economy));
            return MenuPrices.ReferenceOf(MenuPrices.LineOfDish(GameDish), climb); // 037 review: the same source as SimDishPrice
        }

        /// <summary>The 052 price of the dish (its reference price).</summary>
        public static long SimDishPrice(ClimbCatalog climb) => (climb ?? throw new ArgumentNullException(nameof(climb))).Dish(climb.Stage1DishId).ReferencePrice;

        private static long Scaled(long simMoney, EconomyCatalog economy, ClimbCatalog climb) =>
            Scale(simMoney, GameDishPrice(economy, climb), SimDishPrice(climb));

        /// <summary>The rent goal: the scaled cost of the 052 rent purchase.</summary>
        public static long RentGoal(EconomyCatalog economy, ClimbCatalog climb) => Scaled(climb.Purchase(GoalPurchase).Cost, economy, climb);

        /// <summary>The pitch fee per service: the scaled 052 stage 1 upkeep.</summary>
        public static long PitchFee(EconomyCatalog economy, ClimbCatalog climb) => Scaled(climb.StageOf(ClimbStage.FoodCorner).Upkeep, economy, climb);

        /// <summary>The starting money of a new shop: the scaled 052 starting money.</summary>
        public static long StartingMoney(EconomyCatalog economy, ClimbCatalog climb) => Scaled(climb.StartingMoney, economy, climb);

        /// <summary>The goal flips once: at a service end the balance reached the goal and it was not reached before.</summary>
        public static bool ReachesGoal(bool alreadyReached, long balance, long goal) => !alreadyReached && goal > 0 && balance >= goal;

        /// <summary>
        /// Follow-up 2026-11-11 evening: the HUD's line at the service start, when the supplier's drop is paid from the shop wallet:
        /// "Potatoes delivered -$0.96 (2 on the shelf)", so the money that leaves at the day's start is seen leaving (a Continue opens the
        /// next service at once, so the wallet soon reads less than the title's saved balance). Nothing came: says why.
        /// </summary>
        public static string DeliveryText(int units, long cost) =>
            units > 0
                ? "Potatoes delivered " + (cost > 0 ? EconomyRules.Money(-cost) + " " : "") + "(" + units + " on the shelf)"
                : "No potatoes delivered: the potato slot is full, or the money is short";

        /// <summary>
        /// The HUD's goal line: "Save for the rent: $230.00 / $300.00". Follow-up 2026-11-11 evening: no clamp at 0 (the shop balance never goes
        /// below 0, see ShopLedger; if it ever did, it would read "-$1.20", not $0.00).
        /// </summary>
        public static string GoalLine(long balance, long goal) => "Save for the rent: " + EconomyRules.Money(balance) + " / " + EconomyRules.Money(goal);

        /// <summary>The shift summary's goal line with its percent (0 to 100): "Save for the rent: $230.00 / $300.00 (76 %)".</summary>
        public static string GoalProgressLine(long balance, long goal) => GoalLine(balance, goal) + " (" + GoalPercent(balance, goal) + " %)";

        /// <summary>The balance against the goal, 0 to 100 (rounded down).</summary>
        public static int GoalPercent(long balance, long goal)
        {
            if (goal <= 0) return 100;
            if (balance <= 0) return 0;
            return balance >= goal ? 100 : (int)(balance * 100 / goal);
        }

        /// <summary>
        /// The supplier's fixed drop at a service start: up to the slot's room (<paramref name="capacity"/> less <paramref name="count"/>),
        /// only as many units as <paramref name="canPay"/> (the bulk price of n units) says the money covers. 0 when nothing fits or is affordable.
        /// </summary>
        public static int DropUnits(int count, int capacity, Func<int, bool> canPay)
        {
            if (canPay == null) return 0;
            for (int n = Math.Max(0, capacity - Math.Max(0, count)); n > 0; n--)
                if (canPay(n)) return n;
            return 0;
        }

        /// <summary>Whether a guest wish of <paramref name="market"/> may come at <paramref name="stage"/> (stage 1: only the dish's market).</summary>
        public static bool WantsMarket(byte stage, string market) => WantsMarket(stage, 0, null, market);

        /// <summary>
        /// Spec 057: the same with stage 2's frozen <paramref name="menu"/>, which <see cref="RestaurantMenu.WantsMarket"/> decides (a market with a
        /// dish on the menu); <paramref name="climb"/> null is the default catalog.
        /// </summary>
        public static bool WantsMarket(byte stage, byte menu, ClimbCatalog climb, string market)
        {
            if (stage == ShopStageRules.OpenKitchen) return true;
            if (stage >= ShopStageRules.FirstRestaurant) return RestaurantMenu.WantsMarket(climb ?? DefaultClimb, menu, market);
            return market == Menu.ById(GameDish).Market;
        }

        /// <summary>Spec 057: the default climb catalog (immutable), for callers without their own.</summary>
        public static readonly ClimbCatalog DefaultClimb = ClimbCatalog.CreateDefault();

        /// <summary>
        /// The order a guest gives at <paramref name="stage"/>. The draws stay the same (same seed, same draw count: the caller drew them as
        /// in the open kitchen); stage 1 overrides the results: the dish, to go (never eat-in), no drink, cash (<c>PaysByCard</c> false).
        /// The patience base and the note are kept.
        /// </summary>
        public static GuestOrder StageOrder(byte stage, GuestOrder drawn, ref MenuDish dish, ref string drink, ref bool eatIn) =>
            StageOrder(stage, 0, null, drawn, ref dish, ref drink, ref eatIn);

        /// <summary>
        /// Spec 057: the same with stage 2's frozen <paramref name="menu"/>: <see cref="RestaurantMenu.StageOrder"/> (the dish on the menu, the drink only
        /// when the drinks are on; eat-in and the card as drawn).
        /// </summary>
        public static GuestOrder StageOrder(byte stage, byte menu, ClimbCatalog climb, GuestOrder drawn, ref MenuDish dish, ref string drink,
            ref bool eatIn)
        {
            if (stage == ShopStageRules.OpenKitchen || drawn == null) return drawn;
            if (stage >= ShopStageRules.FirstRestaurant) return RestaurantMenu.StageOrder(climb ?? DefaultClimb, menu, drawn, ref dish, ref drink);
            dish = Menu.ById(GameDish);
            drink = null;
            eatIn = false;
            return drawn with { Modifiers = Array.Empty<OrderModifier>(), PaysByCard = false };
        }
    }
}
