using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using Worktown.Game.Cooking;
using Worktown.Game.Net;
using Worktown.Sim.Layout;

namespace Worktown.Game.Layout
{
    /// <summary>
    /// Spec 062b, server: build mode in the world. Every intent (BuildInteractor.RequestBuild) goes through the gate (the rate limit, the
    /// shop closed, the stage, empty hands: BuildDesk.Gate), then the BuildDesk decides (claims, the snapped and validated move, store,
    /// take out, undo); this service applies what it decided: the piece's pose (PieceBehaviour.ServerSetPose: every client moves it), its
    /// claim (ClaimedBySlot), a stored piece despawned, a taken-out piece spawned on the pallet (parked: its stations and surfaces wait),
    /// and the items that travel. Items are found through the spot table (PickupService: the surface id they lie on), never by geometry:
    /// those on a moved piece keep their surface id and spot and move with it (<see cref="Carry"/>, from the piece's synced pose, so a
    /// host's predicted move cannot hide the start); loose items under its new footprint go to the nearest free floor cell; a stored
    /// piece's tools get new homes on another worktop (the layout's item homes follow, so they come back on the next load; an undo puts
    /// both back) and its other items go to the nearest free worktop spot, else the floor; a held item is never touched. A hot station
    /// (StationIsOn) is refused for a claim, a move and an undo. The layout in the world (LayoutSpawner.Current) follows every change, so
    /// the save holds it. When the shop opens the claims end, the undo histories are cleared (question 4) and the game is saved once
    /// (SaveTrigger.LayoutChanged) if the layout changed. A disconnect releases the claim and forgets the player's history; 60 s without
    /// input releases a claim. Lives next to the LayoutSpawner (it adds this component).
    /// </summary>
    public sealed class BuildService : MonoBehaviour
    {
        /// <summary>An item farther than this (horizontally, m) from its recorded spot no longer lies there.</summary>
        private const float SpotTolerance = 0.1f;
        /// <summary>How far above the floor an item not in the spot table lies on a piece (lower: on the floor).</summary>
        public const float OnPieceHeight = 0.4f;

        private NetworkManager _networkManager;
        private LayoutSpawner _spawner;
        private PickupService _pickups;
        private KitchenService _kitchen;
        private Worktown.Game.Save.SaveService _saves;
        private BuildDesk _desk;
        private bool _changedSinceOpen;
        private float _nextLapseCheck;

        private static BuildService _instance;

        /// <summary>The server's build service (null on a pure client or before it woke).</summary>
        public static BuildService Find() => _instance != null ? _instance : _instance = FindFirstObjectByType<BuildService>();

        /// <summary>Server, tests: the book (the layout as decided, the claims, the histories).</summary>
        public BuildDesk Desk => _desk ??= new BuildDesk(_spawner != null ? _spawner.Catalog : null, _spawner != null ? _spawner.Room : null);

        /// <summary>A player's slot: client id + 1 (1 to 255); 0 for no connection.</summary>
        public static byte SlotOf(NetworkConnection connection)
        {
            if (connection == null || !connection.IsValid) return 0;
            return (byte)Mathf.Clamp(connection.ClientId + 1, 1, 255);
        }

        private void Awake()
        {
            _instance = this;
            _networkManager = GetComponent<NetworkManager>();
            if (_networkManager == null) _networkManager = FindFirstObjectByType<NetworkManager>();
            _spawner = GetComponent<LayoutSpawner>();
            _desk = Desk;
            if (_spawner != null) _spawner.Spawned += OnSpawned;
            ShopDoor.ServerChanged += OnDoorChanged;
        }

        private void Start()
        {
            if (_networkManager != null && _networkManager.ServerManager != null)
                _networkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            if (_spawner != null) _spawner.Spawned -= OnSpawned;
            ShopDoor.ServerChanged -= OnDoorChanged;
            if (_networkManager != null && _networkManager.ServerManager != null)
                _networkManager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
        }

        private void OnSpawned(ShopLayout layout)
        {
            Desk.Reset(layout);
            _changedSinceOpen = false;
        }

        private void Update()
        {
            if (_networkManager == null || !_networkManager.IsServerStarted || Time.unscaledTime < _nextLapseCheck) return;
            _nextLapseCheck = Time.unscaledTime + 1f;
            foreach (byte slot in Desk.Lapsed(Time.unscaledTimeAsDouble))
            {
                Debug.Log("[Build] slot " + slot + "'s claim lapsed (60 s without input)");
                ReleaseInWorld(slot);
            }
        }

        private void OnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped) return;
            ServerPlayerLeft(SlotOf(connection));
        }

        /// <summary>
        /// Server: the player with <paramref name="slot"/> left: its claim ends (a piece from storage goes back) and its undo history is
        /// forgotten (062b review: a later player may get the slot).
        /// </summary>
        public void ServerPlayerLeft(byte slot)
        {
            if (slot == 0) return;
            int piece = Desk.ForgetSlot(slot, out bool fromStorage);
            AfterRelease(piece, fromStorage);
        }

        private void OnDoorChanged(ShopDoorState before, ShopDoorState after)
        {
            if (before != ShopDoorState.Closed || after == ShopDoorState.Closed) return;
            // The shop opens: claims end, the histories are cleared, the new layout is saved once.
            foreach (byte slot in Desk.Holders()) ReleaseInWorld(slot);
            Desk.ClearHistories();
            if (!_changedSinceOpen) return;
            _changedSinceOpen = false;
            if (_saves == null) _saves = GetComponent<Worktown.Game.Save.SaveService>();
            if (_saves == null) _saves = FindFirstObjectByType<Worktown.Game.Save.SaveService>();
            if (_saves != null) _saves.SaveNow(Worktown.Game.Save.SaveTrigger.LayoutChanged);
        }

        /// <summary>Server: one build intent of <paramref name="player"/>; returns the packed answer (BuildDesk.Pack).</summary>
        public byte Handle(BuildInteractor player, BuildIntent intent, bool tooFast)
        {
            if (player == null) return BuildDesk.Pack(BuildOutcome.NotBuilding, null);
            BuildDesk desk = Desk;
            byte slot = SlotOf(player.Owner);
            double now = Time.unscaledTimeAsDouble;
            Lookups();
            if (intent.Kind == BuildIntent.Release)
            {
                if (tooFast) return BuildDesk.Pack(BuildOutcome.Busy, null);
                ReleaseInWorld(slot);
                return BuildDesk.Pack(BuildOutcome.Ok, null);
            }
            var hands = player.GetComponent<PickupInteractor>();
            bool handsEmpty = _pickups == null || hands == null || !_pickups.TryGetHeld(hands, out _);
            var stage = Worktown.Game.Climb.ShopStage.Find();
            bool stageAllows = BuildControlRules.StageAllows(stage != null ? stage.StageNow : Worktown.Game.Climb.ShopStageRules.OpenKitchen);
            BuildOutcome? gate = BuildDesk.Gate(tooFast, ShopDoor.Now, stageAllows, handsEmpty);
            if (gate.HasValue) return BuildDesk.Pack(gate.Value, null);
            desk.Touch(slot, now);

            BuildOutcome outcome;
            LayoutProblem problem = null;
            switch (intent.Kind)
            {
                case BuildIntent.Claim:
                {
                    outcome = desk.TryClaim(slot, intent.PieceId, IsOn(intent.PieceId), now, out int released, out bool fromStorage);
                    if (outcome == BuildOutcome.Ok)
                    {
                        AfterRelease(released, fromStorage);
                        SetClaim(intent.PieceId, slot);
                    }
                    break;
                }
                case BuildIntent.Move:
                {
                    PosePacking.Unpack(intent.Pose, out int x, out int z, out int turns, out _);
                    int held = desk.HeldBy(slot, out _);
                    outcome = desk.TryMove(slot, x, z, turns, IsOn(held), now, out LayoutEdit applied, out problem);
                    if (outcome == BuildOutcome.Ok)
                    {
                        ApplyInWorld(applied);
                        SetClaim(held, 0);
                    }
                    break;
                }
                case BuildIntent.Store:
                {
                    int held = desk.HeldBy(slot, out bool fromStorage);
                    PieceBehaviour piece = LayoutRegistry.Piece(held);
                    var planned = new HashSet<int>();
                    HashSet<long> homes = HomeSpots(desk.Layout);
                    outcome = desk.TryStore(slot, now, h => PlanHome(h, HomeSpot(h, piece), LayoutRegistry.Pieces, piece, homes, planned, IsFree),
                        out LayoutEdit applied, out problem);
                    if (outcome == BuildOutcome.Ok)
                    {
                        if (applied != null) ApplyInWorld(applied);
                        else if (fromStorage) AfterRelease(held, true); // back onto the pallet
                    }
                    break;
                }
                case BuildIntent.TakeOut:
                {
                    int pick = (int)(intent.Pose & 0x7FFFFFFF);
                    outcome = desk.TryTakeOut(slot, pick, now, out int pieceId, out int released, out bool fromStorage);
                    if (outcome == BuildOutcome.Ok)
                    {
                        AfterRelease(released, fromStorage);
                        SpawnOnPallet(pieceId, slot);
                    }
                    break;
                }
                case BuildIntent.Undo:
                {
                    outcome = desk.TryUndo(slot, now, IsOn, out LayoutEdit applied, out problem, out int released, out bool fromStorage);
                    if (outcome == BuildOutcome.Ok)
                    {
                        AfterRelease(released, fromStorage);
                        ApplyInWorld(applied);
                    }
                    break;
                }
                default:
                    outcome = BuildOutcome.Unknown;
                    break;
            }
            if (outcome != BuildOutcome.Ok)
                Debug.Log("[Build] slot " + slot + " " + intent + ": " + outcome + (problem != null ? " (" + problem.Kind + ": " + problem.Text + ")" : ""));
            return BuildDesk.Pack(outcome, outcome == BuildOutcome.Invalid || outcome == BuildOutcome.Blocked ? problem : null);
        }

        private bool IsOn(int pieceId) => pieceId > 0 && _kitchen != null && _kitchen.StationIsOn(pieceId);

        private void Lookups()
        {
            if (_pickups == null) _pickups = GetComponent<PickupService>();
            if (_pickups == null) _pickups = FindFirstObjectByType<PickupService>();
            if (_kitchen == null) _kitchen = GetComponent<KitchenService>();
            if (_kitchen == null) _kitchen = FindFirstObjectByType<KitchenService>();
        }

        private void ReleaseInWorld(byte slot)
        {
            if (slot == 0) return;
            int piece = Desk.Release(slot, out bool fromStorage);
            AfterRelease(piece, fromStorage);
        }

        /// <summary>A piece let go: a placed one stays where it is (its claim mark goes), one from the pallet goes back into storage.</summary>
        private void AfterRelease(int pieceId, bool fromStorage)
        {
            if (pieceId <= 0) return;
            if (!fromStorage)
            {
                SetClaim(pieceId, 0);
                return;
            }
            PieceBehaviour piece = LayoutRegistry.Piece(pieceId);
            if (piece != null) MoveItemsOff(piece); // 062b review: nothing it holds is lost with it
            if (_spawner != null) _spawner.ServerDespawnPiece(pieceId);
        }

        private static void SetClaim(int pieceId, byte slot)
        {
            PieceBehaviour piece = LayoutRegistry.Piece(pieceId);
            if (piece != null && piece.IsServerInitialized) piece.ServerSetClaim(slot);
        }

        /// <summary>A stored piece taken out sits on the pallet (parked), held, until it is placed or stored again.</summary>
        private void SpawnOnPallet(int pieceId, byte slot)
        {
            LayoutPiece record = Desk.Layout.StoredPiece(pieceId);
            if (record == null || _spawner == null) return;
            LayoutPiece pallet = null;
            foreach (LayoutPiece p in Desk.Layout.Pieces)
                if (p.TypeId == PieceCatalog.StoragePallet) { pallet = p; break; }
            int x = pallet != null ? pallet.XMm : record.XMm, z = pallet != null ? pallet.ZMm : record.ZMm;
            PieceBehaviour spawned = _spawner.ServerSpawnPiece(record, x, z, record.Turns, 0);
            if (spawned != null) spawned.ServerSetClaim(slot);
            Debug.Log("[Build] slot " + slot + " took " + record.TypeId + " " + pieceId + " out of storage");
        }

        /// <summary>
        /// The world follows an edit the desk applied (its layout is already the new one; the edit's home changes are those it made): a
        /// store moves the stored piece's tools to their new homes, then its other items off; an undo of a store moves them back once the
        /// piece stands again.
        /// </summary>
        private void ApplyInWorld(LayoutEdit edit)
        {
            if (edit == null) return;
            ShopLayout layout = Desk.Layout;
            switch (edit.Kind)
            {
                case LayoutEditKind.Move:
                case LayoutEditKind.TakeOut:
                {
                    LayoutPiece placed = layout.Piece(edit.PieceId);
                    if (placed == null) break;
                    PieceBehaviour piece = LayoutRegistry.Piece(edit.PieceId);
                    if (piece == null && _spawner != null) piece = _spawner.ServerSpawnPiece(placed, placed.XMm, placed.ZMm, placed.Turns, 0); // an undo of a store
                    if (piece == null) break;
                    MovePiece(piece, placed, layout);
                    piece.ServerSetClaim(0);
                    MoveTools(edit.HomeChanges);
                    break;
                }
                case LayoutEditKind.Store:
                {
                    PieceBehaviour piece = LayoutRegistry.Piece(edit.PieceId);
                    MoveTools(edit.HomeChanges);
                    if (piece != null) MoveItemsOff(piece);
                    if (_spawner != null) _spawner.ServerDespawnPiece(edit.PieceId);
                    break;
                }
            }
            if (_spawner != null) _spawner.ServerSetCurrent(layout);
            _changedSinceOpen = true;
            Debug.Log("[Build] " + edit.Kind + " piece " + edit.PieceId + (edit.Kind == LayoutEditKind.Store ? string.Empty
                : " to (" + edit.XMm + ", " + edit.ZMm + ") turns " + edit.Turns) + "; " + layout.Pieces.Count + " placed, " + layout.Stored.Count + " stored"
                + (edit.HomeChanges != null && edit.HomeChanges.Count > 0 ? "; " + edit.HomeChanges.Count + " item homes moved" : string.Empty));
        }

        private bool IsFree(int surfaceId, int spot) => _pickups != null && _pickups.IsSpotFree(surfaceId, spot);

        /// <summary>Where a home's spot is in the world (else the piece's centre).</summary>
        private static Vector3 HomeSpot(ItemHome home, PieceBehaviour piece)
        {
            PlaceSurface from = LayoutRegistry.Surface(SurfaceId.Of(home.PieceId, home.LocalSurface));
            if (from == null && piece != null)
                foreach (PlaceSurface s in piece.Surfaces)
                    if (s != null && s.Id == SurfaceId.Of(home.PieceId, home.LocalSurface)) from = s;
            return from != null && home.Spot < from.SpotCount ? from.SpotWorldPosition(home.Spot) : piece != null ? piece.transform.position : Vector3.zero;
        }

        /// <summary>Every item home's spot as a key (surface id and spot), so nothing else is put on a home.</summary>
        public static HashSet<long> HomeSpots(ShopLayout layout)
        {
            var used = new HashSet<long>();
            if (layout != null)
                foreach (ItemHome h in layout.Homes) used.Add(HomeKey(SurfaceId.Of(h.PieceId, h.LocalSurface), h.Spot));
            return used;
        }

        private static long HomeKey(int surfaceId, int spot) => ((long)surfaceId << 32) | (uint)spot;

        /// <summary>
        /// 062b review: a new home for <paramref name="home"/> (on the piece <paramref name="leaving"/>, being stored; its spot at
        /// <paramref name="from"/>): the nearest spot on another placed worktop of <paramref name="pieces"/> that is free
        /// (<paramref name="isFree"/>: the spot table and the world), no other home (<paramref name="homes"/>) and not planned already in
        /// this store (<paramref name="planned"/>, which gets it); null when there is none (the home stays: its tool waits for the piece).
        /// </summary>
        public static ItemHome PlanHome(ItemHome home, Vector3 from, IEnumerable<PieceBehaviour> pieces, PieceBehaviour leaving, ISet<long> homes,
            ISet<int> planned, System.Func<int, int, bool> isFree)
        {
            if (home == null || !NearestWorktopSpot(from, pieces, leaving, homes, planned, isFree, out PlaceSurface surface, out int spot)) return null;
            planned.Add(SurfaceKey(surface.Id, spot));
            return new ItemHome(home.ItemKind, SurfaceId.PieceOf(surface.Id), SurfaceId.LocalOf(surface.Id), spot);
        }

        /// <summary>
        /// 062b review: moves each tool of a home change from its old home's spot to its new one: the spot table's occupant there
        /// (<paramref name="occupantOf"/>), only if it is the home's kind of item (<see cref="KindOf"/>; else someone put another thing
        /// there). <paramref name="put"/> places it (false: the new spot is taken). Returns how many moved.
        /// </summary>
        public static int MoveTools(IReadOnlyList<HomeChange> changes, System.Func<int, int, Component> occupantOf, System.Func<int, PlaceSurface> surfaceOf,
            System.Func<Component, PlaceSurface, int, bool> put)
        {
            int moved = 0;
            if (changes == null || occupantOf == null || surfaceOf == null || put == null) return 0;
            foreach (HomeChange change in changes)
            {
                if (change == null) continue;
                Component tool = occupantOf(SurfaceId.Of(change.Old.PieceId, change.Old.LocalSurface), change.Old.Spot);
                PlaceSurface target = surfaceOf(SurfaceId.Of(change.New.PieceId, change.New.LocalSurface));
                if (tool == null || target == null || KindOf(tool.gameObject) != change.Old.ItemKind) continue;
                if (put(tool, target, change.New.Spot)) moved++;
            }
            return moved;
        }

        private void MoveTools(IReadOnlyList<HomeChange> changes)
        {
            if (_pickups == null) return;
            MoveTools(changes, (surfaceId, spot) => _pickups.OccupantOf(surfaceId, spot), LayoutRegistry.Surface, (tool, target, spot) =>
            {
                var nob = tool.GetComponent<NetworkObject>();
                return nob != null && _pickups.ServerPutOnSpot(nob, target, spot, LayoutSpawner.HomePose(tool.gameObject, target, spot));
            });
        }

        /// <summary>The item home kind of an item ("knife", "board", "plate", ...; ItemHome.ItemKind), or null.</summary>
        public static string KindOf(GameObject item)
        {
            if (item == null) return null;
            if (item.TryGetComponent(out Knife _)) return "knife";
            if (item.TryGetComponent(out Spatula _)) return "spatula";
            if (item.TryGetComponent(out WipeCloth _)) return "cloth";
            if (item.TryGetComponent(out Vessel vessel)) return vessel.Kind.ToString().ToLowerInvariant(); // board, pan, pot, plate, bowl
            return null;
        }

        private static int SurfaceKey(int surfaceId, int spot) => surfaceId * 64 + spot;

        /// <summary>
        /// Moves a piece (from its synced pose to <paramref name="placed"/>) and what lies on it (the spot table's items on its surfaces);
        /// then pushes loose floor items out from under its new footprint.
        /// </summary>
        private void MovePiece(PieceBehaviour piece, LayoutPiece placed, ShopLayout layout)
        {
            var carried = new List<NetworkObject>();
            foreach (NetworkObject item in LooseItems())
                if (LiesOn(item, piece)) carried.Add(item);
            Carry(piece, placed, carried, (item, position, rotation) =>
            {
                var nob = item.GetComponent<NetworkObject>();
                if (_pickups != null && nob != null) _pickups.ServerCarry(nob, position, rotation);
                else item.transform.SetPositionAndRotation(position, rotation);
            });
            if (!Desk.Catalog.TryGet(placed.TypeId, out PieceDef def) || LayoutRules.Band(def.Mount) != 0) return;
            RectMm footprint = def.Footprint(placed);
            WalkGrid grid = null;
            foreach (NetworkObject item in LooseItems())
            {
                Vector3 p = item.transform.position;
                if (_pickups != null && _pickups.TryGetSpotOf(item, out _, out _)) continue; // on a top, not on the floor
                if (item.transform.IsChildOf(piece.transform)) continue;
                if (!footprint.Contains(Mathf.RoundToInt(p.x * 1000f), Mathf.RoundToInt(p.z * 1000f))) continue;
                grid ??= WalkGrid.ForLayout(layout, Desk.Room, Desk.Catalog);
                if (!NearestFreeCell(grid, p, out Vector3 cell)) continue;
                _pickups?.ServerPutLoose(item, new Vector3(cell.x, p.y, cell.z), item.transform.rotation);
                Debug.Log("[Build] " + item.name + " pushed out from under " + piece.name);
            }
        }

        /// <summary>
        /// 062b review: moves <paramref name="piece"/> to <paramref name="to"/> and every item in <paramref name="items"/> with it: each keeps
        /// its pose in the piece's frame. The start is the piece's synced pose (PosePacked), not its transform, so a host's predicted move
        /// (the transform already at the target) still carries the items. <paramref name="carry"/> puts an item at its new pose.
        /// </summary>
        public static void Carry(PieceBehaviour piece, LayoutPiece to, IEnumerable<Component> items, System.Action<Component, Vector3, Quaternion> carry)
        {
            if (piece == null || to == null) return;
            float y = piece.transform.position.y;
            Matrix4x4 before = PoseMatrix(piece.XMm, piece.ZMm, piece.Turns, y).inverse;
            var locals = new List<(Component Item, Matrix4x4 Local)>();
            if (items != null)
                foreach (Component item in items)
                    if (item != null) locals.Add((item, before * item.transform.localToWorldMatrix));
            piece.ServerSetPose(PosePacking.Pack(to.XMm, to.ZMm, to.Turns, to.LengthMm));
            Physics.SyncTransforms();
            Matrix4x4 after = PoseMatrix(to.XMm, to.ZMm, to.Turns, y);
            foreach ((Component item, Matrix4x4 local) in locals)
            {
                Matrix4x4 world = after * local;
                carry?.Invoke(item, world.GetColumn(3), world.rotation);
            }
        }

        private static Matrix4x4 PoseMatrix(int xMm, int zMm, int turns, float y) =>
            Matrix4x4.TRS(new Vector3(xMm / 1000f, y, zMm / 1000f), Quaternion.Euler(0f, PosePacking.Yaw(turns), 0f), Vector3.one);

        /// <summary>
        /// The other items on a piece leaving the layout (stored, or put back on the pallet; its tools went to their new homes first): each to
        /// the nearest free worktop spot that is no item's home, else onto the floor next to where the piece stood. One walk grid per call.
        /// </summary>
        private void MoveItemsOff(PieceBehaviour piece)
        {
            HashSet<long> homes = HomeSpots(Desk.Layout);
            var planned = new HashSet<int>();
            WalkGrid grid = null;
            foreach (NetworkObject item in LooseItems())
            {
                if (!LiesOn(item, piece)) continue;
                if (NearestWorktopSpot(item.transform.position, LayoutRegistry.Pieces, piece, homes, planned, IsFree, out PlaceSurface surface, out int spot))
                {
                    Pose pose = LayoutSpawner.HomePose(item.gameObject, surface, spot);
                    if (_pickups != null && _pickups.ServerPutOnSpot(item, surface, spot, pose))
                    {
                        planned.Add(SurfaceKey(surface.Id, spot));
                        continue;
                    }
                }
                grid ??= WalkGrid.ForLayout(Desk.Layout, Desk.Room, Desk.Catalog);
                Vector3 at = NearestFreeCell(grid, item.transform.position, out Vector3 cell) ? cell : piece.transform.position;
                _pickups?.ServerPutLoose(item, new Vector3(at.x, 0.25f, at.z), item.transform.rotation);
            }
        }

        private static bool NearestWorktopSpot(Vector3 from, IEnumerable<PieceBehaviour> pieces, PieceBehaviour leaving, ISet<long> homes, ISet<int> planned,
            System.Func<int, int, bool> isFree, out PlaceSurface best, out int bestSpot)
        {
            best = null;
            bestSpot = -1;
            float bestSqr = float.MaxValue;
            if (pieces == null) return false;
            foreach (PieceBehaviour p in pieces)
            {
                if (p == null || p == leaving || p.Parked || p.TypeId != PieceCatalog.Worktop) continue;
                foreach (PlaceSurface surface in p.Surfaces)
                {
                    if (surface == null || !surface.gameObject.activeInHierarchy || surface.IsBurner) continue;
                    for (int s = 0; s < surface.SpotCount; s++)
                    {
                        float sqr = (surface.SpotWorldPosition(s) - from).sqrMagnitude;
                        if (sqr >= bestSqr || (planned != null && planned.Contains(SurfaceKey(surface.Id, s)))
                            || (homes != null && homes.Contains(HomeKey(surface.Id, s)))) continue;
                        if (isFree != null && !isFree(surface.Id, s)) continue;
                        bestSqr = sqr;
                        best = surface;
                        bestSpot = s;
                    }
                }
            }
            return best != null;
        }

        private static bool NearestFreeCell(WalkGrid grid, Vector3 from, out Vector3 cell)
        {
            cell = Vector3.zero;
            long bestSqr = long.MaxValue;
            int fx = Mathf.RoundToInt(from.x * 1000f), fz = Mathf.RoundToInt(from.z * 1000f);
            for (int i = 0; i < grid.CellCount; i++)
            {
                if (!grid.IsFree(i)) continue;
                PointMm c = grid.Centre(i);
                long dx = c.X - fx, dz = c.Z - fz, sqr = dx * dx + dz * dz;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                cell = new Vector3(c.X / 1000f, 0f, c.Z / 1000f);
            }
            return bestSqr != long.MaxValue;
        }

        /// <summary>Every spawned item that nobody holds (held items are never moved).</summary>
        private List<NetworkObject> LooseItems()
        {
            var items = new List<NetworkObject>();
            if (_networkManager == null) return items;
            foreach (NetworkObject nob in _networkManager.ServerManager.Objects.Spawned.Values)
                if (nob != null && nob.TryGetComponent(out Pickup pickup) && !pickup.IsHeld) items.Add(nob);
            return items;
        }

        /// <summary>
        /// 062b review: whether an item lies on <paramref name="piece"/>: the spot table has it on one of the piece's surfaces and it still
        /// lies near that spot; an item the spot table does not know (a delivery box on its delivery spot) by the old footprint check.
        /// </summary>
        private bool LiesOn(NetworkObject item, PieceBehaviour piece)
        {
            if (item == null || piece == null) return false;
            bool known = _pickups != null && _pickups.TryGetSpotOf(item, out _, out _);
            int surfaceId = SurfaceId.None, spot = -1;
            if (known) _pickups.TryGetSpotOf(item, out surfaceId, out spot);
            bool nearSpot = false;
            if (known && SurfaceId.PieceOf(surfaceId) == piece.PieceId)
            {
                PlaceSurface surface = null;
                foreach (PlaceSurface s in piece.Surfaces)
                    if (s != null && s.Id == surfaceId) surface = s;
                nearSpot = surface != null && spot >= 0 && spot < surface.SpotCount
                    && Vector3.ProjectOnPlane(item.transform.position - surface.SpotWorldPosition(spot), Vector3.up).sqrMagnitude <= SpotTolerance * SpotTolerance;
            }
            if (!Desk.Catalog.TryGet(piece.TypeId, out PieceDef def)) return false;
            var at = new LayoutPiece(piece.PieceId, piece.TypeId, piece.XMm, piece.ZMm, piece.Turns, piece.LengthMm, 1, 0);
            return OnPiece(item.transform.position, known, SurfaceId.PieceOf(surfaceId), nearSpot, at, def, item.transform.IsChildOf(piece.transform));
        }

        /// <summary>
        /// 062b re-review, pure: an item on a piece. In the spot table (<paramref name="inSpotTable"/>): on it when its surface is the piece's
        /// (<paramref name="spotPieceId"/>) and it still lies there (<paramref name="nearSpot"/>). Not in it (a delivery box): above the floor
        /// (over <see cref="OnPieceHeight"/>, under 2.2 m) and inside the piece's footprint (20 mm margin). Never one of the piece's own parts.
        /// </summary>
        public static bool OnPiece(Vector3 position, bool inSpotTable, int spotPieceId, bool nearSpot, LayoutPiece piece, PieceDef def, bool partOfPiece)
        {
            if (piece == null || def == null || partOfPiece) return false;
            if (inSpotTable) return spotPieceId == piece.Id && nearSpot;
            if (position.y <= OnPieceHeight || position.y >= 2.2f) return false;
            RectMm r = def.Footprint(piece);
            int x = Mathf.RoundToInt(position.x * 1000f), z = Mathf.RoundToInt(position.z * 1000f);
            return x >= r.MinX - 20 && x <= r.MaxX + 20 && z >= r.MinZ - 20 && z <= r.MaxZ + 20;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => _instance = null;
    }
}
