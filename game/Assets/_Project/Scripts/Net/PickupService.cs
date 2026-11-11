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
        // Spec 047: where each held item was picked up from (item object id), so it goes back there when its holder leaves.
        private readonly System.Collections.Generic.Dictionary<int, PickupOrigin> _origins =
            new System.Collections.Generic.Dictionary<int, PickupOrigin>();
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
                _origins.Clear(); // spec 047
            }
        }

        /// <summary>
        /// 042 review: the server's pick-up check and claim, pure (TryPickup runs it before it moves anything): the player already
        /// holds something (AlreadyHolding), the item is out of reach (TooFar), the kitchen's veto says no (<paramref name="allowed"/>
        /// false, spec 042: someone pours into that cup: Taken), another player holds it (Taken); else the item is claimed for
        /// <paramref name="clientId"/> (Ok). Nothing is claimed unless the answer is Ok.
        /// </summary>
        public static PickupOutcome PlanPickup(PickupRegistry registry, int itemId, int clientId, bool inReach, bool allowed)
        {
            if (registry == null) return PickupOutcome.NotFound;
            if (registry.TryGetHeldItem(clientId, out _)) return PickupOutcome.AlreadyHolding;
            if (!inReach) return PickupOutcome.TooFar;
            if (!allowed) return PickupOutcome.Taken;
            return registry.TryClaim(itemId, clientId) ? PickupOutcome.Ok : PickupOutcome.Taken;
        }

        public PickupOutcome TryPickup(PickupInteractor player, NetworkObject target)
        {
            if (target == null || !target.IsSpawned || !target.TryGetComponent(out Pickup pickup))
                return PickupOutcome.NotFound;

            int clientId = player.Owner.ClientId;
            PickupOutcome claimed = PlanPickup(_registry, target.ObjectId, clientId,
                PickupRules.IsInReach(player.transform.position, pickup.transform.position),
                _canTake == null || _canTake(player, target));
            if (claimed != PickupOutcome.Ok) return claimed;

            // Spec 047: remember where it lay (its spot, else its pose) before the spot is freed.
            bool onSpot = _spots.TryGetSpotOf(target.ObjectId, out int fromSurface, out int fromSpot);
            _origins[target.ObjectId] = new PickupOrigin(onSpot, fromSurface, fromSpot, pickup.transform.position, pickup.transform.rotation);
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
                found ? pickup.ColliderHalfExtents : Vector3.zero, // 041 review: a turn may not lay it over a neighbour
                found ? pickup.ColliderCenter : Vector3.zero, // 054 round (review nit): where the box sits off the pivot
                _isOccupied ??= spot => IsSpotOccupied(spot, _probe), out int spotIndex, out Pose rest);
            if (planned != PickupOutcome.Ok) return planned;
            if (!found) return PickupOutcome.NotFound;
            // Spec 042: a tap's cup spot takes only a cup that fits its tap (the kitchen decides; nothing moves on a no).
            if (_canPlace != null && !_canPlace(item, surface)) return PickupOutcome.Taken;

            _registry.TryRelease(itemId, clientId);
            _origins.Remove(itemId); // spec 047
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
            return PlanPlace(spots, holding, surfaceIndex, surface, packedSpot, playerPosition, handleTool, restTilt, bottomOffset,
                Vector3.zero, occupied, out spotIndex, out rest);
        }

        /// <summary>
        /// 041 review: as above, with the item's local half extents (<paramref name="halfExtents"/>; zero: no footprint check). A wheel
        /// turn (a step other than 0) is refused (Taken) when the turned item would reach an occupied neighbour spot that it does not
        /// reach unturned (PlaceTurn.TurnOverlaps; occupied: in the spot table or found by <paramref name="occupied"/>).
        /// </summary>
        public static PickupOutcome PlanPlace(PlaceSpots spots, bool holding, int surfaceIndex, PlaceSurface surface, int packedSpot,
            Vector3 playerPosition, bool handleTool, Quaternion restTilt, float bottomOffset, Vector3 halfExtents,
            System.Func<Vector3, bool> occupied, out int spotIndex, out Pose rest)
        {
            return PlanPlace(spots, holding, surfaceIndex, surface, packedSpot, playerPosition, handleTool, restTilt, bottomOffset,
                halfExtents, Vector3.zero, occupied, out spotIndex, out rest);
        }

        /// <summary>
        /// 054 round (041 review nits): as above, with the centre of the item's box (<paramref name="boxCenter"/>, local and scaled; see
        /// Pickup.ColliderCenter), so a footprint off the pivot is checked where it lies. A half turn is never checked (PlaceTurn.IsHalfTurn):
        /// it is never refused. The neighbour test allocates nothing (a state struct and a non-capturing function).
        /// </summary>
        public static PickupOutcome PlanPlace(PlaceSpots spots, bool holding, int surfaceIndex, PlaceSurface surface, int packedSpot,
            Vector3 playerPosition, bool handleTool, Quaternion restTilt, float bottomOffset, Vector3 halfExtents, Vector3 boxCenter,
            System.Func<Vector3, bool> occupied, out int spotIndex, out Pose rest)
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
            Quaternion unturned = ToolRest.RestRotation(handleTool, restTilt, surface.transform.rotation, playerPosition, spot);
            Quaternion rotation = PlaceTurn.Apply(unturned, turn); // spec 041: the player's wheel turn on top of the server's rest
            // 041 review: the wheel's turn may not lay the item over an occupied neighbour spot.
            if (PlaceTurn.Add(turn, 0) != 0 && !PlaceTurn.IsHalfTurn(turn) && halfExtents != Vector3.zero)
            {
                Quaternion surfaceRotation = surface.transform.rotation;
                PlaceTurn.Footprint(halfExtents, boxCenter, unturned, surfaceRotation, out Vector2 restPositive, out Vector2 restNegative);
                PlaceTurn.Footprint(halfExtents, boxCenter, rotation, surfaceRotation, out Vector2 turnedPositive, out Vector2 turnedNegative);
                bool overlaps = PlaceTurn.TurnOverlaps(surface.Size, spotIndex, restPositive, restNegative, turnedPositive, turnedNegative,
                    new NeighbourTest(spots, surfaceIndex, surface, occupied), NeighbourTest.IsTaken);
                if (overlaps) return PickupOutcome.Taken;
            }
            rest = new Pose(spot + surface.transform.up * bottomOffset, rotation);
            return PickupOutcome.Ok;
        }

        /// <summary>054 round (041 review nit): what PlanPlace's neighbour test needs, passed by value (no closure per place request).</summary>
        private readonly struct NeighbourTest
        {
            private readonly PlaceSpots _spots;
            private readonly int _surfaceIndex;
            private readonly PlaceSurface _surface;
            private readonly System.Func<Vector3, bool> _occupied;

            public NeighbourTest(PlaceSpots spots, int surfaceIndex, PlaceSurface surface, System.Func<Vector3, bool> occupied)
            {
                _spots = spots;
                _surfaceIndex = surfaceIndex;
                _surface = surface;
                _occupied = occupied;
            }

            /// <summary>In the spot table or found by the occupied probe. One cached delegate (a method group would allocate each call).</summary>
            public static readonly System.Func<NeighbourTest, int, bool> IsTaken = (t, n) =>
                t._spots.IsTaken(t._surfaceIndex, n) || (t._occupied != null && t._occupied(t._surface.SpotWorldPosition(n)));
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
            _origins.Remove(itemId); // spec 047
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
            _origins.Remove(target.ObjectId); // spec 047: handed over fresh, no place to go back to
            target.GiveOwnership(player.Owner);
            pickup.ServerAttach(player.HoldPoint, clientId);
            _spots.Free(target.ObjectId);
            ItemMoved?.Invoke(target, null);
            return true;
        }

        /// <summary>
        /// Spec 044: as <see cref="TakeAway(PickupInteractor, out NetworkObject)"/>, for an item that is despawned into
        /// <paramref name="exitTo"/> right after: every viewer sees it fly there and hears <paramref name="landing"/>
        /// (<see cref="Pickup.ServerExitTo"/>).
        /// </summary>
        public bool TakeAway(PickupInteractor player, out NetworkObject taken, Vector3 exitTo, Worktown.Game.Audio.SoundKey landing)
        {
            if (!TakeAway(player, out taken)) return false;
            if (taken.TryGetComponent(out Pickup pickup)) pickup.ServerExitTo(exitTo, landing);
            return true;
        }

        /// <summary>Server: removes the held object from the player's hands without dropping it
        /// (it is about to be despawned, e.g. served).</summary>
        public bool TakeAway(PickupInteractor player, out NetworkObject taken)
        {
            if (!TryGetHeld(player, out taken)) return false;
            _registry.TryRelease(taken.ObjectId, player.Owner.ClientId);
            _origins.Remove(taken.ObjectId); // spec 047
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
            bool hasOrigin = _origins.TryGetValue(itemId, out PickupOrigin origin);
            _origins.Remove(itemId);
            if (!_networkManager.ServerManager.Objects.Spawned.TryGetValue(itemId, out NetworkObject item)) return;
            // Spec 047: the held item goes back to where it was picked up, if that place is still free.
            if (hasOrigin && TryReturn(item, origin)) return;
            // The leaving player's object may already be gone; drop below the cube itself.
            Release(itemId, item.transform.position, Vector3.forward);
        }

        /// <summary>
        /// Spec 047, server: puts a leaving holder's item back where it was picked up (its spot, or the pose it lay in) when the place
        /// is still free and, on a spot, the kitchen still allows it there (<see cref="PlanReturn"/>). False: nothing moved, the caller
        /// drops it.
        /// </summary>
        private bool TryReturn(NetworkObject item, PickupOrigin origin)
        {
            if (!item.TryGetComponent(out Pickup pickup)) return false;
            PlaceSurface surface = origin.OnSpot ? SurfaceAt(origin.Surface) : null;
            if (origin.OnSpot && (surface == null || origin.Spot < 0 || origin.Spot >= surface.SpotCount)) return false;
            if (origin.OnSpot) FreeIfStale(origin.Surface, origin.Spot, surface.SpotWorldPosition(origin.Spot));
            bool spotTaken = origin.OnSpot && _spots.IsTaken(origin.Surface, origin.Spot);
            Vector3 probeAt = origin.OnSpot ? surface.SpotWorldPosition(origin.Spot) : origin.Position;
            Pickup lying = spotTaken ? null : OccupantAt(probeAt, _probe);
            System.Func<bool> canPlaceThere = _canPlace != null ? () => _canPlace(item, surface) : (System.Func<bool>)null;
            if (!PlanReturn(origin, canPlaceThere, spotTaken, lying != null && lying != pickup)) return false;
            item.RemoveOwnership();
            pickup.ServerPlaceAt(origin.Position, origin.Rotation);
            if (origin.OnSpot) _spots.TryTake(origin.Surface, origin.Spot, item.ObjectId);
            ItemMoved?.Invoke(item, surface);
            Debug.Log("[Net] a leaving player's " + item.name + " went back to " + (origin.OnSpot ? surface.name + " spot " + origin.Spot : "where it lay"));
            return true;
        }

        /// <summary>
        /// 047 review, server, without the network: whether a leaving holder's item goes back to <paramref name="origin"/>. On a spot,
        /// the same place veto as <see cref="TryPlace"/> (<paramref name="canPlaceThere"/>: the kitchen's check of this item on that
        /// spot; a tap's cup spot takes only a cup that fits its tap) must allow it there; then the place must be free
        /// (PickupOrigin.GoesBack). A pose off the spot table has no veto (the check is not asked). False: the item drops where it is.
        /// </summary>
        public static bool PlanReturn(PickupOrigin origin, System.Func<bool> canPlaceThere, bool spotTaken, bool occupied)
        {
            bool allowed = !origin.OnSpot || canPlaceThere == null || canPlaceThere();
            return PickupOrigin.GoesBack(allowed, spotTaken, occupied);
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
