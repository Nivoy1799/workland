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
    /// claim (ClaimedBySlot), a stored piece despawned, a taken-out piece spawned on the pallet, and the items that travel: items lying on a
    /// moved piece keep their surface id and spot and move with it; loose items under its new footprint go to the nearest free floor cell;
    /// the items of a stored piece go to the nearest free worktop spot, else the floor; a held item is never touched. The layout in the world
    /// (LayoutSpawner.Current) follows every change, so the save holds it. When the shop opens the claims end, the undo histories are
    /// cleared (question 4) and the game is saved once (SaveTrigger.LayoutChanged) if the layout changed. A disconnect or 60 s without
    /// input releases a claim. Lives next to the LayoutSpawner (it adds this component).
    /// </summary>
    public sealed class BuildService : MonoBehaviour
    {
        /// <summary>How far above the floor an item lies on a piece (lower: on the floor).</summary>
        private const float OnPieceHeight = 0.4f;

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
        public BuildDesk Desk => _desk;

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
            _desk = new BuildDesk(_spawner != null ? _spawner.Catalog : null, _spawner != null ? _spawner.Room : null);
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
            _desk.Reset(layout);
            _changedSinceOpen = false;
        }

        private void Update()
        {
            if (_networkManager == null || !_networkManager.IsServerStarted || Time.unscaledTime < _nextLapseCheck) return;
            _nextLapseCheck = Time.unscaledTime + 1f;
            foreach (byte slot in _desk.Lapsed(Time.unscaledTimeAsDouble))
            {
                Debug.Log("[Build] slot " + slot + "'s claim lapsed (60 s without input)");
                ReleaseInWorld(slot);
            }
        }

        private void OnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped) return;
            ReleaseInWorld(SlotOf(connection)); // a disconnect releases the claim
        }

        private void OnDoorChanged(ShopDoorState before, ShopDoorState after)
        {
            if (before != ShopDoorState.Closed || after == ShopDoorState.Closed) return;
            // The shop opens: claims end, the histories are cleared, the new layout is saved once.
            foreach (byte slot in _desk.Holders()) ReleaseInWorld(slot);
            _desk.ClearHistories();
            if (!_changedSinceOpen) return;
            _changedSinceOpen = false;
            if (_saves == null) _saves = GetComponent<Worktown.Game.Save.SaveService>();
            if (_saves == null) _saves = FindFirstObjectByType<Worktown.Game.Save.SaveService>();
            if (_saves != null) _saves.SaveNow(Worktown.Game.Save.SaveTrigger.LayoutChanged);
        }

        /// <summary>Server: one build intent of <paramref name="player"/>; returns the packed answer (BuildDesk.Pack).</summary>
        public byte Handle(BuildInteractor player, BuildIntent intent, bool tooFast)
        {
            if (player == null || _desk == null) return BuildDesk.Pack(BuildOutcome.NotBuilding, null);
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
            _desk.Touch(slot, now);

            BuildOutcome outcome;
            LayoutProblem problem = null;
            switch (intent.Kind)
            {
                case BuildIntent.Claim:
                {
                    bool on = _kitchen != null && _kitchen.StationIsOn(intent.PieceId);
                    outcome = _desk.TryClaim(slot, intent.PieceId, on, now, out int released, out bool fromStorage);
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
                    int held = _desk.HeldBy(slot, out _);
                    outcome = _desk.TryMove(slot, x, z, turns, now, out LayoutEdit applied, out problem);
                    if (outcome == BuildOutcome.Ok)
                    {
                        ApplyInWorld(applied);
                        SetClaim(held, 0);
                    }
                    break;
                }
                case BuildIntent.Store:
                {
                    int held = _desk.HeldBy(slot, out bool fromStorage);
                    outcome = _desk.TryStore(slot, now, out LayoutEdit applied, out problem);
                    if (outcome == BuildOutcome.Ok)
                    {
                        if (applied != null) ApplyInWorld(applied);
                        else if (fromStorage) _spawner.ServerDespawnPiece(held); // back onto the pallet
                    }
                    break;
                }
                case BuildIntent.TakeOut:
                {
                    int pick = (int)(intent.Pose & 0x7FFFFFFF);
                    outcome = _desk.TryTakeOut(slot, pick, now, out int pieceId, out int released, out bool fromStorage);
                    if (outcome == BuildOutcome.Ok)
                    {
                        AfterRelease(released, fromStorage);
                        SpawnOnPallet(pieceId, slot);
                    }
                    break;
                }
                case BuildIntent.Undo:
                {
                    outcome = _desk.TryUndo(slot, now, out LayoutEdit applied, out problem, out int released, out bool fromStorage);
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
            int piece = _desk.Release(slot, out bool fromStorage);
            AfterRelease(piece, fromStorage);
        }

        /// <summary>A piece let go: a placed one stays where it is (its claim mark goes), one from the pallet goes back into storage.</summary>
        private void AfterRelease(int pieceId, bool fromStorage)
        {
            if (pieceId <= 0) return;
            if (fromStorage) _spawner.ServerDespawnPiece(pieceId);
            else SetClaim(pieceId, 0);
        }

        private static void SetClaim(int pieceId, byte slot)
        {
            PieceBehaviour piece = LayoutRegistry.Piece(pieceId);
            if (piece != null && piece.IsServerInitialized) piece.ServerSetClaim(slot);
        }

        /// <summary>A stored piece taken out sits on the pallet, held, until it is placed or stored again.</summary>
        private void SpawnOnPallet(int pieceId, byte slot)
        {
            LayoutPiece record = _desk.Layout.StoredPiece(pieceId);
            if (record == null) return;
            LayoutPiece pallet = null;
            foreach (LayoutPiece p in _desk.Layout.Pieces)
                if (p.TypeId == PieceCatalog.StoragePallet) { pallet = p; break; }
            int x = pallet != null ? pallet.XMm : record.XMm, z = pallet != null ? pallet.ZMm : record.ZMm;
            PieceBehaviour spawned = _spawner.ServerSpawnPiece(record, x, z, record.Turns, 0);
            if (spawned != null) spawned.ServerSetClaim(slot);
            Debug.Log("[Build] slot " + slot + " took " + record.TypeId + " " + pieceId + " out of storage");
        }

        /// <summary>The world follows an edit the desk applied (its layout is already the new one).</summary>
        private void ApplyInWorld(LayoutEdit edit)
        {
            if (edit == null) return;
            ShopLayout layout = _desk.Layout;
            switch (edit.Kind)
            {
                case LayoutEditKind.Move:
                case LayoutEditKind.TakeOut:
                {
                    LayoutPiece placed = layout.Piece(edit.PieceId);
                    if (placed == null) break;
                    PieceBehaviour piece = LayoutRegistry.Piece(edit.PieceId);
                    if (piece == null) piece = _spawner.ServerSpawnPiece(placed, placed.XMm, placed.ZMm, placed.Turns, 0); // an undo of a store
                    if (piece == null) break;
                    MovePiece(piece, placed, layout);
                    piece.ServerSetClaim(0);
                    break;
                }
                case LayoutEditKind.Store:
                {
                    PieceBehaviour piece = LayoutRegistry.Piece(edit.PieceId);
                    if (piece != null) MoveItemsOff(piece);
                    _spawner.ServerDespawnPiece(edit.PieceId);
                    break;
                }
            }
            _spawner.ServerSetCurrent(layout);
            _changedSinceOpen = true;
            Debug.Log("[Build] " + edit.Kind + " piece " + edit.PieceId + (edit.Kind == LayoutEditKind.Store ? string.Empty
                : " to (" + edit.XMm + ", " + edit.ZMm + ") turns " + edit.Turns) + "; " + layout.Pieces.Count + " placed, " + layout.Stored.Count + " stored");
        }

        /// <summary>Moves a piece and what lies on it; then pushes loose floor items out from under its new footprint.</summary>
        private void MovePiece(PieceBehaviour piece, LayoutPiece placed, ShopLayout layout)
        {
            var carried = new List<(NetworkObject Item, Matrix4x4 Local)>();
            Matrix4x4 before = piece.transform.worldToLocalMatrix;
            foreach (NetworkObject item in LooseItems())
                if (LiesOn(item, piece)) carried.Add((item, before * item.transform.localToWorldMatrix));
            piece.ServerSetPose(PosePacking.Pack(placed.XMm, placed.ZMm, placed.Turns, placed.LengthMm));
            Physics.SyncTransforms();
            Matrix4x4 after = piece.transform.localToWorldMatrix;
            foreach ((NetworkObject item, Matrix4x4 local) in carried)
            {
                Matrix4x4 world = after * local;
                _pickups.ServerCarry(item, world.GetColumn(3), world.rotation);
            }
            if (!_desk.Catalog.TryGet(placed.TypeId, out PieceDef def) || LayoutRules.Band(def.Mount) != 0) return;
            RectMm footprint = def.Footprint(placed);
            WalkGrid grid = null;
            foreach (NetworkObject item in LooseItems())
            {
                Vector3 p = item.transform.position;
                if (p.y > OnPieceHeight || item.transform.IsChildOf(piece.transform)) continue;
                if (!footprint.Contains(Mathf.RoundToInt(p.x * 1000f), Mathf.RoundToInt(p.z * 1000f))) continue;
                grid ??= WalkGrid.ForLayout(layout, _desk.Room, _desk.Catalog);
                if (!NearestFreeCell(grid, p, out Vector3 cell)) continue;
                _pickups.ServerPutLoose(item, new Vector3(cell.x, p.y, cell.z), item.transform.rotation);
                Debug.Log("[Build] " + item.name + " pushed out from under " + piece.name);
            }
        }

        /// <summary>A stored piece's items go to the nearest free worktop spot, else onto the floor next to where the piece stood.</summary>
        private void MoveItemsOff(PieceBehaviour piece)
        {
            foreach (NetworkObject item in LooseItems())
            {
                if (!LiesOn(item, piece)) continue;
                if (TryNearestWorktopSpot(item, piece, out PlaceSurface surface, out int spot))
                {
                    Pose pose = LayoutSpawner.HomePose(item.gameObject, surface, spot);
                    if (_pickups != null && _pickups.ServerPutOnSpot(item, surface, spot, pose)) continue;
                }
                WalkGrid grid = WalkGrid.ForLayout(_desk.Layout, _desk.Room, _desk.Catalog);
                Vector3 at = NearestFreeCell(grid, item.transform.position, out Vector3 cell) ? cell : piece.transform.position;
                _pickups.ServerPutLoose(item, new Vector3(at.x, 0.25f, at.z), item.transform.rotation);
            }
        }

        private bool TryNearestWorktopSpot(NetworkObject item, PieceBehaviour leaving, out PlaceSurface best, out int bestSpot)
        {
            best = null;
            bestSpot = -1;
            float bestSqr = float.MaxValue;
            Vector3 from = item.transform.position;
            foreach (PieceBehaviour p in LayoutRegistry.Pieces)
            {
                if (p == null || p == leaving || p.TypeId != PieceCatalog.Worktop) continue;
                foreach (PlaceSurface surface in p.Surfaces)
                {
                    if (surface == null || !surface.gameObject.activeInHierarchy || surface.IsBurner) continue;
                    for (int s = 0; s < surface.SpotCount; s++)
                    {
                        float sqr = (surface.SpotWorldPosition(s) - from).sqrMagnitude;
                        if (sqr >= bestSqr || _pickups == null || !_pickups.IsSpotFree(surface.Id, s)) continue;
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

        /// <summary>Whether an item lies on top of <paramref name="piece"/> (above the floor, inside its footprint; not one of its own parts).</summary>
        private bool LiesOn(NetworkObject item, PieceBehaviour piece)
        {
            if (item.transform.IsChildOf(piece.transform)) return false;
            Vector3 p = item.transform.position;
            if (p.y <= OnPieceHeight) return false;
            if (!_desk.Catalog.TryGet(piece.TypeId, out PieceDef def)) return false;
            var at = new LayoutPiece(piece.PieceId, piece.TypeId, piece.XMm, piece.ZMm, piece.Turns, piece.LengthMm, 1, 0);
            RectMm r = def.Footprint(at);
            int x = Mathf.RoundToInt(p.x * 1000f), z = Mathf.RoundToInt(p.z * 1000f);
            return x >= r.MinX - 20 && x <= r.MaxX + 20 && z >= r.MinZ - 20 && z <= r.MaxZ + 20 && p.y < 2.2f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => _instance = null;
    }
}
