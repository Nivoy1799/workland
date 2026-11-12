using System;
using System.Collections.Generic;
using System.Text;
using Worktown.Game.Climb;
using Worktown.Game.Economy;
using Worktown.Game.Npc;
using Worktown.Sim.Economy;

namespace Worktown.Game.Shop
{
    /// <summary>
    /// Spec 037, pure: the menu board above the counter. Its four buttons are its station options (clients send only the option,
    /// RequestStation): the previous and the next line, and the selected line's price $0.50 down or up (Sim.Economy.MenuPricing). The
    /// board shows the lines on sale at the shop's stage: every dish and drink in the open kitchen, the fries alone at the food corner
    /// (one dish, D34). Prices are cents.
    /// </summary>
    public static class MenuBoardRules
    {
        public const int PreviousOption = 0;
        public const int NextOption = 1;
        public const int CheaperOption = 2;
        public const int DearerOption = 3;
        public const int OptionCount = 4;
        /// <summary>The most lines the board's text shows at once (a window around the selected line).</summary>
        public const int ShownLines = 6;

        /// <summary>The board's lines on sale at <paramref name="stage"/> (indices into MenuPrices.Lines), in board order.</summary>
        public static IReadOnlyList<int> VisibleLines(byte stage)
        {
            var lines = new List<int>();
            if (stage == ShopStageRules.OpenKitchen)
            {
                for (int i = 0; i < MenuPrices.Lines.Count; i++) lines.Add(i);
                return lines;
            }
            int fries = MenuPrices.LineOfDish(FoodCornerRules.GameDish);
            if (stage < ShopStageRules.FirstRestaurant)
            {
                if (fries >= 0) lines.Add(fries);
                return lines;
            }
            // Spec 057: the restaurant prices every dish its recipes can put on the menu (the fries, the 052 stage 2 dishes) and the drinks,
            // so a recipe's price is set before it goes on; guests still order only what the menu holds.
            for (int i = 0; i < MenuPrices.Lines.Count; i++)
            {
                MenuPrices.Line line = MenuPrices.Lines[i];
                if (i == fries || line.Drink != null || IsRecipeDish(line.Dish)) lines.Add(i);
            }
            return lines;
        }

        private static bool IsRecipeDish(byte dish)
        {
            foreach (Worktown.Sim.Climb.PurchaseDef recipe in RestaurantRules.Recipes(FoodCornerRules.DefaultClimb))
                if (RestaurantMenu.TryMap(recipe.Effect.UnlocksDish, out byte mapped, out bool drinks) && !drinks && mapped == dish) return true;
            return false;
        }

        /// <summary>The prompt's words for a button: "previous dish", "next dish", "price down $0.50", "price up $0.50".</summary>
        public static string ButtonWords(int option)
        {
            switch (option)
            {
                case PreviousOption: return "previous dish";
                case NextOption: return "next dish";
                case CheaperOption: return "price down " + EconomyRules.Money(MenuPricing.StepCents);
                case DearerOption: return "price up " + EconomyRules.Money(MenuPricing.StepCents);
                default: return "use the menu board";
            }
        }

        /// <summary>The label on a button: "<", ">", "-", "+".</summary>
        public static string ButtonLabel(int option)
        {
            switch (option)
            {
                case PreviousOption: return "<";
                case NextOption: return ">";
                case CheaperOption: return "-";
                case DearerOption: return "+";
                default: return "?";
            }
        }

        /// <summary>
        /// The board's text: "MENU", then up to <see cref="ShownLines"/> visible lines around the selected one, each "Fries  $4.00" (the
        /// selected one marked with "&gt;"). <paramref name="prices"/> are cents per MenuPrices line (a missing one reads as its reference).
        /// </summary>
        public static string DisplayText(byte stage, IReadOnlyList<int> prices, int selected) => DisplayText(stage, prices, selected, Menu.None);

        /// <summary>Spec 059: the same, with the special of the day's line tagged ("Falafel wrap  $9.00  Special"; <paramref name="special"/> a dish id).</summary>
        public static string DisplayText(byte stage, IReadOnlyList<int> prices, int selected, byte special)
        {
            IReadOnlyList<int> visible = VisibleLines(stage);
            var sb = new StringBuilder("MENU");
            if (visible.Count == 0) return sb.ToString();
            int at = Math.Max(0, IndexIn(visible, selected));
            int first = Math.Max(0, Math.Min(at - ShownLines / 2, visible.Count - ShownLines));
            for (int i = first; i < visible.Count && i < first + ShownLines; i++)
            {
                int line = visible[i];
                MenuPrices.Line l = MenuPrices.Lines[line];
                long price = prices != null && line < prices.Count ? prices[line] : l.Reference;
                sb.Append('\n').Append(i == at ? "> " : "  ").Append(Title(l.Name)).Append("  ").Append(EconomyRules.Money(price));
                if (special != Menu.None && l.Drink == null && l.Dish == special) sb.Append("  ").Append(SpecialTag);
            }
            return sb.ToString();
        }

        /// <summary>Spec 059: the tag the board shows on the special of the day's line.</summary>
        public const string SpecialTag = "Special";

        /// <summary>The index of <paramref name="line"/> in <paramref name="visible"/>, or -1.</summary>
        public static int IndexIn(IReadOnlyList<int> visible, int line)
        {
            for (int i = 0; i < visible.Count; i++)
                if (visible[i] == line) return i;
            return -1;
        }

        /// <summary>"lemonade" to "Lemonade".</summary>
        public static string Title(string name) =>
            string.IsNullOrEmpty(name) ? string.Empty : char.ToUpperInvariant(name[0]) + name.Substring(1);
    }
}
