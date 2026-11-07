using FishNet.Managing;
using FishNet.Object;
using UnityEngine;
using Worktown.Game.Net;
using Worktown.Sim.Cooking;

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
        [Tooltip("Index in this array = Station.Index.")]
        [SerializeField] private Station[] _stations = new Station[0];

        private readonly KitchenState _kitchen = KitchenState.CreateDemo();

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
                default: return Cook(player, station, option);
            }
        }

        private CookOutcome TakeIngredient(PickupInteractor player, Station crate)
        {
            if (_pickups.TryGetHeld(player, out _)) return CookOutcome.HandsFull;
            int ingredient = crate.IngredientIndex;
            if (ingredient < 0 || ingredient >= DemoIngredients.All.Count) return CookOutcome.UnknownStation;

            CookOutcome created = _kitchen.TryCreateIngredient(DemoIngredients.All[ingredient].Id, out Item item);
            if (created != CookOutcome.Ok) return created;

            NetworkObject food = _networkManager.GetPooledInstantiated(_foodPrefab, true);
            food.transform.SetPositionAndRotation(player.HoldPoint.position, player.HoldPoint.rotation);
            _networkManager.ServerManager.Spawn(food);
            _kitchen.Register(food.ObjectId, item);
            food.GetComponent<Food>().ServerSet(KitchenState.NameOf(item), KitchenState.QualityOf(item), (byte)ingredient);
            _pickups.HandTo(player, food);
            Debug.Log("[Kitchen] player " + player.OwnerId + " took " + KitchenState.NameOf(item));
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
            if (!_kitchen.TryRemove(held.ObjectId, out Item item)) return CookOutcome.NotFood;

            _pickups.TakeAway(player, out _);
            _networkManager.ServerManager.Despawn(held);

            string name = KitchenState.NameOf(item);
            float quality = KitchenState.QualityOf(item);
            string entry = "Player " + player.OwnerId + " served " + name + " "
                + StarRating.Format(StarRating.Stars(quality)) + " (q " + quality.ToString("0.00") + ")";
            if (_feed != null) _feed.ServerAdd(entry);
            Debug.Log("[Kitchen] player " + player.OwnerId + " served " + name + " q=" + quality.ToString("0.00"));
            return CookOutcome.Ok;
        }
    }
}
