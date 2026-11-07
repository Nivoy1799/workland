using FishNet.Managing;
using FishNet.Object;
using UnityEngine;
using Worktown.Game.Economy;
using Worktown.Game.Net;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Server-side authority for the kitchen (spec 007). Validates the station, the distance and
    /// what the player holds, applies the sim operation through KitchenState, and publishes the
    /// result on the Food object. Lives next to the NetworkManager; does nothing on pure clients.
    /// </summary>
    public sealed class KitchenService : MonoBehaviour
    {
        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private PickupService _pickups;
        [SerializeField] private NetworkObject _foodPrefab;
        [SerializeField] private ServeFeed _feed;
        [Tooltip("Spec 009. Without it, crates are free and the counter only serves.")]
        [SerializeField] private EconomyService _economy;
        [Tooltip("Spec 014. Optional: visible customers react to sales.")]
        [SerializeField] private Worktown.Game.Npc.CustomerService _customers;
        [Tooltip("Index in this array = Station.Index.")]
        [SerializeField] private Station[] _stations = new Station[0];

        private readonly KitchenState _kitchen = KitchenState.CreateDemo();

        // Start, not OnEnable: NetworkManager creates its ServerManager in its own Awake.
        private void Start()
        {
            _networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
        }

        private void OnDestroy()
        {
            if (_networkManager != null && _networkManager.ServerManager != null)
                _networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
        }

        private void OnServerConnectionState(FishNet.Transporting.ServerConnectionStateArgs args)
        {
            // FishNet despawns everything and reuses object ids on a restart in the same process;
            // old food entries must not attach to new objects (review finding, spec 007).
            if (args.ConnectionState == FishNet.Transporting.LocalConnectionState.Stopped
                && !_networkManager.ServerManager.IsAnyServerStarted())
                _kitchen.Clear(); // station labels reset with their SyncLists when FishNet despawns them
        }

        public CookOutcome TryUseStation(PickupInteractor player, int stationIndex, int option)
        {
            if (stationIndex < 0 || stationIndex >= _stations.Length || _stations[stationIndex] == null)
                return CookOutcome.UnknownStation;
            Station station = _stations[stationIndex];
            if (!StationRules.IsInReach(player.transform.position, station.transform.position))
                return CookOutcome.TooFar;

            switch (station.Kind)
            {
                case StationKind.Crate: return TakeIngredient(player, station);
                case StationKind.Counter: return Serve(player);
                case StationKind.PlatingTable:
                case StationKind.Bowl: return Assemble(player, station);
                default: return Cook(player, station, option);
            }
        }

        private CookOutcome TakeIngredient(PickupInteractor player, Station crate)
        {
            if (_pickups.TryGetHeld(player, out _)) return CookOutcome.HandsFull;
            int ingredient = crate.IngredientIndex;
            if (ingredient < 0 || ingredient >= DemoIngredients.All.Count) return CookOutcome.UnknownStation;

            string baseId = DemoIngredients.All[ingredient].Id;
            CookOutcome created = _kitchen.TryCreateIngredient(baseId, out Item item);
            if (created != CookOutcome.Ok) return created;

            // Spec 009: pay the NPC grocer first; no money, no potato.
            long cost = 0;
            if (_economy != null)
            {
                EconomyError paid = _economy.TryBuy(player.NetworkObject, baseId, out cost);
                if (paid == EconomyError.InsufficientFunds) return CookOutcome.NotEnoughMoney;
                if (paid != EconomyError.None) return CookOutcome.SimRefused;
            }

            if (!SpawnFoodInHands(player, item, (byte)ingredient)) return CookOutcome.SimRefused;
            Debug.Log("[Kitchen] player " + player.OwnerId + " bought " + KitchenState.NameOf(item)
                + " for " + EconomyRules.Money(cost));
            return CookOutcome.Ok;
        }

        /// <summary>Spawns a food object for a sim item directly in the player's hands.</summary>
        private bool SpawnFoodInHands(PickupInteractor player, Item item, byte color)
        {
            NetworkObject food = _networkManager.GetPooledInstantiated(_foodPrefab, true);
            food.transform.SetPositionAndRotation(player.HoldPoint.position, player.HoldPoint.rotation);
            _networkManager.ServerManager.Spawn(food);
            _kitchen.Register(food.ObjectId, item);
            food.GetComponent<Food>().ServerSet(KitchenState.NameOf(item), KitchenState.QualityOf(item), color);
            if (_pickups.HandTo(player, food)) return true;

            // Should not happen (hands were checked); do not leave the item lying around unowned.
            Debug.LogError("[Kitchen] could not hand " + KitchenState.NameOf(item) + " to player " + player.OwnerId);
            _kitchen.TryRemove(food.ObjectId, out _);
            _networkManager.ServerManager.Despawn(food);
            return false;
        }

        /// <summary>
        /// Spec 013: with food in hand, put it on the plating table / in the bowl; with empty hands,
        /// take everything as one plated dish / mix.
        /// </summary>
        private CookOutcome Assemble(PickupInteractor player, Station station)
        {
            StationContents contents = station.GetComponent<StationContents>();
            if (_pickups.TryGetHeld(player, out NetworkObject held))
            {
                if (!held.TryGetComponent(out Food food)) return CookOutcome.NotFood;
                CookOutcome added = _kitchen.TryAddToStation(held.ObjectId, station.Index, station.Kind, food.Ingredient.Value);
                if (added != CookOutcome.Ok) return added;
                _pickups.TakeAway(player, out _);
                _networkManager.ServerManager.Despawn(held);
                if (contents != null) contents.ServerSet(_kitchen.NamesOnStation(station.Index));
                Debug.Log("[Kitchen] player " + player.OwnerId + " put " + food.DisplayName.Value + " on " + station.DisplayName);
                return CookOutcome.Ok;
            }

            // Hand the result over first, clear the station only on success (review finding).
            CookOutcome taken = _kitchen.TryPreviewTake(station.Index, station.Kind, out Item result, out byte color);
            if (taken != CookOutcome.Ok) return taken;
            if (!SpawnFoodInHands(player, result, color)) return CookOutcome.SimRefused;
            _kitchen.ClearStation(station.Index);
            if (contents != null) contents.ServerSet(_kitchen.NamesOnStation(station.Index));
            Debug.Log("[Kitchen] player " + player.OwnerId + " took " + KitchenState.NameOf(result)
                + " from " + station.DisplayName + " q=" + KitchenState.QualityOf(result).ToString("0.00"));
            return CookOutcome.Ok;
        }

        private CookOutcome Cook(PickupInteractor player, Station station, int option)
        {
            if (!_pickups.TryGetHeld(player, out NetworkObject held)) return CookOutcome.NothingHeld;
            if (!held.TryGetComponent(out Food food)) return CookOutcome.NotFood;

            CookOutcome outcome = _kitchen.TryApply(held.ObjectId, station.Kind, option, out Item result, out OperationError error);
            if (outcome != CookOutcome.Ok)
            {
                if (outcome == CookOutcome.SimRefused)
                    Debug.Log("[Kitchen] sim refused " + station.Kind + " " + option + ": " + error);
                return outcome;
            }
            food.ServerSet(KitchenState.NameOf(result), KitchenState.QualityOf(result), food.Ingredient.Value);
            Debug.Log("[Kitchen] player " + player.OwnerId + " made " + KitchenState.NameOf(result)
                + " q=" + KitchenState.QualityOf(result).ToString("0.00"));
            return CookOutcome.Ok;
        }

        private CookOutcome Serve(PickupInteractor player)
        {
            if (!_pickups.TryGetHeld(player, out NetworkObject held)) return CookOutcome.NothingHeld;
            if (!held.TryGetComponent(out Food _) || !_kitchen.TryGet(held.ObjectId, out Item item)) return CookOutcome.NotFood;

            string name = KitchenState.NameOf(item);
            float quality = KitchenState.QualityOf(item);
            string stars = StarRating.Format(StarRating.Stars(quality));
            string entry;

            if (_economy != null)
            {
                // Spec 009: plate and sell. A refused sale keeps the food in the player's hands.
                if (_kitchen.TryPlate(held.ObjectId, out Item plated) != CookOutcome.Ok) return CookOutcome.CannotSell;
                SaleResult sale = _economy.TrySell(player.NetworkObject, plated);
                if (!sale.Ok)
                {
                    Debug.Log("[Economy] player " + player.OwnerId + " could not sell " + name + ": " + sale.Error);
                    return CookOutcome.CannotSell;
                }
                string where = sale.Channel == SaleChannel.NpcDemand ? sale.MarketId : "wholesaler";
                if (sale.Channel == SaleChannel.NpcDemand && _customers != null) _customers.OnSale(sale.MarketId, sale.Payout);
                entry = "Player " + player.OwnerId + " sold " + name + " " + stars + " for "
                    + EconomyRules.Money(sale.Payout) + " (" + where + ")";
                Debug.Log("[Economy] player " + player.OwnerId + " sold " + name + " for " + EconomyRules.Money(sale.Payout)
                    + " to " + where + " q=" + quality.ToString("0.00"));
            }
            else
            {
                entry = "Player " + player.OwnerId + " served " + name + " " + stars + " (q " + quality.ToString("0.00") + ")";
                Debug.Log("[Kitchen] player " + player.OwnerId + " served " + name + " q=" + quality.ToString("0.00"));
            }

            _kitchen.TryRemove(held.ObjectId, out _);
            _pickups.TakeAway(player, out _);
            _networkManager.ServerManager.Despawn(held);
            if (_feed != null) _feed.ServerAdd(entry);
            return CookOutcome.Ok;
        }
    }
}
