using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
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
        private const string MiniCharacters = "Assets/ThirdParty/Kenney/MiniCharacters/";
        /// <summary>Spec 024: every character plays the clips of this file (all six share one rig and bone names).</summary>
        public const string CharacterClipSource = MiniCharacters + "character-male-a.fbx";
        /// <summary>Spec 024: character height in m.</summary>
        public const float CharacterHeight = 1.8f;
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

            // Spec 024: Kenney Mini Characters, 1.8 m, facing +Z (feet and the right arm at +X checked in the import).
            Character(ArtKey.CharacterMaleA, "character-male-a.fbx"),
            Character(ArtKey.CharacterFemaleA, "character-female-a.fbx"),
            Character(ArtKey.CharacterMaleB, "character-male-b.fbx"),
            Character(ArtKey.CharacterMaleC, "character-male-c.fbx"),
            Character(ArtKey.CharacterFemaleB, "character-female-b.fbx"),
            Character(ArtKey.CharacterFemaleC, "character-female-c.fbx")
        };

        private static Def Character(ArtKey key, string model) =>
            new Def { Key = key, Model = MiniCharacters + model, Mode = Fit.Character, Value = CharacterHeight };

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
            if (def.Mode == Fit.Character) ConfigureCharacterImport(def.Model);
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
                    Bounds b = WorldBounds(model);
                    model.transform.localScale = Vector3.one * (def.Value / b.size.y);
                    CenterOnBottom(model);
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
        /// Spec 024: the character FBX files import as a Generic rig with an avatar of their own, so the instance gets an
        /// Animator (the pack ships without .meta files; the default is no avatar and no Animator). Clip settings stay
        /// at the import defaults: CharacterVisual loops the clips itself.
        /// </summary>
        private static void ConfigureCharacterImport(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) throw new InvalidOperationException("Pack model missing: " + path);
            bool changed = false;
            if (importer.animationType != ModelImporterAnimationType.Generic)
            {
                importer.animationType = ModelImporterAnimationType.Generic;
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
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        /// <summary>
        /// Spec 024: an empty <see cref="CharacterVisual.HandName"/> under the right arm bone at the end of the arm, where
        /// carried items are drawn. All six characters share one rig, so the point is measured once on the plain arm of
        /// <see cref="CharacterClipSource"/> (female-a has something extra on her right arm that would push it outwards).
        /// </summary>
        private static void AddHandAnchor(GameObject model, ArtKey key)
        {
            Transform arm = FindDeep(model.transform, CharacterVisual.RightArmBone);
            if (arm == null) throw new InvalidOperationException(key + ": no bone " + CharacterVisual.RightArmBone);
            var hand = new GameObject(CharacterVisual.HandName);
            hand.transform.SetParent(arm, false);
            hand.transform.localPosition = HandInBoneSpace();
            Bounds b = WorldBounds(model);
            Debug.Log("[Art] " + key + " height " + b.size.y.ToString("F2") + " m, hand at " + hand.transform.position.ToString("F2")
                      + " (bone space " + hand.transform.localPosition.ToString("F3") + ")");
        }

        /// <summary>
        /// Spec 024: the end of the right arm in bone space: the centre of the arm vertices in the outer 20 % of its reach
        /// (bind pose, so it holds in every animated pose).
        /// </summary>
        private static Vector3 HandInBoneSpace()
        {
            var reference = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CharacterClipSource));
            try
            {
                Transform arm = FindDeep(reference.transform, CharacterVisual.RightArmBone);
                var points = new List<Vector3>();
                foreach (SkinnedMeshRenderer skin in reference.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    int bone = Array.IndexOf(skin.bones, arm);
                    if (bone < 0 || skin.sharedMesh == null) continue;
                    Vector3[] vertices = skin.sharedMesh.vertices;
                    BoneWeight[] weights = skin.sharedMesh.boneWeights;
                    Matrix4x4 bind = skin.sharedMesh.bindposes[bone];
                    for (int v = 0; v < vertices.Length && v < weights.Length; v++)
                        if (weights[v].boneIndex0 == bone && weights[v].weight0 >= 0.5f) points.Add(bind.MultiplyPoint3x4(vertices[v]));
                }
                if (points.Count == 0) throw new InvalidOperationException(CharacterClipSource + ": no vertices on " + CharacterVisual.RightArmBone);
                float reach = 0f;
                foreach (Vector3 p in points) reach = Mathf.Max(reach, p.magnitude);
                Vector3 sum = Vector3.zero;
                int count = 0;
                foreach (Vector3 p in points)
                {
                    if (p.magnitude < reach * 0.8f) continue;
                    sum += p;
                    count++;
                }
                return sum / count;
            }
            finally { UnityEngine.Object.DestroyImmediate(reference); }
        }

        /// <summary>Spec 024: the clip of that name from <see cref="CharacterClipSource"/>.</summary>
        public static AnimationClip CharacterClip(string name)
        {
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(CharacterClipSource))
                if (asset is AnimationClip clip && clip.name == name) return clip;
            throw new InvalidOperationException("Clip " + name + " missing in " + CharacterClipSource);
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
