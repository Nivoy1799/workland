using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FishNet.Object;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Worktown.Game.Art;
using Worktown.Game.Cooking;
using Worktown.Game.Layout;
using Worktown.Game.Net;
using Worktown.Game.Save;
using Worktown.Sim.Layout;

namespace Worktown.Game.Editor
{
    /// <summary>
    /// Spec 062a: one prefab per piece type of the default layout (Resources/Layouts/default.json), cut from the kitchen the builder's
    /// station code makes (GrayboxSceneBuilder.BuildKitchenParts, in a scene of its own): each station with what belongs to it (its
    /// buttons, surfaces, slots, closed cover; the fryer's baskets, the griddle's spots, the assembly board's build spot, the spit's
    /// cone and tray, which the builder puts next to them) under a piece root (a NetworkObject with a PieceBehaviour) at the piece's pose;
    /// a counter-line module gets its own front counter body. The worktop and the front counter are made at their catalog length and
    /// take a piece's length at spawn (PieceBehaviour). A type placed more than once marks its number ("Stove #"). Writes
    /// <see cref="PrefabsAssetPath"/>, the type to prefab table the LayoutSpawner uses.
    /// </summary>
    public static class PiecePrefabBuilder
    {
        public const string Folder = GrayboxSceneBuilder.Root + "/Prefabs/Pieces";
        public const string PrefabsAssetPath = Folder + "/PiecePrefabs.asset";
        /// <summary>The name of a counter-line module's counter body (PlaceCoverage's "Front counter" exception names it).</summary>
        public const string ModuleCounterName = "Front counter";
        private const string WorktopName = "Worktop #";
        private const string FrontCounterName = GrayboxSceneBuilder.FrontCounterName + " #";
        private const string CounterTopName = "Counter top #";

        public static PiecePrefabs Build()
        {
            GrayboxSceneBuilder.EnsureFolder(Folder);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var catalog = AssetDatabase.LoadAssetAtPath<ArtCatalog>(ArtCatalogBuilder.CatalogPath);
            if (catalog == null) throw new InvalidOperationException("ArtCatalog missing: " + ArtCatalogBuilder.CatalogPath);
            var vessels = new List<NetworkObject>();
            foreach (VesselKind kind in new[] { VesselKind.Board, VesselKind.Pan, VesselKind.Pot, VesselKind.Plate, VesselKind.Bowl })
                vessels.Add(LoadNob(GrayboxSceneBuilder.VesselPrefabPath(kind)));
            NetworkObject knife = LoadNob(GrayboxSceneBuilder.KnifePrefabPath);
            NetworkObject spatula = LoadNob(GrayboxSceneBuilder.SpatulaPrefabPath);
            NetworkObject cloth = LoadNob(GrayboxSceneBuilder.WipeClothPrefabPath);

            PieceCatalog pieces = PieceCatalog.CreateDefault();
            ShopLayout layout = LayoutCodec.ToLayout(ReadDefaultPart(), pieces);
            if (layout.Pieces.Count == 0) throw new InvalidOperationException("the default layout has no pieces: " + LayoutExporter.DefaultLayoutPath);

            var kitchen = new GameObject("Kitchen pieces");
            GrayboxSceneBuilder.KitchenParts parts = GrayboxSceneBuilder.BuildKitchenParts(catalog, kitchen.transform, vessels, knife, spatula);

            // The closed covers (spec 053) are sized over what rests on a station: the cookware at the start, and for a type placed more
            // than once what rests on each copy, moved onto the one that is cut (the pot of stove 2 on stove 1).
            var extra = new List<GameObject>();
            foreach (IGrouping<StationKind, Station> group in parts.Stations.GroupBy(s => s.Kind))
            {
                Station first = group.First();
                foreach (Station other in group.Skip(1))
                    foreach (GameObject item in parts.Items)
                    {
                        if (!RestsOn(item, other)) continue;
                        GameObject copy = UnityEngine.Object.Instantiate(item);
                        copy.transform.SetPositionAndRotation(first.transform.TransformPoint(other.transform.InverseTransformPoint(item.transform.position)),
                            first.transform.rotation * Quaternion.Inverse(other.transform.rotation) * item.transform.rotation);
                        extra.Add(copy);
                    }
            }
            Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            int covers = GrayboxSceneBuilder.CreateClosedCovers(parts.Stations, renderers);
            foreach (GameObject go in parts.Items.Concat(extra)) UnityEngine.Object.DestroyImmediate(go);

            var entries = new List<PiecePrefabs.PieceEntry>();
            foreach (string type in layout.Pieces.Select(p => p.TypeId).Distinct())
            {
                if (!pieces.TryGet(type, out PieceDef def)) throw new InvalidOperationException("the default layout names an unknown type: " + type);
                LayoutPiece first = layout.Pieces.First(p => p.TypeId == type);
                bool numbered = layout.CountOf(type) > 1;
                GameObject root = type == PieceCatalog.Worktop ? BuildWorktop(catalog, def, parts)
                    : type == PieceCatalog.FrontCounter ? BuildFrontCounter(catalog, def)
                    : type == PieceCatalog.GoalBoard ? BuildGoalBoard(catalog, def, first) // spec 057: not in the builder's kitchen
                    : CarveStation(catalog, def, first, parts, numbered);
                root.name = type;
                string path = Folder + "/" + type + ".prefab";
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
                UnityEngine.Object.DestroyImmediate(root);
                entries.Add(new PiecePrefabs.PieceEntry { TypeId = type, Prefab = saved.GetComponent<PieceBehaviour>() });
            }

            var items = new List<PiecePrefabs.ItemEntry>
            {
                Item("board", vessels[0]), Item("pan", vessels[1]), Item("pot", vessels[2]), Item("plate", vessels[3]), Item("bowl", vessels[4]),
                Item("knife", knife), Item("spatula", spatula), Item("cloth", cloth),
            };
            var asset = AssetDatabase.LoadAssetAtPath<PiecePrefabs>(PrefabsAssetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<PiecePrefabs>();
                AssetDatabase.CreateAsset(asset, PrefabsAssetPath);
            }
            asset.Set(entries.ToArray(), items.ToArray());
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log("[Graybox] piece prefabs: " + entries.Count + " types, " + covers + " closed covers, at " + Folder);
            return asset;
        }

        private static PiecePrefabs.ItemEntry Item(string kind, NetworkObject prefab) => new PiecePrefabs.ItemEntry { Kind = kind, Prefab = prefab };

        private static NetworkObject LoadNob(string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null || !go.TryGetComponent(out NetworkObject nob)) throw new InvalidOperationException("prefab missing: " + path);
            return nob;
        }

        private static ShopSave.LayoutPart ReadDefaultPart()
        {
            string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", LayoutExporter.DefaultLayoutPath));
            return JsonUtility.FromJson<ShopSave.LayoutPart>(File.ReadAllText(full));
        }

        private static bool RestsOn(GameObject item, Station station)
        {
            Vector3 local = station.transform.InverseTransformPoint(item.transform.position);
            return Mathf.Abs(local.x) < 0.7f && Mathf.Abs(local.z) < 0.7f && local.y > -1f && local.y < 1.5f;
        }

        /// <summary>A station piece: the station and what belongs to it, moved under a piece root at the piece's pose.</summary>
        private static GameObject CarveStation(ArtCatalog catalog, PieceDef def, LayoutPiece piece, GrayboxSceneBuilder.KitchenParts parts, bool numbered)
        {
            StationKind kind = KindOf(def.TypeId);
            var at = new Vector3(piece.XMm / 1000f, 0f, piece.ZMm / 1000f);
            Station station = parts.Stations.Where(s => s.Kind == kind)
                .OrderBy(s => Vector2.Distance(new Vector2(s.transform.position.x, s.transform.position.z), new Vector2(at.x, at.z))).FirstOrDefault();
            if (station == null) throw new InvalidOperationException("the builder made no " + kind + " for " + def.TypeId);
            GameObject root = NewRoot(def.TypeId, piece);
            var moved = new List<Transform> { station.transform };
            foreach (FryerBasket b in UnityEngine.Object.FindObjectsByType<FryerBasket>(FindObjectsSortMode.None)) if (b.Fryer == station) moved.Add(b.transform);
            foreach (GriddleSpot g in UnityEngine.Object.FindObjectsByType<GriddleSpot>(FindObjectsSortMode.None)) if (g.Griddle == station) moved.Add(g.transform);
            foreach (AssemblySpot a in UnityEngine.Object.FindObjectsByType<AssemblySpot>(FindObjectsSortMode.None)) if (a.Board == station) moved.Add(a.transform);
            foreach (SpitPart sp in UnityEngine.Object.FindObjectsByType<SpitPart>(FindObjectsSortMode.None)) if (sp.Spit == station) moved.Add(sp.transform);
            if (def.IsCounter && def.HasGuestSide && kind != StationKind.Counter) CreateModuleCounter(catalog, root.transform, def.WidthMm / 1000f);
            foreach (Transform t in moved) t.SetParent(root.transform, true);

            if (numbered) MarkNumber(station);
            List<PlaceSurface> surfaces = root.GetComponentsInChildren<PlaceSurface>(true).OrderBy(s => parts.Surfaces.IndexOf(s)).ToList();
            if (surfaces.Any(s => parts.Surfaces.IndexOf(s) < 0)) throw new InvalidOperationException(def.TypeId + ": a surface the builder did not list");
            PieceBehaviour behaviour = AddPiece(root, def.TypeId);
            GrayboxSceneBuilder.SetField(behaviour, "_surfaces", surfaces);
            GrayboxSceneBuilder.SetField(behaviour, "_stations", new List<Station> { station });
            return root;
        }

        /// <summary>"Stove 1" (its object, its name, its sign) becomes "Stove #": the piece writes its number in.</summary>
        private static void MarkNumber(Station station)
        {
            string shown = station.DisplayName;
            string marked = Regex.Replace(shown, @" \d+$", " " + PieceBehaviour.NumberMark);
            if (marked == shown) marked = shown + " " + PieceBehaviour.NumberMark;
            foreach (Transform t in station.GetComponentsInChildren<Transform>(true)) if (t.name == shown) t.name = marked;
            foreach (TextMesh text in station.GetComponentsInChildren<TextMesh>(true)) if (text.text == shown) text.text = marked;
            GrayboxSceneBuilder.SetField(station, "_displayName", marked);
        }

        private static GameObject NewRoot(string type, LayoutPiece piece)
        {
            var root = new GameObject(type);
            root.transform.SetPositionAndRotation(new Vector3(piece.XMm / 1000f, 0f, piece.ZMm / 1000f), Quaternion.Euler(0f, PosePacking.Yaw(piece.Turns), 0f));
            return root;
        }

        private static PieceBehaviour AddPiece(GameObject root, string type)
        {
            NetworkObject nob = root.AddComponent<NetworkObject>();
            GrayboxSceneBuilder.SetField(nob, "_preventDespawnOnDisconnect", true);
            PieceBehaviour behaviour = root.AddComponent<PieceBehaviour>();
            GrayboxSceneBuilder.SetField(behaviour, "_typeId", type);
            return behaviour;
        }

        /// <summary>A counter-line module's counter body: a front counter run of the module's width (spec 027a's cabinet modules).</summary>
        private static void CreateModuleCounter(ArtCatalog catalog, Transform root, float width)
        {
            float height = ArtSizeTable.ServingCounterHeight;
            var body = new GameObject(ModuleCounterName);
            body.transform.SetParent(root, false);
            var box = body.AddComponent<BoxCollider>();
            box.size = new Vector3(width, height, GrayboxSceneBuilder.CounterDepth);
            box.center = new Vector3(0f, height * 0.5f, 0f);
            if (catalog.TryGet(ArtKey.Worktop, out GameObject wrapper))
            {
                (int count, float w) = ArtSizeTable.WorktopModules(width);
                float wrapperHeight = ArtCatalogBuilder.HeightOf(wrapper);
                float x = -width * 0.5f;
                for (int i = 0; i < count; i++)
                {
                    var module = (GameObject)PrefabUtility.InstantiatePrefab(wrapper, body.transform);
                    module.name = "Counter module " + (i + 1);
                    module.transform.localPosition = new Vector3(x + w * 0.5f, 0f, 0f);
                    module.transform.localRotation = GrayboxSceneBuilder.ArtFrontRotation;
                    module.transform.localScale = new Vector3(w / ArtSizeTable.WorktopModuleWidth, height / wrapperHeight,
                        GrayboxSceneBuilder.CounterDepth / GrayboxSceneBuilder.StationWidth);
                    x += w;
                }
            }
            GrayboxSceneBuilder.SetStaticRecursive(body);
        }

        /// <summary>
        /// Spec 020/021 (spec 062a: a piece): a plain worktop as deep as the stations, at the stove top's height, its whole top a place
        /// surface; its length and cabinet modules come from the piece (PieceBehaviour).
        /// </summary>
        private static GameObject BuildWorktop(ArtCatalog catalog, PieceDef def, GrayboxSceneBuilder.KitchenParts parts)
        {
            PlaceSurface model = parts.Surfaces.First(s => s.name.StartsWith("Worktop ") && s.GetComponentInParent<Station>() == null);
            float height = model.TopY;
            float depth = GrayboxSceneBuilder.StationWidth;
            float width = def.WidthMm / 1000f;
            var root = new GameObject(def.TypeId);
            var top = new GameObject(WorktopName);
            top.transform.SetParent(root.transform, false);
            var box = top.AddComponent<BoxCollider>();
            box.size = new Vector3(width, height, depth);
            box.center = new Vector3(0f, height * 0.5f, 0f);
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            UnityEngine.Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(top.transform, false);
            body.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
            body.transform.localScale = new Vector3(width, height, depth);
            body.GetComponent<MeshRenderer>().sharedMaterial = GrayboxSceneBuilder.CreateMaterial(GrayboxSceneBuilder.WorktopMaterialPath, Palette.WoodLight);
            bool art = catalog.TryGet(ArtKey.Worktop, out GameObject wrapper);
            if (art) UnityEngine.Object.DestroyImmediate(body.GetComponent<MeshRenderer>());
            GrayboxSceneBuilder.SetStaticRecursive(top);
            var surface = top.AddComponent<PlaceSurface>();
            GrayboxSceneBuilder.SetField(surface, "_size", new Vector2(width, depth));
            GrayboxSceneBuilder.SetField(surface, "_topY", height);

            PieceBehaviour behaviour = AddPiece(root, def.TypeId);
            GrayboxSceneBuilder.SetField(behaviour, "_surfaces", new List<PlaceSurface> { surface });
            GrayboxSceneBuilder.SetField(behaviour, "_lengthBoxes", new List<BoxCollider> { box });
            GrayboxSceneBuilder.SetField(behaviour, "_lengthSurfaces", new List<PlaceSurface> { surface });
            GrayboxSceneBuilder.SetField(behaviour, "_lengthScaled", new List<Transform> { body.transform });
            if (art) SetModules(behaviour, wrapper, top.transform, GrayboxSceneBuilder.WorktopModuleName, height, 1f);
            return root;
        }

        /// <summary>
        /// Spec 027a/039 (spec 062a: a piece): a plain front counter run (serving-counter high, 0.7 m deep, its cook side on local -z) and
        /// its counter top (spots on the cook half, the aim collider over the whole depth); length and modules from the piece.
        /// </summary>
        private static GameObject BuildFrontCounter(ArtCatalog catalog, PieceDef def)
        {
            float height = ArtSizeTable.ServingCounterHeight;
            float width = def.WidthMm / 1000f;
            var root = new GameObject(def.TypeId);
            var run = new GameObject(FrontCounterName);
            run.transform.SetParent(root.transform, false);
            var box = run.AddComponent<BoxCollider>();
            box.size = new Vector3(width, height, GrayboxSceneBuilder.CounterDepth);
            box.center = new Vector3(0f, height * 0.5f, 0f);
            GrayboxSceneBuilder.SetStaticRecursive(run);
            PlaceSurface top = GrayboxSceneBuilder.CreateCounterTop(run.transform, CounterTopName, -width * 0.5f, width * 0.5f, 0);
            bool art = catalog.TryGet(ArtKey.Worktop, out GameObject wrapper);

            PieceBehaviour behaviour = AddPiece(root, def.TypeId);
            GrayboxSceneBuilder.SetField(behaviour, "_surfaces", new List<PlaceSurface> { top });
            GrayboxSceneBuilder.SetField(behaviour, "_lengthBoxes", new List<BoxCollider> { box, top.GetComponent<BoxCollider>() });
            GrayboxSceneBuilder.SetField(behaviour, "_lengthSurfaces", new List<PlaceSurface> { top });
            if (art) SetModules(behaviour, wrapper, run.transform, "Counter module", height, GrayboxSceneBuilder.CounterDepth / GrayboxSceneBuilder.StationWidth);
            return root;
        }

        public const string GoalBoardName = "Goal board";
        public const string GoalBoardRowName = "Goal row";

        /// <summary>
        /// Spec 057: the goal board, a wall piece (GoalBoardArtRules; a graybox stand-in until the art pass): the station at the board's middle
        /// with a "Body" box collider of the board's size (the aim: the row nearest the hit), the catalog's graybox, the sign under it, and one
        /// slot per row (option = the row) with its Ink text line, which the client's GoalBoardView writes from the synced stage.
        /// </summary>
        private static GameObject BuildGoalBoard(ArtCatalog catalog, PieceDef def, LayoutPiece piece)
        {
            GameObject root = NewRoot(def.TypeId, piece);
            var stationObject = new GameObject(GoalBoardName);
            stationObject.transform.SetParent(root.transform, false);
            stationObject.transform.localPosition = new Vector3(0f, GoalBoardArtRules.CentreHeight, 0f);
            var station = stationObject.AddComponent<Station>();
            GrayboxSceneBuilder.SetField(station, "_kind", (int)StationKind.GoalBoard);
            GrayboxSceneBuilder.SetField(station, "_displayName", GoalBoardName);
            GrayboxSceneBuilder.SetField(station, "_ingredientIndex", -1);
            var body = new GameObject("Body");
            body.transform.SetParent(stationObject.transform, false);
            body.transform.localScale = new Vector3(def.WidthMm / 1000f, GoalBoardArtRules.Height, GoalBoardArtRules.Depth); // 1.8 cm: clear of the back line
            body.AddComponent<BoxCollider>(); // size one: the body's scale is the board
            float h = GoalBoardArtRules.Height;
            if (catalog.TryGet(ArtCatalogRules.StationKey(StationKind.GoalBoard), out GameObject wrapper))
            {
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(wrapper, stationObject.transform);
                visual.transform.localPosition = new Vector3(0f, -h * 0.5f, 0f);
                visual.transform.localRotation = GrayboxSceneBuilder.ArtFrontRotation; // the model's front (+Z) towards the aisle
            }
            else Debug.LogError("[Graybox] no goal board model in the catalog");
            TextMesh sign = Label(stationObject.transform, new Vector3(0f, -h * 0.5f - 0.03f, GoalBoardArtRules.FrontZ - StationSign.Standoff), 60, 0.05f);
            sign.anchor = TextAnchor.UpperCenter;
            sign.transform.localScale = Vector3.one * StationSign.BaseScale;
            sign.gameObject.AddComponent<StationSign>();
            sign.text = GoalBoardName + " · your next goal";

            var slots = new List<StockSlot>(Worktown.Game.Climb.GoalBoardRules.RowCount);
            var rows = new TextMesh[Worktown.Game.Climb.GoalBoardRules.RowCount];
            for (int row = 0; row < rows.Length; row++)
            {
                var slotObject = new GameObject(GoalBoardRowName + " " + row);
                slotObject.transform.SetParent(stationObject.transform, false);
                slotObject.transform.localPosition = GoalBoardArtRules.RowCentre(row);
                var slot = slotObject.AddComponent<StockSlot>();
                GrayboxSceneBuilder.SetField(slot, "_station", station);
                GrayboxSceneBuilder.SetField(slot, "_option", row);
                GrayboxSceneBuilder.SetField(slot, "_ingredientIndex", -1);
                slots.Add(slot);
                TextMesh text = Label(stationObject.transform, GoalBoardArtRules.RowTextCorner(row), 40, 0.0075f);
                text.name = GoalBoardRowName + " text " + row;
                text.anchor = TextAnchor.MiddleLeft;
                text.alignment = TextAlignment.Left;
                text.color = Palette.Ink; // D32: Ink on the Cream face
                text.text = row == 0 ? GoalBoardName : string.Empty;
                rows[row] = text;
            }
            GrayboxSceneBuilder.SetField(station, "_slots", slots);
            foreach (Transform t in stationObject.GetComponentsInChildren<Transform>(true))
                if (t.GetComponent<TextMesh>() == null) t.gameObject.isStatic = true; // only the text changes
            var view = stationObject.AddComponent<Worktown.Game.UI.GoalBoardView>();
            GrayboxSceneBuilder.SetField(view, "_rows", rows);

            PieceBehaviour behaviour = AddPiece(root, def.TypeId);
            GrayboxSceneBuilder.SetField(behaviour, "_surfaces", new List<PlaceSurface>());
            GrayboxSceneBuilder.SetField(behaviour, "_stations", new List<Station> { station });
            return root;
        }

        /// <summary>A world label (the builder's: the built-in font, Cream unless set).</summary>
        private static TextMesh Label(Transform parent, Vector3 localPosition, int fontSize, float characterSize)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var text = go.AddComponent<TextMesh>();
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            text.fontSize = fontSize;
            text.characterSize = characterSize;
            text.anchor = TextAnchor.LowerCenter;
            text.alignment = TextAlignment.Center;
            text.color = Palette.Cream;
            return text;
        }

        private static void SetModules(PieceBehaviour behaviour, GameObject wrapper, Transform parent, string name, float height, float depthScale)
        {
            GrayboxSceneBuilder.SetField(behaviour, "_moduleArt", wrapper);
            GrayboxSceneBuilder.SetField(behaviour, "_moduleParent", parent);
            GrayboxSceneBuilder.SetField(behaviour, "_moduleName", name);
            GrayboxSceneBuilder.SetField(behaviour, "_moduleHeight", height);
            GrayboxSceneBuilder.SetField(behaviour, "_moduleArtHeight", ArtCatalogBuilder.HeightOf(wrapper));
            GrayboxSceneBuilder.SetField(behaviour, "_moduleArtWidth", ArtSizeTable.WorktopModuleWidth);
            GrayboxSceneBuilder.SetField(behaviour, "_moduleDepthScale", depthScale);
        }

        /// <summary>The station kind a station piece type carries.</summary>
        public static StationKind KindOf(string type)
        {
            switch (type)
            {
                case PieceCatalog.ShelfDry: return StationKind.Shelf;
                case PieceCatalog.Fridge: return StationKind.Fridge;
                case PieceCatalog.Stove: return StationKind.Stove;
                case PieceCatalog.ServingCounter: return StationKind.Counter;
                case PieceCatalog.PlateShelf: return StationKind.PlateShelf;
                case PieceCatalog.Fryer: return StationKind.Fryer;
                case PieceCatalog.Griddle: return StationKind.Griddle;
                case PieceCatalog.Assembly: return StationKind.Assembly;
                case PieceCatalog.RegisterModule: return StationKind.Register;
                case PieceCatalog.DispenserModule: return StationKind.DrinkDispenser;
                case PieceCatalog.Terminal: return StationKind.Terminal;
                case PieceCatalog.DishReturnModule: return StationKind.DishReturn;
                case PieceCatalog.Sink: return StationKind.Sink;
                case PieceCatalog.Trash: return StationKind.TrashCan;
                case PieceCatalog.Bin: return StationKind.Bin;
                case PieceCatalog.SpitModule: return StationKind.Spit;
                case PieceCatalog.MenuBoard: return StationKind.MenuBoard;
                case PieceCatalog.GoalBoard: return StationKind.GoalBoard; // spec 057
                default: throw new InvalidOperationException("no station kind for piece type " + type);
            }
        }
    }
}
