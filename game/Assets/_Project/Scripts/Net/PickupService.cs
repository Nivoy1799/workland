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

        // Spec 042, server: the kitchen's vetoes (null: anything goes). A cup under another player's running pour cannot be taken; a
        // tap's cup spot takes only a cup that fits that tap.
        private System.Func<PickupInteractor, NetworkObject, bool> _canTake;
        private System.Func<NetworkObject, PlaceSurface, bool> _canPlace;

        /// <summary>Spec 042, server: sets the kitchen's vetoes on picking up (<paramref name="canTake"/>) and on placing (<paramref name="canPlace"/>).</summary>
        public void SetGuards(System.Func<PickupInteractor, NetworkObject, bool> canTake, System.Func<NetworkObject, PlaceSurface, bool> canPlace)
        {
            _canTake = canTake;
            _canPlace = canPlace;
        }

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
            if (_canTake != null && !_canTake(player, target)) return PickupOutcome.Taken; // spec 042: someone pours into that cup
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
        /// tilt (Pickup.RestTilt, e.g. the knife flat), turned with the surface's yaw; spec 039: a tool with a handle turned
        /// so the handle points to the player (ToolRest). Spec 041: <paramref name="packedSpot"/> also carries the player's wheel turn
        /// (PlaceTurn: the spot in the low bits, the 45 degree step above); the rest rotation is turned by it, and a step out of range
        /// is refused like an unknown spot.
        /// </summary>
        public PickupOutcome TryPlace(PickupInteractor player, int surfaceIndex, int packedSpot)
        {
            int clientId = player.Owner.ClientId;
            bool holding = _registry.TryGetHeldItem(clientId, out int itemId);
            PlaceSurface surface = surfaceIndex >= 0 && surfaceIndex < _surfaces.Length ? _surfaces[surfaceIndex] : null;
            if (PlaceTurn.TryUnpack(packedSpot, out int unpacked, out _) && surface != null && unpacked >= 0 && unpacked < surface.SpotCount)
                FreeIfStale(surfaceIndex, unpacked, surface.SpotWorldPosition(unpacked));
            Pickup pickup = null;
            NetworkObject item = null;
            bool found = holding && _networkManager.ServerManager.Objects.Spawned.TryGetValue(itemId, out item) && item.TryGetComponent(out pickup);
            PickupOutcome planned = PlanPlace(_spots, holding, surfaceIndex, surface, packedSpot, player.transform.position,
                found && pickup.IsHandleTool, found ? pickup.RestTilt : Quaternion.identity, found ? pickup.BottomOffset : 0f,
                _isOccupied ??= spot => IsSpotOccupied(spot, _probe), out int spotIndex, out Pose rest);
            if (planned != PickupOutcome.Ok) return planned;
            if (!found) return PickupOutcome.NotFound;
            // Spec 042: a tap's cup spot takes only a cup that fits its tap (the kitchen decides; nothing moves on a no).
            if (_canPlace != null && !_canPlace(item, surface)) return PickupOutcome.Taken;

            _registry.TryRelease(itemId, clientId);
            item.RemoveOwnership();
            pickup.ServerPlaceAt(rest.position, rest.rotation);
            _spots.TryTake(surfaceIndex, spotIndex, itemId);
            ItemMoved?.Invoke(item, surface);
            return PickupOutcome.Ok;
        }

        private System.Func<Vector3, bool> _isOccupied;

        /// <summary>
        /// 041 review, server, without the network: every check of <see cref="TryPlace"/> and where the item comes to rest, in its order.
        /// The packed spot is unpacked first (PlaceTurn; a step of 8 or more is refused like an unknown spot, NotFound); then
        /// PlaceSpots.CheckPlace (hands, surface, spot, reach to the spot from <paramref name="playerPosition"/>, the spot table), then
        /// <paramref name="occupied"/> (something else lies there: Taken). On Ok, <paramref name="rest"/> is the item's pose on the spot:
        /// its bottom (<paramref name="bottomOffset"/>) on the top, the resting tilt turned with the surface's yaw, a handle tool's handle
        /// towards the player (ToolRest), turned by the wheel's steps (PlaceTurn.Apply). Changes nothing: the caller moves the item and
        /// takes the spot.
        /// </summary>
        public static PickupOutcome PlanPlace(PlaceSpots spots, bool holding, int surfaceIndex, PlaceSurface surface, int packedSpot,
            Vector3 playerPosition, bool handleTool, Quaternion restTilt, float bottomOffset, System.Func<Vector3, bool> occupied,
            out int spotIndex, out Pose rest)
        {
            rest = default;
            if (!PlaceTurn.TryUnpack(packedSpot, out spotIndex, out int turn)) spotIndex = -1;
            int spotCount = surface != null ? surface.SpotCount : 0;
            bool validSpot = spotIndex >= 0 && spotIndex < spotCount;
            Vector3 spot = validSpot ? surface.SpotWorldPosition(spotIndex) : Vector3.zero;
            bool inReach = validSpot && PickupRules.IsInReach(playerPosition, spot);
            PickupOutcome check = spots != null
                ? spots.CheckPlace(holding, surface != null, spotCount, surfaceIndex, spotIndex, inReach)
                : PickupOutcome.NotFound;
            if (check != PickupOutcome.Ok) return check;
            if (occupied != null && occupied(spot)) return PickupOutcome.Taken;
            // Spec 039: a tool lies with its handle towards where the server has the player (ToolRest); anything else as before.
            Quaternion rotation = PlaceTurn.Apply(ToolRest.RestRotation(handleTool, restTilt, surface.transform.rotation, playerPosition, spot),
                turn); // spec 041: the player's wheel turn on top of the server's rest
            rest = new Pose(spot + surface.transform.up * bottomOffset, rotation);
            return PickupOutcome.Ok;
        }

        /// <summary>
        /// Spec 020: true when a free (not held) item lies on the spot: a tiny probe just above the top touches its
        /// collider. Used by the server and by the client to pick a free spot (clients cannot see the server's
        /// spot table, but they see the items).
        /// </summary>
        public static bool IsSpotOccupied(Vector3 spot, Collider[] buffer) => OccupantAt(spot, buffer) != null;

        /// <summary>Spec 042: the free (not held) item lying on the spot (the same probe as <see cref="IsSpotOccupied"/>), or null.</summary>
        public static Pickup OccupantAt(Vector3 spot, Collider[] buffer)
        {
            int count = Physics.OverlapSphereNonAlloc(spot + Vector3.up * (SpotProbeRadius + 0.005f), SpotProbeRadius, buffer,
                PhysicsLayers.Interact, QueryTriggerInteraction.Ignore); // spec 039: items lie on their own layer
            for (int i = 0; i < count; i++)
            {
                Pickup pickup = buffer[i] != null ? buffer[i].GetComponentInParent<Pickup>() : null;
                if (pickup != null && !pickup.IsHeld) return pickup;
            }
            return null;
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
        /// Spec 029b: whether <see cref="HandTo"/> would accept a fresh object made from <paramref name="prefab"/> for the
        /// player now (it can be held, the hands are empty). Changes nothing.
        /// </summary>
        public bool CanHandTo(PickupInteractor player, NetworkObject prefab)
        {
            if (player == null || player.Owner == null || prefab == null || !prefab.TryGetComponent(out Pickup _)) return false;
            return _registry.CanClaim(player.Owner.ClientId);
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
