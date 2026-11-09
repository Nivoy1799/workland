using System.Collections.Generic;
using FishNet.Component.Spawning;
using FishNet.Managing;
using FishNet.Object;
using UnityEngine;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;

namespace Worktown.Game.Economy
{
    /// <summary>
    /// Server-side economy for the demo (spec 009). Opens an account per player on spawn, runs the
    /// in-game clock (one economy tick per <see cref="EconomyRules.SecondsPerHour"/>) and publishes
    /// wallets and the market board. Does nothing on pure clients.
    /// </summary>
    public sealed class EconomyService : MonoBehaviour
    {
        private static readonly string[] BoardMarkets = { "meal", "snack", "drink" };

        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private MarketBoard _board;

        private readonly TownEconomy _economy = TownEconomy.CreateDemo();
        private float _secondsIntoHour;

        /// <summary>Spec 014: raised on the server after every in-game hour (demand changed).</summary>
        public event System.Action HourTicked;

        /// <summary>Spec 014: open NPC orders of a market right now (0 if unknown).</summary>
        public int OpenDemand(string marketId)
        {
            return _economy.TryGetMarket(marketId, out MarketState market) ? market.OpenDemand : 0;
        }

        /// <summary>Spec 022: the in-game clock (one tick per hour, day 1 starts at tick 0).</summary>
        public long Tick => _economy.Tick;

        /// <summary>
        /// Spec 022, server: moves the clock forward by <paramref name="hours"/> economy ticks at once (the night between
        /// two shifts), then publishes the board and raises <see cref="HourTicked"/> once.
        /// </summary>
        public void SkipHours(int hours)
        {
            if (!_networkManager.IsServerStarted || hours <= 0) return;
            for (int i = 0; i < hours; i++) _economy.TickHour();
            _secondsIntoHour = 0f;
            PublishBoard();
            Debug.Log("[Economy] skipped " + hours + " h to " + EconomyRules.ClockText(_economy.Tick) + " " + PriceText());
            try { HourTicked?.Invoke(); }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        private void Start()
        {
            PlayerSpawner spawner = _networkManager.GetComponent<PlayerSpawner>();
            if (spawner != null) spawner.OnSpawned += OnPlayerSpawned;
        }

        private void OnDestroy()
        {
            if (_networkManager == null) return;
            PlayerSpawner spawner = _networkManager.GetComponent<PlayerSpawner>();
            if (spawner != null) spawner.OnSpawned -= OnPlayerSpawned;
        }

        private void Update()
        {
            if (!_networkManager.IsServerStarted) return;
            _secondsIntoHour += Time.deltaTime;
            if (_secondsIntoHour < EconomyRules.SecondsPerHour)
            {
                if (_board != null && _board.IsSpawned && _board.Clock.Value.Length == 0) PublishBoard();
                return;
            }
            _secondsIntoHour -= EconomyRules.SecondsPerHour;
            _economy.TickHour();
            PublishBoard();
            Debug.Log("[Economy] " + EconomyRules.ClockText(_economy.Tick) + " " + PriceText());
            // Last, and isolated: listeners (spec 014 customers) must not break the clock.
            try { HourTicked?.Invoke(); }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        private void OnPlayerSpawned(NetworkObject player)
        {
            string account = EconomyRules.AccountId(player.OwnerId);
            if (_economy.TryOpenAccount(account, EconomyRules.StartingGrant))
                Debug.Log("[Economy] player " + player.OwnerId + " joined with " + EconomyRules.Money(EconomyRules.StartingGrant));
            PublishWallet(player);
        }

        /// <summary>Server: charge the grocer price for one base item.</summary>
        public EconomyError TryBuy(NetworkObject player, string baseId, out long cost)
        {
            EconomyError error = _economy.TryBuyBase(EconomyRules.AccountId(player.OwnerId), baseId, out cost);
            PublishWallet(player);
            return error;
        }

        /// <summary>Spec 031b, server: charge <paramref name="percent"/> % of one base item's grocer price (a squeezed portion).</summary>
        public EconomyError TryBuyPortion(NetworkObject player, string baseId, int percent, out long cost)
        {
            EconomyError error = _economy.TryBuyPortion(EconomyRules.AccountId(player.OwnerId), baseId, percent, out cost);
            PublishWallet(player);
            return error;
        }

        /// <summary>031b review, server: whether the player could pay <paramref name="percent"/> % of one base item now (no charge).</summary>
        public bool CanAffordPortion(NetworkObject player, string baseId, int percent)
        {
            if (player == null || !_economy.TryGetGrocerPrice(baseId, out long price)) return false;
            return _economy.BalanceOf(EconomyRules.AccountId(player.OwnerId)) >= PriceRules.PortionCost(price, percent);
        }

        /// <summary>Spec 033, server: charge one pour at the drink dispenser (the drink's share, and the cup when it was empty), once.</summary>
        public EconomyError TryBuyFill(NetworkObject player, string drinkId, int percent, bool withCup, out long cost)
        {
            EconomyError error = _economy.TryBuyFill(EconomyRules.AccountId(player.OwnerId), drinkId, percent, withCup, out cost);
            PublishWallet(player);
            return error;
        }

        /// <summary>Spec 033, server: whether the player could pay such a pour now (no charge).</summary>
        public bool CanAffordFill(NetworkObject player, string drinkId, int percent, bool withCup)
        {
            if (player == null || !_economy.TryGetGrocerPrice(drinkId, out long drink)) return false;
            long cup = 0;
            if (withCup && !_economy.TryGetGrocerPrice(Worktown.Sim.Cooking.Drinks.CupId, out cup)) return false;
            return _economy.BalanceOf(EconomyRules.AccountId(player.OwnerId)) >= PriceRules.FillCost(cup, drink, percent, withCup);
        }

        /// <summary>Spec 034: the economy's catalog (the supplier's offers).</summary>
        public EconomyCatalog Catalog => _economy.Catalog;

        /// <summary>
        /// Spec 034, server: the game minute (one economy tick is an hour; the minutes run with the real seconds into it), for the
        /// supplier's delivery times.
        /// </summary>
        public long GameMinute => _economy.Tick * 60L + (long)System.Math.Floor(_secondsIntoHour * 60f / EconomyRules.SecondsPerHour);

        /// <summary>Spec 034, server: pays an order from the supplier, once (the bulk price).</summary>
        public EconomyError TryBuySupply(NetworkObject player, SupplierOffer offer, int packs, out long cost)
        {
            EconomyError error = _economy.TryBuySupply(EconomyRules.AccountId(player.OwnerId), offer, packs, out cost);
            PublishWallet(player);
            return error;
        }

        /// <summary>Spec 034, server: whether the player could pay <paramref name="cost"/> now (no charge).</summary>
        public bool CanAfford(NetworkObject player, long cost) =>
            player != null && _economy.BalanceOf(EconomyRules.AccountId(player.OwnerId)) >= cost;

        /// <summary>
        /// 034 review: the game this economy is (the object identity of its state's owner): a new economy is a new game. The stock and
        /// other per-game state tie their start to it, not to a server start in the same process.
        /// </summary>
        public object Game => _economy;

        /// <summary>
        /// 034 review, server: a refund to account <paramref name="accountId"/> (its player may be gone, e.g. a supply order refunded when
        /// the server stops); bounded like <see cref="Refund"/> by what the account paid.
        /// </summary>
        public EconomyError RefundAccount(string accountId, long amount) => _economy.Refund(accountId, amount);

        /// <summary>Spec 034, server, the refund backstop: <paramref name="amount"/> goes back to the player's account.</summary>
        public EconomyError Refund(NetworkObject player, long amount)
        {
            EconomyError error = _economy.Refund(EconomyRules.AccountId(player.OwnerId), amount);
            PublishWallet(player);
            return error;
        }

        /// <summary>Server: sell a plated dish to NPC demand or the wholesaler.</summary>
        public SaleResult TrySell(NetworkObject player, Item plated)
        {
            SaleResult result = _economy.TrySell(EconomyRules.AccountId(player.OwnerId), plated);
            PublishWallet(player);
            if (result.Ok) PublishBoard();
            return result;
        }

        /// <summary>Spec 032, server: a dish handed to a waiting guest (paid later at the register; see TownEconomy.TrySellForGuest).</summary>
        public SaleResult TrySellForGuest(NetworkObject player, Item plated)
        {
            SaleResult result = _economy.TrySellForGuest(EconomyRules.AccountId(player.OwnerId), plated);
            PublishWallet(player);
            if (result.Ok) PublishBoard();
            return result;
        }

        /// <summary>
        /// Spec 032, server: the register pays player <paramref name="ownerId"/> (its wallet is published when its object is given).
        /// False for an unknown account.
        /// </summary>
        public bool TryPayGuest(int ownerId, NetworkObject player, long amount, out long paid)
        {
            bool ok = _economy.TryPayGuest(EconomyRules.AccountId(ownerId), amount, out paid);
            if (ok && player != null) PublishWallet(player);
            return ok;
        }

        private void PublishWallet(NetworkObject player)
        {
            if (player != null && player.TryGetComponent(out Wallet wallet))
                wallet.ServerSet(_economy.BalanceOf(EconomyRules.AccountId(player.OwnerId)));
        }

        private void PublishBoard()
        {
            if (_board == null || !_board.IsSpawned) return;
            _board.ServerSet(EconomyRules.ClockText(_economy.Tick), PriceText());
        }

        private string PriceText()
        {
            var parts = new List<string>(BoardMarkets.Length);
            foreach (string id in BoardMarkets)
                if (_economy.TryGetMarket(id, out MarketState m))
                    parts.Add(id + " " + EconomyRules.Money(m.Price) + " (" + m.OpenDemand + " orders)");
            return string.Join(", ", parts);
        }
    }
}
