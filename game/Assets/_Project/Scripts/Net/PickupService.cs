using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Net
{
    /// <summary>
    /// Server-side authority for pickups. Validates distance (PickupRules) and exclusivity
    /// (PickupRegistry), then sets the cube's owner and attaches it (spec 003 design).
    /// Lives next to the NetworkManager; does nothing on pure clients.
    /// </summary>
    public sealed class PickupService : MonoBehaviour
    {
        [SerializeField] private NetworkManager _networkManager;

        private readonly PickupRegistry _registry = new PickupRegistry();

        // Start, not OnEnable: NetworkManager creates its ServerManager in its own Awake.
        private void Start()
        {
            _networkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            _networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
        }

        private void OnDestroy()
        {
            if (_networkManager == null || _networkManager.ServerManager == null) return;
            _networkManager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
            _networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
        }

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            // A server restart in the same process must not keep claims for despawned cubes.
            if (args.ConnectionState == LocalConnectionState.Stopped && !_networkManager.ServerManager.IsAnyServerStarted())
                _registry.Clear();
        }

        public PickupOutcome TryPickup(PickupInteractor player, NetworkObject target)
        {
            if (target == null || !target.IsSpawned || !target.TryGetComponent(out Pickup pickup))
                return PickupOutcome.NotFound;

            int clientId = player.Owner.ClientId;
            if (_registry.TryGetHeldItem(clientId, out _))
                return PickupOutcome.AlreadyHolding;
            if (!PickupRules.IsInReach(player.transform.position, pickup.transform.position))
                return PickupOutcome.TooFar;
            if (!_registry.TryClaim(target.ObjectId, clientId))
                return PickupOutcome.Taken;

            target.GiveOwnership(player.Owner);
            pickup.ServerAttach(player.HoldPoint, clientId);
            return PickupOutcome.Ok;
        }

        public PickupOutcome TryDrop(PickupInteractor player)
        {
            int clientId = player.Owner.ClientId;
            if (!_registry.TryGetHeldItem(clientId, out int itemId))
                return PickupOutcome.NothingHeld;

            _registry.TryRelease(itemId, clientId);
            Release(itemId, player.transform.position, player.transform.forward);
            return PickupOutcome.Ok;
        }

        private void OnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            // FishNet raises this before despawning the client's owned objects, so taking the
            // ownership back here keeps the held cube in the world.
            if (args.ConnectionState != RemoteConnectionState.Stopped) return;
            if (!_registry.ReleaseAllFor(connection.ClientId, out int itemId)) return;
            // The leaving player's object may already be gone; drop below the cube itself.
            if (_networkManager.ServerManager.Objects.Spawned.TryGetValue(itemId, out NetworkObject item))
                Release(itemId, item.transform.position, Vector3.forward);
        }

        private void Release(int itemId, Vector3 holderPosition, Vector3 dropForward)
        {
            if (!_networkManager.ServerManager.Objects.Spawned.TryGetValue(itemId, out NetworkObject item))
                return;
            item.RemoveOwnership();
            if (item.TryGetComponent(out Pickup pickup))
                pickup.ServerDetach(holderPosition, dropForward);
        }
    }
}
