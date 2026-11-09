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
        /// <summary>Spec 025 (D29): character height in m (spec 026: from ArtSizeTable).</summary>
        public static float CharacterHeight => ArtSizeTable.TargetSize(ArtKey.CharacterPlayerMale);
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

        /// <summary>Station footprint the models are fitted into (matches the cube body); the height comes from ArtSizeTable.</summary>
        public const float FitXZ = 1.2f;
        /// <summary>Turns the Kenney furniture so its front (doors, knobs) faces the wrapper's +Z (spec 027a: the scene builder turns the wrappers to the stations' -Z fronts).</summary>
        private const float StationYaw = 180f;
        /// <summary>Spec 026: where the palette recolours of the Furniture Kit materials are kept.</summary>
        private const string RecolourFolder = GrayboxSceneBuilder.Root + "/Art/Recolour";

        /// <summary>
        /// Spec 026: every fit uses the target size of its key from <see cref="ArtSizeTable"/> (no free scale factors).
        /// Station: footprint into <see cref="FitXZ"/> (uniform in XZ), height to the target. Module: a worktop cabinet
        /// of <see cref="ArtSizeTable.WorktopModuleWidth"/> x target height x <see cref="FitXZ"/>. Height / Max: uniform by
        /// the height or the largest dimension. Tile: flat road tile, XZ to the target. Piece: a primitive at its real
        /// size x the class factor. Character: the baked body to the target height.
        /// </summary>
        private enum Fit { Station, Module, Height, Max, Tile, Piece, Character, Graybox }

        private sealed class Def
        {
            public ArtKey Key;
            public string Model = string.Empty;
            public Fit Mode;
            public float Yaw;
            public string Extra = null; // optional second model put on top of the first (Station mode)
            public PrimitiveType? Primitive; // spec 017: generic piece built from a Unity primitive (Model unused)
            public Vector3 PieceReal = Vector3.zero; // spec 026, Piece mode: the piece's real size in m (x, y, z)
            public bool AlongZ; // spec 019, Max mode: turn the model so its longest horizontal side runs along Z
            public string Skin = null; // spec 025, Character mode: texture name of the skin material
            public bool Flat; // spec 028: a stand-in model drawn with the white piece material (FoodVisual tints it flat)
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
            // Spec 028b: the stock stations. The Furniture Kit in the repo has no fridge and no bookcase (the full kit has them;
            // not downloaded), so they are graybox boxes in palette colours at their StockArt size, fronts on +Z like the kit.
            new Def { Key = ArtKey.StationFridge, Mode = Fit.Graybox },
            new Def { Key = ArtKey.StationShelf, Mode = Fit.Graybox },
            // Spec 029: no fryer model exists in the packs: a graybox fryer (steel body, two oil wells, a hook bar) and its wire
            // basket, fronts on +Z like the kit (the scene builder turns them to the aisle).
            new Def { Key = ArtKey.StationFryer, Mode = Fit.Graybox },
            new Def { Key = ArtKey.ToolFryerBasket, Mode = Fit.Graybox },
            // Spec 030: no griddle model in the packs either: a graybox steel plate on a steel cabinet with a front panel.
            new Def { Key = ArtKey.StationGriddle, Mode = Fit.Graybox },

            Fitted(ArtKey.Road, Roads + "road-straight.fbx", Fit.Tile),
            Fitted(ArtKey.RoadCrossroad, Roads + "road-crossroad.fbx", Fit.Tile),
            Fitted(ArtKey.BuildingShop, Commercial + "building-a.fbx", Fit.Height),
            Fitted(ArtKey.BuildingHouseA, Suburban + "building-type-a.fbx", Fit.Height),
            Fitted(ArtKey.BuildingHouseC, Suburban + "building-type-c.fbx", Fit.Height),
            Fitted(ArtKey.BuildingHouseF, Suburban + "building-type-f.fbx", Fit.Height),
            Fitted(ArtKey.BuildingHouseK, Suburban + "building-type-k.fbx", Fit.Height),
            Fitted(ArtKey.PropTreeSmall, Suburban + "tree-small.fbx", Fit.Height),
            Fitted(ArtKey.PropTreeLarge, Suburban + "tree-large.fbx", Fit.Height),
            Fitted(ArtKey.PropPlanter, Suburban + "planter.fbx", Fit.Max),
            Fitted(ArtKey.PropLamp, Roads + "light-square.fbx", Fit.Height),
            Fitted(ArtKey.PropDumpster, Roads + "dumpster.fbx", Fit.Max),
            Fitted(ArtKey.PropTrashcan, Furniture + "trashcan.fbx", Fit.Height),
            Fitted(ArtKey.PropFence, Suburban + "fence.fbx", Fit.Max),
            Fitted(ArtKey.PropAwning, Commercial + "detail-awning.fbx", Fit.Max),

            // Spec 017: food. Spec 026: sizes from ArtSizeTable (real size x1.5, largest dimension).
            // No potato in the Food Kit: the egg is the stand-in, tinted potato brown by FoodVisual.
            FoodDef(ArtKey.FoodPotato, "egg.fbx"),
            FoodDef(ArtKey.FoodApple, "apple.fbx"),
            FoodDef(ArtKey.FoodTomato, "tomato.fbx"),
            FoodDef(ArtKey.FoodOnion, "onion.fbx"),
            FoodDef(ArtKey.FoodAppleHalf, "apple-half.fbx"),
            FoodDef(ArtKey.FoodOnionHalf, "onion-half.fbx"),
            FoodDef(ArtKey.FoodTomatoSlice, "tomato-slice.fbx"),
            FoodDef(ArtKey.FoodFries, "fries.fbx"),
            // Spec 028: the street-food stock. The kit has no flatbread, falafel, cucumber or seitan: the nearest shapes stand in
            // (bread, a round loaf, the eggplant, the patty; the cabbage for lettuce and red cabbage); FoodVisual tints the stand-ins with the ingredient colour.
            FoodDef(ArtKey.FoodFlatbread, "bread.fbx"),
            FoodDef(ArtKey.FoodWrap, "taco.fbx"),
            FlatFoodDef(ArtKey.FoodSeitan, "meat-patty.fbx"),
            FlatFoodDef(ArtKey.FoodFalafelMix, "loaf-round.fbx"),
            FlatFoodDef(ArtKey.FoodLettuce, "cabbage.fbx"),
            FlatFoodDef(ArtKey.FoodCucumber, "eggplant.fbx"),
            FlatFoodDef(ArtKey.FoodRedCabbage, "cabbage.fbx"),
            FoodDef(ArtKey.FoodLemon, "lemon.fbx"),
            FoodDef(ArtKey.FoodLemonHalf, "lemon-half.fbx"),
            FlatFoodDef(ArtKey.FoodSauceGarlicHerb, "bottle-musterd.fbx"),
            FoodDef(ArtKey.FoodSauceChili, "bottle-ketchup.fbx"),
            // Spec 030: the grill vegetables (the kit's own models and colours).
            FoodDef(ArtKey.FoodPaprika, "paprika.fbx"),
            FoodDef(ArtKey.FoodEggplant, "eggplant.fbx"),
            FoodDef(ArtKey.FoodMushroom, "mushroom.fbx"),
            // Generic pieces at their real size (m): a half fruit, a 1 cm slice, a fry-sized stick, a 3 cm cube.
            Piece(ArtKey.FoodPieceHalf, PrimitiveType.Sphere, new Vector3(0.08f, 0.04f, 0.08f)),
            Piece(ArtKey.FoodPieceSlice, PrimitiveType.Cylinder, new Vector3(0.07f, 0.01f, 0.07f)),
            Piece(ArtKey.FoodPieceStick, PrimitiveType.Cube, new Vector3(0.015f, 0.015f, 0.09f)),
            Piece(ArtKey.FoodPieceCube, PrimitiveType.Cube, new Vector3(0.03f, 0.03f, 0.03f)),
            // Spec 029: a falafel ball (tinted brown by FoodVisual) and the empty fries box stacked at the fryer.
            Piece(ArtKey.FoodFalafelBall, PrimitiveType.Sphere, new Vector3(0.035f, 0.035f, 0.035f)),
            FoodDef(ArtKey.PropFriesBox, "fries-empty.fbx"),
            // Cookware (real size x1.15).
            FoodDef(ArtKey.FoodPlate, "plate.fbx"),
            FoodDef(ArtKey.FoodBowl, "bowl.fbx"),
            FoodDef(ArtKey.FoodPan, "frying-pan.fbx"),
            FoodDef(ArtKey.FoodPot, "pot.fbx"),

            // Spec 019: the cutting board on the board station, the knife (its long side along Z, see AlongZ).
            new Def { Key = ArtKey.ToolCuttingBoard, Model = FoodKit + "cutting-board.fbx", Mode = Fit.Max },
            new Def { Key = ArtKey.ToolKnife, Model = FoodKit + "cooking-knife.fbx", Mode = Fit.Max, AlongZ = true },
            // Spec 030: the griddle spatula (Food Kit), its long side along Z like the knife.
            new Def { Key = ArtKey.ToolSpatula, Model = FoodKit + "cooking-spatula.fbx", Mode = Fit.Max, AlongZ = true },

            // Spec 023, spec 026: one cabinet module (0.6 m wide, worktop high, station deep); the scene builder lines up
            // as many as a worktop run needs instead of stretching one cabinet.
            new Def { Key = ArtKey.Worktop, Model = Furniture + "kitchenCabinet.fbx", Mode = Fit.Module, Yaw = StationYaw },

            // Spec 025: the Kenney body at 1.75 m, facing +Z (toes at +Z, right hand at +X checked in the import), one
            // skin each. Customers use the same skins for now; Customer tints them.
            Character(ArtKey.CharacterPlayerMale, "skaterMaleA"),
            Character(ArtKey.CharacterPlayerFemale, "skaterFemaleA"),
            Character(ArtKey.CharacterCustomerMale, CustomerLooks.Of(ArtKey.CharacterCustomerMale).SkinName),
            Character(ArtKey.CharacterCustomerFemale, CustomerLooks.Of(ArtKey.CharacterCustomerFemale).SkinName),
            Character(ArtKey.CharacterCustomerMale2, CustomerLooks.Of(ArtKey.CharacterCustomerMale2).SkinName),
            Character(ArtKey.CharacterCustomerFemale2, CustomerLooks.Of(ArtKey.CharacterCustomerFemale2).SkinName),
            Character(ArtKey.CharacterCustomerMale3, CustomerLooks.Of(ArtKey.CharacterCustomerMale3).SkinName),
            Character(ArtKey.CharacterCustomerFemale3, CustomerLooks.Of(ArtKey.CharacterCustomerFemale3).SkinName)
        };

        /// <summary>
        /// Spec 026: Furniture Kit material (by name) to the palette colour it is recoloured with; the kit's own light
        /// peach wood and cool white metal clash with the warm muted palette. Unlisted materials (glass) keep their
        /// colour. The other packs draw from one shared colour-map texture and are not tinted.
        /// </summary>
        private static readonly (string Material, string Palette)[] FurnitureRecolours =
        {
            ("wood", nameof(Palette.WoodLight)), ("woodDark", nameof(Palette.WoodDark)), ("metal", nameof(Palette.Steel)),
            ("metalMedium", nameof(Palette.Steel)), ("metalDark", nameof(Palette.Charcoal)), ("carpetWhite", nameof(Palette.Cream))
        };

        private static Def Character(ArtKey key, string skin) =>
            new Def { Key = key, Model = CharacterBody, Mode = Fit.Character, Skin = skin };

        /// <summary>Spec 028: a stand-in whose own colours would fight the tint (eggplant for cucumber): one flat colour.</summary>
        private static Def FlatFoodDef(ArtKey key, string model) =>
            new Def { Key = key, Model = FoodKit + model, Mode = Fit.Max, Flat = true };

        private static Def FoodDef(ArtKey key, string model) =>
            new Def { Key = key, Model = FoodKit + model, Mode = Fit.Max };

        private static Def Piece(ArtKey key, PrimitiveType primitive, Vector3 real) =>
            new Def { Key = key, Primitive = primitive, Mode = Fit.Piece, PieceReal = real };

        private static Def Station(ArtKey key, string model) =>
            new Def { Key = key, Model = model, Mode = Fit.Station, Yaw = StationYaw };

        private static Def Fitted(ArtKey key, string model, Fit mode) =>
            new Def { Key = key, Model = model, Mode = mode };

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
            GameObject model = def.Mode == Fit.Graybox ? BuildGraybox(def.Key, root.transform)
                : def.Primitive.HasValue ? CreatePiece(def.Primitive.Value, root.transform)
                : InstantiateModel(def.Model, root.transform, "Model");
            model.transform.localRotation = Quaternion.Euler(0f, def.Yaw, 0f);
            if (def.Model.StartsWith(Furniture)) Recolour(model);
            if (def.Flat)
                foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
                    r.sharedMaterial = FoodPieceMaterial();
            ArtSize size = ArtSizeTable.Of(def.Key);
            float target = size.Target;

            switch (def.Mode)
            {
                case Fit.Station:
                {
                    // Spec 026: the footprint keeps the 1.2 m station (uniform in XZ), the height is the target (0.90 m
                    // worktop, 1.05 m counter); Kenney's furniture has flat colours, so the squash does not stretch a texture.
                    Bounds b = WorldBounds(model);
                    float xz = Mathf.Min(FitXZ / b.size.x, FitXZ / b.size.z);
                    float y = target / b.size.y;
                    ScaleBy(model, new Vector3(xz, y, xz));
                    CenterOnBottom(model);
                    if (def.Extra != null)
                    {
                        // The blender keeps its proportions (the height scale of the base).
                        GameObject extra = InstantiateModel(def.Extra, root.transform, "Extra");
                        if (def.Extra.StartsWith(Furniture)) Recolour(extra);
                        extra.transform.localRotation = Quaternion.Euler(0f, def.Yaw, 0f);
                        ScaleBy(extra, Vector3.one * y);
                        Bounds baseBounds = WorldBounds(model);
                        CenterOnBottom(extra);
                        extra.transform.localPosition += new Vector3(baseBounds.center.x, baseBounds.max.y, baseBounds.center.z);
                    }
                    break;
                }
                case Fit.Module:
                {
                    // Spec 026: one worktop cabinet, 0.6 m wide, worktop high and as deep as the stations.
                    Bounds b = WorldBounds(model);
                    ScaleBy(model, new Vector3(ArtSizeTable.WorktopModuleWidth / b.size.x, target / b.size.y, FitXZ / b.size.z));
                    CenterOnBottom(model);
                    break;
                }
                case Fit.Height:
                {
                    Bounds b = WorldBounds(model);
                    ScaleBy(model, Vector3.one * (target / b.size.y));
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
                    ScaleBy(model, Vector3.one * (target / Largest(b.size)));
                    CenterOnBottom(model);
                    break;
                }
                case Fit.Tile:
                {
                    // Road tiles: stretched in XZ only so they stay flat.
                    Bounds b = WorldBounds(model);
                    float xz = target / Mathf.Max(b.size.x, b.size.z);
                    ScaleBy(model, new Vector3(xz, 1f, xz));
                    CenterOnBottom(model);
                    break;
                }
                case Fit.Piece:
                {
                    // Spec 026: the generic piece at its real size x the class factor; the table's real size is its largest side.
                    if (!Mathf.Approximately(Largest(def.PieceReal), size.Real))
                        throw new InvalidOperationException(def.Key + ": piece of " + Largest(def.PieceReal) + " m, ArtSizeTable says " + size.Real);
                    Vector3 s = def.PieceReal * ArtSizeTable.Factor(size.Class);
                    if (def.Primitive == PrimitiveType.Cylinder) s.y *= 0.5f; // Unity's cylinder is 2 m high
                    model.transform.localScale = s;
                    CenterOnBottom(model);
                    break;
                }
                case Fit.Character:
                {
                    // Spec 025: the skinned bounds of the import are loose; the baked mesh gives the real height and feet.
                    Material skin = SkinMaterial(def.Skin);
                    foreach (SkinnedMeshRenderer r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) r.sharedMaterial = skin;
                    Bounds b = MeshBounds(model);
                    ScaleBy(model, Vector3.one * (target / b.size.y));
                    b = MeshBounds(model);
                    if (Mathf.Abs(b.size.y - target) > target * 0.01f)
                        throw new InvalidOperationException(def.Key + ": height " + b.size.y + " m after scaling, expected " + target);
                    model.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
                    ConfigureAnimator(model);
                    AddHandAnchor(model, def.Key);
                    break;
                }
                case Fit.Graybox:
                    break; // spec 028b: built at its size (StockArt), bottom at the origin, centred in X
                default:
                    throw new InvalidOperationException(def.Key + ": unknown fit " + def.Mode);
            }

            LogSize(def, size, model);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, WrapperPath(def.Key));
            UnityEngine.Object.DestroyImmediate(root);
            return saved;
        }

        /// <summary>
        /// Spec 026: multiplies the model's own scale (some pack models import with a root scale, e.g. the trashcan 0.47
        /// and the bowl 0.93, which a plain assignment dropped, so they came out 2.1x and 1.08x too big).
        /// </summary>
        private static void ScaleBy(GameObject model, Vector3 factor) =>
            model.transform.localScale = Vector3.Scale(model.transform.localScale, factor);

        private static float Largest(Vector3 v) => Mathf.Max(v.x, Mathf.Max(v.y, v.z));

        /// <summary>Spec 026: the built size against the table (ArtSizeTests fails the build outside the tolerance).</summary>
        private static void LogSize(Def def, ArtSize size, GameObject model)
        {
            Bounds b = def.Mode == Fit.Character ? MeshBounds(model) : WorldBounds(model);
            float built = size.Axis == SizeAxis.Height ? b.size.y : Largest(b.size);
            string line = "[Art] " + def.Key + " size " + b.size.ToString("F3") + ": " + size.Axis.ToString().ToLowerInvariant() + " "
                          + built.ToString("F3") + " m, target " + size.Target.ToString("F3") + " m (" + size.Class + ")";
            if (ArtSizeTable.Fits(built, size.Target)) Debug.Log(line);
            else Debug.LogWarning(line + ": outside the tolerance");
        }

        /// <summary>
        /// Spec 026: replaces the Furniture Kit's own materials of a model with palette recolours (copies of the kit material
        /// with the palette base colour, one per kit material name, in <see cref="RecolourFolder"/>). A material override
        /// in the wrapper; the pack files are not touched.
        /// </summary>
        private static void Recolour(GameObject model)
        {
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material recoloured = RecolourOf(materials[i]);
                    if (recoloured == null) continue;
                    materials[i] = recoloured;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = materials;
            }
        }

        private static Material RecolourOf(Material source)
        {
            if (source == null) return null;
            string paletteName = null;
            foreach ((string kitMaterial, string palette) in FurnitureRecolours)
                if (kitMaterial == source.name) paletteName = palette;
            if (paletteName == null) return null;
            Color color = PaletteColour(paletteName);

            EnsureFolder(RecolourFolder);
            string path = RecolourFolder + "/Furniture_" + source.name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(source) { name = "Furniture_" + source.name };
                AssetDatabase.CreateAsset(material, path);
            }
            string colorProperty = material.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
            if (material.GetColor(colorProperty) != color)
            {
                material.SetColor(colorProperty, color);
                EditorUtility.SetDirty(material);
            }
            return material;
        }

        /// <summary>The <see cref="Palette"/> colour of that field name.</summary>
        private static Color PaletteColour(string name)
        {
            foreach ((string Name, Color Color) entry in Palette.GuideColours)
                if (entry.Name == name) return entry.Color;
            throw new InvalidOperationException("no palette colour " + name);
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

        private const string SkinFolder = GrayboxSceneBuilder.Root + "/Art/CharacterSkins";

        private static CustomerLook FindLookBySkin(string skin)
        {
            foreach (CustomerLook look in CustomerLooks.All) if (look.SkinName == skin) return look;
            return null;
        }

        /// <summary>
        /// Spec 027d: paints the recoloured atlas of a customer look next to the project's art (the Kenney file is read as
        /// bytes and never touched) and returns its asset path. Same inputs, same pixels, so reruns change nothing.
        /// </summary>
        private static string PaintSkin(CustomerLook look)
        {
            string source = Protagonists + "Skins/" + look.BaseSkin + ".png";
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(System.IO.File.ReadAllBytes(source))) throw new InvalidOperationException("Cannot read " + source);
            if (texture.width != texture.height) throw new InvalidOperationException(source + " is not square");
            Color32[] pixels = SkinRecolourRules.Apply(texture.GetPixels32(), texture.width, look.Female, look.Shirt, look.Trousers, look.Hair);
            texture.SetPixels32(pixels);
            byte[] png = texture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(texture);
            System.IO.Directory.CreateDirectory(SkinFolder);
            string path = SkinFolder + "/" + look.SkinName + ".png";
            if (!System.IO.File.Exists(path) || !System.Linq.Enumerable.SequenceEqual(System.IO.File.ReadAllBytes(path), png))
            {
                System.IO.File.WriteAllBytes(path, png);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            else if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null) AssetDatabase.ImportAsset(path);
            return path;
        }

        /// <summary>Spec 025: the URP Lit material of one skin texture of the body (shared by every wrapper with that skin).</summary>
        private static Material SkinMaterial(string skin)
        {
            string texturePath = Protagonists + "Skins/" + skin + ".png";
            CustomerLook look = FindLookBySkin(skin);
            if (look != null && look.SkinName != look.BaseSkin) texturePath = PaintSkin(look);
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

        /// <summary>Spec 028b: name of the part of a stock model that is its cabinet (the fridge's doors stand out of it).</summary>
        public const string StockCabinetName = "Cabinet";
        public const string StockDoorsName = "Doors";

        /// <summary>
        /// Spec 028b: a graybox stock station as the wrapper's "Model": a "Cabinet" (and the fridge's open "Doors") of boxes in
        /// palette colours, at the StockArt size, its bottom on the origin, centred in X and Z, its open front on +Z like the
        /// kit's furniture (the scene builder turns it to the aisle).
        /// </summary>
        private static GameObject BuildGraybox(ArtKey key, Transform parent)
        {
            if (key == ArtKey.StationFryer) return BuildFryer(parent);
            if (key == ArtKey.ToolFryerBasket) return BuildBasket(parent);
            if (key == ArtKey.StationGriddle) return BuildGriddle(parent); // spec 030
            var model = new GameObject("Model");
            model.transform.SetParent(parent, false);
            var cabinet = new GameObject(StockCabinetName);
            cabinet.transform.SetParent(model.transform, false);
            const float w = StockArt.Width, t = StockArt.Wall;
            float h = ArtSizeTable.StockHeight;
            if (key == ArtKey.StationFridge)
            {
                const float d = StockArt.FridgeDepth;
                Material shell = GrayboxMaterial("Fridge", Palette.Cream);
                Material inside = GrayboxMaterial("FridgeInside", Palette.SoftBlue);
                Material steel = GrayboxMaterial("Steel", Palette.Steel);
                Material seam = GrayboxMaterial("Seam", Palette.Charcoal);
                float open = StockArt.FridgeDrawerTop, header = StockArt.FridgeHeaderBottom;
                Box(cabinet, "Back", new Vector3(0f, h * 0.5f, -d * 0.5f + t * 0.5f), new Vector3(w, h, t), shell);
                Box(cabinet, "Side left", new Vector3(-w * 0.5f + t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, d), shell);
                Box(cabinet, "Side right", new Vector3(w * 0.5f - t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, d), shell);
                Box(cabinet, "Header", new Vector3(0f, (header + h) * 0.5f, 0f), new Vector3(w, h - header, d), shell);
                Box(cabinet, "Drawer", new Vector3(0f, open * 0.5f, 0f), new Vector3(w, open, d), shell);
                Box(cabinet, "Drawer seam", new Vector3(0f, open * 0.5f, d * 0.5f + 0.002f), new Vector3(w - 0.12f, 0.012f, 0.004f), seam);
                Box(cabinet, "Drawer handle", new Vector3(0f, open - 0.1f, d * 0.5f + 0.015f), new Vector3(0.5f, 0.025f, 0.03f), steel);
                Box(cabinet, "Drawer handle 2", new Vector3(0f, open * 0.5f - 0.1f, d * 0.5f + 0.015f), new Vector3(0.5f, 0.025f, 0.03f), steel);
                // The cold inside: a blue lining behind and under the items, and a steel shelf between the two rows.
                Box(cabinet, "Lining", new Vector3(0f, (open + header) * 0.5f, -d * 0.5f + t + 0.005f), new Vector3(w - 2f * t, header - open, 0.01f), inside);
                Box(cabinet, "Lining left", new Vector3(-w * 0.5f + t + 0.005f, (open + header) * 0.5f, t * 0.5f), new Vector3(0.01f, header - open, d - t), inside);
                Box(cabinet, "Lining right", new Vector3(w * 0.5f - t - 0.005f, (open + header) * 0.5f, t * 0.5f), new Vector3(0.01f, header - open, d - t), inside);
                float shelfTop = StockArt.FridgeRowHeights[0];
                Box(cabinet, "Shelf", new Vector3(0f, shelfTop - 0.0125f, t * 0.5f), new Vector3(w - 2f * t, 0.025f, d - t), steel);
                Box(cabinet, "Shelf edge", new Vector3(0f, shelfTop - 0.0125f, d * 0.5f - 0.006f), new Vector3(w - 2f * t, 0.035f, 0.012f), steel);

                // Two doors over the open part, opened to the sides (StockArt.DoorOpenAngle), hinged at the front corners.
                var doors = new GameObject(StockDoorsName);
                doors.transform.SetParent(model.transform, false);
                for (int side = -1; side <= 1; side += 2)
                {
                    float doorWidth = w * 0.5f, doorHeight = h - open - 0.02f, thick = 0.05f;
                    // Closed, the left door runs from its hinge towards +X, its outer face on +Z; opened it turns outwards.
                    Quaternion yaw = Quaternion.Euler(0f, side < 0 ? -StockArt.DoorOpenAngle : StockArt.DoorOpenAngle, 0f);
                    Vector3 along = yaw * new Vector3(-side, 0f, 0f);
                    Vector3 outer = yaw * Vector3.forward; // the closed door's outer face, now facing away from the opening
                    var hinge = new Vector3(side * w * 0.5f, open + 0.01f + doorHeight * 0.5f, d * 0.5f);
                    string name = side < 0 ? "Door left" : "Door right";
                    GameObject door = Box(doors, name, hinge + along * (doorWidth * 0.5f) - outer * (thick * 0.5f), new Vector3(doorWidth, doorHeight, thick), shell);
                    door.transform.localRotation = yaw;
                    GameObject lining = Box(doors, name + " lining", hinge + along * (doorWidth * 0.5f) - outer * (thick + 0.005f),
                        new Vector3(doorWidth - 0.08f, doorHeight - 0.08f, 0.01f), inside);
                    lining.transform.localRotation = yaw;
                    GameObject handle = Box(doors, name + " handle", hinge + along * (doorWidth - 0.06f) + outer * 0.015f + Vector3.down * 0.1f,
                        new Vector3(0.025f, 0.3f, 0.03f), steel);
                    handle.transform.localRotation = yaw;
                }
            }
            else
            {
                const float d = StockArt.ShelfDepth;
                Material wood = GrayboxMaterial("ShelfWood", Palette.WoodLight);
                Material back = GrayboxMaterial("ShelfBack", Palette.WoodDark);
                Box(cabinet, "Back", new Vector3(0f, h * 0.5f, -d * 0.5f + t * 0.5f), new Vector3(w, h, t), back);
                Box(cabinet, "Side left", new Vector3(-w * 0.5f + t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, d), wood);
                Box(cabinet, "Side right", new Vector3(w * 0.5f - t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, d), wood);
                Box(cabinet, "Header", new Vector3(0f, (StockArt.FridgeHeaderBottom + h) * 0.5f, 0f), new Vector3(w, h - StockArt.FridgeHeaderBottom, d), wood);
                Box(cabinet, "Plinth", new Vector3(0f, 0.04f, 0f), new Vector3(w, 0.08f, d), wood);
                for (int b = 0; b < StockArt.ShelfRowHeights.Count; b++)
                    Box(cabinet, "Board " + (b + 1), new Vector3(0f, StockArt.ShelfRowHeights[b] - t * 0.5f, t * 0.5f), new Vector3(w - 2f * t, t, d - t), wood);
            }
            return model;
        }

        /// <summary>
        /// Spec 029: the graybox deep fryer as the wrapper's "Model" (<see cref="FryerArt"/>, station z mirrored: the front on +Z):
        /// a steel cabinet, a darker front panel for the controls, two oil wells with a golden oil surface between steel rims and
        /// a divider, a deck behind them and a hook bar the raised baskets hang in front of.
        /// </summary>
        private static GameObject BuildFryer(Transform parent)
        {
            var model = new GameObject("Model");
            model.transform.SetParent(parent, false);
            Material steel = GrayboxMaterial("Steel", Palette.Steel);
            Material panel = GrayboxMaterial("FryerPanel", Palette.Shade(Palette.Steel, 0.78f));
            Material oil = GrayboxMaterial("FryerOil", Color.Lerp(Palette.Mustard, Palette.WoodDark, 0.4f));
            Material dark = GrayboxMaterial("FryerDark", Palette.Charcoal);
            const float w = FryerArt.Width, d = FryerArt.Depth, h = FryerArt.Height, floor = FryerArt.WellFloor;
            float Z(float stationZ) => -stationZ; // the wrapper's front is +Z
            float front = -d * 0.5f + FryerArt.FrontPanel;
            Box(model, "Cabinet", new Vector3(0f, floor * 0.5f, 0f), new Vector3(w, floor, d), steel);
            Box(model, "Front panel", new Vector3(0f, (floor + h) * 0.5f - 0.01f, Z(-d * 0.5f + FryerArt.FrontPanel * 0.5f)),
                new Vector3(w, h - floor - 0.02f, FryerArt.FrontPanel), panel);
            Box(model, "Front deck", new Vector3(0f, (floor + h) * 0.5f, Z((front + FryerArt.WellFront) * 0.5f)),
                new Vector3(w, h - floor, FryerArt.WellFront - front), steel);
            Box(model, "Back deck", new Vector3(0f, (floor + h) * 0.5f, Z((FryerArt.WellBack + d * 0.5f) * 0.5f)),
                new Vector3(w, h - floor, d * 0.5f - FryerArt.WellBack), steel);
            float wellLength = FryerArt.WellBack - FryerArt.WellFront, wellZ = Z((FryerArt.WellBack + FryerArt.WellFront) * 0.5f);
            float half = FryerArt.WellWidth * 0.5f;
            float left = FryerArt.BasketX[0] - half, right = FryerArt.BasketX[1] + half;
            float innerLeft = FryerArt.BasketX[0] + half, innerRight = FryerArt.BasketX[1] - half;
            float wallY = (floor + h) * 0.5f, wallH = h - floor;
            Box(model, "Side left", new Vector3((-w * 0.5f + left) * 0.5f, wallY, wellZ), new Vector3(left + w * 0.5f, wallH, wellLength), steel);
            Box(model, "Side right", new Vector3((w * 0.5f + right) * 0.5f, wallY, wellZ), new Vector3(w * 0.5f - right, wallH, wellLength), steel);
            Box(model, "Divider", new Vector3((innerLeft + innerRight) * 0.5f, wallY, wellZ), new Vector3(innerRight - innerLeft, wallH, wellLength), steel);
            for (int b = 0; b < FryerArt.BasketX.Length; b++)
            {
                float x = FryerArt.BasketX[b];
                Box(model, "Well " + b, new Vector3(x, floor + 0.005f, wellZ), new Vector3(FryerArt.WellWidth, 0.01f, wellLength), dark);
                Box(model, "Oil " + b, new Vector3(x, FryerArt.OilHeight - 0.005f, wellZ), new Vector3(FryerArt.WellWidth, 0.01f, wellLength), oil);
            }
            // A low rail behind the wells (the worktop stays at 0.90 m): the raised baskets hang in front of it.
            float barZ = Z(FryerArt.WellBack + 0.03f);
            Box(model, "Rail", new Vector3(0f, h + 0.006f, barZ), new Vector3(w - 0.06f, 0.012f, 0.02f), dark);
            return model;
        }

        /// <summary>
        /// Spec 030: the graybox griddle as the wrapper's "Model" (<see cref="GriddleArtRules"/>, station z mirrored: the front on +Z):
        /// a steel cabinet with a darker front panel for the knob, the dark steel plate (0.9 × 0.6 m) at the front with a low rim on
        /// its sides and back and a grease trough along its front edge, and a steel deck behind it.
        /// </summary>
        private static GameObject BuildGriddle(Transform parent)
        {
            var model = new GameObject("Model");
            model.transform.SetParent(parent, false);
            Material steel = GrayboxMaterial("Steel", Palette.Steel);
            Material panel = GrayboxMaterial("FryerPanel", Palette.Shade(Palette.Steel, 0.78f));
            Material plate = GrayboxMaterial("GriddlePlate", Color.Lerp(Palette.Charcoal, Palette.Steel, 0.3f));
            Material dark = GrayboxMaterial("FryerDark", Palette.Charcoal);
            const float w = GriddleArtRules.Width, d = GriddleArtRules.Depth, h = GriddleArtRules.PlateTop, t = GriddleArtRules.PlateThickness;
            float Z(float stationZ) => -stationZ; // the wrapper's front is +Z
            float body = h - t;
            Box(model, "Cabinet", new Vector3(0f, body * 0.5f, 0f), new Vector3(w, body, d), steel);
            Box(model, "Front panel", new Vector3(0f, body * 0.5f, Z(-d * 0.5f + 0.02f)), new Vector3(w, body - 0.02f, 0.04f), panel);
            float pw = Worktown.Game.Cooking.GriddleRules.PlateWidth, pd = Worktown.Game.Cooking.GriddleRules.PlateDepth;
            float pz = GriddleArtRules.PlateCenterZ;
            Box(model, "Plate", new Vector3(0f, h - t * 0.5f, Z(pz)), new Vector3(pw, t, pd), plate);
            Box(model, "Rim back", new Vector3(0f, h - 0.01f, Z(pz + pd * 0.5f + 0.01f)), new Vector3(pw + 0.04f, 0.028f, 0.02f), steel);
            Box(model, "Rim left", new Vector3(-pw * 0.5f - 0.01f, h - 0.01f, Z(pz)), new Vector3(0.02f, 0.028f, pd), steel);
            Box(model, "Rim right", new Vector3(pw * 0.5f + 0.01f, h - 0.01f, Z(pz)), new Vector3(0.02f, 0.028f, pd), steel);
            float frontEdge = pz - pd * 0.5f;
            Box(model, "Trough", new Vector3(0f, h - t - 0.01f, Z(frontEdge - 0.015f)), new Vector3(pw, 0.02f, 0.03f), dark);
            float deckFront = pz + pd * 0.5f + 0.02f;
            Box(model, "Deck", new Vector3(0f, h - t * 0.5f, Z((deckFront + d * 0.5f) * 0.5f)), new Vector3(w, t, d * 0.5f - deckFront), steel);
            return model;
        }

        /// <summary>
        /// Spec 029: the graybox wire basket as the wrapper's "Model": a cage of thin steel bars (rims, uprights, a floor grid) with
        /// a handle rising from its front (+Z) and a dark grip. The pivot is the middle of its floor.
        /// </summary>
        private static GameObject BuildBasket(Transform parent)
        {
            var model = new GameObject("Model");
            model.transform.SetParent(parent, false);
            Material wire = GrayboxMaterial("Wire", Palette.Steel);
            Material grip = GrayboxMaterial("Grip", Palette.Charcoal);
            const float bw = FryerArt.BasketWidth, bl = FryerArt.BasketLength, bh = FryerArt.BasketHeight, t = FryerArt.WireThickness;
            foreach (float y in new[] { t * 0.5f, bh - t * 0.5f })
            {
                Box(model, "Rim front", new Vector3(0f, y, bl * 0.5f), new Vector3(bw + t, t * 1.4f, t * 1.4f), wire);
                Box(model, "Rim back", new Vector3(0f, y, -bl * 0.5f), new Vector3(bw + t, t * 1.4f, t * 1.4f), wire);
                Box(model, "Rim left", new Vector3(-bw * 0.5f, y, 0f), new Vector3(t * 1.4f, t * 1.4f, bl + t), wire);
                Box(model, "Rim right", new Vector3(bw * 0.5f, y, 0f), new Vector3(t * 1.4f, t * 1.4f, bl + t), wire);
            }
            const float step = 0.045f;
            int along = Mathf.RoundToInt(bl / step), across = Mathf.RoundToInt(bw / step);
            for (int i = 1; i < along; i++)
            {
                float z = -bl * 0.5f + bl * i / along;
                Box(model, "Upright left " + i, new Vector3(-bw * 0.5f, bh * 0.5f, z), new Vector3(t, bh, t), wire);
                Box(model, "Upright right " + i, new Vector3(bw * 0.5f, bh * 0.5f, z), new Vector3(t, bh, t), wire);
                Box(model, "Floor across " + i, new Vector3(0f, t * 0.5f, z), new Vector3(bw, t, t), wire);
            }
            for (int i = 1; i < across; i++)
            {
                float x = -bw * 0.5f + bw * i / across;
                Box(model, "Upright front " + i, new Vector3(x, bh * 0.5f, bl * 0.5f), new Vector3(t, bh, t), wire);
                Box(model, "Upright back " + i, new Vector3(x, bh * 0.5f, -bl * 0.5f), new Vector3(t, bh, t), wire);
                Box(model, "Floor along " + i, new Vector3(x, t * 0.5f, 0f), new Vector3(t, t, bl), wire);
            }
            // The handle: a rod rising from the front rim towards the aisle, the grip on its end.
            float length = Mathf.Sqrt(FryerArt.HandleLength * FryerArt.HandleLength + FryerArt.HandleRise * FryerArt.HandleRise);
            Quaternion rise = Quaternion.Euler(-Mathf.Atan2(FryerArt.HandleRise, FryerArt.HandleLength) * Mathf.Rad2Deg, 0f, 0f);
            Vector3 start = new Vector3(0f, bh, bl * 0.5f), end = start + new Vector3(0f, FryerArt.HandleRise, FryerArt.HandleLength);
            GameObject rod = Box(model, "Handle", (start + end) * 0.5f, new Vector3(t * 1.6f, t * 1.6f, length), wire);
            rod.transform.localRotation = rise;
            GameObject hold = Box(model, "Grip", start + (end - start) * 0.75f, new Vector3(0.024f, 0.024f, length * 0.5f), grip);
            hold.transform.localRotation = rise;
            return model;
        }

        /// <summary>Spec 028b: one graybox part, a cube without its collider.</summary>
        private static GameObject Box(GameObject parent, string name, Vector3 centre, Vector3 size, Material material)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            UnityEngine.Object.DestroyImmediate(box.GetComponent<Collider>());
            box.transform.SetParent(parent.transform, false);
            box.transform.localPosition = centre;
            box.transform.localScale = size;
            box.GetComponent<MeshRenderer>().sharedMaterial = material;
            return box;
        }

        /// <summary>Spec 028b: a palette material for the graybox stock models (in the Art folder, one per name).</summary>
        private static Material GrayboxMaterial(string name, Color color)
        {
            string path = GrayboxSceneBuilder.Root + "/Art/Graybox_" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            if (material.color != color)
            {
                material.color = color;
                EditorUtility.SetDirty(material);
            }
            return material;
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
