using System;
using System.Collections.Generic;
using Worktown.Game.Cooking;
using Worktown.Game.Npc;
using Worktown.Sim.Climb;
using Worktown.Sim.Economy;

namespace Worktown.Game.Shop
{
    /// <summary>
    /// Spec 037, server, pure: the menu board's state: one price per MenuPrices line (cents, starting at its reference) and the selected
    /// line. A click (<see cref="Use"/>) moves the selection or the selected price one step (Sim.Economy.MenuPricing); what guests pay
    /// (<see cref="BillOf"/>), how many come (<see cref="DemandPermille"/>, per market, into the economy's tick) and how much they tip
    /// (<see cref="TipPermilleOf"/>) all read these prices through spec 052's guest choice. No draws: the same clicks give the same board.
    /// </summary>
    public sealed class MenuBoardDesk
    {
        private readonly long[] _prices = new long[MenuPrices.Lines.Count];
        private readonly ChoiceWeights _weights;

        public MenuBoardDesk(ChoiceWeights weights)
        {
            _weights = weights ?? throw new ArgumentNullException(nameof(weights));
            Reset();
        }

        /// <summary>The selected MenuPrices line.</summary>
        public int Selected { get; private set; }

        /// <summary>Raised after a price or the selection changed (the synced board and the economy's demand follow).</summary>
        public event Action Changed;

        /// <summary>Every price back at its reference, the first line selected (a new game).</summary>
        public void Reset()
        {
            for (int i = 0; i < _prices.Length; i++) _prices[i] = MenuPrices.Lines[i].Reference;
            Selected = 0;
            Changed?.Invoke();
        }

        /// <summary>The price (cents) of MenuPrices line <paramref name="line"/>; 0 for no line.</summary>
        public long PriceOf(int line) => line >= 0 && line < _prices.Length ? _prices[line] : 0;

        /// <summary>Every line's price (cents), in MenuPrices order.</summary>
        public IReadOnlyList<long> Prices => Array.AsReadOnly((long[])_prices.Clone());

        /// <summary>
        /// One click of button <paramref name="option"/> at <paramref name="stage"/>: the previous or next visible line (wrapping), or the
        /// selected line's price one step down or up (kept within its bounds). A selection the stage does not show snaps to its first line.
        /// </summary>
        public CookOutcome Use(int option, byte stage)
        {
            if (option < 0 || option >= MenuBoardRules.OptionCount) return CookOutcome.InvalidOption;
            IReadOnlyList<int> visible = MenuBoardRules.VisibleLines(stage);
            if (visible.Count == 0) return CookOutcome.InvalidOption;
            int at = MenuBoardRules.IndexIn(visible, Selected);
            if (at < 0) at = 0;
            switch (option)
            {
                case MenuBoardRules.PreviousOption: at = (at + visible.Count - 1) % visible.Count; break;
                case MenuBoardRules.NextOption: at = (at + 1) % visible.Count; break;
                default:
                {
                    int line = visible[at];
                    int steps = option == MenuBoardRules.DearerOption ? 1 : -1;
                    _prices[line] = MenuPricing.Step(MenuPrices.Lines[line].Reference, _prices[line], steps);
                    break;
                }
            }
            Selected = visible[at];
            Changed?.Invoke();
            return CookOutcome.Ok;
        }

        /// <summary>
        /// What a guest's order line costs at the board: the price of its dish, or (<paramref name="drink"/>) of the drink it ordered;
        /// <paramref name="fallback"/> when the board has no such line.
        /// </summary>
        public long BillOf(byte dish, bool drink, string drinkId, long fallback)
        {
            int line = drink ? MenuPrices.LineOfDrink(drinkId) : MenuPrices.LineOfDish(dish);
            return line >= 0 ? _prices[line] : fallback;
        }

        /// <summary>The tip's share (permille) for an order of <paramref name="dish"/> at its board price (1000 at or below the reference).</summary>
        public int TipPermilleOf(byte dish)
        {
            int line = MenuPrices.LineOfDish(dish);
            return line < 0 ? 1000 : MenuPricing.TipPermille(_weights, _prices[line], MenuPrices.Lines[line].Reference);
        }

        /// <summary>
        /// The NPC demand of each market at the board's prices (permille, 1000 = as at the references): the mean over the market's lines
        /// on sale at <paramref name="stage"/>. Markets without a visible line are left out (unchanged).
        /// </summary>
        public IReadOnlyDictionary<string, int> DemandPermille(byte stage)
        {
            var byMarket = new Dictionary<string, List<(long, long)>>(StringComparer.Ordinal);
            foreach (int line in MenuBoardRules.VisibleLines(stage))
            {
                MenuPrices.Line l = MenuPrices.Lines[line];
                if (!byMarket.TryGetValue(l.Market, out List<(long, long)> list)) byMarket[l.Market] = list = new List<(long, long)>();
                list.Add((_prices[line], l.Reference));
            }
            var demand = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, List<(long, long)>> kv in byMarket)
                demand[kv.Key] = MenuPricing.MarketDemandPermille(_weights, kv.Value);
            return demand;
        }

        /// <summary>The save's lines: every price that differs from its reference, as (line id, cents).</summary>
        public IReadOnlyList<(string Id, long Cents)> Capture()
        {
            var list = new List<(string, long)>();
            for (int i = 0; i < _prices.Length; i++)
                if (_prices[i] != MenuPrices.Lines[i].Reference) list.Add((MenuPrices.Lines[i].Id, _prices[i]));
            return list;
        }

        /// <summary>
        /// A loaded save's prices: every line at its reference, then each known id at its saved price, put on the step grid and within
        /// bounds (MenuPricing.Normalize). Unknown ids are ignored.
        /// </summary>
        public void Restore(IEnumerable<(string Id, long Cents)> saved)
        {
            for (int i = 0; i < _prices.Length; i++) _prices[i] = MenuPrices.Lines[i].Reference;
            Selected = 0;
            if (saved != null)
                foreach ((string id, long cents) in saved)
                {
                    int line = MenuPrices.LineOfId(id);
                    if (line >= 0) _prices[line] = MenuPricing.Normalize(MenuPrices.Lines[line].Reference, cents);
                }
            Changed?.Invoke();
        }
    }
}
