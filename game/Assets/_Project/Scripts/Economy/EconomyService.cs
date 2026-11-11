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
    /// Server-side economy for the demo (spec 009). Runs the in-game clock (one economy tick per
    /// <see cref="EconomyRules.SecondsPerHour"/>) and publishes wallets and the market board. Does nothing on pure clients.
    /// Spec 053 (pillar 2, the money belongs to the group): one shop wallet. Every player's money step goes to the one shop account
    /// (<see cref="EconomyRules.ShopAccount"/>), every change is published to every player's Wallet, and the account opens once per
    /// game: <see cref="StartGame"/> (a new shop with its starting money, or a loaded save's balance, no grant) or, in the open kitchen,
    /// with <see cref="EconomyRules.StartingGrant"/> when the first player spawns. A joining or rejoining player gets nothing.
    /// </summary>
    public sealed class EconomyService : MonoBehaviour
    {
        private static readonly string[] BoardMarkets = { "meal", "snack", "drink" };

        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private MarketBoard _board;
        [Tooltip("Spec 053 review: the one synced shop balance (a global scene object).")]
        [SerializeField] private ShopLedger _ledger;

        private TownEconomy _economy = TownEconomy.CreateDemo();
        private float _secondsIntoHour;
        // Spec 053: the shown day of a loaded game (the economy itself starts fresh at day 1), every player's wallet, and what the shop
        // earned and spent in this game (for the save).
        private long _tickOffset;
        private long _earned;
        private long _spent;

        /// <summary>Spec 014: raised on the server after every in-game hour (demand changed).</summary>
        public event System.Action HourTicked;

        /// <summary>Spec 014: open NPC orders of a market right now (0 if unknown).</summary>
        public int OpenDemand(string marketId)
        {
            return _economy.TryGetMarket(marketId, out MarketState market) ? market.OpenDemand : 0;
        }

        /// <summary>Spec 022: the in-game clock (one tick per hour, day 1 starts at tick 0). Spec 053: with a loaded game's day offset.</summary>
        public long Tick => _economy.Tick + _tickOffset;

        /// <summary>Spec 053, server: the one shop balance (-1 before the shop opened).</summary>
        public long ShopBalance => _economy.BalanceOf(EconomyRules.ShopAccount);

        /// <summary>Spec 053: what the shop took in (sales, guests' payments) and paid out (stock, fees; refunds count back) in this game.</summary>
        public long Earned => _earned;
        public long Spent => _spent;

        /// <summary>
        /// Spec 053, server: a new game (New shop, or Continue from a save): a fresh economy (seeded with <paramref name="seed"/>, ticked to
        /// 08:00), shown as day <paramref name="day"/>, and the shop account opened once with <paramref name="opening"/> (the starting money,
        /// or the saved balance: no grant). <paramref name="earned"/> and <paramref name="spent"/> carry the save's totals on. The stock and
        /// the cleaning see a new game (<see cref="Game"/>) and start over; the save service then applies the saved stock and hygiene.
        /// </summary>
        public void StartGame(ulong seed, long day, long opening, long earned = 0, long spent = 0)
        {
            _economy = TownEconomy.CreateGame(seed);
            _tickOffset = EconomyRules.DayOffsetTicks(day);
            _secondsIntoHour = 0f;
            _economy.TryOpenAccount(EconomyRules.ShopAccount, System.Math.Max(0L, System.Math.Min(EconomyLimits.MaxBalance, opening)));
            _earned = System.Math.Max(0L, earned);
            _spent = System.Math.Max(0L, spent);
            Debug.Log("[Economy] a new game: day " + System.Math.Max(1L, day) + ", the shop opens with " + EconomyRules.Money(ShopBalance));
            PublishWallets();
            PublishBoard();
        }

        /// <summary>Spec 053, server: the shop pays a fee (the food corner's pitch fee), up to its balance. Returns what was paid.</summary>
        public long PayFee(long amount)
        {
            long before = ShopBalance;
            long paid = _economy.PayFee(EconomyRules.ShopAccount, amount);
            Track(before, false);
            PublishWallets();
            return paid;
        }

        /// <summary>Spec 053, server: whether the shop could pay <paramref name="cost"/> now (no charge).</summary>
        public bool CanAffordShop(long cost) => ShopBalance >= cost;

        /// <summary>Spec 053, server: the shop pays a supply nobody ordered (the food corner's fixed drop), once, at the bulk price.</summary>
        public EconomyError TryBuySupplyShop(SupplierOffer offer, int packs, out long cost)
        {
            long before = ShopBalance;
            EconomyError error = _economy.TryBuySupply(EconomyRules.ShopAccount, offer, packs, out cost);
            Track(before, false);
            PublishWallets();
            return error;
        }

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
            Debug.Log("[Economy] skipped " + hours + " h to " + EconomyRules.ClockText(Tick) + " " + PriceText());
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
            Debug.Log("[Economy] " + EconomyRules.ClockText(Tick) + " " + PriceText());
            // Last, and isolated: listeners (spec 014 customers) must not break the clock.
            try { HourTicked?.Invoke(); }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        private void OnPlayerSpawned(NetworkObject player)
        {
            // Spec 053: one shop wallet: the open kitchen opens it once with the grant (a game started by StartGame has it open already);
            // a joining or rejoining player adds no money.
            if (_economy.TryOpenAccount(EconomyRules.ShopAccount, EconomyRules.StartingGrant))
                Debug.Log("[Economy] the shop opened with " + EconomyRules.Money(EconomyRules.StartingGrant) + " (the open kitchen)");
            if (player != null) Debug.Log("[Economy] player " + player.OwnerId + " joined; the shop has " + EconomyRules.Money(ShopBalance));
            PublishWallets();
        }

        /// <summary>Spec 053: a change of the shop balance since <paramref name="before"/> counts as earned or spent (a refund counts back).</summary>
        private void Track(long before, bool refund)
        {
            long after = ShopBalance;
            if (before < 0 || after < 0 || after == before) return;
            long delta = after - before;
            if (refund || delta < 0) _spent = System.Math.Max(0L, _spent - delta);
            else _earned += delta;
        }

        /// <summary>Server: charge the grocer price for one base item.</summary>
        public EconomyError TryBuy(NetworkObject player, string baseId, out long cost)
        {
            long before = ShopBalance;
            EconomyError error = _economy.TryBuyBase(EconomyRules.AccountId(player.OwnerId), baseId, out cost);
            Track(before, false);
            PublishWallets();
            return error;
        }

        /// <summary>Spec 031b, server: charge <paramref name="percent"/> % of one base item's grocer price (a squeezed portion).</summary>
        public EconomyError TryBuyPortion(NetworkObject player, string baseId, int percent, out long cost)
        {
            long before = ShopBalance;
            EconomyError error = _economy.TryBuyPortion(EconomyRules.AccountId(player.OwnerId), baseId, percent, out cost);
            Track(before, false);
            PublishWallets();
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
            long before = ShopBalance;
            EconomyError error = _economy.TryBuyFill(EconomyRules.AccountId(player.OwnerId), drinkId, percent, withCup, out cost);
            Track(before, false);
            PublishWallets();
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
        public long GameMinute => Tick * 60L + (long)System.Math.Floor(_secondsIntoHour * 60f / EconomyRules.SecondsPerHour);

        /// <summary>Spec 034, server: pays an order from the supplier, once (the bulk price).</summary>
        public EconomyError TryBuySupply(NetworkObject player, SupplierOffer offer, int packs, out long cost)
        {
            long before = ShopBalance;
            EconomyError error = _economy.TryBuySupply(EconomyRules.AccountId(player.OwnerId), offer, packs, out cost);
            Track(before, false);
            PublishWallets();
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
        public EconomyError RefundAccount(string accountId, long amount)
        {
            long before = ShopBalance;
            EconomyError error = _economy.Refund(accountId, amount);
            Track(before, true);
            PublishWallets();
            return error;
        }

        /// <summary>Spec 034, server, the refund backstop: <paramref name="amount"/> goes back to the player's account.</summary>
        public EconomyError Refund(NetworkObject player, long amount)
        {
            long before = ShopBalance;
            EconomyError error = _economy.Refund(EconomyRules.AccountId(player.OwnerId), amount);
            Track(before, true);
            PublishWallets();
            return error;
        }

        /// <summary>Server: sell a plated dish to NPC demand or the wholesaler.</summary>
        public SaleResult TrySell(NetworkObject player, Item plated)
        {
            long before = ShopBalance;
            SaleResult result = _economy.TrySell(EconomyRules.AccountId(player.OwnerId), plated);
            Track(before, false);
            PublishWallets();
            if (result.Ok) PublishBoard();
            return result;
        }

        /// <summary>Spec 032, server: a dish handed to a waiting guest (paid later at the register; see TownEconomy.TrySellForGuest).</summary>
        public SaleResult TrySellForGuest(NetworkObject player, Item plated)
        {
            long before = ShopBalance;
            SaleResult result = _economy.TrySellForGuest(EconomyRules.AccountId(player.OwnerId), plated);
            Track(before, false);
            PublishWallets();
            if (result.Ok) PublishBoard();
            return result;
        }

        /// <summary>
        /// Spec 032, server: the register pays player <paramref name="ownerId"/> (its wallet is published when its object is given).
        /// False for an unknown account.
        /// </summary>
        public bool TryPayGuest(int ownerId, NetworkObject player, long amount, out long paid)
        {
            long before = ShopBalance;
            bool ok = _economy.TryPayGuest(EconomyRules.AccountId(ownerId), amount, out paid);
            Track(before, false);
            if (ok) PublishWallets(); // spec 053: every player sees the one shop balance
            return ok;
        }

        /// <summary>
        /// Spec 053: the one shop balance for every player. 053 review (netcode): synced once on the ShopLedger (a global object), not
        /// once per player's Wallet to every observer; each Wallet reads it.
        /// </summary>
        private void PublishWallets()
        {
            if (_ledger == null) _ledger = FindFirstObjectByType<ShopLedger>();
            // Follow-up 2026-11-11 evening: no clamp (the balance never goes below 0, see ShopLedger.ServerSet); -1 is "no account yet".
            long balance = ShopBalance;
            if (_ledger != null && balance >= 0) _ledger.ServerSet(balance);
        }

        private void PublishBoard()
        {
            if (_board == null || !_board.IsSpawned) return;
            _board.ServerSet(EconomyRules.ClockText(Tick), PriceText());
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
