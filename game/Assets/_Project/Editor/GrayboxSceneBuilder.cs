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
using Worktown.Game.Audio;
using Worktown.Game.Cooking;
using Worktown.Game.Economy;
using Worktown.Sim.Economy;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;
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
        private const string KnifePrefabPath = Root + "/Prefabs/Knife.prefab";
        private const string StoveButtonMaterialPath = Root + "/Settings/StoveButton.mat";
        // Spec 019: the knife's pickup collider (long side along Z, like its model).
        private static readonly Vector3 KnifeColliderSize = new Vector3(0.12f, 0.08f, 0.34f);
        private const float StationWidth = 1.2f;
        private const float WorktopGapMargin = 0.02f;
        private const string SteamMaterialPath = Root + "/Settings/Steam.mat";
        private const string WorktopMaterialPath = Root + "/Settings/Worktop.mat";
        private const string PlaceMarkerMaterialPath = Root + "/Settings/PlaceMarker.mat";
        private const float FoodSize = 0.4f;
        // Spec 021: the kitchen row after the crates. Stations keep the spec 015 grid (3, 5.5, 8.5, 11); worktops fill
        // the gaps and both ends (from WorktopStartX to the first stove, from the shelf to WorktopEndX).
        private const float StoveAX = 3f;
        private const float StoveBX = 5.5f;
        private const float CounterX = 8.5f;
        private const float PlateShelfX = 11f;
        private const float WorktopStartX = -0.1f;
        private const float WorktopEndX = 14.1f;
        // Spec 021: burners on the stove top (local x, z), one spot each; the stove top behind them is a place surface.
        private static readonly float[] BurnerX = { -0.3f, 0.3f };
        private const float BurnerZ = -0.12f;
        private const float BurnerSize = 0.42f;
        private const float BurnerRingDiameter = 0.3f;
        private const float StoveTopMinZ = 0.09f;
        private const float MinVesselColliderHeight = 0.06f;
        private const int PlateStackCount = 5;
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
            NetworkObject playerPrefab = CreatePlayerPrefab(pingPrefab, builtCatalog);
            GameObject pickupPrefab = CreatePickupPrefab(pickupMat);
            NetworkObject foodPrefab = CreateFoodPrefab(pickupMat, builtCatalog);
            NetworkObject customerPrefab = CreateCustomerPrefab(builtCatalog);
            NetworkObject knifePrefab = CreateKnifePrefab(builtCatalog); // spec 019
            List<NetworkObject> vesselPrefabs = CreateVesselPrefabs(builtCatalog); // spec 021
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
            CreateKitchen(networkManager, foodPrefab, knifePrefab, vesselPrefabs, catalog);
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
        /// Spec 024: the graphic shows a person (two variants, chosen by the owner id) instead of the capsule and nose.
        /// </summary>
        private static NetworkObject CreatePlayerPrefab(PingMarker pingPrefab, ArtCatalog catalog)
        {
            var root = new GameObject("Player");
            root.tag = "Player";
            var controller = root.AddComponent<CharacterController>();
            controller.height = 2f;
            controller.radius = 0.4f;
            controller.center = new Vector3(0f, 1f, 0f);

            var graphic = new GameObject("Graphic");
            graphic.transform.SetParent(root.transform, false);

            // Spec 024: the person replaces the spec 003 capsule and its nose (the controller capsule is unchanged).
            Character character = CreateCharacter(catalog, graphic.transform, ArtCatalogRules.PlayerCharacterKeys());

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
            // Spec 024: no _body any more: the crouch squash of the capsule is replaced by the crouch pose.
            SetField(player, "_eyes", eyes.transform);
            var interactor = root.AddComponent<PickupInteractor>();
            root.AddComponent<Wallet>(); // spec 009
            SetField(interactor, "_holdPoint", hold.transform);

            // Spec 012: emote bubble above the head (above a carried item) and social input.
            TextMesh bubbleLabel = CreateLabel(root.transform, new Vector3(0f, 3.2f, 0f), 56, 0.045f);
            bubbleLabel.color = new Color(1f, 0.9f, 0.4f);
            var bubble = root.AddComponent<EmoteBubble>();
            SetField(bubble, "_label", bubbleLabel);
            SetField(bubble, "_body", character.Container); // spec 024: the person wobbles
            var social = root.AddComponent<SocialInteractor>();
            SetField(social, "_pingPrefab", pingPrefab);
            SetField(social, "_bubble", bubble);

            // Spec 018: owner-only first-person visuals (own body shadows only, held food in view).
            var firstPersonBody = root.AddComponent<FirstPersonBody>();
            SetField(firstPersonBody, "_body", character.Renderers); // spec 024: every variant's renderers
            SetField(firstPersonBody, "_overhead", new Renderer[] { bubbleLabel.GetComponent<Renderer>() });
            root.AddComponent<HeldItemView>();
            AddCharacterVisual(root, graphic.transform, true, character, player, firstPersonBody);
            // Spec 020: owner-only target glow with the place marker, and the first-person label fade.
            var highlight = root.AddComponent<InteractionHighlight>();
            SetField(highlight, "_markerMaterial", CreatePlaceMarkerMaterial());
            root.AddComponent<WorldLabelFade>();

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

        /// <summary>
        /// Spec 014: NPC customer, moved by the server; no collider so it never blocks players.
        /// Spec 024: a person (four variants, chosen by the object id) tinted with the wish colour.
        /// </summary>
        private static NetworkObject CreateCustomerPrefab(ArtCatalog catalog)
        {
            var root = new GameObject("Customer");
            Character character = CreateCharacter(catalog, root.transform, ArtCatalogRules.CustomerCharacterKeys());
            TextMesh label = CreateLabel(root.transform, new Vector3(0f, 2.1f, 0f), 56, 0.045f);

            NetworkObject nob = root.AddComponent<NetworkObject>();
            AddServerAuthoritativeTransform(root);
            var customer = root.AddComponent<Customer>();
            SetField(customer, "_label", label);
            SetField(customer, "_tinted", character.Renderers);
            AddCharacterVisual(root, root.transform, false, character, null, null);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, CustomerPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return saved.GetComponent<NetworkObject>();
        }

        /// <summary>Spec 024: the character variants under one container, in ArtCatalogRules variant order.</summary>
        private sealed class Character
        {
            public Transform Container;
            public GameObject[] Variants;
            public Transform[] Hands;
            public Renderer[] Renderers;
        }

        /// <summary>Spec 024: one catalog wrapper per key under a "Character" container; the first is active until spawn.</summary>
        private static Character CreateCharacter(ArtCatalog catalog, Transform parent, ArtKey[] keys)
        {
            var container = new GameObject("Character");
            container.transform.SetParent(parent, false);
            var character = new Character
            {
                Container = container.transform,
                Variants = new GameObject[keys.Length],
                Hands = new Transform[keys.Length]
            };
            var renderers = new List<Renderer>();
            for (int i = 0; i < keys.Length; i++)
            {
                if (catalog == null || !catalog.TryGet(keys[i], out GameObject wrapper))
                    throw new InvalidOperationException("ArtCatalog has no " + keys[i]);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(wrapper, container.transform);
                instance.SetActive(i == 0);
                character.Variants[i] = instance;
                character.Hands[i] = ArtCatalogBuilder.FindDeep(instance.transform, CharacterVisual.HandName);
                if (character.Hands[i] == null) throw new InvalidOperationException(keys[i] + " has no " + CharacterVisual.HandName);
                renderers.AddRange(instance.GetComponentsInChildren<Renderer>(true));
            }
            character.Renderers = renderers.ToArray();
            return character;
        }

        /// <summary>Spec 024: wires the CharacterVisual (variants, hands, clips from the pack).</summary>
        private static void AddCharacterVisual(GameObject root, Transform tracked, bool byOwner, Character character,
            PlayerController player, FirstPersonBody firstPerson)
        {
            var visual = root.AddComponent<CharacterVisual>();
            SetField(visual, "_variants", character.Variants);
            SetField(visual, "_hands", character.Hands);
            SetField(visual, "_container", character.Container);
            SetField(visual, "_tracked", tracked);
            SetField(visual, "_variantByOwner", byOwner);
            if (player != null) SetField(visual, "_player", player);
            if (firstPerson != null) SetField(visual, "_firstPerson", firstPerson);
            AnimationClip walk = ArtCatalogBuilder.CharacterClip(ArtCatalogBuilder.WalkClip);
            AnimationClip jog = ArtCatalogBuilder.CharacterClip(ArtCatalogBuilder.JogClip);
            AnimationClip sprint = ArtCatalogBuilder.CharacterClip(ArtCatalogBuilder.RunClip);
            AnimationClip crouchWalk = ArtCatalogBuilder.CharacterClip(ArtCatalogBuilder.CrouchWalkClip);
            SetField(visual, "_idle", ArtCatalogBuilder.CharacterClip(ArtCatalogBuilder.IdleClip));
            SetField(visual, "_walk", walk);
            SetField(visual, "_jog", jog);
            SetField(visual, "_crouch", ArtCatalogBuilder.CharacterClip(ArtCatalogBuilder.CrouchClip));
            SetField(visual, "_crouchWalk", crouchWalk);
            SetField(visual, "_hold", ArtCatalogBuilder.HoldClip());

            // Spec 025 amendment: the clip speeds on this body (all variants share it), from the root motion; the feet
            // (StrideRules) are a cross-check.
            var body = PrefabUtility.GetCorrespondingObjectFromSource(character.Variants[0]);
            SetGait(visual, body, "_walk", walk);
            SetGait(visual, body, "_jog", jog);
            SetGait(visual, body, "_sprint", sprint);
            SetGait(visual, body, "_crouchWalk", crouchWalk);
            float offset = ArtCatalogBuilder.LeftFootForwardPhase(body, jog) - ArtCatalogBuilder.LeftFootForwardPhase(body, walk);
            offset = Mathf.Repeat(offset + 0.5f, 1f) - 0.5f;
            SetField(visual, "_jogPhaseOffset", offset);

            var so = new SerializedObject(visual);
            var walkGait = new GaitClip(so.FindProperty("_walkClipSpeed").floatValue, walk.length);
            var jogGait = new GaitClip(so.FindProperty("_jogClipSpeed").floatValue, jog.length);
            foreach (float speed in new[] { 1f, CustomerRules.WalkSpeed, MovementRules.WalkSpeed, MovementRules.SprintSpeed })
            {
                LocomotionBlend blend = CharacterAnimRules.Locomotion(speed, walkGait, jogGait);
                Debug.Log("[Art] " + root.name + " at " + speed.ToString("F2") + " m/s: jog weight " + blend.Weight.ToString("F3") + ", rate "
                          + blend.Rate.ToString("F3") + ", cadence " + blend.Cadence.ToString("F2") + " steps/s (target "
                          + CharacterAnimRules.TargetCadence(speed).ToString("F2") + "), feet "
                          + CharacterAnimRules.GroundSpeed(blend, walkGait, jogGait).ToString("F2") + " m/s");
            }
            Debug.Log("[Art] " + root.name + ": jog phase offset " + offset.ToString("F3"));
        }

        /// <summary>Spec 025 amendment: stores a clip's measured speed and loop length; warns when the feet disagree.</summary>
        private static void SetGait(CharacterVisual visual, GameObject body, string prefix, AnimationClip clip)
        {
            float speed = ArtCatalogBuilder.RootMotionSpeed(body, clip);
            if (speed <= 0f) throw new InvalidOperationException(clip.name + ": no root motion, clip speed unknown");
            float feet = ArtCatalogBuilder.MeasureClipSpeed(body, clip);
            float average = ArtCatalogBuilder.AverageSpeedOnBody(body, clip);
            SetField(visual, prefix + "ClipSpeed", speed);
            SetField(visual, prefix + "LoopLength", clip.length);
            string line = "[Art] " + clip.name + ": " + speed.ToString("F3") + " m/s (root motion), feet " + feet.ToString("F3")
                          + ", averageSpeed x human scale " + average.ToString("F3") + ", loop " + clip.length.ToString("F3") + " s";
            if (Mathf.Abs(feet - speed) > speed * 0.15f) Debug.LogWarning(line + ": the feet differ by more than 15 %");
            else Debug.Log(line);
        }

        /// <summary>Spec 014: street entry east of the kitchen and the queue in front of the counter.</summary>
        private static void CreateCustomers(NetworkManager networkManager, NetworkObject customerPrefab)
        {
            var parent = new GameObject("Customers");
            var entry = new GameObject("StreetEntry");
            entry.transform.SetParent(parent.transform, false);
            entry.transform.SetPositionAndRotation(new Vector3(24f, 0f, KitchenZ - 2.2f), Quaternion.Euler(0f, -90f, 0f)); // spec 024: y 0 (was 0.1), feet on the ground
            var head = new GameObject("QueueHead");
            head.transform.SetParent(parent.transform, false);
            head.transform.position = new Vector3(8.5f, 0f, KitchenZ - 2.2f); // in front of the counter; spec 024: y 0

            var service = networkManager.gameObject.AddComponent<CustomerService>();
            SetField(service, "_networkManager", networkManager);
            SetField(service, "_economy", networkManager.GetComponent<EconomyService>());
            SetField(service, "_customerPrefab", customerPrefab);
            SetField(service, "_entry", entry.transform);
            SetField(service, "_queueHead", head.transform);
            SetField(service, "_rail", CreateTicketRail()); // spec 022
            SetField(networkManager.GetComponent<KitchenService>(), "_customers", service);
        }

        // Spec 022: the ticket rail board above the counter, facing the players (south). Two rows of three cards.
        private const int TicketCards = 6;
        private static readonly Vector3 RailPosition = new Vector3(CounterX, 2.75f, KitchenZ + 0.75f);
        private static readonly Vector2 RailSize = new Vector2(2.5f, 1.15f);
        private static readonly Vector2 CardSize = new Vector2(0.74f, 0.4f);
        private const float CardStepX = 0.8f;
        private const float CardStepY = 0.46f;

        /// <summary>
        /// Spec 022: a global network object (orders and shift numbers for every client) with a dark board, a title and
        /// six cards (cream quad + text, hidden while unused). No colliders, so it never blocks aiming or walking.
        /// </summary>
        private static TicketRailView CreateTicketRail()
        {
            var root = new GameObject("TicketRail");
            root.transform.position = RailPosition;
            NetworkObject nob = root.AddComponent<NetworkObject>();
            SetField(nob, "_isGlobal", true);

            GameObject board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = "Board";
            UnityEngine.Object.DestroyImmediate(board.GetComponent<Collider>());
            board.transform.SetParent(root.transform, false);
            board.transform.localScale = new Vector3(RailSize.x, RailSize.y, 0.05f);
            board.GetComponent<MeshRenderer>().sharedMaterial = CreateMaterial(Root + "/Settings/TicketRail.mat", new Color(0.22f, 0.16f, 0.12f));
            board.isStatic = true;

            TextMesh title = CreateLabel(root.transform, new Vector3(0f, RailSize.y * 0.5f + 0.04f, -0.03f), 60, 0.03f);
            title.name = "Title";
            title.text = "ORDERS";

            Material cardMat = CreateMaterial(Root + "/Settings/TicketCard.mat", new Color(0.96f, 0.93f, 0.82f));
            var cards = new List<TextMesh>(TicketCards);
            var backs = new List<Renderer>(TicketCards);
            for (int i = 0; i < TicketCards; i++)
            {
                int column = i % 3;
                int row = i / 3;
                var centre = new Vector3((column - 1) * CardStepX, (0.5f - row) * CardStepY, 0f);

                GameObject back = GameObject.CreatePrimitive(PrimitiveType.Quad);
                back.name = "Card " + (i + 1);
                UnityEngine.Object.DestroyImmediate(back.GetComponent<Collider>());
                back.transform.SetParent(root.transform, false);
                back.transform.localPosition = centre + new Vector3(0f, 0f, -0.03f);
                back.transform.localScale = new Vector3(CardSize.x, CardSize.y, 1f);
                var backRenderer = back.GetComponent<MeshRenderer>();
                backRenderer.sharedMaterial = cardMat;
                backs.Add(backRenderer);

                TextMesh text = CreateLabel(root.transform, centre + new Vector3(0f, 0f, -0.04f), 48, 0.018f);
                text.name = "Card text " + (i + 1);
                text.anchor = TextAnchor.MiddleCenter;
                text.color = new Color(0.12f, 0.1f, 0.08f);
                text.text = string.Empty;
                cards.Add(text);
            }

            var rail = root.AddComponent<TicketRailView>();
            SetField(rail, "_title", title);
            SetField(rail, "_cards", cards);
            SetField(rail, "_cardBacks", backs);
            return rail;
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

        /// <summary>
        /// Spec 019: the kitchen knife, a normal pickup (carried like food) with a Knife marker and the Food Kit
        /// model. Its model lies along Z; the visual child sits on the bottom of the collider.
        /// </summary>
        private static NetworkObject CreateKnifePrefab(ArtCatalog catalog)
        {
            var root = new GameObject("Knife");
            var collider = root.AddComponent<BoxCollider>();
            collider.size = KnifeColliderSize;

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(0f, -KnifeColliderSize.y * 0.5f, 0f);
            if (catalog != null && catalog.TryGet(ArtKey.ToolKnife, out GameObject wrapper))
                PrefabUtility.InstantiatePrefab(wrapper, visual.transform);
            else
                Debug.LogError("[Graybox] no ToolKnife model in the art catalog");

            NetworkObject nob = root.AddComponent<NetworkObject>();
            // Like the scene cubes: a missed release on disconnect must never despawn the knife.
            SetField(nob, "_preventDespawnOnDisconnect", true);
            AddServerAuthoritativeTransform(root);
            root.AddComponent<Pickup>();
            root.AddComponent<Knife>();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, KnifePrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return saved.GetComponent<NetworkObject>();
        }

        // ---- spec 021: cookware prefabs ----

        private const string VesselPrefabFolder = Root + "/Prefabs";

        private static string VesselPrefabPath(VesselKind kind) => VesselPrefabFolder + "/" + kind + ".prefab";

        /// <summary>Spec 021: how deep contents sit in each kind of cookware (fraction of its height), part scale and radius.</summary>
        private static (float fill, float partScale, float partRadius) VesselContents(VesselKind kind)
        {
            switch (kind)
            {
                case VesselKind.Board: return (BoardFill, 1f, 0.06f);
                case VesselKind.Pan: return (PanFill, 0.65f, 0.06f);
                case VesselKind.Pot: return (PotFill, 0.6f, 0.05f);
                case VesselKind.Plate: return (PlateFill, 0.5f, 0.075f);
                default: return (BowlFill, 0.45f, 0.06f);
            }
        }

        private static List<NetworkObject> CreateVesselPrefabs(ArtCatalog catalog)
        {
            var prefabs = new List<NetworkObject>();
            foreach (VesselKind kind in new[] { VesselKind.Board, VesselKind.Pan, VesselKind.Pot, VesselKind.Plate, VesselKind.Bowl })
                prefabs.Add(CreateVesselPrefab(catalog, kind));
            return prefabs;
        }

        /// <summary>
        /// Spec 021: a piece of cookware, a normal pickup (carried like the knife) with a Vessel. The spec 017/019 model is
        /// the "Visual" child with its pivot at the bottom centre; the box collider wraps the model (at least 6 cm high so
        /// the aim ray and the spot probe find a flat board or plate); "Contents" draws what lies in it.
        /// </summary>
        private static NetworkObject CreateVesselPrefab(ArtCatalog catalog, VesselKind kind)
        {
            var root = new GameObject(kind.ToString());
            Bounds model = new Bounds(Vector3.zero, new Vector3(0.3f, 0.05f, 0.3f));
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            if (catalog != null && catalog.TryGet(ArtCatalogRules.VesselKey(kind), out GameObject wrapper))
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(wrapper, visual.transform);
                model = LocalBounds(root, instance);
            }
            else Debug.LogError("[Graybox] no model for " + kind + " in the art catalog");

            float height = Mathf.Max(model.max.y, MinVesselColliderHeight);
            var collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(model.size.x, height, model.size.z);
            collider.center = new Vector3(model.center.x, height * 0.5f, model.center.z);

            (float fill, float partScale, float partRadius) = VesselContents(kind);
            var anchor = new GameObject("Contents");
            anchor.transform.SetParent(root.transform, false);
            anchor.transform.localPosition = new Vector3(0f, model.max.y * fill, 0f);
            var food = anchor.AddComponent<FoodVisual>();
            SetField(food, "_catalog", catalog);
            SetField(food, "_partScale", partScale);
            SetField(food, "_partRadius", partRadius);

            TextMesh label = CreateLabel(root.transform, new Vector3(0f, height + 0.22f, 0f), 40, 0.03f);
            label.text = string.Empty;

            NetworkObject nob = root.AddComponent<NetworkObject>();
            // Like the scene cubes and the knife: a missed release on disconnect must never despawn cookware.
            SetField(nob, "_preventDespawnOnDisconnect", true);
            AddServerAuthoritativeTransform(root);
            root.AddComponent<Pickup>();
            var vessel = root.AddComponent<Vessel>();
            SetField(vessel, "_kind", (int)kind);
            SetField(vessel, "_label", label);
            SetField(vessel, "_visual", food);
            if (kind == VesselKind.Pan || kind == VesselKind.Pot)
                SetField(root.AddComponent<VesselSteam>(), "_material", CreateSteamMaterial());

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, VesselPrefabPath(kind));
            UnityEngine.Object.DestroyImmediate(root);
            return saved.GetComponent<NetworkObject>();
        }

        /// <summary>Bounds of the model's renderers in the root's space (the root sits at the origin).</summary>
        private static Bounds LocalBounds(GameObject root, GameObject model)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.one * 0.1f);
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
            b.center -= root.transform.position;
            return b;
        }

        /// <summary>Spec 021: a scene instance of a prefab (cookware, the knife) resting on a spot of a place surface.</summary>
        private static GameObject PlaceOnSpot(NetworkObject prefab, PlaceSurface surface, int spot, string name)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject);
            instance.name = name;
            float bottom = 0f;
            var box = instance.GetComponent<BoxCollider>();
            if (box != null) bottom = box.size.y * 0.5f - box.center.y; // the same offset Pickup.BottomOffset uses
            instance.transform.SetPositionAndRotation(surface.SpotWorldPosition(spot) + Vector3.up * bottom, surface.transform.rotation);
            return instance;
        }

        /// <summary>The spot of the surface nearest to a point in its top plane given in metres from its centre.</summary>
        private static int SpotNear(PlaceSurface surface, float x, float z)
        {
            return PlaceSpots.NearestFree(surface.Size, new Vector2(x, z), null);
        }

        /// <summary>
        /// Spec 020/021: a plain worktop from <paramref name="minX"/> to <paramref name="maxX"/>, as deep as the stations,
        /// <paramref name="height"/> high (the stove top); its whole top is a place surface. Not scaled (PlaceSurface
        /// works in metres).
        /// </summary>
        private static PlaceSurface CreateWorktop(Transform parent, float minX, float maxX, float height, int index, Material material,
            ArtCatalog catalog)
        {
            float width = maxX - minX;
            var root = new GameObject("Worktop " + index);
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3((minX + maxX) * 0.5f, 0f, KitchenZ);
            var box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(width, height, StationWidth);
            box.center = new Vector3(0f, height * 0.5f, 0f);

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            UnityEngine.Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
            body.transform.localScale = new Vector3(width, height, StationWidth);
            body.GetComponent<MeshRenderer>().sharedMaterial = material;

            // Spec 023: the Furniture Kit cabinet stretched to the worktop; the cube and collider stay as they were.
            if (catalog != null && catalog.TryGet(ArtKey.Worktop, out GameObject wrapper))
            {
                UnityEngine.Object.DestroyImmediate(body.GetComponent<MeshRenderer>());
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(wrapper, root.transform);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localScale = new Vector3(width, height, StationWidth);
            }
            SetStaticRecursive(root);

            var surface = root.AddComponent<PlaceSurface>();
            SetField(surface, "_index", index);
            SetField(surface, "_size", new Vector2(width, StationWidth));
            SetField(surface, "_topY", height);
            return surface;
        }

        /// <summary>
        /// Spec 021: a place surface on top of a station (a burner or the stove top behind the burners): a thin collider
        /// just above the station's body, so the aim ray hits it first; items rest on the furniture top.
        /// </summary>
        private static PlaceSurface CreateTopSurface(Station station, string name, Vector2 centre, Vector2 size, int index)
        {
            float top = StationTop(station);
            float colliderTop = Mathf.Max(top, StationHeight * 0.5f) + 0.005f;
            var go = new GameObject(name);
            go.transform.SetParent(station.transform, false);
            go.transform.localPosition = new Vector3(centre.x, 0f, centre.y);
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(size.x, 0.02f, size.y);
            box.center = new Vector3(0f, colliderTop - 0.01f, 0f);
            go.isStatic = true;
            var surface = go.AddComponent<PlaceSurface>();
            SetField(surface, "_index", index);
            SetField(surface, "_size", size);
            SetField(surface, "_topY", top);
            return surface;
        }

        /// <summary>Spec 023: transparent URP particle material for the steam (texture and tint are set at runtime).</summary>
        private static Material CreateSteamMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(SteamMaterialPath);
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, SteamMaterialPath);
            }
            else if (material.shader != shader) material.shader = shader;
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 1f); // transparent
            material.SetFloat("_Blend", 0f);   // alpha
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Spec 020: translucent green for the place marker disc (URP Unlit, transparent, no depth write).</summary>
        private static Material CreatePlaceMarkerMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(PlaceMarkerMaterialPath);
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Transparent");
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, PlaceMarkerMaterialPath);
            }
            else if (material.shader != shader) material.shader = shader;
            material.SetColor("_BaseColor", new Color(0.35f, 1f, 0.45f, 0.45f));
            material.SetFloat("_Surface", 1f); // transparent
            material.SetFloat("_Blend", 0f);   // alpha
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Spec 019/021: per burner an on/off knob and a fry/sauté button, small colliders on the stove's front with a
        /// StationButton (option = StationRules.StoveOption(burner, button)), and a flat ring on the burner; all coloured
        /// from the synced stove flags by a StoveButtonVisual.
        /// </summary>
        private static void AddStoveButtons(Station station, PlaceSurface[] burners)
        {
            Material material = CreateMaterial(StoveButtonMaterialPath, Color.white);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            var visual = station.gameObject.AddComponent<StoveButtonVisual>();
            SetField(visual, "_contents", station.GetComponent<StationContents>());

            var switches = new List<UnityEngine.Object>();
            var modes = new List<UnityEngine.Object>();
            var labels = new List<UnityEngine.Object>();
            var rings = new List<UnityEngine.Object>();
            for (int b = 0; b < burners.Length; b++)
            {
                float x = BurnerX[b];
                switches.Add(CreateButton(station, "Switch " + b, new Vector3(x - 0.09f, 0.3f, -0.64f),
                    StationRules.StoveOption(b, StationRules.StoveSwitch), material));
                CreateButtonLabel(station, "SwitchLabel " + b, new Vector3(x - 0.09f, 0.39f, -0.68f), "on/off");
                modes.Add(CreateButton(station, "ModeButton " + b, new Vector3(x + 0.09f, 0.3f, -0.64f),
                    StationRules.StoveOption(b, StationRules.PanMode), material));
                labels.Add(CreateButtonLabel(station, "ModeLabel " + b, new Vector3(x + 0.09f, 0.39f, -0.68f), "fry"));
                rings.Add(CreateBurnerRing(burners[b], material));
            }
            SetField(visual, "_switches", switches);
            SetField(visual, "_modeButtons", modes);
            SetField(visual, "_modeLabels", labels);
            SetField(visual, "_rings", rings);
        }

        private static Renderer CreateButton(Station station, string name, Vector3 localPosition, int option, Material material)
        {
            GameObject button = GameObject.CreatePrimitive(PrimitiveType.Cube); // keeps its BoxCollider for the aim ray
            button.name = name;
            button.transform.SetParent(station.transform, false);
            button.transform.localPosition = localPosition;
            button.transform.localScale = new Vector3(0.14f, 0.14f, 0.06f);
            var renderer = button.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            var marker = button.AddComponent<StationButton>();
            SetField(marker, "_station", station);
            SetField(marker, "_option", option);
            return renderer;
        }

        private static TextMesh CreateButtonLabel(Station station, string name, Vector3 localPosition, string text)
        {
            TextMesh label = CreateLabel(station.transform, localPosition, 40, 0.02f);
            label.gameObject.name = name; // not "Label": WorldLabelFade finds the station label by that name
            label.text = text;
            return label;
        }

        /// <summary>Spec 021: a flat ring on the burner (no collider), dark while off and glowing while on.</summary>
        private static Renderer CreateBurnerRing(PlaceSurface burner, Material material)
        {
            GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Ring";
            UnityEngine.Object.DestroyImmediate(ring.GetComponent<Collider>());
            ring.transform.SetParent(burner.transform, false);
            ring.transform.localPosition = new Vector3(0f, burner.TopY + 0.003f, 0f);
            ring.transform.localScale = new Vector3(BurnerRingDiameter, 0.003f, BurnerRingDiameter);
            var renderer = ring.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return renderer;
        }

        /// <summary>Spec 017/019: top of the station's furniture model in station space (the root sits at half height).</summary>
        private static float StationTop(Station station)
        {
            Transform art = station.transform.Find("Art_" + ArtCatalogRules.StationKey(station.Kind));
            return art != null ? ArtCatalogBuilder.BaseTopOf(art.gameObject) + art.localPosition.y : StationHeight * 0.5f;
        }

        /// <summary>
        /// Spec 007/021: the kitchen row north of the spawn: crates, worktops, two stoves (two burners each), the counter
        /// and the plate shelf; the cookware lying on top; the serve feed and the kitchen service.
        /// </summary>
        private static void CreateKitchen(NetworkManager networkManager, NetworkObject foodPrefab, NetworkObject knifePrefab,
            List<NetworkObject> vesselPrefabs, ArtCatalog catalog)
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
            Material stoveMat = CreateMaterial(Root + "/Settings/Station_Pan.mat", new Color(0.2f, 0.2f, 0.22f));
            Station stoveA = CreateStation(catalog, parent.transform, stations.Count, StationKind.Stove, "Stove 1", StoveAX, stoveMat, 0);
            stations.Add(stoveA);
            Station stoveB = CreateStation(catalog, parent.transform, stations.Count, StationKind.Stove, "Stove 2", StoveBX, stoveMat, 0);
            stations.Add(stoveB);
            stations.Add(CreateStation(catalog, parent.transform, stations.Count, StationKind.Counter, "Counter", CounterX,
                CreateMaterial(Root + "/Settings/Station_Counter.mat", new Color(0.92f, 0.92f, 0.9f)), 0));
            Station shelf = CreateStation(catalog, parent.transform, stations.Count, StationKind.PlateShelf, "Plates", PlateShelfX,
                CreateMaterial(Root + "/Settings/Station_Plating.mat", new Color(0.86f, 0.84f, 0.72f)), 0);
            CreatePlateStack(catalog, shelf);
            stations.Add(shelf);

            // Worktops at the stove top's height: before the first stove, between the stations, after the shelf.
            float height = stoveA.transform.position.y + StationTop(stoveA);
            float half = StationWidth * 0.5f + WorktopGapMargin;
            Material worktopMat = CreateMaterial(WorktopMaterialPath, new Color(0.78f, 0.74f, 0.66f));
            var surfaces = new List<PlaceSurface>();
            PlaceSurface first = CreateWorktop(parent.transform, WorktopStartX, StoveAX - half, height, surfaces.Count, worktopMat, catalog);
            surfaces.Add(first);
            PlaceSurface[] burnersA = CreateStoveSurfaces(stoveA, surfaces);
            surfaces.Add(CreateWorktop(parent.transform, StoveAX + half, StoveBX - half, height, surfaces.Count, worktopMat, catalog));
            PlaceSurface[] burnersB = CreateStoveSurfaces(stoveB, surfaces);
            surfaces.Add(CreateWorktop(parent.transform, StoveBX + half, CounterX - half, height, surfaces.Count, worktopMat, catalog));
            PlaceSurface nearCounter = CreateWorktop(parent.transform, CounterX + half, PlateShelfX - half, height, surfaces.Count, worktopMat, catalog);
            surfaces.Add(nearCounter);
            PlaceSurface last = CreateWorktop(parent.transform, PlateShelfX + half, WorktopEndX, height, surfaces.Count, worktopMat, catalog);
            surfaces.Add(last);
            SetField(networkManager.GetComponent<PickupService>(), "_surfaces", surfaces);
            foreach (Station stove in new[] { stoveA, stoveB })
            {
                stove.gameObject.AddComponent<NetworkObject>();
                stove.gameObject.AddComponent<StationContents>();
            }
            AddStoveButtons(stoveA, burnersA);
            AddStoveButtons(stoveB, burnersB);

            // Spec 021: the cookware lying on top at the start (each one's home spot, where a lost one comes back).
            NetworkObject Prefab(VesselKind kind) => vesselPrefabs.Find(p => p.GetComponent<Vessel>().Kind == kind);
            PlaceOnSpot(Prefab(VesselKind.Board), first, SpotNear(first, 0f, -0.125f), "Cutting board");
            PlaceOnSpot(knifePrefab, first, SpotNear(first, 0.5f, -0.125f), "Knife");
            PlaceOnSpot(Prefab(VesselKind.Pan), burnersA[0], 0, "Pan");
            PlaceOnSpot(Prefab(VesselKind.Pot), burnersB[0], 0, "Pot");
            PlaceOnSpot(Prefab(VesselKind.Plate), nearCounter, SpotNear(nearCounter, -0.25f, -0.125f), "Plate 1");
            PlaceOnSpot(Prefab(VesselKind.Plate), nearCounter, SpotNear(nearCounter, 0.25f, -0.125f), "Plate 2");
            PlaceOnSpot(Prefab(VesselKind.Bowl), last, SpotNear(last, -0.5f, -0.125f), "Bowl");

            new GameObject("KitchenSounds").AddComponent<KitchenSounds>(); // spec 023, client only

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
            SetField(kitchen, "_knifePrefab", knifePrefab); // spec 019: replaces a lost knife
            SetField(kitchen, "_vesselPrefabs", vesselPrefabs); // spec 021: respawns and the plate shelf

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

        /// <summary>
        /// Spec 021: a stove's two burners (one spot each, the burner flag) and the stove top behind them (a normal place
        /// surface), appended to <paramref name="surfaces"/> in that order: stove top, burner 0, burner 1.
        /// </summary>
        private static PlaceSurface[] CreateStoveSurfaces(Station stove, List<PlaceSurface> surfaces)
        {
            float back = StationWidth * 0.5f;
            surfaces.Add(CreateTopSurface(stove, "StoveTop", new Vector2(0f, (StoveTopMinZ + back) * 0.5f),
                new Vector2(StationWidth, back - StoveTopMinZ), surfaces.Count));
            var burners = new PlaceSurface[StationRules.BurnersPerStove];
            for (int b = 0; b < burners.Length; b++)
            {
                PlaceSurface burner = CreateTopSurface(stove, "Burner " + b, new Vector2(BurnerX[b], BurnerZ),
                    new Vector2(BurnerSize, BurnerSize), surfaces.Count);
                SetField(burner, "_isBurner", true);
                SetField(burner, "_stoveIndex", stove.Index);
                SetField(burner, "_burner", b);
                surfaces.Add(burner);
                burners[b] = burner;
            }
            return burners;
        }

        /// <summary>Spec 021: a static stack of plate models on the plate shelf (the shelf gives the real plates).</summary>
        private static void CreatePlateStack(ArtCatalog catalog, Station shelf)
        {
            if (catalog == null || !catalog.TryGet(ArtKey.FoodPlate, out GameObject plate)) return;
            float top = StationTop(shelf);
            float step = Mathf.Max(ArtCatalogBuilder.HeightOf(plate) * 0.6f, 0.015f);
            var stack = new GameObject("PlateStack");
            stack.transform.SetParent(shelf.transform, false);
            stack.transform.localPosition = new Vector3(0f, top, -0.1f);
            for (int i = 0; i < PlateStackCount; i++)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(plate, stack.transform);
                instance.transform.localPosition = new Vector3(0f, i * step, 0f);
            }
            SetStaticRecursive(stack);
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

            // Spec 020: no keys on the world labels any more (first person clicks; the prompts say how).
            TextMesh label = CreateLabel(root.transform, new Vector3(0f, labelY, 0f), 60, 0.05f);
            string price = string.Empty;
            if (kind == StationKind.Crate
                && GrocerPrices.TryGetGrocerPrice(DemoIngredients.All[ingredientIndex].Id, out long dollars))
                price = " " + EconomyRules.Money(dollars);
            // Short labels so neighbouring stations do not overlap (spec 011 screenshot check).
            string shortName = kind == StationKind.Crate ? displayName.Replace(" crate", string.Empty) : displayName;
            label.text = price.Length > 0 ? shortName + "\n" + price.Trim() : shortName;

            var station = root.AddComponent<Station>();
            SetField(station, "_index", index);
            SetField(station, "_kind", (int)kind);
            SetField(station, "_displayName", displayName);
            SetField(station, "_ingredientIndex", ingredientIndex);
            return station;
        }

        // Spec 017: how deep contents sit in their container, as a fraction of its height.
        private const float PlateFill = 0.4f;
        private const float BowlFill = 0.3f;
        private const float PanFill = 0.35f;
        private const float PotFill = 0.75f;
        private const float BoardFill = 1f; // spec 019: food lies on top of the cutting board

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
            var summary = go.AddComponent<ShiftSummaryView>(); // spec 022
            SetField(summary, "_style", style);
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
                case Vector2 v: prop.vector2Value = v; break;
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
