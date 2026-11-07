using System;
using System.Collections.Generic;
using System.Reflection;
using FishNet.Component.Observing;
using FishNet.Component.Spawning;
using FishNet.Component.Transforming;
using FishNet.Component.Transforming.Beta;
using FishNet.Managing;
using FishNet.Managing.Logging;
using FishNet.Managing.Object;
using FishNet.Managing.Observing;
using FishNet.Managing.Timing;
using FishNet.Managing.Transporting;
using FishNet.Object;
using FishNet.Observing;
using FishNet.Transporting;
using FishNet.Transporting.Multipass;
using FishNet.Transporting.Tugboat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Worktown.Game.Art;
using Worktown.Game.Cooking;
using Worktown.Game.Economy;
using Worktown.Sim.Economy;
using Worktown.Game.Net;
using Worktown.Game.Player;
using Worktown.Game.UI;
using Worktown.Game.Social;
using Worktown.Game.Npc;
using UnityEngine.UIElements;
using Unity.Cinemachine;
using UnityEngine.InputSystem;

namespace Worktown.Game.Editor
{
    /// <summary>
    /// Generates the spec 003 graybox: player and pickup prefabs, the 40 m distance condition
    /// and Graybox.unity with the NetworkManager setup. Re-running overwrites them, so the
    /// scene is reproducible from code instead of hand-edited.
    /// </summary>
    public static class GrayboxSceneBuilder
    {
        public const string Root = "Assets/_Project";
        public const string ScenePath = Root + "/Scenes/Graybox.unity";
        private const string PlayerPrefabPath = Root + "/Prefabs/Player.prefab";
        private const string PickupPrefabPath = Root + "/Prefabs/PickupCube.prefab";
        private const string FoodPrefabPath = Root + "/Prefabs/Food.prefab";
        private const string PingMarkerPrefabPath = Root + "/Prefabs/PingMarker.prefab";
        private const string CustomerPrefabPath = Root + "/Prefabs/Customer.prefab";
        private const float FoodSize = 0.4f;
        private const float StationHeight = 1f;
        private const float KitchenZ = 13f;
        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
        private const string UiThemePath = Root + "/UI/WorktownTheme.tss";
        private const string UiStylePath = Root + "/UI/Worktown.uss";
        private const string UiPanelPath = Root + "/UI/WorktownPanel.asset";
        private static readonly EconomyCatalog GrocerPrices = EconomyCatalog.CreateDefault();
        private const string DistanceConditionPath = Root + "/Settings/DistanceCondition40m.asset";
        private const string LoggingConfigurationPath = Root + "/Settings/FishNetLogging.asset";
        private const string GroundMaterialPath = Root + "/Settings/Ground.mat";
        private const string PickupMaterialPath = Root + "/Settings/Pickup.mat";
        private const string DefaultPrefabObjectsPath = "Assets/DefaultPrefabObjects.asset";
        private const string RefreshPrefabsMenu = "Tools/Fish-Networking/Utility/Refresh Default Prefabs";

        private const int PickupCount = 20;
        private const float InterestDistance = 40f;
        private const string SteamTransportTypeName = "FishySteamworks.FishySteamworks";

        [MenuItem("Worktown/Build Graybox Scene")]
        public static void Build()
        {
            EnsureFolder(Root + "/Prefabs");
            EnsureFolder(Root + "/Scenes");
            EnsureFolder(Root + "/Settings");

            ArtCatalog builtCatalog = ArtCatalogBuilder.Build(); // spec 015: wrappers and catalog first
            Material groundMat = CreateMaterial(GroundMaterialPath, new Color(0.45f, 0.48f, 0.45f));
            Material pickupMat = CreateMaterial(PickupMaterialPath, new Color(0.95f, 0.6f, 0.15f));
            PingMarker pingPrefab = CreatePingMarkerPrefab(pickupMat);
            NetworkObject playerPrefab = CreatePlayerPrefab(pingPrefab);
            GameObject pickupPrefab = CreatePickupPrefab(pickupMat);
            NetworkObject foodPrefab = CreateFoodPrefab(pickupMat, builtCatalog);
            NetworkObject customerPrefab = CreateCustomerPrefab(pickupMat);
            // Overwritten prefabs lose their AssetPathHash, and FishNet's import hook only hashes
            // newly added entries. A full refresh re-hashes every prefab in DefaultPrefabObjects.
            AssetDatabase.SaveAssets();
            if (!EditorApplication.ExecuteMenuItem(RefreshPrefabsMenu))
                throw new InvalidOperationException("FishNet menu not found: " + RefreshPrefabsMenu);
            DistanceCondition distance = CreateDistanceCondition();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Load after NewScene: opening a scene unloads unused assets, which destroys a catalog loaded earlier.
            var catalog = AssetDatabase.LoadAssetAtPath<ArtCatalog>(ArtCatalogBuilder.CatalogPath);
            if (catalog == null) throw new InvalidOperationException("ArtCatalog missing: " + ArtCatalogBuilder.CatalogPath);
            CreateEnvironment(groundMat);
            CreateStreet(catalog);
            Transform[] spawns = CreateSpawnPoints();
            CreatePickups(pickupPrefab);
            NetworkManager networkManager = CreateNetworkManager(playerPrefab, spawns, distance);
            CreateKitchen(networkManager, foodPrefab, catalog);
            CreateCustomers(networkManager, customerPrefab);
            CreateBootstrap(networkManager);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssignSceneIds(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            SetBuildScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("[Graybox] scene generated at " + ScenePath);
        }

        /// <summary>
        /// Spec 010: player with FishNet client-side prediction, structured like FishNet's
        /// CharacterControllerPrediction example: the root simulates (no NetworkTransform), the
        /// visible "Graphic" child is smoothed by a NetworkTickSmoother and followed by the camera.
        /// </summary>
        private static NetworkObject CreatePlayerPrefab(PingMarker pingPrefab)
        {
            var root = new GameObject("Player");
            root.tag = "Player";
            var controller = root.AddComponent<CharacterController>();
            controller.height = 2f;
            controller.radius = 0.4f;
            controller.center = new Vector3(0f, 1f, 0f);

            var graphic = new GameObject("Graphic");
            graphic.transform.SetParent(root.transform, false);

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            UnityEngine.Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(graphic.transform, false);
            body.transform.localPosition = new Vector3(0f, 1f, 0f);

            GameObject nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "Facing";
            UnityEngine.Object.DestroyImmediate(nose.GetComponent<Collider>());
            nose.transform.SetParent(graphic.transform, false);
            nose.transform.localPosition = new Vector3(0f, 1.5f, 0.45f);
            nose.transform.localScale = new Vector3(0.3f, 0.15f, 0.2f);

            // Spec 018: first-person camera anchor inside the smoothed graphic, so the view is as smooth as the body.
            var eyes = new GameObject("Eyes");
            eyes.transform.SetParent(graphic.transform, false);
            eyes.transform.localPosition = new Vector3(0f, ViewRules.EyeHeight, ViewRules.EyeForward);

            // Above the head, so a carried cube never blocks the CharacterController.
            var hold = new GameObject("HoldPoint");
            hold.transform.SetParent(root.transform, false);
            hold.transform.localPosition = new Vector3(0f, 2.5f, 0f);

            NetworkObject nob = root.AddComponent<NetworkObject>();
            SetField(nob, "_enablePrediction", true);
            SetField(nob, "_predictionType", 0); // Other: CharacterController, not a rigidbody
            SetField(nob, "_enableStateForwarding", true);

            var smoother = graphic.AddComponent<NetworkTickSmoother>();
            SetField(smoother, "_initializationSettings.TargetTransform", root.transform);

            var player = root.AddComponent<PlayerController>();
            SetField(player, "_graphic", graphic.transform);
            SetField(player, "_body", body.transform);
            SetField(player, "_eyes", eyes.transform);
            var interactor = root.AddComponent<PickupInteractor>();
            root.AddComponent<Wallet>(); // spec 009
            SetField(interactor, "_holdPoint", hold.transform);

            // Spec 012: emote bubble above the head (above a carried item) and social input.
            TextMesh bubbleLabel = CreateLabel(root.transform, new Vector3(0f, 3.2f, 0f), 56, 0.045f);
            bubbleLabel.color = new Color(1f, 0.9f, 0.4f);
            var bubble = root.AddComponent<EmoteBubble>();
            SetField(bubble, "_label", bubbleLabel);
            SetField(bubble, "_body", body.transform);
            var social = root.AddComponent<SocialInteractor>();
            SetField(social, "_pingPrefab", pingPrefab);
            SetField(social, "_bubble", bubble);

            // Spec 018: owner-only first-person visuals (own body shadows only, held food in view).
            var firstPersonBody = root.AddComponent<FirstPersonBody>();
            SetField(firstPersonBody, "_body", new Renderer[] { body.GetComponent<Renderer>(), nose.GetComponent<Renderer>() });
            SetField(firstPersonBody, "_overhead", new Renderer[] { bubbleLabel.GetComponent<Renderer>() });
            root.AddComponent<HeldItemView>();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return saved.GetComponent<NetworkObject>();
        }

        private static GameObject CreatePickupPrefab(Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "PickupCube";
            cube.transform.localScale = Vector3.one * 0.5f;
            cube.GetComponent<MeshRenderer>().sharedMaterial = material;
            NetworkObject nob = cube.AddComponent<NetworkObject>();
            // The holder owns a carried cube. PickupService takes ownership back on disconnect;
            // this is the safety net so a missed release never despawns a scene cube.
            SetField(nob, "_preventDespawnOnDisconnect", true);
            AddServerAuthoritativeTransform(cube);
            cube.AddComponent<Pickup>();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(cube, PickupPrefabPath);
            UnityEngine.Object.DestroyImmediate(cube);
            return saved;
        }

        /// <summary>Spec 014: NPC customer, moved by the server; no collider so it never blocks players.</summary>
        private static NetworkObject CreateCustomerPrefab(Material material)
        {
            var root = new GameObject("Customer");
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            UnityEngine.Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.8f, 0.9f, 0.8f);
            var renderer = body.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            TextMesh label = CreateLabel(root.transform, new Vector3(0f, 2.1f, 0f), 56, 0.045f);

            NetworkObject nob = root.AddComponent<NetworkObject>();
            AddServerAuthoritativeTransform(root);
            var customer = root.AddComponent<Customer>();
            SetField(customer, "_label", label);
            SetField(customer, "_body", renderer);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, CustomerPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return saved.GetComponent<NetworkObject>();
        }

        /// <summary>Spec 014: street entry east of the kitchen and the queue in front of the counter.</summary>
        private static void CreateCustomers(NetworkManager networkManager, NetworkObject customerPrefab)
        {
            var parent = new GameObject("Customers");
            var entry = new GameObject("StreetEntry");
            entry.transform.SetParent(parent.transform, false);
            entry.transform.SetPositionAndRotation(new Vector3(24f, 0.1f, KitchenZ - 2.2f), Quaternion.Euler(0f, -90f, 0f));
            var head = new GameObject("QueueHead");
            head.transform.SetParent(parent.transform, false);
            head.transform.position = new Vector3(8.5f, 0.1f, KitchenZ - 2.2f); // in front of the counter

            var service = networkManager.gameObject.AddComponent<CustomerService>();
            SetField(service, "_networkManager", networkManager);
            SetField(service, "_economy", networkManager.GetComponent<EconomyService>());
            SetField(service, "_customerPrefab", customerPrefab);
            SetField(service, "_entry", entry.transform);
            SetField(service, "_queueHead", head.transform);
            SetField(networkManager.GetComponent<KitchenService>(), "_customers", service);
        }

        /// <summary>Spec 012: local (not networked) ping marker: a thin pillar and a label.</summary>
        private static PingMarker CreatePingMarkerPrefab(Material material)
        {
            var root = new GameObject("PingMarker");
            GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pillar.name = "Pillar";
            UnityEngine.Object.DestroyImmediate(pillar.GetComponent<Collider>());
            pillar.transform.SetParent(root.transform, false);
            pillar.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            pillar.transform.localScale = new Vector3(0.15f, 1.5f, 0.15f);
            var renderer = pillar.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            TextMesh label = CreateLabel(root.transform, new Vector3(0f, 3.3f, 0f), 56, 0.045f);
            var marker = root.AddComponent<PingMarker>();
            SetField(marker, "_pillar", renderer);
            SetField(marker, "_label", label);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PingMarkerPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return saved.GetComponent<PingMarker>();
        }

        /// <summary>Spec 007: a food item. Root holds the network components and the collider;
        /// the visual cube and the label are children so their scale does not affect each other.
        /// Spec 017: a FoodVisual child draws the model; the cube stays as its fallback.</summary>
        private static NetworkObject CreateFoodPrefab(Material material, ArtCatalog catalog)
        {
            var root = new GameObject("Food");
            var collider = root.AddComponent<BoxCollider>();
            collider.size = Vector3.one * FoodSize;

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            UnityEngine.Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = Vector3.one * FoodSize;
            var renderer = body.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            TextMesh label = CreateLabel(root.transform, new Vector3(0f, 0.55f, 0f), 40, 0.035f);

            // Models stand on the bottom of the pickup collider (wrapper pivots are bottom centre).
            var visualObject = new GameObject("Visual");
            visualObject.transform.SetParent(root.transform, false);
            visualObject.transform.localPosition = new Vector3(0f, -FoodSize * 0.5f, 0f);
            var visual = visualObject.AddComponent<FoodVisual>();
            SetField(visual, "_catalog", catalog);
            SetField(visual, "_fallback", renderer);
            SetField(visual, "_partScale", 0.5f);
            SetField(visual, "_partRadius", 0.075f);
            SetField(visual, "_plateContentHeight", ContentHeight(catalog, ArtKey.FoodPlate, PlateFill));
            SetField(visual, "_bowlContentHeight", ContentHeight(catalog, ArtKey.FoodBowl, BowlFill));

            NetworkObject nob = root.AddComponent<NetworkObject>();
            SetField(nob, "_preventDespawnOnDisconnect", true);
            AddServerAuthoritativeTransform(root);
            root.AddComponent<Pickup>();
            var food = root.AddComponent<Food>();
            SetField(food, "_label", label);
            SetField(food, "_visual", visual);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, FoodPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return saved.GetComponent<NetworkObject>();
        }

        /// <summary>Spec 007: a row of stations north of the spawn, the serve feed and the kitchen service.</summary>
        private static void CreateKitchen(NetworkManager networkManager, NetworkObject foodPrefab, ArtCatalog catalog)
        {
            var parent = new GameObject("Kitchen");
            var stations = new List<Station>();
            Color[] crateColors =
            {
                new Color(0.85f, 0.72f, 0.45f), new Color(0.80f, 0.15f, 0.15f),
                new Color(0.95f, 0.35f, 0.20f), new Color(0.85f, 0.80f, 0.90f)
            };
            for (int i = 0; i < DemoIngredients.All.Count; i++)
            {
                string id = DemoIngredients.All[i].Id;
                Material mat = CreateMaterial(Root + "/Settings/Crate_" + id + ".mat", crateColors[i]);
                stations.Add(CreateStation(catalog, parent.transform, stations.Count, StationKind.Crate,
                    char.ToUpperInvariant(id[0]) + id.Substring(1) + " crate", -8.5f + i * 2f, mat, i));
            }
            stations.Add(CreateStation(catalog, parent.transform, stations.Count, StationKind.Board, "Board", 0.5f,
                CreateMaterial(Root + "/Settings/Station_Board.mat", new Color(0.75f, 0.58f, 0.35f)), 0));
            // Spec 016: pan and pot cook over time; their label (knob, what is cooking) is networked.
            Station pan = CreateStation(catalog, parent.transform, stations.Count, StationKind.Pan, "Pan", 3f,
                CreateMaterial(Root + "/Settings/Station_Pan.mat", new Color(0.2f, 0.2f, 0.22f)), 0);
            AddStationContents(catalog, pan, ArtKey.FoodPan, 0f, PanFill, 0.65f, 0.06f);
            stations.Add(pan);
            Station pot = CreateStation(catalog, parent.transform, stations.Count, StationKind.Pot, "Pot", 5.5f,
                CreateMaterial(Root + "/Settings/Station_Pot.mat", new Color(0.4f, 0.5f, 0.65f)), 0);
            AddStationContents(catalog, pot, ArtKey.FoodPot, 0f, PotFill, 0.6f, 0.05f);
            stations.Add(pot);
            stations.Add(CreateStation(catalog, parent.transform, stations.Count, StationKind.Counter, "Counter", 8.5f,
                CreateMaterial(Root + "/Settings/Station_Counter.mat", new Color(0.92f, 0.92f, 0.9f)), 0));

            // Spec 013: combine stations next to the counter; their contents are networked.
            Station plating = CreateStation(catalog, parent.transform, stations.Count, StationKind.PlatingTable, "Plating table", 11f,
                CreateMaterial(Root + "/Settings/Station_Plating.mat", new Color(0.86f, 0.84f, 0.72f)), 0);
            AddStationContents(catalog, plating, ArtKey.FoodPlate, 0f, PlateFill, 0.5f, 0.075f);
            stations.Add(plating);
            Station bowl = CreateStation(catalog, parent.transform, stations.Count, StationKind.Bowl, "Bowl", 13.5f,
                CreateMaterial(Root + "/Settings/Station_Bowl.mat", new Color(0.55f, 0.75f, 0.62f)), 0);
            // The bowl stands next to the blender of the spec 015 bowl station.
            AddStationContents(catalog, bowl, ArtKey.FoodBowl, -0.32f, BowlFill, 0.45f, 0.06f);
            stations.Add(bowl);

            var feedObject = new GameObject("ServeFeed");
            NetworkObject feedNob = feedObject.AddComponent<NetworkObject>();
            // Global: every client gets the feed, whatever the distance condition says.
            SetField(feedNob, "_isGlobal", true);
            var feed = feedObject.AddComponent<ServeFeed>();

            var kitchen = networkManager.gameObject.AddComponent<KitchenService>();
            SetField(kitchen, "_networkManager", networkManager);
            SetField(kitchen, "_pickups", networkManager.GetComponent<PickupService>());
            SetField(kitchen, "_foodPrefab", foodPrefab);
            SetField(kitchen, "_feed", feed);
            SetField(kitchen, "_stations", stations);

            // Spec 009: market board (global, like the feed) and the server-side economy.
            var boardObject = new GameObject("MarketBoard");
            NetworkObject boardNob = boardObject.AddComponent<NetworkObject>();
            SetField(boardNob, "_isGlobal", true);
            var board = boardObject.AddComponent<MarketBoard>();
            var economy = networkManager.gameObject.AddComponent<EconomyService>();
            SetField(economy, "_networkManager", networkManager);
            SetField(economy, "_board", board);
            SetField(kitchen, "_economy", economy);
        }

        private static Station CreateStation(ArtCatalog catalog, Transform parent, int index, StationKind kind, string displayName,
            float x, Material material, int ingredientIndex)
        {
            var root = new GameObject(displayName);
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(x, StationHeight * 0.5f, KitchenZ);

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(1.2f, StationHeight, 1.2f);
            body.GetComponent<MeshRenderer>().sharedMaterial = material;
            body.isStatic = true;

            // Spec 015 swap layer: the cube body and its collider stay; the catalog visual is a sibling.
            float labelY = StationHeight * 0.5f + 0.6f;
            if (catalog != null && catalog.TryGet(ArtCatalogRules.StationKey(kind), out GameObject wrapper))
            {
                UnityEngine.Object.DestroyImmediate(body.GetComponent<MeshRenderer>());
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(wrapper, root.transform);
                visual.transform.localPosition = new Vector3(0f, -StationHeight * 0.5f, 0f);
                SetStaticRecursive(visual);
                // Raise the label only when the model is taller than the cube.
                float top = ArtCatalogBuilder.TopOf(visual) - StationHeight * 0.5f;
                labelY = Mathf.Max(labelY, top + 0.1f);
            }

            string keys = StationRules.KeysHint(kind);
            TextMesh label = CreateLabel(root.transform, new Vector3(0f, labelY, 0f), 60, 0.05f);
            string price = string.Empty;
            if (kind == StationKind.Crate
                && GrocerPrices.TryGetGrocerPrice(DemoIngredients.All[ingredientIndex].Id, out long dollars))
                price = " " + EconomyRules.Money(dollars);
            // Short labels so neighbouring stations do not overlap (spec 011 screenshot check).
            string shortName = kind == StationKind.Crate ? displayName.Replace(" crate", string.Empty) : displayName;
            label.text = shortName + "\n" + (price.Length > 0 ? price.Trim() + " · " : string.Empty) + keys;

            var station = root.AddComponent<Station>();
            SetField(station, "_index", index);
            SetField(station, "_kind", (int)kind);
            SetField(station, "_displayName", displayName);
            SetField(station, "_ingredientIndex", ingredientIndex);
            return station;
        }

        /// <summary>
        /// Spec 013: scene network object that syncs what lies on the station to its label. Spec 017: a static
        /// tool model (plate, bowl, pan, pot) on top of the station and a content anchor with a FoodVisual that
        /// draws the contents inside it, at <paramref name="fill"/> of the tool's height.
        /// </summary>
        private static void AddStationContents(ArtCatalog catalog, Station station, ArtKey tool, float offsetX, float fill,
            float partScale, float partRadius)
        {
            station.gameObject.AddComponent<NetworkObject>();
            var contents = station.gameObject.AddComponent<StationContents>();
            SetField(contents, "_label", station.transform.Find("Label").GetComponent<TextMesh>());

            // Top of the furniture in station space (the root sits at half the station height).
            Transform art = station.transform.Find("Art_Station" + station.Kind);
            float top = art != null
                ? ArtCatalogBuilder.BaseTopOf(art.gameObject) + art.localPosition.y
                : StationHeight * 0.5f;
            float contentY = top;
            if (catalog != null && catalog.TryGet(tool, out GameObject toolPrefab))
            {
                var toolInstance = (GameObject)PrefabUtility.InstantiatePrefab(toolPrefab, station.transform);
                toolInstance.name = "Tool_" + tool;
                toolInstance.transform.localPosition = new Vector3(offsetX, top, 0f);
                SetStaticRecursive(toolInstance);
                contentY = top + ArtCatalogBuilder.HeightOf(toolPrefab) * fill;
            }

            var anchor = new GameObject("Contents");
            anchor.transform.SetParent(station.transform, false);
            anchor.transform.localPosition = new Vector3(offsetX, contentY, 0f);
            var visual = anchor.AddComponent<FoodVisual>();
            SetField(visual, "_catalog", catalog);
            SetField(visual, "_partScale", partScale);
            SetField(visual, "_partRadius", partRadius);
            SetField(contents, "_visual", visual);
        }

        // Spec 017: how deep contents sit in their container, as a fraction of its height.
        private const float PlateFill = 0.4f;
        private const float BowlFill = 0.3f;
        private const float PanFill = 0.35f;
        private const float PotFill = 0.75f;

        private static float ContentHeight(ArtCatalog catalog, ArtKey container, float fill)
        {
            if (catalog == null || !catalog.TryGet(container, out GameObject prefab)) return 0.02f;
            return ArtCatalogBuilder.HeightOf(prefab) * fill;
        }

        private static TextMesh CreateLabel(Transform parent, Vector3 localPosition, int fontSize, float characterSize)
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
            text.color = Color.white;
            return text;
        }

        /// <summary>
        /// Spec 010: the standard Cinemachine third-person rig. One camera in the scene; the local
        /// player binds it on spawn (PlayerCameraRig). Orbit, zoom range and wall avoidance here.
        /// </summary>
        private static void CreatePlayerCamera(Transform viewHold)
        {
            var go = new GameObject("PlayerCamera");
            go.transform.SetPositionAndRotation(new Vector3(0f, 6f, -10f), Quaternion.Euler(20f, 0f, 0f));
            var cam = go.AddComponent<CinemachineCamera>();
            cam.Lens.FieldOfView = 60f;
            // Spec 018: the brain copies the live camera's lens, so both cameras carry the short near clip.
            cam.Lens.NearClipPlane = ViewRules.NearClip;
            cam.Priority = 11; // live before the local player binds (the menu view), as before spec 018

            var orbit = go.AddComponent<CinemachineOrbitalFollow>();
            orbit.OrbitStyle = CinemachineOrbitalFollow.OrbitStyles.Sphere;
            orbit.Radius = 5f;
            orbit.TargetOffset = new Vector3(0f, 1.6f, 0f);
            orbit.VerticalAxis.Range = new Vector2(-30f, 70f);
            orbit.VerticalAxis.Value = 20f;
            orbit.VerticalAxis.Center = 20f;
            orbit.RadialAxis.Range = new Vector2(0.4f, 1.6f); // 2 to 8 m
            orbit.RadialAxis.Value = 1f;
            orbit.TrackerSettings.PositionDamping = new Vector3(0.2f, 0.2f, 0.2f);

            var composer = go.AddComponent<CinemachineRotationComposer>();
            composer.TargetOffset = new Vector3(0f, 1.6f, 0f);
            composer.Damping = new Vector2(0.1f, 0.1f);

            var deoccluder = go.AddComponent<CinemachineDeoccluder>();
            deoccluder.IgnoreTag = "Player";

            CinemachineCamera firstPerson = CreateFirstPersonCamera();

            var input = new GameObject("PlayerInput");
            var reader = input.AddComponent<PlayerInputReader>();
            SetField(reader, "_actions", AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath));
            var rig = input.AddComponent<PlayerCameraRig>();
            SetField(rig, "_thirdPerson", cam);
            SetField(rig, "_firstPerson", firstPerson);
            SetField(rig, "_viewHold", viewHold);
        }

        /// <summary>
        /// Spec 018: first-person camera. Hard lock to the player's Eyes anchor (no damping), rotation from
        /// pan (yaw) and tilt (pitch) in world axes, no deoccluder. PlayerCameraRig feeds the axes and the priority.
        /// </summary>
        private static CinemachineCamera CreateFirstPersonCamera()
        {
            var go = new GameObject("FirstPersonCamera");
            go.transform.SetPositionAndRotation(new Vector3(0f, ViewRules.EyeHeight, -3f), Quaternion.identity);
            var cam = go.AddComponent<CinemachineCamera>();
            cam.Lens.FieldOfView = ViewRules.FieldOfView;
            cam.Lens.NearClipPlane = ViewRules.NearClip;
            cam.Priority = 10;

            var follow = go.AddComponent<CinemachineHardLockToTarget>();
            follow.Damping = 0f;

            var panTilt = go.AddComponent<CinemachinePanTilt>();
            panTilt.ReferenceFrame = CinemachinePanTilt.ReferenceFrames.World;
            panTilt.PanAxis.Range = new Vector2(-180f, 180f);
            panTilt.PanAxis.Wrap = true;
            panTilt.TiltAxis.Range = new Vector2(ViewRules.MinPitch, ViewRules.MaxPitch);
            panTilt.TiltAxis.Wrap = false;
            panTilt.TiltAxis.Value = 0f;
            return cam;
        }

        private static void AddServerAuthoritativeTransform(GameObject go)
        {
            var nt = go.AddComponent<NetworkTransform>();
            SetField(nt, "_clientAuthoritative", false);
            SetField(nt, "_sendToOwner", true);
        }

        private static DistanceCondition CreateDistanceCondition()
        {
            var condition = AssetDatabase.LoadAssetAtPath<DistanceCondition>(DistanceConditionPath);
            if (condition == null)
            {
                condition = ScriptableObject.CreateInstance<DistanceCondition>();
                AssetDatabase.CreateAsset(condition, DistanceConditionPath);
            }
            SetField(condition, "_maximumDistance", InterestDistance);
            return condition;
        }

        private static void CreateEnvironment(Material groundMat)
        {
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var cam = new GameObject("Main Camera");
            cam.tag = "MainCamera";
            var unityCamera = cam.AddComponent<Camera>();
            unityCamera.nearClipPlane = ViewRules.NearClip; // spec 018: held food and nearby counters do not clip
            cam.AddComponent<AudioListener>();
            cam.transform.SetPositionAndRotation(new Vector3(0f, 12f, -18f), Quaternion.Euler(30f, 0f, 0f));
            var brain = cam.AddComponent<CinemachineBrain>();
            // Spec 018 (open question 3): short ease between first and third person.
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, ViewRules.BlendSeconds);
            // Spec 018: where the held food is drawn in first person (lower right of the view).
            var viewHold = new GameObject("ViewHold");
            viewHold.transform.SetParent(cam.transform, false);
            viewHold.transform.localPosition = ViewRules.ViewHoldOffset;
            CreatePlayerCamera(viewHold.transform);

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(10f, 1f, 10f); // 100 x 100 m
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMat;
            ground.isStatic = true;

            var walls = new GameObject("Boxes");
            Vector3[] positions =
            {
                new Vector3(17f, 1f, 2f), new Vector3(-8f, 1.5f, 4f), new Vector3(3f, 0.75f, -9f),
                new Vector3(-5f, 1f, -6f), new Vector3(30f, 2f, 30f), new Vector3(-35f, 2f, -25f)
            };
            Vector3[] sizes =
            {
                new Vector3(4f, 2f, 1f), new Vector3(1f, 3f, 6f), new Vector3(3f, 1.5f, 3f),
                new Vector3(2f, 2f, 2f), new Vector3(6f, 4f, 6f), new Vector3(6f, 4f, 6f)
            };
            for (int i = 0; i < positions.Length; i++)
            {
                GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = "Box " + (i + 1);
                box.transform.SetParent(walls.transform, false);
                box.transform.localPosition = positions[i];
                box.transform.localScale = sizes[i];
                box.isStatic = true;
            }
        }

        /// <summary>
        /// Spec 015: a short visual-only street east of the kitchen. The customers walk along
        /// z = 10.8 (x 8.5 to 24) on the north side of the road; houses stand behind them.
        /// Nothing here has a collider, so movement and interaction do not change.
        /// </summary>
        private static void CreateStreet(ArtCatalog catalog)
        {
            var street = new GameObject("Street");
            // Road strip z 5.5 to 10.5, x 9 to 39, 5 m tiles; a crossroad at the east end.
            for (int i = 0; i < 6; i++)
                PlaceArt(catalog, street.transform, i == 5 ? ArtKey.RoadCrossroad : ArtKey.Road, new Vector3(11.5f + i * 5f, 0.02f, 8f), 0f);
            // Facades north of the walking path, fronts facing the road (south).
            PlaceArt(catalog, street.transform, ArtKey.BuildingShop, new Vector3(21f, 0f, 17f), 180f);
            PlaceArt(catalog, street.transform, ArtKey.BuildingHouseA, new Vector3(28f, 0f, 17f), 180f);
            PlaceArt(catalog, street.transform, ArtKey.BuildingHouseC, new Vector3(35f, 0f, 17f), 180f);
            PlaceArt(catalog, street.transform, ArtKey.BuildingHouseF, new Vector3(43f, 0f, 17f), 180f);
            // One house south of the road, facing north.
            PlaceArt(catalog, street.transform, ArtKey.BuildingHouseK, new Vector3(30f, 0f, 0.5f), 0f);
            PlaceArt(catalog, street.transform, ArtKey.PropAwning, new Vector3(21f, 2.4f, 14.8f), 180f);
            // Street lights along the south edge of the road, arms over the road.
            foreach (float x in new[] { 14f, 26f, 38f })
                PlaceArt(catalog, street.transform, ArtKey.PropLamp, new Vector3(x, 0f, 4.8f), 180f);
            PlaceArt(catalog, street.transform, ArtKey.PropDumpster, new Vector3(24.2f, 0f, 15f), 0f);
            PlaceArt(catalog, street.transform, ArtKey.PropTrashcan, new Vector3(22f, 0f, 13.2f), 0f);
            PlaceArt(catalog, street.transform, ArtKey.PropTreeLarge, new Vector3(-11f, 0f, 17f), 0f);
            PlaceArt(catalog, street.transform, ArtKey.PropTreeSmall, new Vector3(16f, 0f, 14.3f), 0f);
            PlaceArt(catalog, street.transform, ArtKey.PropPlanter, new Vector3(-10f, 0f, 14.2f), 0f);
            PlaceArt(catalog, street.transform, ArtKey.PropPlanter, new Vector3(15.5f, 0f, 16f), 0f);
            // A fence behind the kitchen row.
            for (int i = 0; i < 10; i++)
                PlaceArt(catalog, street.transform, ArtKey.PropFence, new Vector3(-8.5f + i * 2.4f, 0f, 15.5f), 0f);
        }

        private static void SetStaticRecursive(GameObject root)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.isStatic = true;
        }

        private static void PlaceArt(ArtCatalog catalog, Transform parent, ArtKey key, Vector3 position, float yaw)
        {
            if (catalog == null || !catalog.TryGet(key, out GameObject wrapper)) return;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(wrapper, parent);
            instance.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            SetStaticRecursive(instance);
        }

        private static Transform[] CreateSpawnPoints()
        {
            var parent = new GameObject("SpawnPoints");
            var result = new List<Transform>();
            for (int i = 0; i < 4; i++)
            {
                var spawn = new GameObject("Spawn " + (i + 1));
                spawn.transform.SetParent(parent.transform, false);
                float angle = i * 90f;
                spawn.transform.SetPositionAndRotation(
                    Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, 0.1f, -3f),
                    Quaternion.Euler(0f, angle, 0f));
                result.Add(spawn.transform);
            }
            return result.ToArray();
        }

        private static void CreatePickups(GameObject pickupPrefab)
        {
            // 16 cubes on a ring near the spawn, 4 far away to exercise interest management.
            // Kept at the scene root: FishNet scene objects keep simple hierarchies.
            for (int i = 0; i < PickupCount; i++)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(pickupPrefab);
                instance.name = "PickupCube " + (i + 1);
                Vector3 position;
                if (i < 16)
                {
                    float angle = i * (360f / 16f);
                    position = Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, 0.25f, 5f + (i % 2) * 2f);
                }
                else
                {
                    position = new Vector3(i % 2 == 0 ? 45f : -45f, 0.25f, i < 18 ? 45f : -45f);
                }
                instance.transform.position = position;
            }
        }

        private static NetworkManager CreateNetworkManager(NetworkObject playerPrefab, Transform[] spawns, DistanceCondition distance)
        {
            var go = new GameObject("NetworkManager");
            var networkManager = go.AddComponent<NetworkManager>();
            // FishNet refuses to initialize without a prefab collection. DefaultPrefabObjects is
            // kept up to date by FishNet's prefab generator (it includes Player and PickupCube).
            var prefabs = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(DefaultPrefabObjectsPath);
            if (prefabs == null)
                throw new InvalidOperationException(DefaultPrefabObjectsPath + " not found");
            networkManager.SpawnablePrefabs = prefabs;
            SetField(networkManager, "_logging", CreateLoggingConfiguration());
            go.AddComponent<TimeManager>();

            var tugboat = go.AddComponent<Tugboat>();
            Transport steam = AddSteamTransport(go);
            var multipass = go.AddComponent<Multipass>();
            var transports = new List<Transport> { tugboat };
            if (steam != null) transports.Add(steam);
            SetField(multipass, "_transports", transports);

            var transportManager = go.AddComponent<TransportManager>();
            transportManager.Transport = multipass;

            var observers = go.AddComponent<ObserverManager>();
            SetField(observers, "_defaultConditions", new List<ObserverCondition> { distance });

            var spawner = go.AddComponent<PlayerSpawner>();
            SetField(spawner, "_playerPrefab", playerPrefab);
            spawner.Spawns = spawns;

            var pickups = go.AddComponent<PickupService>();
            SetField(pickups, "_networkManager", networkManager);
            return networkManager;
        }

        /// <summary>
        /// FishNet's default logs only errors in server builds (UNITY_SERVER), which hides
        /// warnings such as refused spawns on the dedicated server. Log warnings everywhere and
        /// common messages on the headless server.
        /// </summary>
        private static LevelLoggingConfiguration CreateLoggingConfiguration()
        {
            var logging = AssetDatabase.LoadAssetAtPath<LevelLoggingConfiguration>(LoggingConfigurationPath);
            if (logging == null)
            {
                logging = ScriptableObject.CreateInstance<LevelLoggingConfiguration>();
                AssetDatabase.CreateAsset(logging, LoggingConfigurationPath);
            }
            logging.LoggingConstructor(true, LoggingType.Common, LoggingType.Warning, LoggingType.Common);
            EditorUtility.SetDirty(logging);
            return logging;
        }

        private static Transport AddSteamTransport(GameObject go)
        {
            Type type = FindType(SteamTransportTypeName);
            if (type == null)
            {
                Debug.LogError("[Graybox] FishySteamworks transport type not found; only Tugboat added");
                return null;
            }
            var steam = (Transport)go.AddComponent(type);
            // P2P: clients join by SteamID64 (a host's user id or the dedicated server's game server id).
            SetField(steam, "_peerToPeer", true);
            return steam;
        }

        private static void CreateBootstrap(NetworkManager networkManager)
        {
            var go = new GameObject("Bootstrap");
            var steam = go.AddComponent<SteamBootstrap>();
            var bootstrap = go.AddComponent<NetBootstrap>();
            SetField(bootstrap, "_networkManager", networkManager);
            SetField(bootstrap, "_steam", steam);
            CreateUi(bootstrap);
        }

        /// <summary>
        /// Spec 011: one UI Toolkit document with the HUD, the F1 debug menu and the What's-new
        /// banner. The panel settings and the theme are assets so they stay reviewable.
        /// </summary>
        private static void CreateUi(NetBootstrap bootstrap)
        {
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(UiThemePath);
            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(UiStylePath);
            if (theme == null || style == null)
                throw new InvalidOperationException("UI theme or stylesheet missing: " + UiThemePath + ", " + UiStylePath);

            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(UiPanelPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, UiPanelPath);
            }
            panel.themeStyleSheet = theme;
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1280, 720);
            panel.match = 0.5f;
            EditorUtility.SetDirty(panel);

            var go = new GameObject("UI");
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = panel;
            var hud = go.AddComponent<HudView>();
            SetField(hud, "_bootstrap", bootstrap);
            SetField(hud, "_style", style);
            var debug = go.AddComponent<DebugMenuView>();
            SetField(debug, "_bootstrap", bootstrap);
            SetField(debug, "_style", style);
            var whatsNew = go.AddComponent<WhatsNewView>();
            SetField(whatsNew, "_style", style);
            go.AddComponent<DevScreenshot>();
            var wheel = go.AddComponent<EmoteWheelView>(); // spec 012
            SetField(wheel, "_style", style);
        }

        /// <summary>
        /// FishNet assigns scene ids itself from OnValidate, but throttles that to once per 250 ms,
        /// so objects created in one batch are missed. Call its generator once for the whole saved
        /// scene (the same thing its "Reserialize Scenes" menu does) and fail loudly if any id is unset.
        /// </summary>
        private static void AssignSceneIds(Scene scene)
        {
            MethodInfo create = typeof(NetworkObject).GetMethod("CreateSceneId",
                BindingFlags.Static | BindingFlags.NonPublic, null,
                new[] { typeof(Scene), typeof(bool), typeof(int).MakeByRefType() }, null);
            if (create == null)
                throw new InvalidOperationException("FishNet NetworkObject.CreateSceneId(Scene, bool, out int) not found");
            create.Invoke(null, new object[] { scene, true, 0 });
            EditorSceneManager.MarkSceneDirty(scene);

            FieldInfo sceneIdField = typeof(NetworkObject).GetField("SceneId", BindingFlags.Instance | BindingFlags.NonPublic);
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (NetworkObject nob in root.GetComponentsInChildren<NetworkObject>(true))
            {
                count++;
                if (sceneIdField != null && (ulong)sceneIdField.GetValue(nob) == NetworkObject.UNSET_SCENEID_VALUE)
                    throw new InvalidOperationException("Scene object " + nob.name + " has no SceneId");
            }
            Debug.Log("[Graybox] scene ids set on " + count + " scene network objects");
        }

        private static void SetBuildScenes()
        {
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static Material CreateMaterial(string path, Color color)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }

        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = assembly.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>Sets a serialized private field through SerializedObject so Unity records it.</summary>
        private static void SetField(UnityEngine.Object target, string field, object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
                throw new InvalidOperationException(target.GetType().Name + " has no serialized field " + field);

            switch (value)
            {
                case bool b: prop.boolValue = b; break;
                case float f: prop.floatValue = f; break;
                case int i: prop.intValue = i; break;
                case string s: prop.stringValue = s; break;
                case UnityEngine.Object o: prop.objectReferenceValue = o; break;
                case System.Collections.IList list:
                    prop.arraySize = list.Count;
                    for (int i = 0; i < list.Count; i++)
                        prop.GetArrayElementAtIndex(i).objectReferenceValue = (UnityEngine.Object)list[i];
                    break;
                default:
                    throw new ArgumentException("unsupported value type " + value.GetType().Name);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
