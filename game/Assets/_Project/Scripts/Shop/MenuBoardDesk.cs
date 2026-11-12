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
        public CookOutcome Use(int option, byte stage) => Use(option, stage, -1);

        /// <summary>Spec 059: the same, by player <paramref name="playerSlot"/> (a price step goes through <see cref="ServerStepPrice"/>).</summary>
        public CookOutcome Use(int option, byte stage, int playerSlot)
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
                    Selected = visible[at];
                    return ServerStepPrice(visible[at], option == MenuBoardRules.DearerOption ? 1 : -1, playerSlot);
            }
            Selected = visible[at];
            Changed?.Invoke();
            return CookOutcome.Ok;
        }

        /// <summary>
        /// Spec 059: the one server method that moves a price, used by the menu board's buttons and the office terminal's Menu app: line
        /// <paramref name="line"/>'s price <paramref name="delta"/> steps of $0.50 (kept within its bounds, MenuPricing.Step), by player
        /// <paramref name="playerSlot"/> (-1 unknown). A guest who joined before keeps its quote (037 fix 1). The caller checked that the stage
        /// sells the line. Raises <see cref="PriceStepped"/> when the price moved.
        /// </summary>
        public CookOutcome ServerStepPrice(int line, int delta, int playerSlot)
        {
            if (line < 0 || line >= _prices.Length || delta == 0) return CookOutcome.InvalidOption;
            long before = _prices[line];
            _prices[line] = MenuPricing.Step(MenuPrices.Lines[line].Reference, _prices[line], delta);
            Changed?.Invoke();
            if (_prices[line] != before)
            {
                try { PriceStepped?.Invoke(line, _prices[line], playerSlot); }
                catch (Exception e) { UnityEngine.Debug.LogException(e); }
            }
            return CookOutcome.Ok;
        }

        /// <summary>Spec 059: a price moved: the line, its new price (cents) and the player (-1 unknown); the office terminal's log follows.</summary>
        public event Action<int, long, int> PriceStepped;

        /// <summary>
        /// What a guest's order line costs at the board: the price of its dish, or (<paramref name="drink"/>) of the drink it ordered;
        /// <paramref name="fallback"/> when the board has no such line.
        /// </summary>
        public long BillOf(byte dish, bool drink, string drinkId, long fallback)
        {
            int line = drink ? MenuPrices.LineOfDrink(drinkId) : MenuPrices.LineOfDish(dish);
            return line >= 0 ? _prices[line] : fallback;
        }

        /// <summary>
        /// 037 review: what a guest joining now will pay: the board's price of its dish and of its drink (-1: no such line, or no drink)
        /// and its tip share. The guest keeps this quote; a later click on the board does not change it.
        /// </summary>
        public BoardQuote Quote(byte dish, string drinkId) => new BoardQuote(BillOf(dish, false, null, -1),
            drinkId != null ? BillOf(Menu.None, true, drinkId, -1) : -1, TipPermilleOf(dish));

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

    /// <summary>037 review: a guest's prices (cents; -1 none) and tip share (permille) from the menu board as it joined.</summary>
    public readonly struct BoardQuote
    {
        public BoardQuote(long dishPrice, long drinkPrice, int tipPermille)
        {
            DishPrice = dishPrice;
            DrinkPrice = drinkPrice;
            TipPermille = tipPermille;
        }

        public long DishPrice { get; }
        public long DrinkPrice { get; }
        public int TipPermille { get; }

        /// <summary>The quote's price of an order line: the drink's for a drink line, else the dish's.</summary>
        public long PriceOf(bool drink) => drink ? DrinkPrice : DishPrice;
    }
}
