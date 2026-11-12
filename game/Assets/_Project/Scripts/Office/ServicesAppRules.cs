using System;
using System.Collections.Generic;
using Worktown.Game.Climb;
using Worktown.Game.Cooking;
using Worktown.Sim.Climb;

namespace Worktown.Game.Office
{
    /// <summary>
    /// Spec 059, pure: the Services app of the office terminal: one row per spec 052 channel. The counter ("Counter: open") is the only
    /// service for now and cannot be switched off; delivery, custom orders, tables and the buffet read "comes with stage 3" and refuse with
    /// that text. 062b adds the door row (open, closes after this service, closed) and the build mode row here; until then the app has
    /// only the channels (spec 059: "the Services app ships without the door and build rows").
    /// </summary>
    public static class ServicesAppRules
    {
        public const int CounterRow = 0;
        public const string CounterName = "Counter";

        /// <summary>The 052 channels of stage 3, in the order the rows show them.</summary>
        private static readonly string[] LaterChannels = { "delivery", "custom_orders", "tables", "buffet" };

        /// <summary>The refusal and row text of a stage 3 channel.</summary>
        public static readonly string ComesLaterText = "Comes with stage " + (int)ClimbStage.ServiceMix;

        /// <summary>The rows' names: "Counter", then the stage 3 channels ("Delivery", "Custom orders", "Tables", "Buffet").</summary>
        public static IReadOnlyList<string> Names(ClimbCatalog climb)
        {
            var names = new List<string> { CounterName };
            climb ??= FoodCornerRules.DefaultClimb;
            foreach (string id in LaterChannels)
            {
                ChannelDef def = null;
                foreach (ChannelDef c in climb.Channels) if (c.Id == id) def = c;
                if (def != null) names.Add(Sentence(def.Name));
            }
            return names;
        }

        /// <summary>"Custom Orders" to "Custom orders".</summary>
        public static string Sentence(string name) =>
            string.IsNullOrEmpty(name) ? string.Empty : name.Substring(0, 1).ToUpperInvariant() + name.Substring(1).ToLowerInvariant();

        /// <summary>Row <paramref name="row"/>'s text: "Counter: open", "Delivery: comes with stage 3" (empty past the last row).</summary>
        public static string RowText(int row, ClimbCatalog climb)
        {
            IReadOnlyList<string> names = Names(climb);
            if (row < 0 || row >= names.Count) return string.Empty;
            if (row == CounterRow) return CounterName + ": open";
            return names[row] + ": " + ComesLaterText.ToLowerInvariant();
        }

        /// <summary>A press of row <paramref name="row"/>: the counter stays on (AlwaysOn); a stage 3 channel ComesLater; anything else is no option.</summary>
        public static CookOutcome Decide(int row, int action, ClimbCatalog climb)
        {
            int count = Names(climb).Count;
            if (row < 0 || row >= count || action != TerminalOptions.Press) return CookOutcome.InvalidOption;
            return row == CounterRow ? CookOutcome.AlwaysOn : CookOutcome.ComesLater;
        }

        /// <summary>The prompt's words for a row: "the counter stays open", "delivery comes with stage 3".</summary>
        public static string PressWords(int row, ClimbCatalog climb)
        {
            IReadOnlyList<string> names = Names(climb);
            if (row < 0 || row >= names.Count) return null;
            if (row == CounterRow) return "the counter stays open";
            return names[row].ToLowerInvariant() + " " + ComesLaterText.ToLowerInvariant();
        }

        /// <summary>The rows this app has.</summary>
        public static int RowCount(ClimbCatalog climb) => Math.Max(1, Names(climb).Count);
    }
}
