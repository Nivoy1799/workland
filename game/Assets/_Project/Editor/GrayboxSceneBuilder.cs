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
        /// <summary>Spec 027b: the clip whose legs are blended over the idle (feet under the hips).</summary>
        public const string StandLegsClip = "A_TPose";
        public const string ScenePath = Root + "/Scenes/Graybox.unity";
        private const string PlayerPrefabPath = Root + "/Prefabs/Player.prefab";
        private const string PickupPrefabPath = Root + "/Prefabs/PickupCube.prefab";
        private const string FoodPrefabPath = Root + "/Prefabs/Food.prefab";
        private const string PingMarkerPrefabPath = Root + "/Prefabs/PingMarker.prefab";
        private const string CustomerPrefabPath = Root + "/Prefabs/Customer.prefab";
        private const string KnifePrefabPath = Root + "/Prefabs/Knife.prefab";
        private const string SpatulaPrefabPath = Root + "/Prefabs/Spatula.prefab"; // spec 030
        private const string StoveButtonMaterialPath = Root + "/Settings/StoveButton.mat";
        // Spec 019: the knife's pickup collider (long side along Z, like its model).
        private static readonly Vector3 KnifeColliderSize = new Vector3(0.12f, 0.08f, 0.345f); // spec 026: the 0.345 m knife
        private const float StationWidth = ArtCatalogBuilder.FitXZ; // the station footprint, also the worktop depth
        private const float WorktopGapMargin = 0.02f;
        /// <summary>Spec 026: name prefix of the cabinet modules under a worktop ("Module 1", "Module 2", ...).</summary>
        public const string WorktopModuleName = "Module";
        private const string SteamMaterialPath = Root + "/Settings/Steam.mat";
        private const string WorktopMaterialPath = Root + "/Settings/Worktop.mat";
        private const string PlaceMarkerMaterialPath = Root + "/Settings/PlaceMarker.mat";
        private const float FoodSize = 0.4f;
        // Spec 027a (docs/feel.md 6): the shop floor plan. A back line along the back wall (z KitchenZ, fronts facing -z),
        // left to right as seen from the aisle: a landing worktop by the staff gap, the stock (spec 028b), the prep worktop (board
        // and knife), stove A, a worktop, stove B, a worktop (plates), the plate shelf (plating table), a worktop (bowl).
        // Then the cook aisle (AisleWidth between the bodies) and the front counter (1.05 m) with the serving counter
        // opposite the plate shelf, turned so its cook side faces the aisle. Guests queue on the street side of the front
        // counter. A back wall closes the back line; low side walls close both ends, with the staff gap at the left end.
        // Spec 028b: the stock is a dry shelf and a fridge (StockArt, StockLayout) where the last two of the four 027a crates
        // stood, right next to the prep worktop: landing worktop, shelf, fridge, prep. The 028 crate bank and the four crates
        // are gone; the prep worktop, the stoves and everything right of them keep their 027a places, and the kitchen's
        // left end (with the staff gap) moved 2.5 m in, so the kitchen is shorter than 027a's.
        private const float LandingWidth = 1.26f;
        private const float StationHalfGap = StationWidth * 0.5f + WorktopGapMargin;
        private const float PrepStartX = 4.22f; // spec 027a's prep worktop start
        private const float FridgeX = PrepStartX - StationHalfGap;
        private const float StockShelfX = FridgeX - 2f * StationHalfGap;
        private const float KitchenMinX = StockShelfX - StationHalfGap - LandingWidth;
        // Spec 030: the prep run is one board wide (0.74 m: the board and the knife), so the griddle fits next to the fryer and
        // every 027a/029b distance holds (board to stove A 2.9 m, the fryer and everything right of it unchanged).
        private const float PrepWidth = 0.74f;
        private const float BetweenWidth = 1.26f;
        // Spec 029: the deep fryer joins the back line between prep and the stoves (dish flow: stock, prep, fryer, stoves, plates).
        private const float FryerHalfGap = FryerArt.Width * 0.5f + WorktopGapMargin;
        // Spec 030: the griddle in the heat block, between prep and the fryer (dish flow: stock, prep, griddle, fryer, stoves, plates).
        private const float GriddleHalfGap = GriddleArtRules.Width * 0.5f + WorktopGapMargin;
        private const float GriddleX = PrepStartX + PrepWidth + GriddleHalfGap;
        private const float FryerX = GriddleX + GriddleHalfGap + FryerHalfGap;
        // Spec 029b: the two stoves stand side by side (the worktop that stood between them moved to the right end), so the
        // plating worktop and the plate shelf come 1.26 m closer to the fryer: fryer to the first plate about 3.2 m (was 4.5).
        // The kitchen keeps its length; the serving counter, the queue and the rail move with the plate shelf.
        private const float StoveAX = FryerX + FryerHalfGap + StationHalfGap;
        private const float StoveBX = StoveAX + 2f * StationHalfGap;
        private const float PlateShelfX = StoveBX + 2f * StationHalfGap + BetweenWidth;
        private const float KitchenMaxX = PlateShelfX + StationHalfGap + 2f * BetweenWidth;
        /// <summary>Spec 027a: the serving counter stands in the front counter, opposite the plate shelf.</summary>
        private const float CounterX = PlateShelfX;
        /// <summary>Spec 027a: clear floor between the back line's bodies and the front counter (feel.md 6: at least 1.2 m).</summary>
        private const float AisleWidth = 1.5f; // orchestrator: the top of the range, so two cooks (0.3 m controllers) pass each other
        /// <summary>Spec 027a (orchestrator): the front counter and the serving counter are 0.7 m deep (stations are 1.2 m).</summary>
        private const float CounterDepth = 0.7f;
        private const float FrontCounterZ = KitchenZ - StationWidth * 0.5f - AisleWidth - CounterDepth * 0.5f;
        private const float AisleZ = KitchenZ - (StationWidth + AisleWidth) * 0.5f;
        /// <summary>Spec 027a: the guests' queue line, this far in front of the front counter's street side.</summary>
        private const float QueueGap = 0.4f;
        private const float QueueZ = FrontCounterZ - CounterDepth * 0.5f - QueueGap;
        private const float StreetEntryX = 24f;
        private const float WallThickness = 0.2f;
        private const float WallGap = 0.02f;
        private const float BackWallHeight = 2.4f;
        private const float SideWallHeight = 1.2f;
        private static readonly float[] SpawnX = { 1.2f, 2.6f, 4.0f, 5.4f }; // spec 028b: the aisle starts at x 0.46 now
        /// <summary>
        /// Spec 027a: the catalog's station and cabinet wrappers show their front (the stove's knobs and oven door, the
        /// cabinet doors) on their local +z, so they are turned half way round to put it on the station's front (local -z,
        /// where the buttons and the label are). Until now the cook saw the backs ("buttons are on the back").
        /// </summary>
        private static readonly Quaternion ArtFrontRotation = Quaternion.Euler(0f, 180f, 0f);
        // Spec 021: burners on the stove top (local x, z), one spot each; the stove top behind them is a place surface.
        private static readonly float[] BurnerX = { -0.3f, 0.3f };
        private const float BurnerZ = -0.12f;
        private const float BurnerSize = 0.42f;
        private const float BurnerRingDiameter = 0.3f;
        private const float StoveTopMinZ = 0.09f;
        private const float MinVesselColliderHeight = 0.06f;
        private const int PlateStackCount = 5;
        // Spec 026: the knob row and its labels sit where they were on the 1.1 m stove (0.8 and 0.89 m above the floor),
        // as fractions of the stove's height, so they stay on the same part of the scaled model.
        private const float KnobRowFraction = 0.8f / 1.1f;
        private const float KnobLabelFraction = 0.89f / 1.1f;
        private const float KitchenZ = 14.3f; // spec 027a: was 13; the back line, its aisle and the front counter north of the road

        /// <summary>
        /// Spec 026 (was a fixed 1.0 m): height of a station's body and furniture: the worktop height (0.90 m), the serving
        /// counter 1.05 m. The station root sits at half of it.
        /// </summary>
        private static float StationHeightOf(StationKind kind) =>
            kind == StationKind.Counter ? ArtSizeTable.ServingCounterHeight
            : StockLayout.IsStock(kind) ? ArtSizeTable.StockHeight // spec 028b: the fridge and the dry shelf
            : ArtSizeTable.WorktopHeight;

        /// <summary>Spec 027a/028b: how deep a station's body is: 1.2 m, the counter 0.7 m, the fridge 0.62 m, the shelf 0.5 m.</summary>
        private static float StationDepthOf(StationKind kind) =>
            kind == StationKind.Counter ? CounterDepth : StockLayout.IsStock(kind) ? StockArt.DepthOf(kind) : StationWidth;
        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
        private const string UiThemePath = Root + "/UI/WorktownTheme.tss";
        private const string UiStylePath = Root + "/UI/Worktown.uss";
        private const string UiPanelPath = Root + "/UI/WorktownPanel.asset";
        private static readonly EconomyCatalog GrocerPrices = EconomyCatalog.CreateDefault();
        private const string DistanceConditionPath = Root + "/Settings/DistanceCondition40m.asset";
        private const string LoggingConfigurationPath = Root + "/Settings/FishNetLogging.asset";
        private const string GroundMaterialPath = Root + "/Settings/Ground.mat";
        private const string WallMaterialPath = Root + "/Settings/Wall.mat"; // spec 026: the graybox boxes
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
            EnsureLabelMaterials(); // spec 027d

            ArtCatalog builtCatalog = ArtCatalogBuilder.Build(); // spec 015: wrappers and catalog first
            // Spec 026: palette colours; graybox fallbacks are Plaster (walls, floors) and Steel (machines).
            Material groundMat = CreateMaterial(GroundMaterialPath, Palette.Plaster);
            Material pickupMat = CreateMaterial(PickupMaterialPath, Palette.Mustard);
            PingMarker pingPrefab = CreatePingMarkerPrefab(pickupMat);
            NetworkObject playerPrefab = CreatePlayerPrefab(pingPrefab, builtCatalog);
            GameObject pickupPrefab = CreatePickupPrefab(pickupMat);
            NetworkObject foodPrefab = CreateFoodPrefab(pickupMat, builtCatalog);
            NetworkObject customerPrefab = CreateCustomerPrefab(builtCatalog);
            NetworkObject knifePrefab = CreateKnifePrefab(builtCatalog); // spec 019
            NetworkObject spatulaPrefab = CreateSpatulaPrefab(builtCatalog); // spec 030
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
            CreateEnvironment(groundMat, CreateMaterial(WallMaterialPath, Palette.Plaster));
            CreateStreet(catalog);
            Transform[] spawns = CreateSpawnPoints();
            CreatePickups(pickupPrefab);
            NetworkManager networkManager = CreateNetworkManager(playerPrefab, spawns, distance);
            CreateKitchen(networkManager, foodPrefab, knifePrefab, spatulaPrefab, vesselPrefabs, catalog);
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
            controller.radius = 0.3f; // spec 027a (orchestrator): real body width, so two players pass in the 1.5 m aisle (was 0.4)
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
            bubbleLabel.color = Palette.Mustard;
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
            // Spec 027b: the idle stands with one foot well ahead; the legs of the library's A pose pull the feet together.
            SetField(visual, "_stand", ArtCatalogBuilder.CharacterClip(StandLegsClip));

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

        /// <summary>
        /// Spec 014: street entry east of the kitchen and the queue in front of the counter. Spec 027a: the queue runs along
        /// the front counter's street side, head at the serving counter, slots towards the landing end (-x), so every slot
        /// has the counter between it and the aisle; queued guests turn to face the counter (+z). Entry, queue and exit
        /// share the line z = QueueZ, south of the counter and the side walls, so no guest path crosses the aisle.
        /// </summary>
        private static void CreateCustomers(NetworkManager networkManager, NetworkObject customerPrefab)
        {
            var parent = new GameObject("Customers");
            var entry = new GameObject("StreetEntry");
            entry.transform.SetParent(parent.transform, false);
            entry.transform.SetPositionAndRotation(new Vector3(StreetEntryX, 0f, QueueZ), Quaternion.Euler(0f, -90f, 0f)); // spec 024: y 0 (was 0.1), feet on the ground
            var head = new GameObject("QueueHead");
            head.transform.SetParent(parent.transform, false);
            head.transform.position = new Vector3(CounterX, 0f, QueueZ); // in front of the serving counter; spec 024: y 0

            var service = networkManager.gameObject.AddComponent<CustomerService>();
            SetField(service, "_networkManager", networkManager);
            SetField(service, "_economy", networkManager.GetComponent<EconomyService>());
            SetField(service, "_customerPrefab", customerPrefab);
            SetField(service, "_entry", entry.transform);
            SetField(service, "_queueHead", head.transform);
            SetField(service, "_queueDirection", Vector3.left); // spec 027a: along the counter, away from the street end
            SetField(service, "_queueFacing", Vector3.forward); // spec 027a: waiting guests face the cook across the counter
            SetField(service, "_rail", CreateTicketRail()); // spec 022
            SetField(networkManager.GetComponent<KitchenService>(), "_customers", service);
        }

        // Spec 022: the ticket rail board above the counter. Two rows of three cards. Spec 027a: above the front counter's
        // street half at the serving counter, turned to face the aisle (north), so the cook reads it; its bottom (2.5 - 0.575 m)
        // clears the guests' heads.
        private const int TicketCards = 6;
        private static readonly Vector3 RailPosition = new Vector3(CounterX, 2.5f, FrontCounterZ - 0.15f);
        private static readonly Quaternion RailRotation = Quaternion.Euler(0f, 180f, 0f);
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
            root.transform.SetPositionAndRotation(RailPosition, RailRotation);
            NetworkObject nob = root.AddComponent<NetworkObject>();
            SetField(nob, "_isGlobal", true);

            GameObject board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = "Board";
            UnityEngine.Object.DestroyImmediate(board.GetComponent<Collider>());
            board.transform.SetParent(root.transform, false);
            board.transform.localScale = new Vector3(RailSize.x, RailSize.y, 0.05f);
            board.GetComponent<MeshRenderer>().sharedMaterial = CreateMaterial(Root + "/Settings/TicketRail.mat", Palette.Charcoal);
            board.isStatic = true;

            TextMesh title = CreateLabel(root.transform, new Vector3(0f, RailSize.y * 0.5f + 0.04f, -0.03f), 60, 0.03f);
            title.name = "Title";
            title.text = "ORDERS";

            Material cardMat = CreateMaterial(Root + "/Settings/TicketCard.mat", Palette.Cream);
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
                text.color = Palette.Ink;
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
            // Spec 026: a plated dish lays out its parts like the plate, a bowl dish like the bowl.
            (_, float plateScale, float plateRadius) = VesselContents(VesselKind.Plate);
            (_, float bowlScale, float bowlRadius) = VesselContents(VesselKind.Bowl);
            SetField(visual, "_partScale", plateScale);
            SetField(visual, "_partRadius", plateRadius);
            SetField(visual, "_bowlPartScale", bowlScale);
            SetField(visual, "_bowlPartRadius", bowlRadius);
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

        /// <summary>
        /// Spec 021: how deep contents sit in each kind of cookware (fraction of its height), part scale and radius.
        /// Spec 026: radii for the spec 026 cookware sizes (the spec 021 radii scaled with the smaller cookware, so the parts
        /// stay inside; ScaleSceneTests checks it); the part scale is kept (the food models shrank about as much).
        /// </summary>
        private static (float fill, float partScale, float partRadius) VesselContents(VesselKind kind)
        {
            switch (kind)
            {
                case VesselKind.Board: return (BoardFill, 1f, 0.055f);
                case VesselKind.Pan: return (PanFill, 0.65f, 0.056f);
                case VesselKind.Pot: return (PotFill, 0.6f, 0.041f);
                case VesselKind.Plate: return (PlateFill, 0.5f, 0.064f);
                default: return (BowlFill, 0.45f, 0.041f);
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
            Vector2 body = Vector2.zero;
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            if (catalog != null && catalog.TryGet(ArtCatalogRules.VesselKey(kind), out GameObject wrapper))
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(wrapper, visual.transform);
                model = LocalBounds(root, instance);
                body = BaseCentre(instance);
            }
            else Debug.LogError("[Graybox] no model for " + kind + " in the art catalog");

            float height = Mathf.Max(model.max.y, MinVesselColliderHeight);
            var collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(model.size.x, height, model.size.z);
            collider.center = new Vector3(model.center.x, height * 0.5f, model.center.z);

            (float fill, float partScale, float partRadius) = VesselContents(kind);
            var anchor = new GameObject("Contents");
            anchor.transform.SetParent(root.transform, false);
            // Spec 026: over the middle of the vessel's base, not of its bounds (the pan's handle moves those off the pan).
            anchor.transform.localPosition = new Vector3(body.x, model.max.y * fill, body.y);
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

        /// <summary>
        /// Spec 026: the XZ centre of a model's base (its vertices in the lowest quarter of its height), in world space with
        /// the root at the origin. A pan's handle rises from the rim, so the base is the pan itself.
        /// </summary>
        private static Vector2 BaseCentre(GameObject model)
        {
            var points = new List<Vector3>();
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                foreach (Vector3 v in filter.sharedMesh.vertices) points.Add(filter.transform.TransformPoint(v));
            }
            if (points.Count == 0) return Vector2.zero;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (Vector3 p in points)
            {
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }
            float limit = minY + (maxY - minY) * 0.25f;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (Vector3 p in points)
            {
                if (p.y > limit) continue;
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.z);
                maxZ = Mathf.Max(maxZ, p.z);
            }
            return new Vector2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);
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

            // Spec 023, spec 026: a row of equal Furniture Kit cabinet modules (about 0.6 m each, see ArtSizeTable.WorktopModules)
            // instead of one stretched cabinet; the cube, the collider and the place surface stay one piece.
            if (catalog != null && catalog.TryGet(ArtKey.Worktop, out GameObject wrapper))
            {
                UnityEngine.Object.DestroyImmediate(body.GetComponent<MeshRenderer>());
                (int count, float w) = ArtSizeTable.WorktopModules(width);
                float wrapperHeight = ArtCatalogBuilder.HeightOf(wrapper);
                float x = -width * 0.5f;
                for (int i = 0; i < count; i++)
                {
                    var module = (GameObject)PrefabUtility.InstantiatePrefab(wrapper, root.transform);
                    module.name = WorktopModuleName + " " + (i + 1);
                    module.transform.localPosition = new Vector3(x + w * 0.5f, 0f, 0f);
                    module.transform.localRotation = ArtFrontRotation; // spec 027a: the cabinet doors face the aisle
                    module.transform.localScale = new Vector3(w / ArtSizeTable.WorktopModuleWidth, height / wrapperHeight, 1f);
                    x += w;
                }
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
            float colliderTop = Mathf.Max(top, StationHeightOf(station.Kind) * 0.5f) + 0.005f;
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
            material.SetColor("_BaseColor", Palette.WithAlpha(Palette.Sage, 0.45f)); // spec 026: the "use" colour
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
            // Spec 026: the knob row follows the stove's height (station space: the root sits at half the height).
            float rootY = station.transform.localPosition.y;
            float stoveTop = rootY + StationTop(station);
            float buttonY = stoveTop * KnobRowFraction - rootY;
            float labelY = stoveTop * KnobLabelFraction - rootY;
            for (int b = 0; b < burners.Length; b++)
            {
                float x = BurnerX[b];
                switches.Add(CreateButton(station, "Switch " + b, new Vector3(x - 0.09f, buttonY, -0.64f),
                    StationRules.StoveOption(b, StationRules.StoveSwitch), material));
                CreateButtonLabel(station, "SwitchLabel " + b, new Vector3(x - 0.09f, labelY, -0.68f), "on/off");
                modes.Add(CreateButton(station, "ModeButton " + b, new Vector3(x + 0.09f, buttonY, -0.64f),
                    StationRules.StoveOption(b, StationRules.PanMode), material));
                labels.Add(CreateButtonLabel(station, "ModeLabel " + b, new Vector3(x + 0.09f, labelY, -0.68f), "fry"));
                rings.Add(CreateBurnerRing(burners[b], material));
            }
            SetField(visual, "_switches", switches);
            SetField(visual, "_modeButtons", modes);
            SetField(visual, "_modeLabels", labels);
            SetField(visual, "_rings", rings);
        }

        /// <summary>Spec 029: the fryer's name (its sign and the server log) and its baskets' object names.</summary>
        public const string FryerName = "Fryer";
        public const string FryerBasketName = "Fryer basket";

        /// <summary>
        /// Spec 029: the fryer's front and baskets (<see cref="FryerArt"/>). The body's collider stops under the wells, so the aim
        /// finds a lowered basket. On the front face: the power knob in the middle and a lower/raise button under each basket
        /// (StationButtons, option FryerRules.PowerOption / BasketOption), labels and a pilot light, coloured by a
        /// FryerButtonVisual. On the back deck a stack of empty fries boxes (Kenney fries-empty). Each basket is its own scene
        /// NetworkObject next to the fryer (never moves; no Pickup): a Vessel of kind Basket with its FryerBasket, the "Rig" that
        /// BasketView swings (wire basket, collider, contents drawn loose), steam and smoke, the ring and the oil bubbles.
        /// </summary>
        private static void BuildFryer(ArtCatalog catalog, Station fryer, Transform parent)
        {
            float rootY = fryer.transform.localPosition.y; // the station root sits at half its height
            BoxCollider body = fryer.transform.Find("Body").GetComponent<BoxCollider>();
            float height = FryerArt.Height;
            body.size = new Vector3(1f, FryerArt.BodyColliderTop / height, 1f);
            body.center = new Vector3(0f, (FryerArt.BodyColliderTop * 0.5f - height * 0.5f) / height, 0f);

            Material material = CreateMaterial(StoveButtonMaterialPath, Color.white);
            var visual = fryer.gameObject.AddComponent<FryerButtonVisual>();
            SetField(visual, "_contents", fryer.GetComponent<StationContents>());
            float frontZ = -FryerArt.Depth * 0.5f - 0.04f;
            float buttonY = FryerArt.ButtonHeight - rootY, labelY = FryerArt.LabelHeight - rootY;
            SetField(visual, "_power", CreateButton(fryer, "Power", new Vector3(FryerArt.PowerX, buttonY, frontZ), FryerRules.PowerOption, material));
            // The labels over the controls, small: the three sit 19 cm apart (the station sign is under the knob).
            CreateButtonLabel(fryer, "PowerLabel", new Vector3(FryerArt.PowerX, labelY, frontZ - 0.04f), "on/off").characterSize = 0.014f;
            var baskets = new List<UnityEngine.Object>();
            for (int b = 0; b < FryerRules.BasketsPerFryer; b++)
            {
                float x = FryerArt.BasketX[b];
                baskets.Add(CreateButton(fryer, "Basket button " + b, new Vector3(x, buttonY, frontZ), FryerRules.BasketOption(b), material));
                CreateButtonLabel(fryer, "BasketLabel " + b, new Vector3(x, labelY, frontZ - 0.04f), "basket").characterSize = 0.014f;
            }
            SetField(visual, "_baskets", baskets);
            GameObject pilot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pilot.name = "Pilot light";
            UnityEngine.Object.DestroyImmediate(pilot.GetComponent<Collider>());
            pilot.transform.SetParent(fryer.transform, false);
            pilot.transform.localPosition = new Vector3(FryerArt.PowerX + 0.095f, buttonY + 0.045f, frontZ + 0.02f);
            pilot.transform.localScale = Vector3.one * 0.035f;
            pilot.GetComponent<MeshRenderer>().sharedMaterial = material;
            SetField(visual, "_pilot", pilot.GetComponent<MeshRenderer>());

            if (catalog != null && catalog.TryGet(ArtKey.PropFriesBox, out GameObject box))
            {
                for (int i = 0; i < FryerArt.BoxCount; i++)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(box, fryer.transform);
                    instance.name = "Fries box " + (i + 1);
                    instance.transform.localPosition = new Vector3((i - (FryerArt.BoxCount - 1) * 0.5f) * 0.17f, height - rootY, FryerArt.BoxesZ);
                    instance.transform.localRotation = ArtFrontRotation * Quaternion.Euler(0f, (i - 1) * 9f, 0f);
                    SetStaticRecursive(instance);
                }
            }

            Material steam = CreateSteamMaterial();
            for (int b = 0; b < FryerRules.BasketsPerFryer; b++) CreateFryerBasket(catalog, fryer, b, parent, steam);
        }

        private static void CreateFryerBasket(ArtCatalog catalog, Station fryer, int basket, Transform parent, Material steam)
        {
            float rootY = fryer.transform.localPosition.y;
            var root = new GameObject(FryerBasketName + " " + basket);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(
                fryer.transform.TransformPoint(new Vector3(FryerArt.BasketX[basket], FryerArt.BasketBottom - rootY, FryerArt.BasketZ)),
                fryer.transform.rotation);
            var rig = new GameObject(Vessel.RigName);
            rig.transform.SetParent(root.transform, false);
            if (catalog != null && catalog.TryGet(ArtKey.ToolFryerBasket, out GameObject wrapper))
            {
                var art = (GameObject)PrefabUtility.InstantiatePrefab(wrapper, rig.transform);
                art.transform.localRotation = ArtFrontRotation; // the handle towards the aisle
            }
            else Debug.LogError("[Graybox] no fryer basket model in the art catalog");
            // The collider moves with the rig (the aim follows the drawn basket); the handle runs towards -z.
            float bh = FryerArt.BasketHeight + FryerArt.HandleRise + 0.03f, bl = FryerArt.BasketLength + FryerArt.HandleLength;
            var collider = rig.AddComponent<BoxCollider>();
            collider.size = new Vector3(FryerArt.BasketWidth + 0.02f, bh, bl);
            collider.center = new Vector3(0f, bh * 0.5f, -FryerArt.HandleLength * 0.5f);

            var anchor = new GameObject(Vessel.ContentsName);
            anchor.transform.SetParent(rig.transform, false);
            anchor.transform.localPosition = new Vector3(0f, FryerArt.ContentsHeight, 0f);
            var food = anchor.AddComponent<FoodVisual>();
            SetField(food, "_catalog", catalog);
            SetField(food, "_partScale", 0.75f);
            SetField(food, "_partRadius", 0.05f);
            SetField(food, "_loose", true);

            TextMesh label = CreateLabel(root.transform, new Vector3(0f, FryerArt.Height - FryerArt.BasketBottom + 0.42f, 0f), 40, 0.03f);
            label.text = string.Empty;

            NetworkObject nob = root.AddComponent<NetworkObject>();
            SetField(nob, "_preventDespawnOnDisconnect", true);
            var vessel = root.AddComponent<Vessel>();
            SetField(vessel, "_kind", (int)VesselKind.Basket);
            SetField(vessel, "_label", label);
            SetField(vessel, "_visual", food);
            var marker = root.AddComponent<FryerBasket>();
            SetField(marker, "_fryer", fryer);
            SetField(marker, "_basket", basket);
            var view = root.AddComponent<BasketView>();
            SetField(view, "_rig", rig.transform);
            SetField(view, "_raisedOffset", FryerArt.RaisedOffset);
            SetField(view, "_raisedTilt", FryerArt.RaisedTilt);
            SetField(view, "_particleMaterial", steam);
            float oil = FryerArt.OilHeight - FryerArt.BasketBottom;
            var steamView = root.AddComponent<VesselSteam>();
            SetField(steamView, "_material", steam);
            SetField(steamView, "_steamHeight", oil + 0.02f);
            var bubbles = root.AddComponent<FryerBubbles>();
            SetField(bubbles, "_material", steam);
            SetField(bubbles, "_oilHeight", oil);
            SetField(bubbles, "_area", new Vector2(FryerArt.WellWidth - 0.04f, FryerArt.WellBack - FryerArt.WellFront - 0.06f));
        }

        /// <summary>Spec 030: the griddle's name (its sign and the server log) and its spots' object names.</summary>
        public const string GriddleName = "Griddle";
        public const string GriddleSpotName = "Griddle spot";

        /// <summary>
        /// Spec 030: the griddle's front and spots (<see cref="GriddleArtRules"/>). On the front face the power knob right of the sign
        /// (a StationButton, option GriddleRules.PowerOption), its label and a pilot light, coloured by a FryerButtonVisual (the same
        /// on bit). Each of the six spots is its own scene NetworkObject on the plate (never moves; no Pickup): a Vessel of kind
        /// Griddle with its GriddleSpot, a low aim collider, the "Contents" GriddleSpotView flips, steam and the two-half ring.
        /// </summary>
        private static void BuildGriddle(ArtCatalog catalog, Station griddle, Transform parent)
        {
            float rootY = griddle.transform.localPosition.y;
            Material material = CreateMaterial(StoveButtonMaterialPath, Color.white);
            var visual = griddle.gameObject.AddComponent<FryerButtonVisual>();
            SetField(visual, "_contents", griddle.GetComponent<StationContents>());
            float frontZ = -GriddleArtRules.Depth * 0.5f - 0.04f;
            float buttonY = GriddleArtRules.ButtonHeight - rootY, labelY = GriddleArtRules.LabelHeight - rootY;
            SetField(visual, "_power", CreateButton(griddle, "Power", new Vector3(GriddleArtRules.KnobX, buttonY, frontZ), GriddleRules.PowerOption, material));
            CreateButtonLabel(griddle, "PowerLabel", new Vector3(GriddleArtRules.KnobX, labelY, frontZ - 0.04f), "on/off").characterSize = 0.014f;
            GameObject pilot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pilot.name = "Pilot light";
            UnityEngine.Object.DestroyImmediate(pilot.GetComponent<Collider>());
            pilot.transform.SetParent(griddle.transform, false);
            pilot.transform.localPosition = new Vector3(GriddleArtRules.KnobX - 0.11f, buttonY + 0.045f, frontZ + 0.02f);
            pilot.transform.localScale = Vector3.one * 0.035f;
            pilot.GetComponent<MeshRenderer>().sharedMaterial = material;
            SetField(visual, "_pilot", pilot.GetComponent<MeshRenderer>());

            Material steam = CreateSteamMaterial();
            Material marks = CreateMaterial(Root + "/Settings/GrillMarks.mat", Palette.Charcoal);
            for (int s = 0; s < GriddleRules.SpotsPerGriddle; s++) CreateGriddleSpot(catalog, griddle, s, parent, steam, marks);
        }

        private static void CreateGriddleSpot(ArtCatalog catalog, Station griddle, int spot, Transform parent, Material steam, Material marks)
        {
            float rootY = griddle.transform.localPosition.y;
            Vector2 p = GriddleArtRules.SpotPosition(spot);
            var root = new GameObject(GriddleSpotName + " " + spot);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(griddle.transform.TransformPoint(new Vector3(p.x, GriddleArtRules.PlateTop - rootY, p.y)),
                griddle.transform.rotation);
            var collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(GriddleArtRules.SpotSize, GriddleArtRules.SpotColliderHeight, GriddleArtRules.SpotSize);
            collider.center = new Vector3(0f, GriddleArtRules.SpotColliderHeight * 0.5f, 0f);

            var anchor = new GameObject(Vessel.ContentsName);
            anchor.transform.SetParent(root.transform, false);
            anchor.transform.localPosition = new Vector3(0f, 0.002f, 0f);
            var food = anchor.AddComponent<FoodVisual>();
            SetField(food, "_catalog", catalog);
            SetField(food, "_partScale", 0.7f);
            SetField(food, "_partRadius", 0.05f);

            TextMesh label = CreateLabel(root.transform, new Vector3(0f, 0.36f, 0f), 40, 0.03f);
            label.text = string.Empty;

            NetworkObject nob = root.AddComponent<NetworkObject>();
            SetField(nob, "_preventDespawnOnDisconnect", true);
            var vessel = root.AddComponent<Vessel>();
            SetField(vessel, "_kind", (int)VesselKind.Griddle);
            SetField(vessel, "_label", label);
            SetField(vessel, "_visual", food);
            var marker = root.AddComponent<GriddleSpot>();
            SetField(marker, "_griddle", griddle);
            SetField(marker, "_spot", spot);
            SetField(root.AddComponent<GriddleSpotView>(), "_markMaterial", marks);
            var steamView = root.AddComponent<VesselSteam>();
            SetField(steamView, "_material", steam);
            SetField(steamView, "_steamHeight", 0.06f);
        }

        /// <summary>Spec 030: the steel deck behind the griddle's plate, a place surface at the plate's height.</summary>
        private static PlaceSurface CreateGriddleDeck(Station griddle, int index)
        {
            float rootY = griddle.transform.localPosition.y;
            float top = GriddleArtRules.PlateTop - rootY;
            var size = new Vector2(GriddleArtRules.Width, GriddleArtRules.DeckDepth);
            var go = new GameObject("Griddle deck");
            go.transform.SetParent(griddle.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, GriddleArtRules.DeckCenterZ);
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(size.x, 0.02f, size.y);
            box.center = new Vector3(0f, top + 0.005f - 0.01f, 0f);
            go.isStatic = true;
            var surface = go.AddComponent<PlaceSurface>();
            SetField(surface, "_index", index);
            SetField(surface, "_size", size);
            SetField(surface, "_topY", top);
            return surface;
        }

        /// <summary>
        /// Spec 030: the griddle spatula, a normal pickup like the knife with a Spatula marker and the Food Kit cooking-spatula
        /// (fitted to ArtSizeTable, long side along Z); the collider wraps the model, its visual on the collider's bottom.
        /// </summary>
        private static NetworkObject CreateSpatulaPrefab(ArtCatalog catalog)
        {
            var root = new GameObject("Spatula");
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            Bounds model = new Bounds(new Vector3(0f, 0.02f, 0f), new Vector3(0.1f, 0.04f, 0.345f));
            if (catalog != null && catalog.TryGet(ArtKey.ToolSpatula, out GameObject wrapper))
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(wrapper, visual.transform);
                model = LocalBounds(root, instance);
            }
            else Debug.LogError("[Graybox] no ToolSpatula model in the art catalog");
            float height = Mathf.Max(model.size.y, MinVesselColliderHeight);
            var collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(Mathf.Max(model.size.x, 0.08f), height, model.size.z);
            collider.center = new Vector3(model.center.x, 0f, model.center.z);
            visual.transform.localPosition = new Vector3(0f, -height * 0.5f - model.min.y, 0f);
            Debug.Log("[Graybox] spatula model " + model.size.x.ToString("0.000") + " x " + model.size.y.ToString("0.000") + " x "
                + model.size.z.ToString("0.000") + " m (target " + ArtSizeTable.TargetSize(ArtKey.ToolSpatula).ToString("0.000") + " m)");

            NetworkObject nob = root.AddComponent<NetworkObject>();
            SetField(nob, "_preventDespawnOnDisconnect", true); // like the knife
            AddServerAuthoritativeTransform(root);
            root.AddComponent<Pickup>();
            root.AddComponent<Spatula>();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, SpatulaPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return saved.GetComponent<NetworkObject>();
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
            return art != null ? ArtCatalogBuilder.BaseTopOf(art.gameObject) + art.localPosition.y : StationHeightOf(station.Kind) * 0.5f;
        }

        /// <summary>
        /// Spec 007/021: the kitchen (spec 027a: the shop floor plan, see the constants): the stock (spec 028b: the dry shelf and the
        /// fridge), worktops, two stoves (two burners each), the counter and the plate shelf; the cookware lying on top; the serve
        /// feed and the kitchen service.
        /// </summary>
        private static void CreateKitchen(NetworkManager networkManager, NetworkObject foodPrefab, NetworkObject knifePrefab, NetworkObject spatulaPrefab,
            List<NetworkObject> vesselPrefabs, ArtCatalog catalog)
        {
            var parent = new GameObject("Kitchen");
            var stations = new List<Station>();
            // Spec 028b: every ingredient in one slot of the dry shelf or the fridge, both against the back wall next to prep.
            Station dryShelf = CreateStation(catalog, parent.transform, stations.Count, StationKind.Shelf, StockShelfName, StockShelfX,
                StockZ(StationKind.Shelf), 0f, CreateMaterial(Root + "/Settings/Station_Shelf.mat", Palette.WoodLight), 0);
            CreateStockSlots(catalog, dryShelf);
            stations.Add(dryShelf);
            Station fridge = CreateStation(catalog, parent.transform, stations.Count, StationKind.Fridge, FridgeName, FridgeX,
                StockZ(StationKind.Fridge), 0f, CreateMaterial(Root + "/Settings/Station_Fridge.mat", Palette.Cream), 0);
            CreateStockSlots(catalog, fridge);
            stations.Add(fridge);
            Material stoveMat = CreateMaterial(Root + "/Settings/Station_Pan.mat", Palette.Steel);
            Station stoveA = CreateStation(catalog, parent.transform, stations.Count, StationKind.Stove, "Stove 1", StoveAX, KitchenZ, 0f, stoveMat, 0);
            stations.Add(stoveA);
            Station stoveB = CreateStation(catalog, parent.transform, stations.Count, StationKind.Stove, "Stove 2", StoveBX, KitchenZ, 0f, stoveMat, 0);
            stations.Add(stoveB);
            // Spec 027a: the serving counter stands in the front counter, turned so its cook side (and label) faces the aisle.
            stations.Add(CreateStation(catalog, parent.transform, stations.Count, StationKind.Counter, "Counter", CounterX, FrontCounterZ, 180f,
                CreateMaterial(Root + "/Settings/Station_Counter.mat", Palette.WoodLight), 0));
            Station shelf = CreateStation(catalog, parent.transform, stations.Count, StationKind.PlateShelf, "Plates", PlateShelfX, KitchenZ, 0f,
                CreateMaterial(Root + "/Settings/Station_Plating.mat", Palette.WoodLight), 0);
            CreatePlateStack(catalog, shelf);
            stations.Add(shelf);
            // Spec 029: the fryer, after prep (appended last, so the earlier station indices stay).
            Station fryer = CreateStation(catalog, parent.transform, stations.Count, StationKind.Fryer, FryerName, FryerX, KitchenZ, 0f, stoveMat, 0,
                FryerArt.Width);
            stations.Add(fryer);
            // Spec 030: the griddle, between prep and the fryer (appended last, so the earlier station indices stay).
            Station griddle = CreateStation(catalog, parent.transform, stations.Count, StationKind.Griddle, GriddleName, GriddleX, KitchenZ, 0f,
                stoveMat, 0, GriddleArtRules.Width);
            stations.Add(griddle);

            // Worktops at the stove top's height: the landing by the staff gap, the prep run after the crates, the pass at the
            // right end (spec 029b: it stood between the stoves; created in the same order, so the surface indices stay),
            // between the second stove and the plate shelf (plating), after the shelf (the bowl).
            float height = stoveA.transform.position.y + StationTop(stoveA);
            const float half = StationHalfGap;
            Material worktopMat = CreateMaterial(WorktopMaterialPath, Palette.WoodLight);
            var surfaces = new List<PlaceSurface>();
            surfaces.Add(CreateWorktop(parent.transform, KitchenMinX, KitchenMinX + LandingWidth, height, surfaces.Count, worktopMat, catalog));
            PlaceSurface prep = CreateWorktop(parent.transform, PrepStartX, PrepStartX + PrepWidth, height, surfaces.Count, worktopMat, catalog);
            surfaces.Add(prep);
            PlaceSurface[] burnersA = CreateStoveSurfaces(stoveA, surfaces);
            surfaces.Add(CreateWorktop(parent.transform, PlateShelfX + half + BetweenWidth, KitchenMaxX, height, surfaces.Count, worktopMat, catalog));
            PlaceSurface[] burnersB = CreateStoveSurfaces(stoveB, surfaces);
            PlaceSurface plating = CreateWorktop(parent.transform, StoveBX + half, PlateShelfX - half, height, surfaces.Count, worktopMat, catalog);
            surfaces.Add(plating);
            PlaceSurface last = CreateWorktop(parent.transform, PlateShelfX + half, PlateShelfX + half + BetweenWidth, height, surfaces.Count,
                worktopMat, catalog);
            surfaces.Add(last);
            // Spec 030: the griddle's deck behind its plate (appended, so the earlier surface indices stay): the spatula's home.
            PlaceSurface griddleDeck = CreateGriddleDeck(griddle, surfaces.Count);
            surfaces.Add(griddleDeck);
            CreateFrontCounter(parent.transform, catalog);
            CreateShopWalls(parent.transform);
            SetField(networkManager.GetComponent<PickupService>(), "_surfaces", surfaces);
            foreach (Station stove in new[] { stoveA, stoveB })
            {
                stove.gameObject.AddComponent<NetworkObject>();
                stove.gameObject.AddComponent<StationContents>();
            }
            AddStoveButtons(stoveA, burnersA);
            AddStoveButtons(stoveB, burnersB);
            fryer.gameObject.AddComponent<NetworkObject>();
            fryer.gameObject.AddComponent<StationContents>();
            BuildFryer(catalog, fryer, parent.transform);
            griddle.gameObject.AddComponent<NetworkObject>();
            griddle.gameObject.AddComponent<StationContents>();
            BuildGriddle(catalog, griddle, parent.transform);

            // Spec 021: the cookware lying on top at the start (each one's home spot, where a lost one comes back).
            NetworkObject Prefab(VesselKind kind) => vesselPrefabs.Find(p => p.GetComponent<Vessel>().Kind == kind);
            // Spec 027a: the board and the knife on the prep run, the plates next to the plate shelf, the bowl after it.
            // Spec 028b: the board at the prep run's stock end (about 1 m from the fridge, 2.2 m from the shelf).
            // Spec 030: the prep run is one board wide: the board in its middle at the front, the knife behind it on the right.
            PlaceOnSpot(Prefab(VesselKind.Board), prep, SpotNear(prep, 0f, -0.125f), "Cutting board");
            PlaceOnSpot(knifePrefab, prep, SpotNear(prep, 0.25f, 0.375f), "Knife");
            // Spec 030: the spatula on the griddle's deck, right behind the plate (its home spot).
            PlaceOnSpot(spatulaPrefab, griddleDeck, SpotNear(griddleDeck, 0.375f, 0.125f), "Spatula");
            PlaceOnSpot(Prefab(VesselKind.Pan), burnersA[0], 0, "Pan");
            PlaceOnSpot(Prefab(VesselKind.Pot), burnersB[0], 0, "Pot");
            PlaceOnSpot(Prefab(VesselKind.Plate), plating, SpotNear(plating, -0.3f, -0.125f), "Plate 1");
            PlaceOnSpot(Prefab(VesselKind.Plate), plating, SpotNear(plating, 0.3f, -0.125f), "Plate 2");
            PlaceOnSpot(Prefab(VesselKind.Bowl), last, SpotNear(last, -0.3f, -0.125f), "Bowl");

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
            SetField(kitchen, "_spatulaPrefab", spatulaPrefab); // spec 030: replaces a lost spatula
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

        /// <summary>Spec 027a: name prefix of the plain front counter runs either side of the serving counter.</summary>
        public const string FrontCounterName = "Front counter";

        /// <summary>
        /// Spec 027a: the plain front counter either side of the serving counter, serving-counter high (1.05 m), CounterDepth (0.7 m) deep,
        /// squashed in depth like the serving counter. Cabinet modules like the worktops, turned so their doors face the aisle (the cook side); one box
        /// collider per run, no place surface (the place surfaces stay at worktop height).
        /// </summary>
        private static void CreateFrontCounter(Transform parent, ArtCatalog catalog)
        {
            Material material = CreateMaterial(Root + "/Settings/Station_Counter.mat", Palette.WoodLight);
            float height = ArtSizeTable.ServingCounterHeight;
            var runs = new[] { (KitchenMinX, CounterX - StationHalfGap), (CounterX + StationHalfGap, KitchenMaxX) };
            for (int r = 0; r < runs.Length; r++)
            {
                (float minX, float maxX) = runs[r];
                float width = maxX - minX;
                var root = new GameObject(FrontCounterName + " " + (r + 1));
                root.transform.SetParent(parent, false);
                root.transform.SetPositionAndRotation(new Vector3((minX + maxX) * 0.5f, 0f, FrontCounterZ), Quaternion.Euler(0f, 180f, 0f));
                var box = root.AddComponent<BoxCollider>();
                box.size = new Vector3(width, height, CounterDepth);
                box.center = new Vector3(0f, height * 0.5f, 0f);
                if (catalog != null && catalog.TryGet(ArtKey.Worktop, out GameObject wrapper))
                {
                    (int count, float w) = ArtSizeTable.WorktopModules(width);
                    float wrapperHeight = ArtCatalogBuilder.HeightOf(wrapper);
                    float x = -width * 0.5f;
                    for (int i = 0; i < count; i++)
                    {
                        var module = (GameObject)PrefabUtility.InstantiatePrefab(wrapper, root.transform);
                        module.name = "Counter module " + (i + 1);
                        module.transform.localPosition = new Vector3(x + w * 0.5f, 0f, 0f);
                        module.transform.localRotation = ArtFrontRotation; // spec 027a: the doors face the aisle (the cook side)
                        module.transform.localScale = new Vector3(w / ArtSizeTable.WorktopModuleWidth, height / wrapperHeight, CounterDepth / StationWidth);
                        x += w;
                    }
                }
                else
                {
                    GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    body.name = "Body";
                    UnityEngine.Object.DestroyImmediate(body.GetComponent<Collider>());
                    body.transform.SetParent(root.transform, false);
                    body.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
                    body.transform.localScale = new Vector3(width, height, CounterDepth);
                    body.GetComponent<MeshRenderer>().sharedMaterial = material;
                }
                SetStaticRecursive(root);
            }
        }

        /// <summary>Spec 027a: names of the shop's walls (graybox Plaster boxes with colliders).</summary>
        public const string BackWallName = "Back wall";
        public const string SideWallName = "Side wall";

        /// <summary>
        /// Spec 027a: a plain back wall behind the back line (2.4 m) and low side walls (1.2 m) from the back wall to the front
        /// counter's street side at both ends. The left one leaves the staff gap where the aisle meets it (the aisle's
        /// width), so the cook can leave west, away from the guests; the right one is closed.
        /// </summary>
        private static void CreateShopWalls(Transform parent)
        {
            Material material = CreateMaterial(WallMaterialPath, Palette.Plaster);
            var walls = new GameObject("Walls");
            walls.transform.SetParent(parent, false);
            float backFront = KitchenZ + StationWidth * 0.5f + WallGap;   // the back wall's inner face
            float backLineFront = KitchenZ - StationWidth * 0.5f;          // where the aisle starts
            float counterBack = FrontCounterZ + CounterDepth * 0.5f;       // where the aisle ends
            float counterFront = FrontCounterZ - CounterDepth * 0.5f;      // the street side of the front counter
            float leftX = KitchenMinX - WallGap - WallThickness;
            float rightX = KitchenMaxX + WallGap;
            CreateWall(walls.transform, BackWallName, leftX, rightX + WallThickness, backFront, backFront + WallThickness, BackWallHeight, material);
            CreateWall(walls.transform, SideWallName + " left (back)", leftX, leftX + WallThickness, backLineFront, backFront, SideWallHeight, material);
            CreateWall(walls.transform, SideWallName + " left (front)", leftX, leftX + WallThickness, counterFront, counterBack, SideWallHeight, material);
            CreateWall(walls.transform, SideWallName + " right", rightX, rightX + WallThickness, counterFront, backFront, SideWallHeight, material);
        }

        private static void CreateWall(Transform parent, string name, float minX, float maxX, float minZ, float maxZ, float height, Material material)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube); // keeps its BoxCollider
            wall.name = name;
            wall.transform.SetParent(parent, false);
            wall.transform.localPosition = new Vector3((minX + maxX) * 0.5f, height * 0.5f, (minZ + maxZ) * 0.5f);
            wall.transform.localScale = new Vector3(maxX - minX, height, maxZ - minZ);
            wall.GetComponent<MeshRenderer>().sharedMaterial = material;
            wall.isStatic = true;
        }

        /// <summary>Spec 028b: names of the stock stations (their signs and the server log).</summary>
        public const string FridgeName = "Fridge";
        public const string StockShelfName = "Dry shelf";
        /// <summary>Spec 028b: name prefix of a stock slot's object ("Slot 0 lettuce") and of its price tag.</summary>
        public const string SlotName = "Slot";
        public const string SlotTagName = "SlotTag";

        /// <summary>Spec 028b: the centre z of a stock station, its back on the back line's back (so its front is set back).</summary>
        private static float StockZ(StationKind kind) => KitchenZ + StationWidth * 0.5f - StockArt.DepthOf(kind) * 0.5f;

        /// <summary>
        /// Spec 028b: the slots of a fridge or dry shelf (StockLayout, StockArt): one StockSlot per ingredient, its transform at
        /// the middle of the items standing in it (the anchor the aim and the server's reach use), the items' models as its
        /// children (not static, no colliders: the client copies one for the take's arc), and a small price tag on the board edge
        /// under it. Stand-ins (see <see cref="FoodArtRules.WholeIsTinted"/>) get a flat material in the ingredient colour.
        /// </summary>
        private static void CreateStockSlots(ArtCatalog catalog, Station station)
        {
            StationKind kind = station.Kind;
            IReadOnlyList<string> ids = StockLayout.SlotsOf(kind);
            float height = StationHeightOf(kind), depth = StockArt.DepthOf(kind);
            var slots = new List<StockSlot>();
            for (int s = 0; s < ids.Count; s++)
            {
                int ingredient = StockLayout.IngredientIndexOf(ids[s]);
                if (ingredient < 0) throw new InvalidOperationException("stock slot " + s + " of the " + kind + ": unknown ingredient " + ids[s]);
                Vector3 stand = StockArt.StandPoint(kind, s);
                // Station local: y from the root (half way up), the front face at -depth / 2, the items set back from it.
                var standLocal = new Vector3(stand.x, stand.y - height * 0.5f, -depth * 0.5f + stand.z);
                GameObject prefab = null;
                Material tint = null;
                var look = new FoodLook((byte)ingredient, FoodLook.ShapeWhole, 0, 0, 0, 0);
                if (catalog != null && FoodArtRules.TryGetPiece(look, out FoodPiece piece) && catalog.TryGet(piece.Key, out prefab) && piece.Tinted)
                    tint = CreateMaterial(Root + "/Settings/Sample_" + ids[s] + ".mat", FoodArtRules.IngredientColor((byte)ingredient));
                float itemHeight = prefab != null ? ArtCatalogBuilder.HeightOf(prefab) : 0.1f;
                float itemSize = prefab != null && FoodArtRules.TryGetPiece(look, out FoodPiece sized) ? ArtSizeTable.TargetSize(sized.Key) : 0.1f;

                var slotObject = new GameObject(SlotName + " " + s + " " + ids[s]);
                slotObject.transform.SetParent(station.transform, false);
                slotObject.transform.localPosition = standLocal + Vector3.up * (itemHeight * 0.5f);
                var slot = slotObject.AddComponent<StockSlot>();
                SetField(slot, "_station", station);
                SetField(slot, "_option", s);
                SetField(slot, "_ingredientIndex", ingredient);
                slots.Add(slot);

                if (prefab != null)
                {
                    int count = StockArt.SamplesPerSlot(kind, itemSize);
                    float spacing = Mathf.Min(itemSize + 0.02f, (StockArt.Pitch(kind) - 0.03f) / count);
                    for (int n = 0; n < count; n++)
                    {
                        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, slotObject.transform);
                        instance.transform.localPosition = new Vector3((n - (count - 1) * 0.5f) * spacing, -itemHeight * 0.5f, n % 2 == 0 ? 0f : 0.03f);
                        instance.transform.localRotation = Quaternion.Euler(0f, 25f + 37f * n, 0f);
                        if (tint != null) foreach (Renderer r in instance.GetComponentsInChildren<Renderer>()) r.sharedMaterial = tint;
                    }
                }

                // A price tag on the board's front edge under the slot, read from the aisle like the station's sign.
                string name = DemoIngredients.All[ingredient].DisplayName;
                string price = GrocerPrices.TryGetGrocerPrice(ids[s], out long dollars) ? " " + EconomyRules.Money(dollars) : string.Empty;
                TextMesh tag = CreateLabel(station.transform, new Vector3(stand.x, stand.y - height * 0.5f - SlotTagDrop, -depth * 0.5f - StationSign.Standoff),
                    60, 0.05f);
                tag.name = SlotTagName + " " + s;
                tag.anchor = TextAnchor.UpperCenter;
                tag.transform.localScale = Vector3.one * (StationSign.BaseScale * SlotTagScale);
                tag.text = name + price;
                tag.gameObject.AddComponent<StationSign>();
            }
            SetField(station, "_slots", slots);
        }

        private const float SlotTagDrop = 0.01f;  // m under the board's top edge
        private const float SlotTagScale = 0.6f;  // of a station sign

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

        /// <summary>Spec 027a: <paramref name="yaw"/> turns the station; its front (controls, label) faces local -z.</summary>
        private static Station CreateStation(ArtCatalog catalog, Transform parent, int index, StationKind kind, string displayName,
            float x, float z, float yaw, Material material, int ingredientIndex, float width = StationWidth)
        {
            float height = StationHeightOf(kind); // spec 026: 0.90 m, the counter 1.05 m; spec 028b: the stock 1.80 m
            float depth = StationDepthOf(kind); // spec 027a, 028b
            bool stock = StockLayout.IsStock(kind);
            var root = new GameObject(displayName);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(new Vector3(x, height * 0.5f, z), Quaternion.Euler(0f, yaw, 0f));

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(width, height, depth); // spec 028: the street-food crates are narrow
            body.GetComponent<MeshRenderer>().sharedMaterial = material;
            body.isStatic = true;

            // Spec 015 swap layer: the cube body and its collider stay; the catalog visual is a sibling.
            float frontZ = -depth * 0.5f; // spec 027d: the front-most surface (the stove model stands out of its cube)
            if (catalog != null && catalog.TryGet(ArtCatalogRules.StationKey(kind), out GameObject wrapper))
            {
                UnityEngine.Object.DestroyImmediate(body.GetComponent<MeshRenderer>());
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(wrapper, root.transform);
                visual.transform.localPosition = new Vector3(0f, -height * 0.5f, 0f);
                visual.transform.localRotation = ArtFrontRotation; // spec 027a: the model's front (knobs, oven door) on the controls' side
                // Spec 027a: the serving counter is squashed to the front counter's depth, like the worktop modules. Spec 028b: the
                // stock models are built at their own size (StockArt).
                // Spec 029: the graybox fryer is built at its own size too.
                if (!stock && kind != StationKind.Fryer && kind != StationKind.Griddle) // spec 030: the graybox griddle too
                    visual.transform.localScale = Vector3.Scale(visual.transform.localScale, new Vector3(width / StationWidth, 1f, depth / StationWidth));
                SetStaticRecursive(visual);
                // Spec 028b: the fridge's open doors stand out of the body; the sign stays on the body's front.
                if (!stock) foreach (Renderer r in visual.GetComponentsInChildren<Renderer>())
                {
                    Bounds wb = r.bounds;
                    for (int c = 0; c < 8; c++)
                    {
                        var corner = new Vector3((c & 1) == 0 ? wb.min.x : wb.max.x, (c & 2) == 0 ? wb.min.y : wb.max.y, (c & 4) == 0 ? wb.min.z : wb.max.z);
                        frontZ = Mathf.Min(frontZ, root.transform.InverseTransformPoint(corner).z);
                    }
                }
            }

            // Spec 020: no keys on the world labels any more (first person clicks; the prompts say how).
            // Spec 027d: a small sign on the cook side of the front face (never a name floating above the station).
            // Spec 028b: on a fridge or shelf the sign sits on the top panel, above the items.
            Vector3 signAt = stock
                ? new Vector3(0f, StockArt.FridgeHeaderBottom + 0.02f - height * 0.5f, frontZ - StationSign.Standoff)
                : StationSign.LocalPosition(height, frontZ);
            TextMesh label = CreateLabel(root.transform, signAt, 60, 0.05f);
            label.transform.localScale = Vector3.one * StationSign.BaseScale;
            label.gameObject.AddComponent<StationSign>();
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

        /// <summary>Spec 027d: the text and panel materials WorldLabelFade copies at run time (in Resources, so the shader is in every build).</summary>
        private static void EnsureLabelMaterials()
        {
            EnsureFolder(Root + "/Resources/Labels");
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Resources/Labels/WorldText.shader");
            if (shader == null) throw new InvalidOperationException("WorldText.shader is missing");
            foreach (string name in new[] { "WorldText", "WorldPanel" })
            {
                string path = Root + "/Resources/Labels/" + name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, path);
                }
                else if (material.shader != shader) material.shader = shader;
            }
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
            text.color = Palette.Cream; // spec 026: world labels are Cream
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
            // Spec 027b: closer and lower than before (5 m, 1.6 m, 20 degrees), so the person fills more of the frame and the
            // kitchen stays visible; less damping (feel.md 5). The numbers live in FeelSettings (preset A); PlayerCameraRig
            // applies the current ones again on bind and when the F2 panel changes them.
            FeelSettings feel = FeelSettings.PresetA();
            orbit.Radius = feel.ThirdPersonDistance;
            orbit.TargetOffset = new Vector3(0f, feel.ThirdPersonHeight, 0f);
            orbit.VerticalAxis.Range = new Vector2(-30f, 70f);
            orbit.VerticalAxis.Value = feel.ThirdPersonPitch;
            orbit.VerticalAxis.Center = feel.ThirdPersonPitch;
            orbit.RadialAxis.Range = new Vector2(0.5f, 2.2f); // about 1.6 to 7 m with the wheel
            orbit.RadialAxis.Value = 1f;
            orbit.TrackerSettings.PositionDamping = Vector3.one * feel.ThirdPersonDamping;

            var composer = go.AddComponent<CinemachineRotationComposer>();
            composer.TargetOffset = new Vector3(0f, feel.ThirdPersonHeight, 0f);
            composer.Damping = Vector2.one * feel.ComposerDamping;

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

        private static void CreateEnvironment(Material groundMat, Material wallMat)
        {
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            light.color = Palette.Cream; // spec 026 (D32): one warm key light

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
                box.GetComponent<MeshRenderer>().sharedMaterial = wallMat;
                box.isStatic = true;
            }
        }

        /// <summary>
        /// Spec 015: a short visual-only street east of the kitchen. The customers walk along
        /// z = 10.8 (spec 027a: x 6.6 to 24, along the front counter) on the north side of the road; houses stand behind them.
        /// Nothing here has a collider, so movement and interaction do not change.
        /// Spec 026: the buildings are at real size now (7.5 m to the ridge, up to 11.7 m wide), so the north row is spaced
        /// for their widths and the widest house that no longer fits there (F) stands south of the road, east of the
        /// crossroad's south arm (x 34 to 39), which stays open. Wrappers are centred now, so the lamps and the awning are
        /// placed by their bounds: the lamp posts stay at the south edge of the road, the awning on the shop's front.
        /// </summary>
        private static void CreateStreet(ArtCatalog catalog)
        {
            var street = new GameObject("Street");
            // Road strip z 5.5 to 10.5, x 9 to 39, 5 m tiles; a crossroad at the east end.
            for (int i = 0; i < 6; i++)
                PlaceArt(catalog, street.transform, i == 5 ? ArtKey.RoadCrossroad : ArtKey.Road, new Vector3(11.5f + i * 5f, 0.02f, 8f), 0f);
            // Facades north of the walking path, fronts facing the road (south).
            GameObject shop = PlaceArt(catalog, street.transform, ArtKey.BuildingShop, new Vector3(21f, 0f, 17f), 180f);
            PlaceArt(catalog, street.transform, ArtKey.BuildingHouseA, new Vector3(31.5f, 0f, 17f), 180f);
            PlaceArt(catalog, street.transform, ArtKey.BuildingHouseC, new Vector3(43f, 0f, 17f), 180f);
            // Two houses south of the road, facing north.
            PlaceArt(catalog, street.transform, ArtKey.BuildingHouseK, new Vector3(30f, 0f, 0.5f), 0f);
            PlaceArt(catalog, street.transform, ArtKey.BuildingHouseF, new Vector3(HouseFX, 0f, -0.5f), 0f);
            // Spec 026: the awning hangs on the shop's front, its back edge 2 cm into the wall, its bottom at the mounting height.
            if (shop != null)
            {
                float front = RendererBounds(shop).min.z;
                GameObject awning = PlaceArt(catalog, street.transform, ArtKey.PropAwning, new Vector3(21f, AwningMountHeight, front), 180f);
                if (awning != null)
                {
                    Bounds b = RendererBounds(awning);
                    awning.transform.position += new Vector3(0f, 0f, front - b.size.z * 0.5f + AwningWallOverlap - b.center.z);
                }
            }
            // Street lights along the south edge of the road, arms over the road (north); the back of each post at LampBackZ.
            // Spec 026: the east one moved from x 38 to 33, off the crossroad's south arm (x 34 to 39).
            foreach (float x in new[] { 14f, 26f, 33f })
            {
                GameObject lamp = PlaceArt(catalog, street.transform, ArtKey.PropLamp, new Vector3(x, 0f, LampBackZ), 180f);
                if (lamp != null) lamp.transform.position += new Vector3(0f, 0f, LampBackZ - RendererBounds(lamp).min.z);
            }
            PlaceArt(catalog, street.transform, ArtKey.PropDumpster, new Vector3(24.5f, 0f, 15f), 0f); // spec 026: clear of the shop (was 24.2, 3 cm into it)
            PlaceArt(catalog, street.transform, ArtKey.PropTrashcan, new Vector3(22f, 0f, 13.2f), 0f);
            PlaceArt(catalog, street.transform, ArtKey.PropTreeLarge, new Vector3(-11f, 0f, 17f), 0f);
            PlaceArt(catalog, street.transform, ArtKey.PropTreeSmall, new Vector3(16f, 0f, 14.3f), 0f);
            PlaceArt(catalog, street.transform, ArtKey.PropPlanter, new Vector3(-10f, 0f, 14.2f), 0f);
            PlaceArt(catalog, street.transform, ArtKey.PropPlanter, new Vector3(15.5f, 0f, 16f), 0f);
            // A fence behind the kitchen row (spec 026: the 2.4 m panels now meet exactly).
            for (int i = 0; i < 10; i++)
                PlaceArt(catalog, street.transform, ArtKey.PropFence, new Vector3(-8.5f + i * 2.4f, 0f, 15.5f), 0f);
        }

        // Spec 026: see CreateStreet.
        private const float HouseFX = 44.5f;          // east of the crossroad's south arm (x 34 to 39), inside the ground (x 50)
        private const float AwningMountHeight = 2.3f; // bottom edge of the awning above the pavement (m)
        private const float AwningWallOverlap = 0.02f; // the awning's back edge reaches 2 cm into the shop wall
        private const float LampBackZ = 4.6f;         // back of the lamp posts, south of the road (z 5.5)

        private static Bounds RendererBounds(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        private static void SetStaticRecursive(GameObject root)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.isStatic = true;
        }

        private static GameObject PlaceArt(ArtCatalog catalog, Transform parent, ArtKey key, Vector3 position, float yaw)
        {
            if (catalog == null || !catalog.TryGet(key, out GameObject wrapper)) return null;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(wrapper, parent);
            instance.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            SetStaticRecursive(instance);
            return instance;
        }

        private static Transform[] CreateSpawnPoints()
        {
            // Spec 027a: in the cook aisle (its centre line), facing the back line (+z); was a ring around the origin.
            var parent = new GameObject("SpawnPoints");
            var result = new List<Transform>();
            for (int i = 0; i < SpawnX.Length; i++)
            {
                var spawn = new GameObject("Spawn " + (i + 1));
                spawn.transform.SetParent(parent.transform, false);
                spawn.transform.SetPositionAndRotation(new Vector3(SpawnX[i], 0.1f, AisleZ), Quaternion.identity);
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
                case Vector3 v3: prop.vector3Value = v3; break;
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
