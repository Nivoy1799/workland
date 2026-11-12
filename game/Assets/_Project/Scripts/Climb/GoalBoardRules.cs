using System;
using System.Collections.Generic;
using Worktown.Game.Cooking;
using Worktown.Game.Economy;
using Worktown.Game.Npc;
using Worktown.Sim.Climb;

namespace Worktown.Game.Climb
{
    /// <summary>What a goal board press does once the server allowed it.</summary>
    public enum GoalBoardAction : byte
    {
        /// <summary>Nothing (a refusal, or the gate row at stage 2).</summary>
        None,
        /// <summary>Stage 1, row 0: pay the rent goal and move (after the confirm).</summary>
        Rent,
        /// <summary>Stage 2, a recipe row not owned: buy it (after the confirm).</summary>
        Buy,
        /// <summary>Stage 2, an owned recipe row: on or off the next service's menu.</summary>
        Toggle
    }

    /// <summary>
    /// Spec 057, pure: the goal board, a wall panel of the layout (piece <c>goal_board</c>). Its station options are its rows: 0 the stage's
    /// step, 1..n the stage 2 recipes (<see cref="RestaurantRules.Recipes"/>). The open kitchen refuses every row; stage 1 rents the
    /// restaurant on row 0 and refuses the recipes; stage 2 shows the gate on row 0 (no action), buys a recipe that is not owned and turns an
    /// owned one on or off the menu (from the next service start). The server decides with <see cref="Decide"/>; the client's text comes
    /// from the synced fields through <see cref="RowText"/>.
    /// </summary>
    public static class GoalBoardRules
    {
        public const string Name = "Goal board";
        /// <summary>The rows the board has: the stage's step and up to four recipes.</summary>
        public const int RowCount = 5;
        public const int StepRow = 0;

        public const string RentedText = "You rented the restaurant!";
        public const string MovingText = "Moving day: the whole kitchen is yours";
        public const string RestaurantName = "Your restaurant";
        public const string NextServiceText = "On the menu from the next service";
        public static string MenuFullText(int maxDishes) => "The menu holds " + maxDishes + " dishes";
        public const string OrderStockText = "Order stock at the terminal";
        public const string ConfirmText = "Press again to pay";
        public const string MoveWaitsText = "You move in when this service ends";

        /// <summary>The board's state as the server (or a client, from the synced fields) sees it.</summary>
        public readonly struct State
        {
            public State(byte stage, long balance, long rentGoal, bool movePending, byte owned, byte menuNext, long rentOwed, long reserve = 0)
            {
                Stage = stage;
                Balance = balance;
                RentGoal = rentGoal;
                MovePending = movePending;
                Owned = owned;
                MenuNext = menuNext;
                RentOwed = rentOwed;
                Reserve = Math.Max(0L, reserve);
            }

            public byte Stage { get; }
            public long Balance { get; }
            /// <summary>Stage 1's goal (the rent purchase for this crew).</summary>
            public long RentGoal { get; }
            public bool MovePending { get; }
            public byte Owned { get; }
            public byte MenuNext { get; }
            public long RentOwed { get; }
            /// <summary>057 review: the money the rent never takes (RestaurantRules.RentReserve): renting keeps it, so stock can be ordered.</summary>
            public long Reserve { get; }
        }

        /// <summary>
        /// What a press of <paramref name="row"/> does: the outcome (Ok when allowed) and the <paramref name="action"/>. <paramref name="recipes"/>
        /// is the number of recipe rows, <paramref name="rowCost"/> the cost of that row's recipe for this crew, <paramref name="maxDishes"/> the
        /// menu's size with the fries.
        /// </summary>
        public static CookOutcome Decide(State s, int row, int recipes, long rowCost, int maxDishes, out GoalBoardAction action)
        {
            action = GoalBoardAction.None;
            int rows = Math.Min(RowCount - 1, Math.Max(0, recipes));
            if (row < 0 || row > rows) return CookOutcome.InvalidOption;
            if (s.Stage == ShopStageRules.OpenKitchen) return CookOutcome.NotOpenYet;
            if (s.Stage == ShopStageRules.FoodCorner)
            {
                if (row != StepRow) return CookOutcome.NotOpenYet;
                if (s.MovePending) return CookOutcome.MoveWaits;
                if (s.RentGoal <= 0 || s.Balance < s.RentGoal + s.Reserve) return CookOutcome.NotEnoughMoney; // 057 review: no soft-lock at the move
                action = GoalBoardAction.Rent;
                return CookOutcome.Ok;
            }
            if (row == StepRow) return CookOutcome.Ok; // the gate line: nothing to press yet (the licence comes later)
            int recipe = row - 1;
            if (!RestaurantMenu.Has(s.Owned, recipe))
            {
                if (s.RentOwed > 0) return CookOutcome.PayRentFirst;
                if (s.Balance < rowCost) return CookOutcome.NotEnoughMoney;
                action = GoalBoardAction.Buy;
                return CookOutcome.Ok;
            }
            if (!RestaurantMenu.Has(s.MenuNext, recipe) && RestaurantMenu.Count(s.MenuNext) + 2 > maxDishes) return CookOutcome.MenuFull;
            action = GoalBoardAction.Toggle;
            return CookOutcome.Ok;
        }

        /// <summary>The aim prompt's words for row <paramref name="row"/>: "press the goal row", "press this recipe row".</summary>
        public static string PressWords(int row) => row == StepRow ? "press the goal row" : "press this recipe row";

        /// <summary>Whether <paramref name="action"/> spends the shop's money (and so needs the two-press confirm).</summary>
        public static bool NeedsConfirm(GoalBoardAction action) => action == GoalBoardAction.Rent || action == GoalBoardAction.Buy;

        /// <summary>The next menu after toggling recipe <paramref name="recipe"/> (an owned row).</summary>
        public static byte Toggled(byte menuNext, int recipe) => RestaurantMenu.With(menuNext, recipe, !RestaurantMenu.Has(menuNext, recipe));

        /// <summary>The name of a recipe's dish on the board: the game dish's name, or "Drinks".</summary>
        public static string DishName(PurchaseDef recipe)
        {
            if (recipe == null) return string.Empty;
            if (!RestaurantMenu.TryMap(recipe.Effect.UnlocksDish, out byte dish, out bool drinks)) return recipe.Name;
            return drinks ? "Drinks" : Menu.NameOf(dish);
        }

        /// <summary>
        /// The text of row <paramref name="row"/> for the board (the client's view; empty for a row that does not exist). The stage 2 step row
        /// is the gate line (<paramref name="gateLine"/>), and "Order stock at the terminal" until the first stage 2 service ended.
        /// </summary>
        public static string RowText(State s, int row, IReadOnlyList<PurchaseDef> recipes, Func<int, long> costOf, byte menuNow,
            string gateLine, bool firstService)
        {
            int count = recipes == null ? 0 : Math.Min(RowCount - 1, recipes.Count);
            if (row < 0 || row > count) return string.Empty;
            if (s.Stage == ShopStageRules.OpenKitchen) return row == StepRow ? Name + ": your goals show here in a saved shop" : string.Empty;
            if (s.Stage == ShopStageRules.FoodCorner)
            {
                if (row == StepRow)
                    return s.MovePending ? "Restaurant rented: " + MoveWaitsText.ToLowerInvariant()
                        : "Rent the restaurant: " + EconomyRules.Money(s.RentGoal)
                            + (s.Reserve > 0 ? " + " + EconomyRules.Money(s.Reserve) + " kept for stock" : string.Empty) + " (press twice)";
                return recipes[row - 1].Name + ": " + ShopStageRules.NotOpenText.ToLowerInvariant();
            }
            if (row == StepRow) return firstService ? gateLine + "\n" + OrderStockText : gateLine;
            int recipe = row - 1;
            PurchaseDef def = recipes[recipe];
            if (!RestaurantMenu.Has(s.Owned, recipe))
                return def.Name + ": " + EconomyRules.Money(costOf != null ? costOf(recipe) : 0L) + " (press twice)";
            bool next = RestaurantMenu.Has(s.MenuNext, recipe);
            string text = DishName(def) + (next ? ": on the menu" : ": off the menu");
            return next != RestaurantMenu.Has(menuNow, recipe) ? text + " (from the next service)" : text;
        }

        /// <summary>The feed line of a purchase: "Player 2 bought the Falafel Wrap Recipe (-$27.00)".</summary>
        public static string BoughtText(int player, string name, long cost) =>
            "Player " + player + " bought the " + name + " (" + EconomyRules.Money(-Math.Max(0L, cost)) + ")";

        /// <summary>The feed line of the rent: "Player 1 rented the restaurant (-$300.00)".</summary>
        public static string RentPaidText(int player, long cost) =>
            "Player " + player + " rented the restaurant (" + EconomyRules.Money(-Math.Max(0L, cost)) + ")";

        /// <summary>The feed line of a toggle: "Falafel wrap: on the menu from the next service" (or off).</summary>
        public static string ToggledText(string dish, bool on) => dish + (on ? ": on" : ": off") + " the menu from the next service";
    }
}
