using UnityEngine;
using Worktown.Game.Cooking;

namespace Worktown.Game.Shop
{
    /// <summary>
    /// Spec 034: the assembly board's and the drink dispenser's "wallet" (IAssemblyWallet, IDrinkWallet) through the shop's stock: a
    /// squeeze draws its portion (10 to 30 % of a bottle) from the opened bottle of that sauce (the next one opens from the dry shelf's
    /// bottles), packaging takes one from the rack, a pour draws its share of a cup from the tap's keg (the cup itself came from the cup
    /// stack). Nothing is charged in money: the orders paid for it. A press finds nothing left: OutOfStock. The refund backstop puts
    /// back exactly what the charge took. 034 review: a good the shop does not stock is never free: OutOfStock (every sauce, packaging
    /// and drink has a stock line; a test pins it). A portion or a pour is drawn whole or not at all (no last drops: the wholesaler's
    /// cap counts the bucket's least share). One instance per server step; each charge starts from nothing drawn.
    /// </summary>
    public sealed class StockWallet : IAssemblyWallet, IDrinkWallet
    {
        private readonly StockRoom _room;
        private int _drawn;
        private int _taken;

        public StockWallet(StockRoom room)
        {
            _room = room ?? throw new System.ArgumentNullException(nameof(room));
        }

        public CookOutcome Short => CookOutcome.OutOfStock;

        /// <summary>What the last charge drew (% of one grocer item) or took (units), for the log and the tests.</summary>
        public int Drawn => _drawn;
        public int Taken => _taken;

        private static int LineOf(string id) => StockCatalog.IndexOf(id);

        public CookOutcome Charge(int owner, string grocerId, int percent, out long cost)
        {
            cost = 0;
            _taken = _drawn = 0;
            int line = LineOf(grocerId);
            if (line < 0) return CookOutcome.OutOfStock;
            if (percent >= 100)
            {
                CookOutcome took = _room.Take(line);
                _taken = took == CookOutcome.Ok ? 1 : 0;
                return took;
            }
            return _room.Draw(line, percent, percent, out _drawn);
        }

        public bool CanAfford(int owner, string grocerId, int percent)
        {
            int line = LineOf(grocerId);
            return line >= 0 && (percent >= 100 ? _room.CanTake(line) : _room.CanDraw(line, percent));
        }

        public void Refund(int owner, string grocerId, int percent, long cost)
        {
            int line = LineOf(grocerId);
            if (line < 0) return;
            if (percent >= 100 && _taken > 0) _room.Return(line, _taken);
            else if (_drawn > 0) _room.ReturnPercent(line, _drawn);
            _taken = _drawn = 0;
            Debug.LogWarning("[Supply] put back what a refused step took from the " + grocerId + " stock");
        }

        public CookOutcome Charge(int owner, string drinkId, int percent, bool withCup, out long cost)
        {
            cost = 0;
            _taken = _drawn = 0;
            int line = LineOf(drinkId);
            return line < 0 ? CookOutcome.OutOfStock : _room.Draw(line, percent, percent, out _drawn);
        }

        public bool CanAfford(int owner, string drinkId, int percent, bool withCup)
        {
            int line = LineOf(drinkId);
            return line >= 0 && _room.CanDraw(line, percent);
        }

        public void Refund(int owner, string drinkId, int percent, bool withCup, long cost)
        {
            int line = LineOf(drinkId);
            if (line >= 0 && _drawn > 0) _room.ReturnPercent(line, _drawn);
            _drawn = 0;
            Debug.LogWarning("[Supply] put back the pour a refused step drew from the " + drinkId + " keg");
        }
    }
}
