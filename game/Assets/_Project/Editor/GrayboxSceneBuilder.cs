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
