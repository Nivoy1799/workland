using System.Collections.Generic;
using Worktown.Sim.Cooking;
using Worktown.Sim.Core;
using Worktown.Sim.Economy;

namespace Worktown.Game.Economy
{
    /// <summary>
    /// Server-side town economy for the demo (spec 009): one spec 008 <see cref="EconomyEngine"/>
    /// plus the current immutable <see cref="EconomyState"/>. Pure, no networking. Every number
    /// (prices, payouts, demand) comes from the engine; this class only keeps the state and
    /// hands out unique sale instance ids.
    /// </summary>
    public sealed class TownEconomy
    {
        private readonly EconomyEngine _engine;
        private readonly EconomyCatalog _catalog;
        private readonly IRandom _random;
        private EconomyState _state;
        private long _nextInstanceId = 1;

        public TownEconomy(EconomyCatalog catalog, ulong seed)
        {
            _catalog = catalog;
            _engine = new EconomyEngine(catalog);
            _random = new SeededRandom(seed);
            _state = _engine.Initial();
        }

        /// <summary>Default catalog, ticked to the start hour so demand exists.</summary>
        public static TownEconomy CreateDemo()
        {
            var economy = new TownEconomy(EconomyCatalog.CreateDefault(), EconomyRules.Seed);
            for (int i = 0; i < EconomyRules.StartHour; i++) economy.TickHour();
            return economy;
        }

        public long Tick => _state.Tick;
        public IReadOnlyList<MarketState> Markets => _state.Markets;

        /// <summary>Opens the account and grants the starting money. False if it already exists
        /// (a reconnect keeps its balance, no second grant).</summary>
        public bool TryOpenAccount(string accountId, long grant)
        {
            LedgerResult opened = _engine.OpenAccount(_state, accountId);
            if (!opened.Ok) return false;
            var accounts = new List<Account>(opened.State.Accounts.Count);
            foreach (Account account in opened.State.Accounts)
                accounts.Add(account.Id == accountId ? account with { Balance = grant } : account);
            _state = opened.State with { Accounts = accounts };
            return true;
        }

        /// <summary>Balance in whole dollars, or -1 for an unknown account.</summary>
        public long BalanceOf(string accountId)
        {
            foreach (Account account in _state.Accounts)
                if (account.Id == accountId) return account.Balance;
            return -1;
        }

        public bool TryGetGrocerPrice(string baseId, out long price)
        {
            return _catalog.TryGetGrocerPrice(baseId, out price);
        }

        /// <summary>Buys one base item from the NPC grocer. On failure nothing changes.</summary>
        public EconomyError TryBuyBase(string accountId, string baseId, out long cost)
        {
            PurchaseResult result = _engine.TryBuyBase(_state, accountId, baseId, 1);
            cost = result.Cost;
            if (result.Ok) _state = result.State;
            return result.Error;
        }

        /// <summary>Sells a plated item. On failure nothing changes.</summary>
        public SaleResult TrySell(string accountId, Item platedItem)
        {
            SaleResult result = _engine.TrySellItem(_state, accountId, _nextInstanceId, platedItem);
            if (result.Ok)
            {
                _state = result.State;
                _nextInstanceId++;
            }
            return result;
        }

        /// <summary>One in-game hour: demand refills, prices move (spec 008 tick).</summary>
        public void TickHour()
        {
            _state = _engine.Tick(_state, new TickInput(_random, EconomyRules.Population));
        }

        public bool TryGetMarket(string marketId, out MarketState market)
        {
            foreach (MarketState m in _state.Markets)
            {
                if (m.MarketId != marketId) continue;
                market = m;
                return true;
            }
            market = null;
            return false;
        }
    }
}
