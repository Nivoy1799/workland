using System;
using System.Collections.Generic;
using System.Reflection;
using FishNet.Component.Observing;
using FishNet.Component.Spawning;
using FishNet.Component.Transforming;
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
using Worktown.Game.Cooking;
using Worktown.Game.Economy;
using Worktown.Sim.Economy;
using Worktown.Game.Net;

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
        private const float FoodSize = 0.4f;
        private const float StationHeight = 1f;
        private const float KitchenZ = 13f;
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

            Material groundMat = CreateMaterial(GroundMaterialPath, new Color(0.45f, 0.48f, 0.45f));
            Material pickupMat = CreateMaterial(PickupMaterialPath, new Color(0.95f, 0.6f, 0.15f));
            NetworkObject playerPrefab = CreatePlayerPrefab();
            GameObject pickupPrefab = CreatePickupPrefab(pickupMat);
            NetworkObject foodPrefab = CreateFoodPrefab(pickupMat);
            // Overwritten prefabs lose their AssetPathHash, and FishNet's import hook only hashes
            // newly added entries. A full refresh re-hashes every prefab in DefaultPrefabObjects.
            AssetDatabase.SaveAssets();
            if (!EditorApplication.ExecuteMenuItem(RefreshPrefabsMenu))
                throw new InvalidOperationException("FishNet menu not found: " + RefreshPrefabsMenu);
            DistanceCondition distance = CreateDistanceCondition();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateEnvironment(groundMat);
            Transform[] spawns = CreateSpawnPoints();
            CreatePickups(pickupPrefab);
            NetworkManager networkManager = CreateNetworkManager(playerPrefab, spawns, distance);
            CreateKitchen(networkManager, foodPrefab);
            CreateBootstrap(networkManager);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssignSceneIds(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            SetBuildScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("[Graybox] scene generated at " + ScenePath);
        }

        private static NetworkObject CreatePlayerPrefab()
        {
            var root = new GameObject("Player");
            var controller = root.AddComponent<CharacterController>();
            controller.height = 2f;
            controller.radius = 0.4f;
            controller.center = new Vector3(0f, 1f, 0f);

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            UnityEngine.Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 1f, 0f);

            GameObject nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "Facing";
            UnityEngine.Object.DestroyImmediate(nose.GetComponent<Collider>());
            nose.transform.SetParent(root.transform, false);
            nose.transform.localPosition = new Vector3(0f, 1.5f, 0.45f);
            nose.transform.localScale = new Vector3(0.3f, 0.15f, 0.2f);

            // Above the head, so a carried cube never blocks the CharacterController.
            var hold = new GameObject("HoldPoint");
            hold.transform.SetParent(root.transform, false);
            hold.transform.localPosition = new Vector3(0f, 2.5f, 0f);

            NetworkObject nob = root.AddComponent<NetworkObject>();
            AddServerAuthoritativeTransform(root);
            root.AddComponent<PlayerController>();
            var interactor = root.AddComponent<PickupInteractor>();
            root.AddComponent<Wallet>(); // spec 009
            SetField(interactor, "_holdPoint", hold.transform);

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

        /// <summary>Spec 007: a food item. Root holds the network components and the collider;
        /// the visual cube and the label are children so their scale does not affect each other.</summary>
        private static NetworkObject CreateFoodPrefab(Material material)
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

            NetworkObject nob = root.AddComponent<NetworkObject>();
            SetField(nob, "_preventDespawnOnDisconnect", true);
            AddServerAuthoritativeTransform(root);
            root.AddComponent<Pickup>();
            var food = root.AddComponent<Food>();
            SetField(food, "_label", label);
            SetField(food, "_renderer", renderer);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, FoodPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return saved.GetComponent<NetworkObject>();
        }

        /// <summary>Spec 007: a row of stations north of the spawn, the serve feed and the kitchen service.</summary>
        private static void CreateKitchen(NetworkManager networkManager, NetworkObject foodPrefab)
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
                stations.Add(CreateStation(parent.transform, stations.Count, StationKind.Crate,
                    char.ToUpperInvariant(id[0]) + id.Substring(1) + " crate", -7f + i * 1.5f, mat, i));
            }
            stations.Add(CreateStation(parent.transform, stations.Count, StationKind.Board, "Board", 0.5f,
                CreateMaterial(Root + "/Settings/Station_Board.mat", new Color(0.75f, 0.58f, 0.35f)), 0));
            stations.Add(CreateStation(parent.transform, stations.Count, StationKind.Pan, "Pan", 3f,
                CreateMaterial(Root + "/Settings/Station_Pan.mat", new Color(0.2f, 0.2f, 0.22f)), 0));
            stations.Add(CreateStation(parent.transform, stations.Count, StationKind.Pot, "Pot", 5.5f,
                CreateMaterial(Root + "/Settings/Station_Pot.mat", new Color(0.4f, 0.5f, 0.65f)), 0));
            stations.Add(CreateStation(parent.transform, stations.Count, StationKind.Counter, "Counter", 8.5f,
                CreateMaterial(Root + "/Settings/Station_Counter.mat", new Color(0.92f, 0.92f, 0.9f)), 0));

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

        private static Station CreateStation(Transform parent, int index, StationKind kind, string displayName,
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

            string keys = kind == StationKind.Crate || kind == StationKind.Counter ? "E" : "1-" + StationRules.OptionCount(kind);
            TextMesh label = CreateLabel(root.transform, new Vector3(0f, StationHeight * 0.5f + 0.6f, 0f), 60, 0.05f);
            string price = string.Empty;
            if (kind == StationKind.Crate
                && GrocerPrices.TryGetGrocerPrice(DemoIngredients.All[ingredientIndex].Id, out long dollars))
                price = " " + EconomyRules.Money(dollars);
            label.text = displayName + price + "\n" + keys;

            var station = root.AddComponent<Station>();
            SetField(station, "_index", index);
            SetField(station, "_kind", (int)kind);
            SetField(station, "_displayName", displayName);
            SetField(station, "_ingredientIndex", ingredientIndex);
            return station;
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
            cam.AddComponent<Camera>();
            cam.AddComponent<AudioListener>();
            cam.transform.SetPositionAndRotation(new Vector3(0f, 12f, -18f), Quaternion.Euler(30f, 0f, 0f));

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(10f, 1f, 10f); // 100 x 100 m
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMat;
            ground.isStatic = true;

            var walls = new GameObject("Boxes");
            Vector3[] positions =
            {
                new Vector3(6f, 1f, 6f), new Vector3(-8f, 1.5f, 4f), new Vector3(3f, 0.75f, -9f),
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
            var overlay = go.AddComponent<DebugOverlay>();
            SetField(overlay, "_bootstrap", bootstrap);
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
