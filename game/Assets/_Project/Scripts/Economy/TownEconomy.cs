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
        public static TownEconomy CreateDemo() => CreateGame(EconomyRules.Seed);

        /// <summary>Spec 053: a new game's economy (Initial(), seeded with <paramref name="seed"/>), ticked to the start hour.</summary>
        public static TownEconomy CreateGame(ulong seed)
        {
            var economy = new TownEconomy(EconomyCatalog.CreateDefault(), seed);
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

        /// <summary>Spec 031b: buys <paramref name="percent"/> % of one base item (a squeezed portion). On failure nothing changes.</summary>
        public EconomyError TryBuyPortion(string accountId, string baseId, int percent, out long cost)
        {
            PurchaseResult result = _engine.TryBuyPortion(_state, accountId, baseId, percent);
            cost = result.Cost;
            if (result.Ok) _state = result.State;
            return result.Error;
        }

        /// <summary>
        /// Spec 033: one pour at the drink dispenser, charged once: <paramref name="percent"/> % of the drink and the cup when
        /// <paramref name="withCup"/>. On failure nothing changes.
        /// </summary>
        public EconomyError TryBuyFill(string accountId, string drinkId, int percent, bool withCup, out long cost)
        {
            PurchaseResult result = _engine.TryBuyFill(_state, accountId, drinkId, percent, withCup);
            cost = result.Cost;
            if (result.Ok) _state = result.State;
            return result.Error;
        }

        /// <summary>Spec 034: the catalog (the supplier's offers and the wholesaler's cap read it).</summary>
        public EconomyCatalog Catalog => _catalog;

        /// <summary>Spec 034: pays <paramref name="packs"/> packs of <paramref name="offer"/> to the supplier, once. On failure nothing changes.</summary>
        public EconomyError TryBuySupply(string accountId, SupplierOffer offer, int packs, out long cost)
        {
            PurchaseResult result = _engine.TryBuySupply(_state, accountId, offer, packs);
            cost = result.Cost;
            if (result.Ok) _state = result.State;
            return result.Error;
        }

        /// <summary>Spec 034, the refund backstop: <paramref name="amount"/> paid to the grocer or supplier goes back. On failure nothing changes.</summary>
        public EconomyError Refund(string accountId, long amount)
        {
            LedgerResult result = _engine.Refund(_state, accountId, amount);
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

        /// <summary>
        /// Spec 032: a dish handed to a waiting guest. The sim sells it as always (demand, price, byproducts); a sale to NPC demand
        /// credits nothing now (the guest pays at the register, <see cref="TryPayGuest"/>) and the result's <c>Payout</c> is the
        /// sim's own payout, the base of the bill. A wholesaler sale is paid at once as before. On failure nothing changes.
        /// </summary>
        public SaleResult TrySellForGuest(string accountId, Item platedItem)
        {
            SaleResult result = TrySell(accountId, platedItem);
            if (!result.Ok || result.Channel != SaleChannel.NpcDemand) return result;
            AdjustBalance(accountId, -result.Payout);
            return result with { State = _state };
        }

        /// <summary>
        /// Spec 032: the register pays <paramref name="amount"/> to the account (what the guest paid: the bill, the tip, less a wrong
        /// change). Capped at the balance limit; returns what was paid; false (nothing paid) for an unknown account.
        /// </summary>
        public bool TryPayGuest(string accountId, long amount, out long paid)
        {
            paid = 0;
            if (BalanceOf(accountId) < 0) return false;
            paid = AdjustBalance(accountId, System.Math.Max(0L, amount));
            return true;
        }

        /// <summary>
        /// Spec 053: a fee (the food corner's pitch fee) leaves the account: up to its balance (the wallet is clamped at 0). Returns what
        /// was paid (0 or more); 0 for an unknown account or no fee.
        /// </summary>
        public long PayFee(string accountId, long amount)
        {
            if (amount <= 0 || BalanceOf(accountId) < 0) return 0;
            return -AdjustBalance(accountId, -amount);
        }

        /// <summary>Adds <paramref name="delta"/> to the account (clamped to 0..MaxBalance) and to the paid-to-players total; the change made.</summary>
        private long AdjustBalance(string accountId, long delta)
        {
            long made = 0;
            var accounts = new List<Account>(_state.Accounts.Count);
            foreach (Account account in _state.Accounts)
            {
                if (account.Id != accountId) { accounts.Add(account); continue; }
                long balance = System.Math.Max(0L, System.Math.Min(EconomyLimits.MaxBalance, account.Balance + delta));
                made = balance - account.Balance;
                accounts.Add(account with { Balance = balance });
            }
            long paidToPlayers = System.Math.Max(0L, _state.Totals.PaidToPlayers + made);
            _state = _state with { Accounts = accounts, Totals = _state.Totals with { PaidToPlayers = paidToPlayers } };
            return made;
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
