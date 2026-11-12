using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Object;
using UnityEngine;
using Worktown.Game.Cooking;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;
using Worktown.Sim.Layout;

namespace Worktown.Game.Layout
{
    /// <summary>
    /// Spec 062a: puts a <see cref="ShopLayout"/> into the world. Server (<see cref="ServerBegin"/>, called by SaveService when a game
    /// begins, before the first player and before a shift opens): the layout is checked (every piece type known, the required pieces there;
    /// DefaultLayout.Backfill adds a missing one; spec 062a Part B: LayoutRules.Validate in the default room), an unusable one is replaced by
    /// the default (logged, and the feed line <see cref="ResetLine"/>); then every piece is spawned in ascending id (its id, pose and number set before the spawn, so a late
    /// joiner gets them in the spawn payload; nested NetworkObjects such as the fryer baskets spawn with their piece) and then the items at
    /// their homes (as the builder's PlaceOnSpot did). Offline (<see cref="InstantiateOffline"/>): the same objects without the network,
    /// for the Editor and the tests. Lives next to the NetworkManager. <see cref="Spawned"/> tells the CustomerService (on the same object),
    /// which builds the guests' route from the layout (NpcRoutes).
    /// </summary>
    public sealed class LayoutSpawner : MonoBehaviour
    {
        public const string ResetLine = "The kitchen was reset to the standard layout";

        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private PiecePrefabs _prefabs;
        [SerializeField] private ServeFeed _feed;

        private readonly List<NetworkObject> _spawnedPieces = new List<NetworkObject>();
        // Spec 062b (062a review follow-up): the items spawned at their homes, so a second ServerBegin despawns them before it spawns new ones.
        private readonly List<NetworkObject> _spawnedItems = new List<NetworkObject>();

        private void Awake()
        {
            // Spec 062b: build mode's server side lives next to the spawner (it moves, spawns and despawns the pieces).
            if (GetComponent<BuildService>() == null) gameObject.AddComponent<BuildService>();
        }
        private readonly PieceCatalog _catalog = PieceCatalog.CreateDefault();
        private readonly RoomDef _room = RoomDef.CreateDefault();

        /// <summary>Spec 062a, server: raised after a layout was spawned, with it.</summary>
        public event System.Action<ShopLayout> Spawned;

        /// <summary>Server: the layout in the world (null before a game began).</summary>
        public ShopLayout Current { get; private set; }
        public PiecePrefabs Prefabs => _prefabs;
        public PieceCatalog Catalog => _catalog;
        /// <summary>Spec 062a: the fixed shell the layouts stand in (the rules' and the guests' routes' room).</summary>
        public RoomDef Room => _room;

        /// <summary>
        /// Server: spawns <paramref name="layout"/> (null: the default) and its items. Returns the layout in the world. Any pieces this
        /// spawner put there before are despawned first, and so are the items it spawned at their homes (spec 062b) unless a player holds
        /// one (that one stays in the hands; the KitchenService owns the items once spawned).
        /// </summary>
        public ShopLayout ServerBegin(ShopLayout layout)
        {
            if (_networkManager == null || !_networkManager.IsServerStarted)
            {
                Debug.LogWarning("[Layout] no layout spawned: " + (_networkManager == null ? "no NetworkManager" : "the server is not started"));
                return null;
            }
            ShopLayout standard = DefaultLayout.Load(_catalog);
            ShopLayout chosen = Choose(layout, standard, _catalog, _room, _prefabs, out bool reset, out List<string> notes);
            foreach (string note in notes) Debug.Log("[Layout] " + note);
            if (reset)
            {
                // The bad layout is logged, not kept (the next save writes the standard one).
                Debug.LogWarning("[Layout] the saved layout could not be used (" + string.Join("; ", notes) + "); the standard layout is spawned. "
                    + "The rejected layout: " + JsonUtility.ToJson(LayoutCodec.ToPart(layout)));
                string line = FeedLineFor(reset);
                if (_feed != null && line != null) _feed.ServerAdd(line);
            }
            DespawnItems();
            DespawnPieces();
            Current = chosen;
            int pieces = 0, items = 0;
            foreach (LayoutPiece piece in chosen.Pieces)
            {
                PieceBehaviour prefab = _prefabs != null ? _prefabs.Piece(piece.TypeId) : null;
                if (prefab == null) continue; // an unknown type: kept in the save, not spawned
                NetworkObject nob = Instantiate(prefab.gameObject).GetComponent<NetworkObject>();
                PieceBehaviour behaviour = nob.GetComponent<PieceBehaviour>();
                behaviour.Configure(piece.Id, PosePacking.Pack(piece.XMm, piece.ZMm, piece.Turns, piece.LengthMm), chosen.NumberOf(piece.Id));
                nob.name = prefab.name + " " + piece.Id;
                _networkManager.ServerManager.Spawn(nob);
                _spawnedPieces.Add(nob);
                pieces++;
            }
            foreach (ItemHome home in chosen.Homes)
            {
                NetworkObject itemPrefab = _prefabs != null ? _prefabs.Item(home.ItemKind) : null;
                PlaceSurface surface = LayoutRegistry.Surface(SurfaceId.Of(home.PieceId, home.LocalSurface));
                if (itemPrefab == null || surface == null || home.Spot >= surface.SpotCount)
                {
                    Debug.LogWarning("[Layout] the " + home.ItemKind + "'s home (piece " + home.PieceId + ", surface " + home.LocalSurface
                        + ", spot " + home.Spot + ") is not in the world; it is not spawned");
                    continue;
                }
                NetworkObject item = _networkManager.GetPooledInstantiated(itemPrefab, true);
                Pose pose = HomePose(itemPrefab.gameObject, surface, home.Spot);
                item.transform.SetPositionAndRotation(pose.position, pose.rotation);
                item.name = ItemName(home.ItemKind, chosen, home);
                _networkManager.ServerManager.Spawn(item);
                _spawnedItems.Add(item);
                items++;
            }
            Debug.Log("[Layout] spawned " + pieces + " pieces and " + items + " items");
            try { Spawned?.Invoke(chosen); }
            catch (System.Exception e) { Debug.LogException(e); }
            return chosen;
        }

        /// <summary>
        /// Spec 062a, pure: the layout to spawn. Null or empty: the standard one. A layout with a piece type that is known to the catalog
        /// but has no prefab, or that misses a required piece even after the backfill, is replaced by the standard one
        /// (<paramref name="reset"/>). Unknown types stay in the layout (and in the save) but are not spawned.
        /// </summary>
        public static ShopLayout Choose(ShopLayout layout, ShopLayout standard, PieceCatalog catalog, PiecePrefabs prefabs, out bool reset,
            out List<string> notes) => Choose(layout, standard, catalog, RoomDef.CreateDefault(), prefabs, out reset, out notes);

        /// <summary>
        /// Spec 062a Part B: as above, and a layout that breaks a rule of LayoutRules.Validate in <paramref name="room"/> (an overlap, a
        /// blocked aisle, an unreachable station, ...) is replaced by the standard one too; each problem is a note. No repair is tried
        /// (question 6).
        /// </summary>
        public static ShopLayout Choose(ShopLayout layout, ShopLayout standard, PieceCatalog catalog, RoomDef room, PiecePrefabs prefabs,
            out bool reset, out List<string> notes)
        {
            notes = new List<string>();
            reset = false;
            if (layout == null || layout.Pieces.Count == 0) return standard;
            ShopLayout filled = DefaultLayout.Backfill(layout, standard, catalog, null, notes);
            foreach (PieceDef row in catalog.Rows)
                if (row.Required && filled.CountOf(row.TypeId) == 0)
                {
                    notes.Add("the layout has no " + row.TypeId);
                    reset = true;
                }
            if (prefabs != null)
                foreach (LayoutPiece p in filled.Pieces)
                    if (catalog.Contains(p.TypeId) && prefabs.Piece(p.TypeId) == null)
                    {
                        notes.Add("no prefab for " + p.TypeId);
                        reset = true;
                    }
            if (!reset && room != null)
            {
                LayoutReport report = LayoutRules.Validate(filled, room, catalog);
                if (!report.IsValid)
                {
                    foreach (string line in report.Lines()) notes.Add("rule: " + line);
                    reset = true;
                }
            }
            return reset ? standard : filled;
        }

        /// <summary>Spec 062a, pure: the serve feed's line after a layout was chosen (null when the saved one was used).</summary>
        public static string FeedLineFor(bool reset) => reset ? ResetLine : null;

        /// <summary>Spec 062b, server: the layout in the world after a build action (BuildService); the save captures it.</summary>
        public void ServerSetCurrent(ShopLayout layout)
        {
            if (layout != null) Current = layout;
        }

        /// <summary>
        /// Spec 062b, server: spawns one piece of <paramref name="piece"/>'s type at <paramref name="xMm"/>, <paramref name="zMm"/> with
        /// <paramref name="turns"/> (a stored piece taken out, or put back by an undo). Returns it, or null without a prefab.
        /// </summary>
        public PieceBehaviour ServerSpawnPiece(LayoutPiece piece, int xMm, int zMm, int turns, int number)
        {
            if (piece == null || _networkManager == null || !_networkManager.IsServerStarted) return null;
            PieceBehaviour prefab = _prefabs != null ? _prefabs.Piece(piece.TypeId) : null;
            if (prefab == null) return null;
            NetworkObject nob = Instantiate(prefab.gameObject).GetComponent<NetworkObject>();
            PieceBehaviour behaviour = nob.GetComponent<PieceBehaviour>();
            behaviour.Configure(piece.Id, PosePacking.Pack(xMm, zMm, turns, piece.LengthMm), number);
            nob.name = prefab.name + " " + piece.Id;
            _networkManager.ServerManager.Spawn(nob);
            _spawnedPieces.Add(nob);
            return behaviour;
        }

        /// <summary>Spec 062b, server: despawns the spawned piece with id <paramref name="pieceId"/> (stored). False when none.</summary>
        public bool ServerDespawnPiece(int pieceId)
        {
            for (int i = _spawnedPieces.Count - 1; i >= 0; i--)
            {
                NetworkObject nob = _spawnedPieces[i];
                if (nob == null || !nob.TryGetComponent(out PieceBehaviour piece) || piece.PieceId != pieceId) continue;
                _spawnedPieces.RemoveAt(i);
                if (nob.IsSpawned) _networkManager.ServerManager.Despawn(nob);
                return true;
            }
            return false;
        }

        private void DespawnItems()
        {
            foreach (NetworkObject item in _spawnedItems)
            {
                if (item == null || !item.IsSpawned) continue;
                if (item.TryGetComponent(out Pickup pickup) && pickup.IsHeld)
                {
                    Debug.Log("[Layout] " + item.name + " stays: a player holds it");
                    continue;
                }
                _networkManager.ServerManager.Despawn(item);
            }
            _spawnedItems.Clear();
        }

        private void DespawnPieces()
        {
            foreach (NetworkObject nob in _spawnedPieces)
                if (nob != null && nob.IsSpawned) _networkManager.ServerManager.Despawn(nob);
            _spawnedPieces.Clear();
        }

        /// <summary>
        /// Where an item from <paramref name="prefab"/> rests on spot <paramref name="spot"/> of <paramref name="surface"/> (the builder's
        /// PlaceOnSpot): its box's bottom on the top; turned with the surface, a tool with a handle with its handle into the aisle (ToolRest).
        /// </summary>
        public static Pose HomePose(GameObject prefab, PlaceSurface surface, int spot)
        {
            float bottom = 0f;
            var box = prefab.GetComponent<BoxCollider>();
            if (box != null) bottom = box.size.y * 0.5f - box.center.y;
            bool tool = prefab.TryGetComponent(out Pickup pickup) && pickup.IsHandleTool;
            Quaternion rotation = tool ? ToolRest.Home(surface.transform.rotation) : surface.transform.rotation;
            return new Pose(surface.SpotWorldPosition(spot) + Vector3.up * bottom, rotation);
        }

        /// <summary>The item's object name: "Cutting board", "Plate 2" (numbered when its kind has more than one home), "Wipe cloth".</summary>
        public static string ItemName(string kind, ShopLayout layout, ItemHome home)
        {
            string name;
            switch (kind)
            {
                case "board": name = "Cutting board"; break;
                case "cloth": name = "Wipe cloth"; break;
                default: name = string.IsNullOrEmpty(kind) ? "Item" : char.ToUpperInvariant(kind[0]) + kind.Substring(1); break;
            }
            int count = 0, rank = 0;
            if (layout != null)
                foreach (ItemHome h in layout.Homes)
                {
                    if (h.ItemKind != kind) continue;
                    count++;
                    if (ReferenceEquals(h, home) || (rank == 0 && h == home)) rank = count;
                }
            return count > 1 && rank > 0 ? name + " " + rank : name;
        }

        /// <summary>
        /// Offline (the Editor, tests): instantiates <paramref name="layout"/>'s pieces (configured and registered as on a server) and, when
        /// <paramref name="withItems"/>, its items at their homes, into <paramref name="scene"/> (the active scene when default). Returns
        /// every object made (the caller destroys them).
        /// </summary>
        public static List<GameObject> InstantiateOffline(ShopLayout layout, PiecePrefabs prefabs, UnityEngine.SceneManagement.Scene scene = default,
            bool withItems = true)
        {
            var made = new List<GameObject>();
            if (layout == null || prefabs == null) return made;
            foreach (LayoutPiece piece in layout.Pieces)
            {
                PieceBehaviour prefab = prefabs.Piece(piece.TypeId);
                if (prefab == null) continue;
                GameObject go = PieceBehaviour.InstantiateOffline(prefab.gameObject);
                go.name = prefab.name + " " + piece.Id;
                if (scene.IsValid()) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
                PieceBehaviour behaviour = go.GetComponent<PieceBehaviour>();
                behaviour.Configure(piece.Id, PosePacking.Pack(piece.XMm, piece.ZMm, piece.Turns, piece.LengthMm), layout.NumberOf(piece.Id));
                behaviour.RegisterOffline();
                made.Add(go);
            }
            if (!withItems)
            {
                Physics.SyncTransforms();
                return made;
            }
            foreach (ItemHome home in layout.Homes)
            {
                NetworkObject itemPrefab = prefabs.Item(home.ItemKind);
                PlaceSurface surface = LayoutRegistry.Surface(SurfaceId.Of(home.PieceId, home.LocalSurface));
                if (itemPrefab == null || surface == null || home.Spot >= surface.SpotCount) continue;
                GameObject item = PieceBehaviour.InstantiateArt(itemPrefab.gameObject, null); // a prefab instance in the Editor, like the builder's scene cookware
                if (scene.IsValid()) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(item, scene);
                Pose pose = HomePose(itemPrefab.gameObject, surface, home.Spot);
                item.transform.SetPositionAndRotation(pose.position, pose.rotation);
                item.name = ItemName(home.ItemKind, layout, home);
                made.Add(item);
            }
            Physics.SyncTransforms(); // outside play mode the colliders' bounds follow the moved pieces only after a sync
            return made;
        }
    }
}
