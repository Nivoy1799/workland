using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using Worktown.Game.Cooking;
using Worktown.Game.Economy;
using Worktown.Game.Net;
using Worktown.Sim.Economy;

namespace Worktown.Game.Shop
{
    /// <summary>
    /// Spec 034, server: the shop's supply. Owns the <see cref="StockRoom"/> (stock, orders, boxes), publishes the counts
    /// (<see cref="StockCounts"/>, one byte per line), takes the terminal's orders (paid once through the economy, with the refund
    /// backstop), and after the delivery delay (game minutes, EconomyService.GameMinute) spawns one box per pack on the delivery
    /// spots at the back door, as spots are free. Unpacking a carried box into a slot is <see cref="Unpack"/>. Lives next to the
    /// NetworkManager; does nothing on pure clients. A new game (the server starting) begins at the starting stock.
    /// </summary>
    public sealed class SupplyService : MonoBehaviour
    {
        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private PickupService _pickups;
        [SerializeField] private EconomyService _economy;
        [SerializeField] private ServeFeed _feed;
        [SerializeField] private StockCounts _counts;
        [Tooltip("The delivery box (a pickup with a SupplyBox); registered in DefaultPrefabObjects.")]
        [SerializeField] private NetworkObject _boxPrefab;
        [Tooltip("Where boxes land at the back door (the landing worktop by the staff door), in order.")]
        [SerializeField] private Transform[] _deliverySpots = new Transform[0];

        /// <summary>Dev builds, the cook tour's host (-tour-orders): the line the tour runs empty starts at this count.</summary>
        public const string TourLine = "tomato";
        public const int TourStart = 1;
        private const float CheckInterval = 0.25f;

        private readonly StockRoom _room = new StockRoom();
        private readonly byte[] _bytes = new byte[StockCatalog.Count];
        private readonly List<SupplyOrder> _arrived = new List<SupplyOrder>();
        private readonly Collider[] _probe = new Collider[16];
        private int _publishedVersion = -1;
        private float _nextCheck;
        private bool _tourStock;

        /// <summary>Server: the stock (KitchenService takes and draws from it).</summary>
        public StockRoom Room => _room;

        private void Start()
        {
            _networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            _tourStock = Worktown.Game.Npc.TourOrders.Requested(System.Environment.GetCommandLineArgs());
            if (_tourStock) StartTourStock();
#endif
        }

        private void OnDestroy()
        {
            if (_networkManager != null && _networkManager.ServerManager != null)
                _networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
        }

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Stopped || _networkManager.ServerManager.IsAnyServerStarted()) return;
            // A new game next time: the starting stock, no orders, no boxes (FishNet despawned them).
            _room.Reset();
            if (_tourStock) StartTourStock();
            _publishedVersion = -1;
        }

        private void StartTourStock()
        {
            int line = StockCatalog.IndexOf(TourLine);
            StockLine l = _room.LineOf(line);
            while (l != null && l.Count > TourStart && _room.Take(line) == CookOutcome.Ok) l = _room.LineOf(line);
            Debug.Log("[Supply] tour stock (-tour-orders): " + TourLine + " starts at " + TourStart);
        }

        private void Update()
        {
            if (!_networkManager.IsServerStarted) return;
            PublishIfChanged();
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + CheckInterval;
            if (_economy != null)
            {
                _room.Deliver(_economy.GameMinute, _arrived);
                foreach (SupplyOrder o in _arrived)
                {
                    int line = StockCatalog.IndexOf(o.GoodId);
                    string text = "Delivery at the back door: " + o.Packs + (o.Packs == 1 ? " box of " : " boxes of ")
                        + StockCatalog.Describe(line, o.UnitsPerPack);
                    Debug.Log("[Supply] order " + o.Id + " arrived: " + text);
                    if (_feed != null) _feed.ServerAdd(text);
                }
            }
            SpawnWaitingBoxes();
            PublishIfChanged();
        }

        /// <summary>Server: sends the counts when the stock changed since the last time.</summary>
        public void PublishIfChanged()
        {
            if (_counts == null || !_counts.IsServerInitialized || _publishedVersion == _room.Version) return;
            _publishedVersion = _room.Version;
            _room.WriteCounts(_bytes);
            _counts.ServerPublish(_bytes, _bytes.Length);
        }

        private void SpawnWaitingBoxes()
        {
            if (_boxPrefab == null || _deliverySpots == null) return;
            while (_room.TryPeekWaiting(out BoxContents box))
            {
                Transform spot = FreeSpot();
                if (spot == null) return; // the rest waits for a free spot
                NetworkObject nob = _networkManager.GetPooledInstantiated(_boxPrefab, true);
                float lift = nob.TryGetComponent(out Pickup pickup) ? pickup.BottomOffset : 0f;
                nob.transform.SetPositionAndRotation(spot.position + Vector3.up * lift, spot.rotation);
                if (nob.TryGetComponent(out SupplyBox supplyBox)) supplyBox.ServerSet(box);
                _networkManager.ServerManager.Spawn(nob);
                _room.ConfirmSpawned(nob.ObjectId);
                Debug.Log("[Supply] a box of " + StockCatalog.Describe(box.Line, box.Units) + " at the back door (" + spot.name + ")");
            }
        }

        private Transform FreeSpot()
        {
            foreach (Transform spot in _deliverySpots)
                if (spot != null && !PickupService.IsSpotOccupied(spot.position, _probe)) return spot;
            return null;
        }

        // ---- the terminal ----

        /// <summary>
        /// Spec 034: a click on a terminal row: <paramref name="option"/> is TerminalRules.OrderOption(line, packs). Checked, paid once
        /// (the bulk price), booked; the feed tells everyone. A refusal charges nothing.
        /// </summary>
        public CookOutcome Order(PickupInteractor player, int option)
        {
            if (player == null || !TerminalRules.TryParseOrder(option, out int line, out int packs)) return CookOutcome.InvalidOption;
            if (_economy == null) return CookOutcome.UnknownStation;
            var wallet = new PlayerSupplyWallet(_economy, player);
            CookOutcome placed = _room.Order(line, packs, EconomyRules.AccountId(player.OwnerId), _economy.GameMinute, _economy.Catalog,
                wallet, out SupplyOrder order);
            if (placed != CookOutcome.Ok) return placed;
            string text = "Player " + player.OwnerId + " ordered " + TerminalRules.PacksText(packs) + " of " + StockCatalog.Goods[line].Name
                + " (" + EconomyRules.Money(order.Cost) + "), due at " + TerminalRules.ClockOf(order.DueMinute);
            Debug.Log("[Supply] order " + order.Id + ": " + text);
            if (_feed != null) _feed.ServerAdd(text);
            PublishIfChanged();
            return CookOutcome.Ok;
        }

        /// <summary>Spec 034: <see cref="ISupplyWallet"/> through the economy service (the bulk price, once; the refund backstop).</summary>
        private sealed class PlayerSupplyWallet : ISupplyWallet
        {
            private readonly EconomyService _economy;
            private readonly PickupInteractor _player;

            public PlayerSupplyWallet(EconomyService economy, PickupInteractor player)
            {
                _economy = economy;
                _player = player;
            }

            public bool CanAfford(long cost) => _economy.CanAfford(_player.NetworkObject, cost);

            public CookOutcome Charge(SupplierOffer offer, int packs, out long cost) =>
                StockRules.PaidOutcome(_economy.TryBuySupply(_player.NetworkObject, offer, packs, out cost));

            public void Refund(long cost)
            {
                EconomyError back = _economy.Refund(_player.NetworkObject, cost);
                if (back != EconomyError.None) Debug.LogError("[Supply] could not refund " + EconomyRules.Money(cost) + ": " + back);
                else Debug.LogWarning("[Supply] refunded " + EconomyRules.Money(cost) + " to player " + _player.OwnerId + " (order not booked)");
            }
        }

        // ---- boxes ----

        /// <summary>Server: the delivery box the player carries, and what is in it; false when they carry none.</summary>
        public bool TryGetHeldBox(PickupInteractor player, out NetworkObject box, out BoxContents contents)
        {
            contents = default;
            box = null;
            if (player == null || _pickups == null || !_pickups.TryGetHeld(player, out box) || box == null) return false;
            if (!box.TryGetComponent(out SupplyBox _)) return false;
            return _room.TryGetBox(box.ObjectId, out contents);
        }

        /// <summary>
        /// Spec 034: the carried box goes into slot line <paramref name="line"/>: up to the capacity fills it; what does not fit stays in
        /// the box (still in the hands); an empty box is gone. WrongBox, StockFull; nothing changes then.
        /// </summary>
        public CookOutcome Unpack(PickupInteractor player, NetworkObject box, int line, string where)
        {
            if (box == null) return CookOutcome.NothingHeld;
            CookOutcome done = _room.Unpack(box.ObjectId, line, out int accepted, out int left);
            if (done != CookOutcome.Ok) return done;
            if (left <= 0)
            {
                _pickups.TakeAway(player, out _);
                _networkManager.ServerManager.Despawn(box);
            }
            else if (box.TryGetComponent(out SupplyBox supplyBox))
            {
                supplyBox.ServerSet(new BoxContents(line, left));
            }
            Debug.Log("[Supply] player " + player.OwnerId + " unpacked " + StockCatalog.Describe(line, accepted) + " into the " + where
                + (left > 0 ? " (" + left + " stay in the box: the slot is full)" : string.Empty) + ": " + Presented(line) + " now");
            PublishIfChanged();
            return CookOutcome.Ok;
        }

        private string Presented(int line) => StockCatalog.Presented(_room.LineOf(line)) + "/" + StockCatalog.PresentedCapacity(line);

        /// <summary>Server: a box object is gone (despawned some other way): forget what was in it.</summary>
        public void ForgetBox(NetworkObject box)
        {
            if (box != null) _room.ForgetBox(box.ObjectId);
        }
    }
}
