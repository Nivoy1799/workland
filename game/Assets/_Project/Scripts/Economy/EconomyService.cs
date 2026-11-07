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

        /// <summary>Server: sell a plated dish to NPC demand or the wholesaler.</summary>
        public SaleResult TrySell(NetworkObject player, Item plated)
        {
            SaleResult result = _economy.TrySell(EconomyRules.AccountId(player.OwnerId), plated);
            PublishWallet(player);
            if (result.Ok) PublishBoard();
            return result;
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
