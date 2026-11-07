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
    /// Spec 020: also puts held items on the spots of place surfaces (worktops); the client only names the
    /// surface and the spot, the server checks hands, reach and the spot and picks the position.
    /// </summary>
    public sealed class PickupService : MonoBehaviour
    {
        [SerializeField] private NetworkManager _networkManager;

        [Tooltip("Spec 020: index in this array = PlaceSurface.Index.")]
        [SerializeField] private PlaceSurface[] _surfaces = new PlaceSurface[0];

        // Spec 020: a probe this small sits inside the item lying on a spot but misses the neighbours.
        private const float SpotProbeRadius = 0.03f;
        // An occupant further than this from its spot (horizontally) no longer lies there.
        private const float SpotTolerance = 0.1f;

        /// <summary>
        /// Spec 021, server: an item was picked up, handed over, put on a spot, dropped or taken away. The surface is
        /// the one it now lies on (null: in the hands, on the floor or gone), so the kitchen knows when a pan or pot
        /// stands on a burner.
        /// </summary>
        public event System.Action<NetworkObject, PlaceSurface> ItemMoved;

        private readonly PickupRegistry _registry = new PickupRegistry();
        private readonly PlaceSpots _spots = new PlaceSpots();
        private readonly Collider[] _probe = new Collider[8];

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
            {
                _registry.Clear();
                _spots.Clear(); // spec 020
            }
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
            _spots.Free(target.ObjectId); // spec 020: picking it up frees its spot
            ItemMoved?.Invoke(target, null);
            return PickupOutcome.Ok;
        }

        /// <summary>
        /// Spec 020: puts the held item on spot <paramref name="spotIndex"/> of surface
        /// <paramref name="surfaceIndex"/>. Checks (PlaceSpots.CheckPlace): something held, the surface exists, the
        /// spot index is valid, the spot is within reach (2.5 m, to the spot), the spot is free; also nothing else
        /// lies there (e.g. the knife at its home spot after a respawn). The item rests on the spot in its resting
        /// tilt (Pickup.RestTilt, e.g. the knife flat), turned with the surface's yaw.
        /// </summary>
        public PickupOutcome TryPlace(PickupInteractor player, int surfaceIndex, int spotIndex)
        {
            int clientId = player.Owner.ClientId;
            bool holding = _registry.TryGetHeldItem(clientId, out int itemId);
            PlaceSurface surface = surfaceIndex >= 0 && surfaceIndex < _surfaces.Length ? _surfaces[surfaceIndex] : null;
            int spotCount = surface != null ? surface.SpotCount : 0;
            bool validSpot = spotIndex >= 0 && spotIndex < spotCount;
            Vector3 spot = validSpot ? surface.SpotWorldPosition(spotIndex) : Vector3.zero;
            bool inReach = validSpot && PickupRules.IsInReach(player.transform.position, spot);
            if (validSpot) FreeIfStale(surfaceIndex, spotIndex, spot);

            PickupOutcome check = _spots.CheckPlace(holding, surface != null, spotCount, surfaceIndex, spotIndex, inReach);
            if (check != PickupOutcome.Ok) return check;
            if (IsSpotOccupied(spot, _probe)) return PickupOutcome.Taken;
            if (!_networkManager.ServerManager.Objects.Spawned.TryGetValue(itemId, out NetworkObject item)
                || !item.TryGetComponent(out Pickup pickup))
                return PickupOutcome.NotFound;

            _registry.TryRelease(itemId, clientId);
            item.RemoveOwnership();
            Quaternion rest = PlaceSpots.RestRotation(pickup.RestTilt, surface.transform.rotation);
            pickup.ServerPlaceAt(spot + surface.transform.up * pickup.BottomOffset, rest);
            _spots.TryTake(surfaceIndex, spotIndex, itemId);
            ItemMoved?.Invoke(item, surface);
            return PickupOutcome.Ok;
        }

        /// <summary>
        /// Spec 020: true when a free (not held) item lies on the spot: a tiny probe just above the top touches its
        /// collider. Used by the server and by the client to pick a free spot (clients cannot see the server's
        /// spot table, but they see the items).
        /// </summary>
        public static bool IsSpotOccupied(Vector3 spot, Collider[] buffer)
        {
            int count = Physics.OverlapSphereNonAlloc(spot + Vector3.up * (SpotProbeRadius + 0.005f), SpotProbeRadius, buffer,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Pickup pickup = buffer[i] != null ? buffer[i].GetComponentInParent<Pickup>() : null;
                if (pickup != null && !pickup.IsHeld) return true;
            }
            return false;
        }

        /// <summary>A spot whose recorded item is gone, held or lies elsewhere is free again (object ids are reused).</summary>
        private void FreeIfStale(int surfaceIndex, int spotIndex, Vector3 spot)
        {
            if (!_spots.TryGetOccupant(surfaceIndex, spotIndex, out int occupantId)) return;
            bool lies = _networkManager.ServerManager.Objects.Spawned.TryGetValue(occupantId, out NetworkObject occupant)
                && occupant.TryGetComponent(out Pickup pickup) && !pickup.IsHeld
                && Vector3.ProjectOnPlane(occupant.transform.position - spot, Vector3.up).sqrMagnitude <= SpotTolerance * SpotTolerance;
            if (!lies) _spots.Free(occupantId);
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

        /// <summary>Server: the object this player holds, if any (spec 007 stations work on it).</summary>
        public bool TryGetHeld(PickupInteractor player, out NetworkObject held)
        {
            held = null;
            return _registry.TryGetHeldItem(player.Owner.ClientId, out int itemId)
                && _networkManager.ServerManager.Objects.Spawned.TryGetValue(itemId, out held);
        }

        /// <summary>
        /// Server: puts an object the server just spawned (e.g. an ingredient from a crate) into
        /// the player's hands. No reach check: the server chose the object, not the client.
        /// </summary>
        public bool HandTo(PickupInteractor player, NetworkObject target)
        {
            if (target == null || !target.TryGetComponent(out Pickup pickup)) return false;
            int clientId = player.Owner.ClientId;
            if (!_registry.TryClaim(target.ObjectId, clientId)) return false;
            target.GiveOwnership(player.Owner);
            pickup.ServerAttach(player.HoldPoint, clientId);
            _spots.Free(target.ObjectId);
            ItemMoved?.Invoke(target, null);
            return true;
        }

        /// <summary>Server: removes the held object from the player's hands without dropping it
        /// (it is about to be despawned, e.g. served).</summary>
        public bool TakeAway(PickupInteractor player, out NetworkObject taken)
        {
            if (!TryGetHeld(player, out taken)) return false;
            _registry.TryRelease(taken.ObjectId, player.Owner.ClientId);
            taken.RemoveOwnership();
            ItemMoved?.Invoke(taken, null);
            return true;
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
            ItemMoved?.Invoke(item, null);
        }

        /// <summary>Spec 021, server: the place surface by index, or null.</summary>
        public PlaceSurface SurfaceAt(int surfaceIndex)
        {
            return surfaceIndex >= 0 && surfaceIndex < _surfaces.Length ? _surfaces[surfaceIndex] : null;
        }

        public int SurfaceCount => _surfaces.Length;

        /// <summary>
        /// Spec 021: the surface whose spot an item's pivot lies on (within 0.1 m horizontally, at most 0.3 m above the
        /// top), or null. Used for cookware lying on a burner at the start or after a respawn.
        /// </summary>
        public PlaceSurface SurfaceUnder(Vector3 position)
        {
            foreach (PlaceSurface surface in _surfaces)
            {
                if (surface == null) continue;
                for (int i = 0; i < surface.SpotCount; i++)
                {
                    Vector3 offset = position - surface.SpotWorldPosition(i);
                    if (Vector3.ProjectOnPlane(offset, Vector3.up).sqrMagnitude <= SpotTolerance * SpotTolerance
                        && offset.y > -0.05f && offset.y < 0.3f)
                        return surface;
                }
            }
            return null;
        }
    }
}
