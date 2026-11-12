using System.Collections.Generic;
using Worktown.Game.Economy;
using Worktown.Game.Npc;
using Worktown.Game.Shop;
using Worktown.Sim.Climb;

namespace Worktown.Game.Office
{
    /// <summary>
    /// Spec 059, pure: the pocket clipboard, the same facts as the office terminal, read-only, from synced state: today's menu with its
    /// prices and the special, the goal line from the HUD and Results page 1. (062b adds the door's state.) Short lines, no table.
    /// </summary>
    public static class ClipboardRules
    {
        public const string Title = "Clipboard";
        public const string MenuTitle = "Today's menu";
        public const string LastServiceTitle = "The last service";

        /// <summary>The close hint under the clipboard, with the clipboard key's name.</summary>
        public static string CloseHint(string key) => key + " or Esc puts it away";

        /// <summary>
        /// The clipboard's lines: the menu sold now (<paramref name="menuNow"/>, the service's frozen menu) with its board prices
        /// (<paramref name="prices"/> in cents per MenuPrices line; a missing one reads as its reference) and the special, an empty line,
        /// the goal line, an empty line, and Results page 1.
        /// </summary>
        public static List<string> Lines(byte stage, ClimbCatalog climb, byte owned, byte menuNow, IReadOnlyList<int> prices, byte special,
            string goalLine, IReadOnlyList<ServiceRecord> history)
        {
            var lines = new List<string> { MenuTitle };
            foreach (int line in MenuAppRules.Lines(stage, climb, owned))
            {
                if (!MenuAppRules.IsOn(stage, climb, menuNow, line)) continue;
                MenuPrices.Line l = MenuPrices.Lines[line];
                long price = prices != null && line < prices.Count ? prices[line] : l.Reference;
                bool isSpecial = l.Drink == null && special != Menu.None && l.Dish == special;
                lines.Add(ResultsRules.Fit(MenuBoardRules.Title(l.Name) + "  " + EconomyRules.Money(price) + (isSpecial ? "  " + SpecialRules.Tag : string.Empty)));
            }
            if (!string.IsNullOrEmpty(goalLine))
            {
                lines.Add(string.Empty);
                foreach (string g in goalLine.Split('\n')) if (g.Length > 0) lines.Add(g); // the HUD's own words (the overlay wraps them)
            }
            lines.Add(string.Empty);
            lines.Add(LastServiceTitle);
            lines.AddRange(ResultsRules.Page1(history));
            return lines;
        }
    }
}
