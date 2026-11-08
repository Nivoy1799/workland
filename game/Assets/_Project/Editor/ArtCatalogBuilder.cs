using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Worktown.Game.Art;
using Worktown.Game.Player;

namespace Worktown.Game.Editor
{
    /// <summary>
    /// Spec 015: generates one wrapper prefab per catalog entry and ArtCatalog.asset from the
    /// imported CC0 packs. Scale, pivot and rotation live in the wrapper, never in the pack folder.
    /// Re-running overwrites the generated assets; called by GrayboxSceneBuilder.Build.
    /// </summary>
    public static class ArtCatalogBuilder
    {
        public const string CatalogPath = GrayboxSceneBuilder.Root + "/Art/ArtCatalog.asset";
        public const string WrapperFolder = GrayboxSceneBuilder.Root + "/Prefabs/Art";
        private const string Furniture = "Assets/ThirdParty/Kenney/FurnitureKit/";
        private const string Suburban = "Assets/ThirdParty/Kenney/CityKitSuburban/";
        private const string Commercial = "Assets/ThirdParty/Kenney/CityKitCommercial/";
        private const string Roads = "Assets/ThirdParty/Kenney/CityKitRoads/";
        private const string FoodKit = "Assets/ThirdParty/Kenney/FoodKit/";
        private const string Protagonists = "Assets/ThirdParty/Kenney/AnimatedCharactersProtagonists/";
        /// <summary>Spec 025: the body of every person (Kenney Animated Characters, Humanoid rig, one texture per skin).</summary>
        public const string CharacterBody = Protagonists + "Model/characterMedium.fbx";
        /// <summary>Spec 025: every person plays Humanoid clips from this file, retargeted onto the Kenney body.</summary>
        public const string CharacterClipSource = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/UAL1_Standard.fbx";
        /// <summary>Spec 025: prefix of the clip names in <see cref="CharacterClipSource"/>.</summary>
        private const string ClipPrefix = "Armature|";
        /// <summary>Spec 025 (D29): character height in m.</summary>
        public const float CharacterHeight = 1.75f;
        /// <summary>
        /// Spec 025 amendment: the clips of the animation states. The walk state blends walk and jog (phase-synced), the
        /// run state plays the sprint, crouch plays the crouch idle standing and the crouch walk moving.
        /// </summary>
        public const string IdleClip = "Idle_Loop";
        public const string WalkClip = "Walk_Loop";
        public const string JogClip = "Jog_Fwd_Loop";
        public const string RunClip = "Sprint_Loop";
        public const string CrouchClip = "Crouch_Idle_Loop";
        public const string CrouchWalkClip = "Crouch_Fwd_Loop";
        private static readonly string[] LoopClips = { IdleClip, WalkClip, JogClip, RunClip, CrouchClip, CrouchWalkClip };
        /// <summary>Spec 025 amendment: the root-motion export faces -Z at its original root; turned to face +Z like the body.</summary>
        private const float RootRotationOffset = 180f;
        /// <summary>Spec 025: the generated holding pose (the library has no carry clip).</summary>
        public const string HoldClipPath = GrayboxSceneBuilder.Root + "/Art/CharacterHold.anim";
        /// <summary>Spec 025: samples per second when the stride of a clip is measured.</summary>
        public const int StrideSampleRate = 60;
        /// <summary>Spec 017: white material for the generic food pieces; FoodVisual tints them per ingredient.</summary>
        public const string FoodPieceMaterialPath = GrayboxSceneBuilder.Root + "/Art/FoodPiece.mat";

        /// <summary>Station footprint the models are fitted into (matches the cube body).</summary>
        private const float FitXZ = 1.2f;
        private const float FitHeight = 1.1f;
        /// <summary>Kenney furniture fronts face +Z; stations face the spawn at -Z.</summary>
        private const float StationYaw = 180f;

        private enum Fit { Station, Height, Scale, Max, Unit, Character }

        private sealed class Def
        {
            public ArtKey Key;
            public string Model = string.Empty;
            public Fit Mode;
            public float Value;   // Height: target height in m. Scale: uniform scale.
            public float Yaw;
            public Vector3 ScaleXyz = Vector3.zero; // Scale mode with a non-uniform scale when set
            public string Extra = null; // optional second model put on top of the first (Station mode)
            public PrimitiveType? Primitive; // spec 017: generic piece built from a Unity primitive (Model unused)
            public bool AlongZ; // spec 019, Max mode: turn the model so its longest horizontal side runs along Z
            public string Skin = null; // spec 025, Character mode: texture name of the skin material
        }

        private static readonly Def[] Defs =
        {
            Station(ArtKey.StationCrate, Furniture + "cardboardBoxOpen.fbx"),
            Station(ArtKey.StationBoard, Furniture + "kitchenCabinetDrawer.fbx"),
            Station(ArtKey.StationPan, Furniture + "kitchenStove.fbx"),
            Station(ArtKey.StationPot, Furniture + "kitchenStoveElectric.fbx"),
            Station(ArtKey.StationCounter, Furniture + "kitchenBar.fbx"),
            Station(ArtKey.StationPlatingTable, Furniture + "kitchenCabinet.fbx"),
            new Def { Key = ArtKey.StationBowl, Model = Furniture + "kitchenCabinet.fbx", Mode = Fit.Station,
                      Yaw = StationYaw, Extra = Furniture + "kitchenBlender.fbx" },

            Tile(ArtKey.Road, Roads + "road-straight.fbx"),
            Tile(ArtKey.RoadCrossroad, Roads + "road-crossroad.fbx"),
            Scaled(ArtKey.BuildingShop, Commercial + "building-a.fbx", 6f),
            Scaled(ArtKey.BuildingHouseA, Suburban + "building-type-a.fbx", 5f),
            Scaled(ArtKey.BuildingHouseC, Suburban + "building-type-c.fbx", 5f),
            Scaled(ArtKey.BuildingHouseF, Suburban + "building-type-f.fbx", 5f),
            Scaled(ArtKey.BuildingHouseK, Suburban + "building-type-k.fbx", 5f),
            Scaled(ArtKey.PropTreeSmall, Suburban + "tree-small.fbx", 5f),
            Scaled(ArtKey.PropTreeLarge, Suburban + "tree-large.fbx", 5f),
            Scaled(ArtKey.PropPlanter, Suburban + "planter.fbx", 4f),
            Scaled(ArtKey.PropLamp, Roads + "light-square.fbx", 5f),
            Scaled(ArtKey.PropDumpster, Roads + "dumpster.fbx", 4f),
            new Def { Key = ArtKey.PropTrashcan, Model = Furniture + "trashcan.fbx", Mode = Fit.Height, Value = 0.9f },
            Scaled(ArtKey.PropFence, Suburban + "fence.fbx", 5f),
            Scaled(ArtKey.PropAwning, Commercial + "detail-awning.fbx", 6f),

            // Spec 017: food. Hand-held food fits ~0.25 m, plates 0.35 m. Max = largest dimension in m.
            // No potato in the Food Kit: the egg is the stand-in, tinted potato brown by FoodVisual.
            FoodDef(ArtKey.FoodPotato, "egg.fbx", 0.16f),
            FoodDef(ArtKey.FoodApple, "apple.fbx", 0.18f),
            FoodDef(ArtKey.FoodTomato, "tomato.fbx", 0.18f),
            FoodDef(ArtKey.FoodOnion, "onion.fbx", 0.18f),
            FoodDef(ArtKey.FoodAppleHalf, "apple-half.fbx", 0.15f),
            FoodDef(ArtKey.FoodOnionHalf, "onion-half.fbx", 0.15f),
            FoodDef(ArtKey.FoodTomatoSlice, "tomato-slice.fbx", 0.1f),
            FoodDef(ArtKey.FoodFries, "fries.fbx", 0.22f),
            Piece(ArtKey.FoodPieceHalf, PrimitiveType.Sphere, new Vector3(0.12f, 0.06f, 0.12f)),
            Piece(ArtKey.FoodPieceSlice, PrimitiveType.Cylinder, new Vector3(0.085f, 0.009f, 0.085f)),
            Piece(ArtKey.FoodPieceStick, PrimitiveType.Cube, new Vector3(0.025f, 0.025f, 0.13f)),
            Piece(ArtKey.FoodPieceCube, PrimitiveType.Cube, new Vector3(0.05f, 0.05f, 0.05f)),
            FoodDef(ArtKey.FoodPlate, "plate.fbx", 0.35f),
            FoodDef(ArtKey.FoodBowl, "bowl.fbx", 0.3f),
            FoodDef(ArtKey.FoodPan, "frying-pan.fbx", 0.55f),
            FoodDef(ArtKey.FoodPot, "pot.fbx", 0.42f),

            // Spec 019: the cutting board on the board station, the knife (its long side along Z, see AlongZ).
            new Def { Key = ArtKey.ToolCuttingBoard, Model = FoodKit + "cutting-board.fbx", Mode = Fit.Max, Value = 0.5f },
            new Def { Key = ArtKey.ToolKnife, Model = FoodKit + "cooking-knife.fbx", Mode = Fit.Max, Value = 0.3f, AlongZ = true },

            // Spec 023: the cabinet squeezed into a 1 x 1 x 1 m box; the scene builder scales the instance to each worktop.
            new Def { Key = ArtKey.Worktop, Model = Furniture + "kitchenCabinet.fbx", Mode = Fit.Unit, Yaw = StationYaw },

            // Spec 025: the Kenney body at 1.75 m, facing +Z (toes at +Z, right hand at +X checked in the import), one
            // skin each. Customers use the same skins for now; Customer tints them.
            Character(ArtKey.CharacterPlayerMale, "skaterMaleA"),
            Character(ArtKey.CharacterPlayerFemale, "skaterFemaleA"),
            Character(ArtKey.CharacterCustomerMale, "skaterMaleA"),
            Character(ArtKey.CharacterCustomerFemale, "skaterFemaleA")
        };

        private static Def Character(ArtKey key, string skin) =>
            new Def { Key = key, Model = CharacterBody, Mode = Fit.Character, Value = CharacterHeight, Skin = skin };

        private static Def FoodDef(ArtKey key, string model, float maxSize) =>
            new Def { Key = key, Model = FoodKit + model, Mode = Fit.Max, Value = maxSize };

        private static Def Piece(ArtKey key, PrimitiveType primitive, Vector3 size) =>
            new Def { Key = key, Primitive = primitive, Mode = Fit.Scale, ScaleXyz = size };

        private static Def Station(ArtKey key, string model) =>
            new Def { Key = key, Model = model, Mode = Fit.Station, Yaw = StationYaw };

        private static Def Scaled(ArtKey key, string model, float scale) =>
            new Def { Key = key, Model = model, Mode = Fit.Scale, Value = scale };

        /// <summary>Road tiles are 1 m native; stretched in XZ only so they stay flat.</summary>
        private static Def Tile(ArtKey key, string model) =>
            new Def { Key = key, Model = model, Mode = Fit.Scale, ScaleXyz = new Vector3(5f, 1f, 5f) };

        public static string WrapperPath(ArtKey key) => WrapperFolder + "/Art_" + key + ".prefab";

        [MenuItem("Worktown/Build Art Catalog")]
        public static ArtCatalog Build()
        {
            EnsureFolder(GrayboxSceneBuilder.Root + "/Art");
            EnsureFolder(WrapperFolder);

            var entries = new List<ArtCatalogEntry>();
            foreach (Def def in Defs)
            {
                GameObject wrapper = BuildWrapper(def);
                entries.Add(new ArtCatalogEntry { Key = def.Key, Prefab = wrapper });
            }

            var catalog = AssetDatabase.LoadAssetAtPath<ArtCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ArtCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            catalog.SetEntries(entries);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log("[Art] catalog built with " + entries.Count + " entries");
            return catalog;
        }

        private static GameObject BuildWrapper(Def def)
        {
            var root = new GameObject("Art_" + def.Key);
            if (def.Mode == Fit.Character) ConfigureHumanoidImport(def.Model);
            GameObject model = def.Primitive.HasValue
                ? CreatePiece(def.Primitive.Value, root.transform)
                : InstantiateModel(def.Model, root.transform, "Model");
            model.transform.localRotation = Quaternion.Euler(0f, def.Yaw, 0f);

            switch (def.Mode)
            {
                case Fit.Station:
                {
                    Bounds b = WorldBounds(model);
                    float s = Mathf.Min(FitXZ / b.size.x, FitXZ / b.size.z, FitHeight / b.size.y);
                    model.transform.localScale = Vector3.one * s;
                    CenterOnBottom(model);
                    if (def.Extra != null)
                    {
                        GameObject extra = InstantiateModel(def.Extra, root.transform, "Extra");
                        extra.transform.localRotation = Quaternion.Euler(0f, def.Yaw, 0f);
                        extra.transform.localScale = Vector3.one * s;
                        Bounds baseBounds = WorldBounds(model);
                        CenterOnBottom(extra);
                        extra.transform.localPosition += new Vector3(baseBounds.center.x, baseBounds.max.y, baseBounds.center.z);
                    }
                    break;
                }
                case Fit.Unit:
                {
                    Bounds b = WorldBounds(model);
                    model.transform.localScale = new Vector3(1f / b.size.x, 1f / b.size.y, 1f / b.size.z);
                    CenterOnBottom(model);
                    break;
                }
                case Fit.Height:
                {
                    Bounds b = WorldBounds(model);
                    model.transform.localScale = Vector3.one * (def.Value / b.size.y);
                    CenterOnBottom(model);
                    break;
                }
                case Fit.Max:
                {
                    Bounds b = WorldBounds(model);
                    if (def.AlongZ && b.size.x > b.size.z)
                    {
                        model.transform.localRotation = Quaternion.Euler(0f, def.Yaw + 90f, 0f);
                        b = WorldBounds(model);
                    }
                    float largest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                    model.transform.localScale = Vector3.one * (def.Value / largest);
                    CenterOnBottom(model);
                    if (def.Key.ToString().StartsWith("Tool")) Debug.Log("[Art] " + def.Key + " size " + WorldBounds(model).size.ToString("F3"));
                    break;
                }
                case Fit.Character:
                {
                    // Spec 025: the skinned bounds of the import are loose; the baked mesh gives the real height and feet.
                    Material skin = SkinMaterial(def.Skin);
                    foreach (SkinnedMeshRenderer r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) r.sharedMaterial = skin;
                    Bounds b = MeshBounds(model);
                    model.transform.localScale = Vector3.one * (def.Value / b.size.y);
                    b = MeshBounds(model);
                    if (Mathf.Abs(b.size.y - def.Value) > def.Value * 0.01f)
                        throw new InvalidOperationException(def.Key + ": height " + b.size.y + " m after scaling, expected " + def.Value);
                    model.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
                    ConfigureAnimator(model);
                    AddHandAnchor(model, def.Key);
                    break;
                }
                default:
                    model.transform.localScale = def.ScaleXyz != Vector3.zero ? def.ScaleXyz : Vector3.one * def.Value;
                    if (def.Primitive.HasValue) CenterOnBottom(model);
                    break;
            }

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, WrapperPath(def.Key));
            UnityEngine.Object.DestroyImmediate(root);
            return saved;
        }

        /// <summary>Instantiates the pack model as a child; wrappers hold no colliders or scripts.</summary>
        private static GameObject InstantiateModel(string path, Transform parent, string name)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) throw new InvalidOperationException("Pack model missing: " + path);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            instance.name = name;
            if (instance.GetComponentsInChildren<Collider>(true).Length > 0
                || instance.GetComponentsInChildren<MonoBehaviour>(true).Length > 0)
                throw new InvalidOperationException(path + " imports with a collider or script; disable it in the importer");
            return instance;
        }

        /// <summary>
        /// Spec 025: the body and the clip file import as Humanoid with an avatar of their own, so Unity retargets the clips
        /// onto the body (both rigs map cleanly). Clip settings stay at the import defaults: CharacterVisual loops the
        /// clips itself. Only the .meta import settings change, never the pack files.
        /// </summary>
        public static void ConfigureHumanoidImport(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) throw new InvalidOperationException("Pack model missing: " + path);
            bool changed = false;
            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                changed = true;
            }
            if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                changed = true;
            }
            if (importer.addCollider)
            {
                importer.addCollider = false;
                changed = true;
            }
            if (changed) importer.SaveAndReimport();
        }

        /// <summary>Spec 024: no controller (CharacterVisual plays the clips), no root motion, animate when only the shadow is drawn.</summary>
        private static void ConfigureAnimator(GameObject model)
        {
            Animator animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            if (animator.avatar == null || !animator.avatar.isHuman) throw new InvalidOperationException(model.name + ": no Humanoid avatar");
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        /// <summary>
        /// Spec 025: an empty <see cref="CharacterVisual.HandName"/> on the rig's real right hand bone, in the palm (half way
        /// to the index finger), where carried items are drawn.
        /// </summary>
        private static void AddHandAnchor(GameObject model, ArtKey key)
        {
            Animator animator = model.GetComponent<Animator>();
            Transform bone = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (bone == null) throw new InvalidOperationException(key + ": the avatar has no right hand");
            Transform finger = animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            var hand = new GameObject(CharacterVisual.HandName);
            hand.transform.SetParent(bone, false);
            hand.transform.localPosition = finger != null && finger.parent == bone ? finger.localPosition * 0.5f : Vector3.zero;
            Bounds b = MeshBounds(model);
            Debug.Log("[Art] " + key + " height " + b.size.y.ToString("F3") + " m, feet at " + b.min.y.ToString("F3") + ", hand on "
                      + bone.name + " at " + hand.transform.position.ToString("F2"));
        }

        /// <summary>Spec 025: the URP Lit material of one skin texture of the body (shared by every wrapper with that skin).</summary>
        private static Material SkinMaterial(string skin)
        {
            string texturePath = Protagonists + "Skins/" + skin + ".png";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture == null) throw new InvalidOperationException("Skin missing: " + texturePath);
            string path = GrayboxSceneBuilder.Root + "/Art/CharacterSkin_" + skin + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            bool changed = false;
            if (material.GetTexture("_BaseMap") != texture) { material.SetTexture("_BaseMap", texture); changed = true; }
            if (material.GetColor("_BaseColor") != Color.white) { material.SetColor("_BaseColor", Color.white); changed = true; }
            if (!Mathf.Approximately(material.GetFloat("_Smoothness"), 0.2f)) { material.SetFloat("_Smoothness", 0.2f); changed = true; }
            if (changed) EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Spec 025 amendment: <see cref="CharacterClipSource"/> is the library's root-motion export. Root motion is never
        /// applied (the server moves the body), so for the loops we play: the horizontal travel stays out of the pose (it
        /// becomes root motion, which is dropped; <c>averageSpeed</c> keeps it, that is the clip speed); rotation and height
        /// are baked into the pose from the original root, so the model neither turns nor sinks over a loop.
        /// </summary>
        public static void ConfigureClipImport()
        {
            ConfigureHumanoidImport(CharacterClipSource);
            var importer = (ModelImporter)AssetImporter.GetAtPath(CharacterClipSource);
            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            bool changed = false;
            if (clips == null || clips.Length == 0)
            {
                clips = importer.defaultClipAnimations;
                changed = true;
            }
            foreach (ModelImporterClipAnimation clip in clips)
            {
                if (Array.IndexOf(LoopClips, clip.name.Replace(ClipPrefix, string.Empty)) < 0) continue;
                changed |= Set(clip.loopTime, true, v => clip.loopTime = v);
                changed |= Set(clip.lockRootRotation, true, v => clip.lockRootRotation = v);
                changed |= Set(clip.keepOriginalOrientation, true, v => clip.keepOriginalOrientation = v);
                if (!Mathf.Approximately(clip.rotationOffset, RootRotationOffset)) { clip.rotationOffset = RootRotationOffset; changed = true; }
                changed |= Set(clip.lockRootHeightY, true, v => clip.lockRootHeightY = v);
                changed |= Set(clip.keepOriginalPositionY, true, v => clip.keepOriginalPositionY = v);
                changed |= Set(clip.lockRootPositionXZ, false, v => clip.lockRootPositionXZ = v);
            }
            if (!changed) return;
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        private static bool Set(bool current, bool wanted, Action<bool> apply)
        {
            if (current == wanted) return false;
            apply(wanted);
            return true;
        }

        /// <summary>
        /// Spec 025 amendment: the m/s a Humanoid clip covers at rate 1 on a character wrapper (1.75 m): one loop played
        /// through the body's avatar with root motion applied (in the editor only; the game never applies it), the travel
        /// of the body over the loop divided by the loop length. Unity's own retargeting scales the travel to the body.
        /// The clip's <c>averageSpeed</c> x the human scale gives the same for the walk but differs by up to 21 % for the
        /// others with this library (see the spec's notes); it is logged by <see cref="AverageSpeedOnBody"/> for comparison.
        /// </summary>
        public static float RootMotionSpeed(GameObject wrapperPrefab, AnimationClip clip)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(wrapperPrefab);
            PlayableGraph graph = default;
            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Animator animator = instance.GetComponentInChildren<Animator>(true);
                animator.applyRootMotion = true;
                graph = PlayableGraph.Create("Root motion " + clip.name);
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(graph, "Root motion", animator);
                output.SetSourcePlayable(AnimationClipPlayable.Create(graph, clip));
                graph.Play();
                graph.Evaluate(0f);
                Vector3 start = animator.transform.position;
                int steps = Mathf.Max(2, Mathf.RoundToInt(clip.length * StrideSampleRate * 2));
                for (int i = 0; i < steps; i++) graph.Evaluate(clip.length / steps);
                Vector3 travel = animator.transform.position - start;
                return new Vector2(travel.x, travel.z).magnitude / clip.length;
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        /// <summary>Spec 025 amendment: <c>clip.averageSpeed</c> x the body's human scale (m/s), for comparison only.</summary>
        public static float AverageSpeedOnBody(GameObject wrapperPrefab, AnimationClip clip)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(wrapperPrefab);
            try
            {
                Vector3 v = clip.averageSpeed;
                return new Vector2(v.x, v.z).magnitude * BodyHumanScale(instance.GetComponentInChildren<Animator>(true));
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        /// <summary>Spec 025 amendment: the avatar's human scale in metres on the scaled body.</summary>
        public static float BodyHumanScale(Animator animator) => animator.humanScale * animator.transform.lossyScale.y;

        /// <summary>
        /// Spec 025 amendment: the normalized clip time at which the left foot is furthest forward (about where it lands),
        /// so walk and jog can be blended on the same step.
        /// </summary>
        public static float LeftFootForwardPhase(GameObject wrapperPrefab, AnimationClip clip)
        {
            Sample(wrapperPrefab, clip, out List<float> times, out List<Vector3> left, out _);
            return StrideRules.FootForwardPhase(times, left);
        }

        /// <summary>Spec 025: the feet of the wrapper over one loop of the clip (see <see cref="MeasureClipSpeed"/>).</summary>
        private static void Sample(GameObject wrapperPrefab, AnimationClip clip, out List<float> times, out List<Vector3> left, out List<Vector3> right)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(wrapperPrefab);
            PlayableGraph graph = default;
            times = new List<float>();
            left = new List<Vector3>();
            right = new List<Vector3>();
            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Animator animator = instance.GetComponentInChildren<Animator>(true);
                Transform leftAnkle = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                Transform rightAnkle = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                Transform leftBall = animator.GetBoneTransform(HumanBodyBones.LeftToes);
                Transform rightBall = animator.GetBoneTransform(HumanBodyBones.RightToes);
                if (leftAnkle == null || rightAnkle == null || leftBall == null || rightBall == null)
                    throw new InvalidOperationException(wrapperPrefab.name + ": the avatar has no feet and toes");
                float heel = leftAnkle.position.y - leftBall.position.y;

                graph = PlayableGraph.Create("Stride " + clip.name);
                var output = AnimationPlayableOutput.Create(graph, "Stride", animator);
                AnimationClipPlayable playable = AnimationClipPlayable.Create(graph, clip);
                playable.SetApplyFootIK(true);
                output.SetSourcePlayable(playable);

                int samples = Mathf.Max(2, Mathf.RoundToInt(clip.length * StrideSampleRate));
                for (int i = 0; i <= samples; i++)
                {
                    float t = clip.length * i / samples;
                    playable.SetTime(t);
                    graph.Evaluate(0f);
                    times.Add(t);
                    left.Add(FootPoint(instance.transform, leftAnkle, leftBall, heel));
                    right.Add(FootPoint(instance.transform, rightAnkle, rightBall, heel));
                }
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        /// <summary>Spec 025: the clip of that name (without the "Armature|" prefix) from <see cref="CharacterClipSource"/>.</summary>
        public static AnimationClip CharacterClip(string name)
        {
            ConfigureClipImport();
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(CharacterClipSource))
                if (asset is AnimationClip clip && clip.name == ClipPrefix + name)
                {
                    if (!clip.humanMotion) throw new InvalidOperationException(clip.name + " is not a Humanoid clip");
                    return clip;
                }
            throw new InvalidOperationException("Clip " + name + " missing in " + CharacterClipSource);
        }

        /// <summary>
        /// Spec 025: the holding pose (the clip library has none): a Humanoid clip with only right arm muscles, the upper arm
        /// down at the side and slightly forward, the forearm forwards at waist height. CharacterVisual masks it to the arm.
        /// </summary>
        public static AnimationClip HoldClip()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(HoldClipPath);
            bool created = clip == null;
            if (created) clip = new AnimationClip { name = "CharacterHold" };
            clip.ClearCurves();
            void Muscle(string muscle, float value) => AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), muscle), AnimationCurve.Constant(0f, 1f, value));
            Muscle("Right Shoulder Down-Up", 0f);
            Muscle("Right Shoulder Front-Back", 0f);
            Muscle("Right Arm Down-Up", -0.5f);
            Muscle("Right Arm Front-Back", 0.1f);
            Muscle("Right Arm Twist In-Out", 0f);
            Muscle("Right Forearm Stretch", -0.3f);
            Muscle("Right Forearm Twist In-Out", 0f);
            Muscle("Right Hand Down-Up", 0f);
            Muscle("Right Hand In-Out", 0f);
            if (created) AssetDatabase.CreateAsset(clip, HoldClipPath);
            EditorUtility.SetDirty(clip);
            if (!clip.humanMotion) throw new InvalidOperationException(HoldClipPath + " is not a Humanoid clip");
            return clip;
        }

        /// <summary>
        /// Spec 025: the m/s a clip covers at playback rate 1 on a character wrapper (1.75 m), from its feet (a cross-check
        /// of <see cref="RootMotionSpeed"/>; it under-reads the sprint, whose feet barely touch). Samples the clip on the body
        /// through its Humanoid avatar at <see cref="StrideSampleRate"/> Hz (foot IK on, as CharacterVisual plays it) and
        /// passes the feet to <see cref="StrideRules"/>. A foot is the point between the ankle and the ball of the foot, as
        /// low as the lower of the heel (the ankle minus its rest height) and the ball, so heel strike and push-off count.
        /// </summary>
        public static float MeasureClipSpeed(GameObject wrapperPrefab, AnimationClip clip)
        {
            Sample(wrapperPrefab, clip, out List<float> times, out List<Vector3> left, out List<Vector3> right);
            return StrideRules.ClipSpeed(times, left, right);
        }

        private static Vector3 FootPoint(Transform root, Transform ankle, Transform ball, float heel)
        {
            Vector3 a = root.InverseTransformPoint(ankle.position);
            Vector3 b = root.InverseTransformPoint(ball.position);
            return new Vector3((a.x + b.x) * 0.5f, Mathf.Min(a.y - heel, b.y), (a.z + b.z) * 0.5f);
        }

        /// <summary>Spec 025: world bounds of the baked (posed) meshes, tighter than the skinned renderer bounds of the import.</summary>
        public static Bounds MeshBounds(GameObject go)
        {
            bool any = false;
            var b = new Bounds();
            var baked = new Mesh();
            try
            {
                foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>())
                {
                    if (renderer is SkinnedMeshRenderer skin)
                    {
                        // useScale: the vertices come out in the renderer's local space, so its full matrix gives world space
                        // (checked against the bone positions in the import inspection).
                        skin.BakeMesh(baked, true);
                        foreach (Vector3 v in baked.vertices)
                        {
                            Vector3 w = skin.transform.TransformPoint(v);
                            if (!any) b = new Bounds(w, Vector3.zero);
                            else b.Encapsulate(w);
                            any = true;
                        }
                    }
                    else
                    {
                        if (!any) b = renderer.bounds;
                        else b.Encapsulate(renderer.bounds);
                        any = true;
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(baked); }
            if (!any) throw new InvalidOperationException(go.name + " has no renderer");
            return b;
        }

        /// <summary>Spec 024: first transform of that name under <paramref name="parent"/> (inactive ones included).</summary>
        public static Transform FindDeep(Transform parent, string name)
        {
            foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        /// <summary>Spec 017: a generic food piece from a Unity primitive, without its collider, white material.</summary>
        private static GameObject CreatePiece(PrimitiveType primitive, Transform parent)
        {
            GameObject piece = GameObject.CreatePrimitive(primitive);
            piece.name = "Model";
            UnityEngine.Object.DestroyImmediate(piece.GetComponent<Collider>());
            piece.transform.SetParent(parent, false);
            piece.GetComponent<MeshRenderer>().sharedMaterial = FoodPieceMaterial();
            return piece;
        }

        private static Material FoodPieceMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(FoodPieceMaterialPath);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, FoodPieceMaterialPath);
            }
            material.color = Color.white;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Spec 017: height of a wrapper prefab's visuals above its pivot.</summary>
        public static float HeightOf(GameObject wrapperPrefab)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(wrapperPrefab);
            try { return TopOf(instance); }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        /// <summary>
        /// Spec 017: top of a station wrapper's base model (without an extra model on top, e.g. the blender), in
        /// wrapper space, so tools like the pan stand on the furniture.
        /// </summary>
        public static float BaseTopOf(GameObject wrapperInstance)
        {
            Transform model = wrapperInstance.transform.Find("Model");
            GameObject target = model != null ? model.gameObject : wrapperInstance;
            return WorldBounds(target).max.y - wrapperInstance.transform.position.y;
        }

        /// <summary>Moves the model so its bounds are centred in XZ with the bottom at the wrapper origin.</summary>
        private static void CenterOnBottom(GameObject model)
        {
            Bounds b = WorldBounds(model);
            model.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
        }

        private static Bounds WorldBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) throw new InvalidOperationException(go.name + " has no renderer");
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }

        /// <summary>Top of the wrapper's visuals in wrapper space (wrapper placed at the origin).</summary>
        public static float TopOf(GameObject wrapperInstance) => WorldBounds(wrapperInstance).max.y - wrapperInstance.transform.position.y;

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
