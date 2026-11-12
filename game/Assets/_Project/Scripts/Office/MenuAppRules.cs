using System.Collections.Generic;
using Worktown.Game.Climb;
using Worktown.Game.Cooking;
using Worktown.Game.Economy;
using Worktown.Game.Npc;
using Worktown.Game.Shop;
using Worktown.Sim.Climb;

namespace Worktown.Game.Office
{
    /// <summary>
    /// Spec 059, pure: the Menu app of the office terminal. Its rows are the MenuPrices lines this stage may sell, in MenuPrices order:
    /// every line in the open kitchen, the fries at the food corner, and at the restaurant the fries plus the lines of each owned recipe
    /// (the drinks recipe brings every drink). A row reads "Falafel wrap  $9.00  on  special". Its buttons step the price (the menu board's
    /// server method, MenuBoardDesk.ServerStepPrice), switch the dish on or off (057's toggle, ShopStage.ServerToggleRecipe: the fries stay
    /// on, the menu holds MaxDishes, a change applies at the next service start) and make it the special (<see cref="SpecialRules"/>).
    /// </summary>
    public static class MenuAppRules
    {
        public const string AlwaysOnText = "That one stays on";

        /// <summary>The app's rows at <paramref name="stage"/> (MenuPrices line indices).</summary>
        public static IReadOnlyList<int> Lines(byte stage, ClimbCatalog climb, byte owned)
        {
            var lines = new List<int>();
            if (stage < ShopStageRules.FirstRestaurant)
            {
                foreach (int line in MenuBoardRules.VisibleLines(stage)) lines.Add(line);
                return lines;
            }
            climb ??= FoodCornerRules.DefaultClimb;
            int fries = MenuPrices.LineOfDish(FoodCornerRules.GameDish);
            for (int i = 0; i < MenuPrices.Lines.Count; i++)
            {
                int row = RecipeRowOf(climb, i);
                if (i == fries || (row >= 0 && RestaurantMenu.Has(owned, row))) lines.Add(i);
            }
            return lines;
        }

        /// <summary>The 057 recipe row that puts line <paramref name="line"/> on the menu (a drink: the drinks recipe), or -1 (the fries, other dishes).</summary>
        public static int RecipeRowOf(ClimbCatalog climb, int line)
        {
            if (line < 0 || line >= MenuPrices.Lines.Count) return -1;
            MenuPrices.Line l = MenuPrices.Lines[line];
            IReadOnlyList<PurchaseDef> recipes = RestaurantRules.Recipes(climb ?? FoodCornerRules.DefaultClimb);
            for (int i = 0; i < recipes.Count && i < RestaurantMenu.MaxRows; i++)
            {
                if (!RestaurantMenu.TryMap(recipes[i].Effect.UnlocksDish, out byte dish, out bool drinks)) continue;
                if (l.Drink != null ? drinks : !drinks && dish == l.Dish) return i;
            }
            return -1;
        }

        /// <summary>Whether line <paramref name="line"/> is sold on <paramref name="menu"/> at <paramref name="stage"/> (the fries always).</summary>
        public static bool IsOn(byte stage, ClimbCatalog climb, byte menu, int line)
        {
            if (line < 0 || line >= MenuPrices.Lines.Count) return false;
            if (stage < ShopStageRules.FirstRestaurant) return MenuBoardRules.IndexIn(MenuBoardRules.VisibleLines(stage), line) >= 0;
            if (MenuPrices.Lines[line].Dish == FoodCornerRules.GameDish && MenuPrices.Lines[line].Drink == null) return true;
            int row = RecipeRowOf(climb, line);
            return row >= 0 && RestaurantMenu.Has(menu, row);
        }

        /// <summary>Whether line <paramref name="line"/> can never be switched off at <paramref name="stage"/> (every line below the restaurant, the fries).</summary>
        public static bool IsFixed(byte stage, ClimbCatalog climb, int line) =>
            stage < ShopStageRules.FirstRestaurant || RecipeRowOf(climb, line) < 0;

        /// <summary>
        /// A row's text, at most 40 characters: "Falafel wrap  $9.00  on  special". The state is "on" or "off", with " next" when it changes at
        /// the next service start (<paramref name="onNow"/> differs from <paramref name="onNext"/>).
        /// </summary>
        public static string RowText(int line, long price, bool onNow, bool onNext, bool special)
        {
            if (line < 0 || line >= MenuPrices.Lines.Count) return string.Empty;
            string state = onNext ? "on" : "off";
            if (onNow != onNext) state += " next";
            string text = MenuBoardRules.Title(MenuPrices.Lines[line].Name) + "  " + EconomyRules.Money(price) + "  " + state
                + (special ? "  " + SpecialRules.Tag : string.Empty);
            return ResultsRules.Fit(text);
        }

        /// <summary>The prompt's words for a row button: "price up $0.50", "switch fries on or off", "make it the special".</summary>
        public static string ButtonWords(int action, int line)
        {
            string name = line >= 0 && line < MenuPrices.Lines.Count ? MenuPrices.Lines[line].Name.ToLowerInvariant() : "it";
            switch (action)
            {
                case TerminalOptions.Minus: return "price down " + EconomyRules.Money(Worktown.Sim.Economy.MenuPricing.StepCents);
                case TerminalOptions.Plus: return "price up " + EconomyRules.Money(Worktown.Sim.Economy.MenuPricing.StepCents);
                case TerminalOptions.Toggle: return "switch " + name + " on or off";
                case TerminalOptions.Special: return "make " + name + " the special";
                default: return null;
            }
        }

        /// <summary>The label on a row button: "-", "+", "on/off", "special" (none for the row itself).</summary>
        public static string ButtonLabel(int action)
        {
            switch (action)
            {
                case TerminalOptions.Minus: return "-";
                case TerminalOptions.Plus: return "+";
                case TerminalOptions.Toggle: return "on/off";
                case TerminalOptions.Special: return "special";
                default: return string.Empty;
            }
        }

        /// <summary>The steps of a price press (-1 or +1), 0 for any other action.</summary>
        public static int StepsOf(int action) => action == TerminalOptions.Plus ? 1 : action == TerminalOptions.Minus ? -1 : 0;

        /// <summary>
        /// What a toggle of line <paramref name="line"/> is: the 057 recipe row to toggle (Ok), or AlwaysOn (the fries, every line below the
        /// restaurant).
        /// </summary>
        public static CookOutcome ToggleRow(byte stage, ClimbCatalog climb, int line, out int recipeRow)
        {
            recipeRow = -1;
            if (IsFixed(stage, climb, line)) return CookOutcome.AlwaysOn;
            recipeRow = RecipeRowOf(climb, line);
            return CookOutcome.Ok;
        }
    }
}
