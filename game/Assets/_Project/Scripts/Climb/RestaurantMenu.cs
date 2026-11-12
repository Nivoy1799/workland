using System;
using System.Collections.Generic;
using System.Linq;
using Worktown.Game.Npc;
using Worktown.Sim.Climb;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;

namespace Worktown.Game.Climb
{
    /// <summary>
    /// Spec 057, pure: the stage 2 menu. Spec 052's recipe dishes map to the game's dishes (<see cref="TryMap"/>; lemonade stands for every
    /// drink of the 033 taps); the fries (the stage 1 dish) are always on. A menu is a byte mask: bit i is recipe row i
    /// (<see cref="RestaurantRules.Recipes"/>, catalog order). A recipe is on only when it is owned, and the on recipes plus the fries are at
    /// most <see cref="RestaurantRules.MaxDishes"/>. Guests at stage 2 draw as in the open kitchen (the same RNGs, the same count); the
    /// menu frozen at the service start then decides what they order (<see cref="StageOrder"/>).
    /// </summary>
    public static class RestaurantMenu
    {
        /// <summary>The most recipe rows a mask holds.</summary>
        public const int MaxRows = 8;
        /// <summary>The 052 dish id that stands for every drink (open question 6).</summary>
        public const string DrinksDishId = "lemonade";

        /// <summary>The game dish of a 052 recipe dish (<paramref name="drinks"/>: it is the drinks); false for a dish this build does not map.</summary>
        public static bool TryMap(string climbDishId, out byte gameDish, out bool drinks)
        {
            gameDish = Menu.None;
            drinks = false;
            switch (climbDishId)
            {
                case "fries": gameDish = Menu.Fries; return true;
                case "falafel_wrap": gameDish = Menu.FalafelWrap; return true;
                case "seitan_wrap": gameDish = Menu.SeitanWrap; return true;
                case "grilled_veggies": gameDish = Menu.GrilledVeggies; return true;
                case DrinksDishId: drinks = true; return true;
                default: return false;
            }
        }

        /// <summary>Whether bit <paramref name="row"/> of <paramref name="mask"/> is set.</summary>
        public static bool Has(byte mask, int row) => row >= 0 && row < MaxRows && (mask & (1 << row)) != 0;

        /// <summary><paramref name="mask"/> with bit <paramref name="row"/> set or cleared.</summary>
        public static byte With(byte mask, int row, bool on)
        {
            if (row < 0 || row >= MaxRows) return mask;
            return on ? (byte)(mask | (1 << row)) : (byte)(mask & ~(1 << row));
        }

        /// <summary>How many bits are set.</summary>
        public static int Count(byte mask)
        {
            int n = 0;
            for (int i = 0; i < MaxRows; i++) if (Has(mask, i)) n++;
            return n;
        }

        /// <summary>Whether <paramref name="menu"/> fits: only owned rows, and its rows plus the fries at most <paramref name="maxDishes"/>.</summary>
        public static bool Fits(byte menu, byte owned, int maxDishes) => (menu & ~owned) == 0 && Count(menu) + 1 <= maxDishes;

        /// <summary>The menu cut to what fits: off rows that are not owned, then the highest rows until it holds (a save's menu, a changed catalog).</summary>
        public static byte Clamp(byte menu, byte owned, int maxDishes)
        {
            byte m = (byte)(menu & owned);
            for (int row = MaxRows - 1; row >= 0 && Count(m) + 1 > Math.Max(1, maxDishes); row--) m = With(m, row, false);
            return m;
        }

        /// <summary>The row of recipe purchase <paramref name="purchaseId"/>, or -1.</summary>
        public static int RowOf(ClimbCatalog climb, string purchaseId)
        {
            IReadOnlyList<PurchaseDef> recipes = RestaurantRules.Recipes(climb);
            for (int i = 0; i < recipes.Count && i < MaxRows; i++)
                if (recipes[i].Id == purchaseId) return i;
            return -1;
        }

        /// <summary>Whether the drinks are on (their recipe row is on).</summary>
        public static bool DrinksOn(ClimbCatalog climb, byte menu)
        {
            IReadOnlyList<PurchaseDef> recipes = RestaurantRules.Recipes(climb);
            for (int i = 0; i < recipes.Count && i < MaxRows; i++)
                if (Has(menu, i) && TryMap(recipes[i].Effect.UnlocksDish, out _, out bool drinks) && drinks) return true;
            return false;
        }

        /// <summary>
        /// The game dishes on <paramref name="menu"/> that guests of <paramref name="market"/> order, in the game's menu order: the fries
        /// (always on) and each on recipe that maps to a dish of that market.
        /// </summary>
        public static IReadOnlyList<byte> OnDishes(ClimbCatalog climb, byte menu, string market)
        {
            var on = new List<byte>();
            IReadOnlyList<PurchaseDef> recipes = RestaurantRules.Recipes(climb);
            foreach (MenuDish dish in Menu.All)
            {
                if (dish.Market != market) continue;
                bool listed = dish.Id == FoodCornerRules.GameDish;
                for (int i = 0; !listed && i < recipes.Count && i < MaxRows; i++)
                    listed = Has(menu, i) && TryMap(recipes[i].Effect.UnlocksDish, out byte d, out bool drinks) && !drinks && d == dish.Id;
                if (listed) on.Add(dish.Id);
            }
            return on;
        }

        /// <summary>Whether a guest wish of <paramref name="market"/> may come at stage 2: the market has a dish on the menu.</summary>
        public static bool WantsMarket(ClimbCatalog climb, byte menu, string market) => OnDishes(climb, menu, market).Count > 0;

        /// <summary>
        /// The order a stage 2 guest gives on <paramref name="menu"/>. The draws were made as in the open kitchen; a drawn dish on the menu is
        /// kept with its modifiers; one that is not becomes the on dish of its market at <c>drawn dish id % count</c> (modifiers cleared);
        /// the drink goes when the drinks are off. Eat-in and the card stay as drawn.
        /// </summary>
        public static GuestOrder StageOrder(ClimbCatalog climb, byte menu, GuestOrder drawn, ref MenuDish dish, ref string drink)
        {
            if (drawn == null || dish == null) return drawn;
            if (!DrinksOn(climb, menu)) drink = null;
            IReadOnlyList<byte> on = OnDishes(climb, menu, dish.Market);
            if (on.Contains(dish.Id) || on.Count == 0) return drawn;
            dish = Menu.ById(on[dish.Id % on.Count]);
            return drawn with { Modifiers = Array.Empty<OrderModifier>() };
        }
    }
}
