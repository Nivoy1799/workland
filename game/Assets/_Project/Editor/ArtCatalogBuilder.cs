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
            public bool CloseLid; // spec 031b: the model's "lid" part is turned shut on its hinge (the open clamshell box)
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
            // Spec 031: no assembly board in the packs: a graybox worktop cabinet with a wooden cutting board on top.
            new Def { Key = ArtKey.StationAssembly, Mode = Fit.Graybox },
            // Spec 032: no register in the packs: a graybox till (body, leaning display, change drawer, buttons, card terminal, mat).
            new Def { Key = ArtKey.StationRegister, Mode = Fit.Graybox },
            // Spec 033: no drink dispenser in the packs: a graybox (Steel body, a panel per drink, spouts, a drip tray).
            new Def { Key = ArtKey.StationDrinkDispenser, Mode = Fit.Graybox },
            // Spec 034: no ordering terminal in the packs: a graybox wall screen (a Charcoal frame, a DeepBlue screen).
            new Def { Key = ArtKey.StationTerminal, Mode = Fit.Graybox },
            // Spec 035: no sink, dish return, kitchen trash can or wheelie bin in the packs: graybox models (CleaningArtRules).
            new Def { Key = ArtKey.StationSink, Mode = Fit.Graybox },
            new Def { Key = ArtKey.StationDishReturn, Mode = Fit.Graybox },
            new Def { Key = ArtKey.StationTrash, Mode = Fit.Graybox },
            new Def { Key = ArtKey.StationBin, Mode = Fit.Graybox },
            // Spec 036: no doner spit in the packs: a graybox (a Steel base, a Charcoal heat panel frame, the skewer, the motor housing).
            new Def { Key = ArtKey.StationSpit, Mode = Fit.Graybox },
            // Spec 037: no menu board in the packs: a graybox (a WoodDark frame round a Charcoal board).
            new Def { Key = ArtKey.StationMenuBoard, Mode = Fit.Graybox },
            // Spec 034: the delivery box is the Furniture Kit's cardboard box (the crate's model), 0.40 m.
            Fitted(ArtKey.PropDeliveryBox, Furniture + "cardboardBoxOpen.fbx", Fit.Max),

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
            // Spec 031: a flat layer (a bread base or a sauce, tinted), the folded wrap and the paper wrap (tinted primitives), the
            // Kenney takeaway box and bag (their own colours).
            Piece(ArtKey.FoodLayerDisc, PrimitiveType.Cylinder, new Vector3(0.25f, 0.008f, 0.25f)),
            Piece(ArtKey.FoodWrapFolded, PrimitiveType.Sphere, new Vector3(0.25f, 0.06f, 0.12f)),
            Piece(ArtKey.FoodWrapPaper, PrimitiveType.Sphere, new Vector3(0.25f, 0.075f, 0.135f)),
            // Spec 031b: the clamshell's lid is turned shut, so the box reads as one closed box, not two trays.
            new Def { Key = ArtKey.PropTakeawayBox, Model = FoodKit + "styrofoam.fbx", Mode = Fit.Max, CloseLid = true },
            FoodDef(ArtKey.PropPaperBag, "bag.fbx"),
            // Spec 033: the takeaway cup. The Kenney cup is a mug and the soda glass a purple cocktail glass with a straw and lemon, so
            // the cup is a graybox: an open, tapered Cream paper cup with its fill line (DrinkArtRules), 12 cm real, x1.5.
            new Def { Key = ArtKey.PropCup, Mode = Fit.Graybox },
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
            if (def.CloseLid) CloseLid(model, def.Key);
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
        /// Spec 031b: turns the model's "lid" part shut. The hinge is the base's edge that faces the lid, at the base's top; of the
        /// turns about that edge (every 5 degrees) the one that leaves the lid most squarely on top of the base wins (its middle
        /// over the base's middle, its bottom on the base's top). Throws when the model has no lid.
        /// </summary>
        private static void CloseLid(GameObject model, ArtKey key)
        {
            Transform lid = FindChild(model.transform, "lid");
            if (lid == null) throw new InvalidOperationException(key + ": the model has no 'lid' part to close");
            Bounds lidBounds = WorldBounds(lid.gameObject);
            bool any = false;
            Bounds baseBounds = default;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
            {
                if (r.transform == lid || r.transform.IsChildOf(lid)) continue;
                if (!any) baseBounds = r.bounds;
                else baseBounds.Encapsulate(r.bounds);
                any = true;
            }
            if (!any) throw new InvalidOperationException(key + ": the model has nothing but its lid");
            Vector3 apart = lidBounds.center - baseBounds.center;
            bool alongX = Mathf.Abs(apart.x) >= Mathf.Abs(apart.z);
            Vector3 axis = alongX ? Vector3.forward : Vector3.right;
            Vector3 hinge = alongX
                ? new Vector3(apart.x >= 0f ? baseBounds.max.x : baseBounds.min.x, baseBounds.max.y, baseBounds.center.z)
                : new Vector3(baseBounds.center.x, baseBounds.max.y, apart.z >= 0f ? baseBounds.max.z : baseBounds.min.z);
            Vector3 startPosition = lid.localPosition;
            Quaternion startRotation = lid.localRotation;
            float bestAngle = 0f, bestScore = float.MaxValue;
            for (int angle = -180; angle <= 180; angle += 5)
            {
                lid.localPosition = startPosition;
                lid.localRotation = startRotation;
                lid.RotateAround(hinge, axis, angle);
                Bounds b = WorldBounds(lid.gameObject);
                Vector2 off = new Vector2(b.center.x - baseBounds.center.x, b.center.z - baseBounds.center.z);
                float score = off.magnitude + Mathf.Abs(b.min.y - baseBounds.max.y) + (b.min.y < baseBounds.min.y ? 1f : 0f);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestAngle = angle;
                }
            }
            lid.localPosition = startPosition;
            lid.localRotation = startRotation;
            lid.RotateAround(hinge, axis, bestAngle);
            Debug.Log("[ArtCatalog] " + key + ": lid turned " + bestAngle + " degrees about the hinge (score " + bestScore.ToString("0.000") + ")");
        }

        private static Transform FindChild(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (string.Equals(child.name, name, StringComparison.OrdinalIgnoreCase)) return child;
                Transform deeper = FindChild(child, name);
                if (deeper != null) return deeper;
            }
            return null;
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
            if (key == ArtKey.StationAssembly) return BuildAssembly(parent); // spec 031
            if (key == ArtKey.StationRegister) return BuildRegister(parent); // spec 032
            if (key == ArtKey.StationDrinkDispenser) return BuildDrinkDispenser(parent); // spec 033
            if (key == ArtKey.PropCup) return BuildCup(parent);
            if (key == ArtKey.StationTerminal) return BuildTerminal(parent); // spec 034
            if (key == ArtKey.StationSink) return BuildSink(parent); // spec 035
            if (key == ArtKey.StationDishReturn) return BuildDishReturn(parent);
            if (key == ArtKey.StationTrash) return BuildTrash(parent);
            if (key == ArtKey.StationBin) return BuildBin(parent);
            if (key == ArtKey.StationSpit) return BuildSpit(parent); // spec 036
            if (key == ArtKey.StationMenuBoard) return BuildMenuBoard(parent); // spec 037
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
        /// Spec 031: the graybox assembly board as the wrapper's "Model" (<see cref="AssemblyArtRules"/>, station z mirrored: the front
        /// on +Z): a wooden cabinet with two door panels and a worktop, the cutting board the build lies on at the front left, a low
        /// shelf the two sauce dispensers stand on at the back, and the packaging rack (a low frame) along the right edge.
        /// </summary>
        private static GameObject BuildAssembly(Transform parent)
        {
            var model = new GameObject("Model");
            model.transform.SetParent(parent, false);
            Material wood = GrayboxMaterial("AssemblyWood", Palette.WoodLight);
            Material dark = GrayboxMaterial("AssemblyWoodDark", Palette.WoodDark);
            Material board = GrayboxMaterial("AssemblyBoard", Palette.Shade(Palette.WoodLight, 0.88f));
            Material steel = GrayboxMaterial("Steel", Palette.Steel);
            const float w = AssemblyArtRules.Width, d = AssemblyArtRules.Depth, h = AssemblyArtRules.Top;
            const float slab = 0.03f;
            float Z(float stationZ) => -stationZ; // the wrapper's front is +Z (turned half way round: x mirrors too)
            float X(float stationX) => -stationX;
            Box(model, "Cabinet", new Vector3(0f, (h - slab) * 0.5f, 0f), new Vector3(w, h - slab, d), wood);
            Box(model, "Worktop", new Vector3(0f, h - slab * 0.5f, 0f), new Vector3(w + 0.02f, slab, d + 0.02f), dark);
            for (int side = -1; side <= 1; side += 2)
                Box(model, side < 0 ? "Door left" : "Door right", new Vector3(side * w * 0.25f, (h - slab) * 0.5f, Z(-d * 0.5f - 0.005f)),
                    new Vector3(w * 0.5f - 0.03f, h - slab - 0.1f, 0.01f), dark);
            Vector2 spot = AssemblyArtRules.SpotPosition;
            Box(model, "Cutting board", new Vector3(X(spot.x), h + AssemblyArtRules.BoardThickness * 0.5f, Z(spot.y)),
                new Vector3(AssemblyArtRules.BoardWidth, AssemblyArtRules.BoardThickness, AssemblyArtRules.BoardDepth), board);
            Vector2 sauce = AssemblyArtRules.ToolPosition(0), chili = AssemblyArtRules.ToolPosition(1);
            Box(model, "Dispenser shelf", new Vector3(X((sauce.x + chili.x) * 0.5f), h + 0.006f, Z(sauce.y)), new Vector3(0.36f, 0.012f, 0.16f), steel);
            Vector2 paper = AssemblyArtRules.ToolPosition(2), bags = AssemblyArtRules.ToolPosition(4);
            float rackLength = bags.y - paper.y + 0.26f, rackZ = (paper.y + bags.y) * 0.5f;
            Box(model, "Rack floor", new Vector3(X(paper.x), h + 0.006f, Z(rackZ)), new Vector3(0.26f, 0.012f, rackLength), dark);
            Box(model, "Rack back", new Vector3(X(paper.x + 0.125f), h + 0.0075f, Z(rackZ)), new Vector3(0.012f, 0.015f, rackLength), dark);
            return model;
        }

        /// <summary>
        /// Spec 032: the graybox register as the wrapper's "Model" (RegisterArtRules, the base on the counter top at the origin): a
        /// Charcoal body, a display leaning towards the front with a DeepBlue screen, the WoodDark change drawer with a coin or note in
        /// each compartment (palette colours by value), the Terracotta BACK and Sage GIVE buttons, the DeepBlue card terminal and the
        /// WoodLight change mat. Front on +Z like the kit (the scene builder turns it to the cook).
        /// </summary>
        /// <summary>
        /// Spec 034: the graybox ordering terminal as the wrapper's "Model" (TerminalArtRules; its bottom on the origin, front on +Z like
        /// the kit): a Charcoal frame and a DeepBlue screen inset on its front. Its rows, buttons and texts are the scene's (they change).
        /// </summary>
        private static GameObject BuildTerminal(Transform parent)
        {
            var model = new GameObject("Model");
            model.transform.SetParent(parent, false);
            Material frame = GrayboxMaterial("TerminalFrame", Palette.Charcoal);
            Material screen = GrayboxMaterial("TerminalScreen", Palette.DeepBlue);
            float w = TerminalArtRules.Width, h = TerminalArtRules.Height, d = TerminalArtRules.Depth;
            Box(model, "Frame", new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, d), frame);
            Box(model, "Screen", new Vector3(0f, h * 0.5f, d * 0.5f + 0.001f), new Vector3(w - 0.05f, h - 0.05f, 0.002f), screen);
            return model;
        }

        /// <summary>
        /// Spec 037: the graybox menu board as the wrapper's "Model" (MenuBoardArtRules, its bottom at the origin): a WoodDark frame round a
        /// Charcoal board. The text and the buttons are the scene's (they change). Front on +Z like the kit (the scene turns it to the cook).
        /// </summary>
        private static GameObject BuildMenuBoard(Transform parent)
        {
            var model = new GameObject("Model");
            model.transform.SetParent(parent, false);
            Material frame = GrayboxMaterial("MenuBoardFrame", Palette.WoodDark);
            Material board = GrayboxMaterial("MenuBoardFace", Palette.Charcoal);
            float w = MenuBoardArtRules.Width, h = MenuBoardArtRules.Height, d = MenuBoardArtRules.Depth;
            Box(model, "Frame", new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, d), frame);
            Box(model, "Board", new Vector3(0f, h * 0.5f, d * 0.5f + 0.001f), new Vector3(w - 0.04f, h - 0.04f, 0.002f), board);
            return model;
        }

        private static GameObject BuildRegister(Transform parent)
        {
            var model = new GameObject("Model");
            model.transform.SetParent(parent, false);
            Material body = GrayboxMaterial("RegisterBody", Palette.Charcoal);
            Material screen = GrayboxMaterial("RegisterScreen", Palette.DeepBlue);
            Material drawer = GrayboxMaterial("RegisterDrawer", Palette.WoodDark);
            Material mat = GrayboxMaterial("RegisterMat", Palette.WoodLight);
            Material back = GrayboxMaterial("RegisterBack", Palette.Terracotta);
            Material give = GrayboxMaterial("RegisterGive", Palette.Sage);
            Vector3 F(Vector3 station) => new Vector3(-station.x, station.y, -station.z); // the wrapper's front is +Z (x mirrors too)
            Box(model, "Body", F(RegisterArtRules.BodyCentre), RegisterArtRules.BodySize, body);
            Quaternion lean = Quaternion.Euler(25f, 0f, 0f); // mirrored: leans back from the +Z front
            Box(model, "Display", F(RegisterArtRules.DisplayCentre), RegisterArtRules.DisplaySize, body).transform.localRotation = lean;
            Box(model, "Screen", F(RegisterArtRules.ScreenCentre), RegisterArtRules.ScreenSize, screen).transform.localRotation = lean;
            Box(model, "Drawer", F(RegisterArtRules.DrawerCentre), RegisterArtRules.DrawerSize, drawer);
            for (int slot = 0; slot < Worktown.Sim.Economy.CashRules.Tray.Count; slot++)
            {
                int value = Worktown.Sim.Economy.CashRules.Tray[slot];
                Box(model, "Money " + value, F(RegisterArtRules.TrayCentre(slot)), RegisterArtRules.TrayMoneySize(value),
                    GrayboxMaterial("Cash" + value, Worktown.Game.Shop.RegisterView.ColorOf(value)));
            }
            Box(model, "Back", F(RegisterArtRules.BackCentre), RegisterArtRules.ButtonSize, back);
            Box(model, "Give", F(RegisterArtRules.GiveCentre), RegisterArtRules.ButtonSize, give);
            Box(model, "Terminal", F(RegisterArtRules.TerminalCentre), RegisterArtRules.TerminalSize, screen);
            Box(model, "Change mat", F(RegisterArtRules.MatCentre), RegisterArtRules.MatSize, mat);
            return model;
        }

        /// <summary>
        /// Spec 033: the graybox drink dispenser as the wrapper's "Model" (DrinkArtRules, the base on the counter top at the origin): a
        /// Steel body with a cap, a coloured panel per drink above its tap, the three Steel spouts and the Charcoal drip tray. The levers,
        /// the cup stack and the lid stack are the scene's (they move or fly). Front on +Z like the kit (the scene turns it to the cook).
        /// </summary>
        private static GameObject BuildDrinkDispenser(Transform parent)
        {
            var model = new GameObject("Model");
            model.transform.SetParent(parent, false);
            Material steel = GrayboxMaterial("DrinkBody", Palette.Steel);
            Material cap = GrayboxMaterial("DrinkCap", Palette.DeepBlue);
            Material tray = GrayboxMaterial("DrinkTray", Palette.Charcoal);
            Material spout = GrayboxMaterial("DrinkSpout", Palette.Charcoal);
            Vector3 F(Vector3 station) => new Vector3(-station.x, station.y, -station.z); // the wrapper's front is +Z (x mirrors too)
            Box(model, "Body", F(DrinkArtRules.BodyCentre), DrinkArtRules.BodySize, steel);
            Box(model, "Cap", F(DrinkArtRules.CapCentre), DrinkArtRules.CapSize, cap);
            Box(model, "Drip tray", F(DrinkArtRules.TrayCentre), DrinkArtRules.TraySize, tray);
            for (int tap = 0; tap < Worktown.Game.Cooking.DrinkRules.TapCount; tap++)
            {
                Box(model, "Panel " + tap, F(DrinkArtRules.PanelCentre(tap)), DrinkArtRules.PanelSize,
                    GrayboxMaterial("DrinkPanel" + tap, DrinkArtRules.DrinkColour(tap)));
                Box(model, "Spout " + tap, F(DrinkArtRules.SpoutCentre(tap)), DrinkArtRules.SpoutSize, spout);
                // The spout's arm back into the body.
                Vector3 arm = DrinkArtRules.SpoutCentre(tap) + new Vector3(0f, DrinkArtRules.SpoutSize.y * 0.5f - 0.01f, 0.06f);
                Box(model, "Arm " + tap, F(arm), new Vector3(0.03f, 0.02f, 0.12f), spout);
            }
            return model;
        }

        /// <summary>
        /// Spec 033: the graybox takeaway cup as the wrapper's "Model": <see cref="DrinkArtRules.CupSides"/> Cream wall slabs leaning out
        /// from the bottom radius to the top one (an open, tapered paper cup, so the liquid shows from above), a Cream bottom, and the fill
        /// line printed round the outside (short Charcoal dashes on every slab). Its bottom at the origin.
        /// </summary>
        /// <summary>Spec 035: the name of a graybox's lid hinge (the scene's views swing it) and of the trash can's rim.</summary>
        public const string LidHingeName = "Lid hinge";
        public const string TrashRimName = "Rim";

        /// <summary>
        /// Spec 035: the graybox sink as the wrapper's "Model" (CleaningArtRules; station x and z mirrored: the front on +Z like the kit): a
        /// WoodLight cabinet with two WoodDark doors, a Steel top with the basin let into its left half (darker walls and bottom), a faucet
        /// at the basin's back and the drainboard's ridges on the right half. The water, the plates and the bubbles are the scene's.
        /// </summary>
        private static GameObject BuildSink(Transform parent)
        {
            var model = new GameObject("Model");
            model.transform.SetParent(parent, false);
            Material wood = GrayboxMaterial("SinkWood", Palette.WoodLight);
            Material dark = GrayboxMaterial("SinkDoor", Palette.WoodDark);
            Material steel = GrayboxMaterial("Steel", Palette.Steel);
            Material inner = GrayboxMaterial("SinkInner", Palette.Shade(Palette.Steel, 0.72f));
            Vector3 F(float x, float y, float z) => new Vector3(-x, y, -z);
            const float w = CleaningArtRules.SinkWidth, d = CleaningArtRules.SinkDepth, slab = 0.04f;
            float h = CleaningArtRules.SinkHeight;
            // The cabinet stops under the basin; round the basin the upper part is four blocks, so the basin is open down to its floor.
            float under = h - CleaningArtRules.BasinDepth - 0.012f;
            Box(model, "Cabinet", F(0f, under * 0.5f, 0f), new Vector3(w, under, d), wood);
            for (int side = -1; side <= 1; side += 2)
                Box(model, side < 0 ? "Door left" : "Door right", F(side * w * 0.25f, (h - slab) * 0.5f, -d * 0.5f - 0.005f),
                    new Vector3(w * 0.5f - 0.03f, h - slab - 0.1f, 0.01f), dark);
            Vector3 c = CleaningArtRules.BasinCentre;
            Vector2 s = CleaningArtRules.BasinSize;
            float x0 = c.x - s.x * 0.5f, x1 = c.x + s.x * 0.5f, z0 = c.z - s.y * 0.5f, z1 = c.z + s.y * 0.5f;
            float top = h - slab * 0.5f;
            float upper = h - slab - under, upperY = under + upper * 0.5f;
            Box(model, "Upper left", F((-w * 0.5f + x0) * 0.5f, upperY, 0f), new Vector3(x0 + w * 0.5f, upper, d), wood);
            Box(model, "Upper right", F((x1 + w * 0.5f) * 0.5f, upperY, 0f), new Vector3(w * 0.5f - x1, upper, d), wood);
            Box(model, "Upper front", F(c.x, upperY, (-d * 0.5f + z0) * 0.5f), new Vector3(s.x, upper, z0 + d * 0.5f), wood);
            Box(model, "Upper back", F(c.x, upperY, (z1 + d * 0.5f) * 0.5f), new Vector3(s.x, upper, d * 0.5f - z1), wood);
            // The top round the basin: left of it, right of it (the drainboard half), in front of it and behind it.
            Box(model, "Top left", F((-w * 0.5f + x0) * 0.5f, top, 0f), new Vector3(x0 + w * 0.5f, slab, d), steel);
            Box(model, "Top right", F((x1 + w * 0.5f) * 0.5f, top, 0f), new Vector3(w * 0.5f - x1, slab, d), steel);
            Box(model, "Top front", F(c.x, top, (-d * 0.5f + z0) * 0.5f), new Vector3(s.x, slab, z0 + d * 0.5f), steel);
            Box(model, "Top back", F(c.x, top, (z1 + d * 0.5f) * 0.5f), new Vector3(s.x, slab, d * 0.5f - z1), steel);
            float bottom = h - CleaningArtRules.BasinDepth;
            const float wall = 0.012f;
            Box(model, "Basin bottom", F(c.x, bottom, c.z), new Vector3(s.x, wall, s.y), inner);
            Box(model, "Basin left", F(x0 + wall * 0.5f, (bottom + h) * 0.5f, c.z), new Vector3(wall, h - bottom, s.y), inner);
            Box(model, "Basin right", F(x1 - wall * 0.5f, (bottom + h) * 0.5f, c.z), new Vector3(wall, h - bottom, s.y), inner);
            Box(model, "Basin front", F(c.x, (bottom + h) * 0.5f, z0 + wall * 0.5f), new Vector3(s.x, h - bottom, wall), inner);
            Box(model, "Basin back", F(c.x, (bottom + h) * 0.5f, z1 - wall * 0.5f), new Vector3(s.x, h - bottom, wall), inner);
            // The faucet stands above the top: the scene builds it (the model's height is the worktop's, as the size table says).
            Vector2 drain = CleaningArtRules.DrainCentre, size = CleaningArtRules.DrainSize;
            for (int i = 0; i < 5; i++)
                Box(model, "Ridge " + (i + 1), F(drain.x - size.x * 0.4f + i * size.x * 0.2f, h - 0.002f, drain.y), new Vector3(0.012f, 0.008f, size.y * 0.9f), inner);
            return model;
        }

        /// <summary>
        /// Spec 035: the graybox dish return as the wrapper's "Model" (on the counter top at the origin; front on +Z): a WoodDark tray with
        /// low rails along its sides and a pass-through arch on the guests' side (its height). The dirty plates are the scene's.
        /// </summary>
        private static GameObject BuildDishReturn(Transform parent)
        {
            var model = new GameObject("Model");
            model.transform.SetParent(parent, false);
            Material dark = GrayboxMaterial("ReturnTray", Palette.WoodDark);
            Material light = GrayboxMaterial("ReturnArch", Palette.WoodLight);
            Vector3 F(float x, float y, float z) => new Vector3(-x, y, -z);
            const float w = CleaningArtRules.ReturnWidth, d = CleaningArtRules.ReturnDepth, h = CleaningArtRules.ReturnHeight;
            const float floor = CleaningArtRules.ReturnShelfHeight;
            Box(model, "Tray", F(0f, floor * 0.5f, 0f), new Vector3(w, floor, d), dark);
            for (int side = -1; side <= 1; side += 2)
                Box(model, side < 0 ? "Rail left" : "Rail right", F(side * (w * 0.5f - 0.01f), floor + 0.03f, 0f), new Vector3(0.02f, 0.06f, d), dark);
            // The arch on the guests' side (station +z): two posts and a lintel.
            for (int side = -1; side <= 1; side += 2)
                Box(model, side < 0 ? "Post left" : "Post right", F(side * (w * 0.5f - 0.02f), h * 0.5f, d * 0.5f - 0.02f), new Vector3(0.04f, h, 0.04f), light);
            Box(model, "Lintel", F(0f, h - 0.02f, d * 0.5f - 0.02f), new Vector3(w, 0.04f, 0.04f), light);
            return model;
        }

        /// <summary>
        /// Spec 035: the graybox kitchen trash can as the wrapper's "Model" (front on +Z): an open Steel box with a Charcoal liner rim (the
        /// view tints it Terracotta when full) and a flip lid on a hinge at its back (<see cref="LidHingeName"/>; the view swings it). The
        /// bag inside is the scene's (it rises with the fill).
        /// </summary>
        private static GameObject BuildTrash(Transform parent)
        {
            var model = new GameObject("Model");
            model.transform.SetParent(parent, false);
            Material steel = GrayboxMaterial("Steel", Palette.Steel);
            Material rim = GrayboxMaterial("TrashRim", Palette.Charcoal);
            Material lid = GrayboxMaterial("TrashLid", Palette.Shade(Palette.Steel, 0.85f));
            Vector3 F(float x, float y, float z) => new Vector3(-x, y, -z);
            const float w = CleaningArtRules.TrashWidth, d = CleaningArtRules.TrashDepth, t = 0.02f, lidT = 0.02f;
            const float h = CleaningArtRules.TrashHeight - lidT; // the walls; the lid on them makes the can's height
            Box(model, "Bottom", F(0f, t * 0.5f, 0f), new Vector3(w, t, d), steel);
            Box(model, "Wall front", F(0f, h * 0.5f, -d * 0.5f + t * 0.5f), new Vector3(w, h, t), steel);
            Box(model, "Wall back", F(0f, h * 0.5f, d * 0.5f - t * 0.5f), new Vector3(w, h, t), steel);
            Box(model, "Wall left", F(-w * 0.5f + t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, d), steel);
            Box(model, "Wall right", F(w * 0.5f - t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, d), steel);
            // The liner's rim: four bars round the opening (the view tints them Terracotta when full; the bag inside shows between them).
            const float bar = 0.03f;
            Box(model, TrashRimName + " front", F(0f, h - 0.012f, -d * 0.5f + bar * 0.5f), new Vector3(w + 0.012f, 0.024f, bar), rim);
            Box(model, TrashRimName + " back", F(0f, h - 0.012f, d * 0.5f - bar * 0.5f), new Vector3(w + 0.012f, 0.024f, bar), rim);
            Box(model, TrashRimName + " left", F(-w * 0.5f + bar * 0.5f, h - 0.012f, 0f), new Vector3(bar, 0.024f, d), rim);
            Box(model, TrashRimName + " right", F(w * 0.5f - bar * 0.5f, h - 0.012f, 0f), new Vector3(bar, 0.024f, d), rim);
            var hinge = new GameObject(LidHingeName);
            hinge.transform.SetParent(model.transform, false);
            hinge.transform.localPosition = F(0f, h, d * 0.5f); // the back edge, on top
            Box(hinge, "Lid", new Vector3(0f, lidT * 0.5f, d * 0.5f), new Vector3(w + 0.012f, lidT, d + 0.012f), lid); // towards the front (+Z)
            return model;
        }

        /// <summary>
        /// Spec 035: the graybox wheelie bin outside as the wrapper's "Model" (front on +Z): a Leaf body, a Charcoal lid on a hinge at its back
        /// (<see cref="LidHingeName"/>), two wheels and a handle at the back.
        /// </summary>
        private static GameObject BuildBin(Transform parent)
        {
            var model = new GameObject("Model");
            model.transform.SetParent(parent, false);
            Material body = GrayboxMaterial("BinBody", Palette.Leaf);
            Material dark = GrayboxMaterial("BinLid", Palette.Charcoal);
            Vector3 F(float x, float y, float z) => new Vector3(-x, y, -z);
            const float w = CleaningArtRules.BinWidth, d = CleaningArtRules.BinDepth, h = CleaningArtRules.BinHeight, lid = 0.05f;
            Box(model, "Body", F(0f, (h - lid) * 0.5f + 0.04f, 0f), new Vector3(w, h - lid - 0.08f, d - 0.04f), body);
            for (int side = -1; side <= 1; side += 2)
                Box(model, side < 0 ? "Wheel left" : "Wheel right", F(side * (w * 0.5f - 0.04f), 0.06f, d * 0.5f - 0.08f), new Vector3(0.05f, 0.12f, 0.12f), dark);
            Box(model, "Handle", F(0f, h - lid - 0.05f, d * 0.5f + 0.02f), new Vector3(w * 0.7f, 0.03f, 0.04f), dark);
            var hinge = new GameObject(LidHingeName);
            hinge.transform.SetParent(model.transform, false);
            hinge.transform.localPosition = F(0f, h - lid, d * 0.5f);
            Box(hinge, "Lid", new Vector3(0f, lid * 0.5f, d * 0.5f), new Vector3(w + 0.03f, lid, d + 0.02f), dark);
            return model;
        }

        private static GameObject BuildCup(Transform parent)
        {
            var model = new GameObject("Model");
            model.transform.SetParent(parent, false);
            Material paper = GrayboxMaterial("Cup", Palette.Cream);
            Material line = GrayboxMaterial("CupLine", Palette.Charcoal);
            float h = DrinkArtRules.CupHeight, rb = DrinkArtRules.CupBottomRadius, rt = DrinkArtRules.CupTopRadius, t = DrinkArtRules.CupWall;
            int n = DrinkArtRules.CupSides;
            float slant = Mathf.Sqrt(h * h + (rt - rb) * (rt - rb));
            float tilt = Mathf.Atan2(rt - rb, h) * Mathf.Rad2Deg;
            float width = 2f * Mathf.PI * (rt + t) / n * 1.04f;
            float lineY = h * DrinkArtRules.FillLineHeight;
            float lineR = rb + (rt - rb) * lineY / h + t;
            for (int i = 0; i < n; i++)
            {
                float a = 360f * i / n;
                Quaternion turn = Quaternion.Euler(0f, a, 0f);
                Vector3 radial = turn * Vector3.forward;
                GameObject wall = Box(model, "Wall " + i, radial * ((rb + rt) * 0.5f + t * 0.5f) + Vector3.up * (h * 0.5f),
                    new Vector3(width, slant, t), paper);
                wall.transform.localRotation = turn * Quaternion.Euler(tilt, 0f, 0f);
                GameObject dash = Box(model, "Fill line " + i, radial * (lineR + 0.0012f) + Vector3.up * lineY,
                    new Vector3(width * 0.55f, 0.004f, 0.0015f), line);
                dash.transform.localRotation = turn * Quaternion.Euler(tilt, 0f, 0f);
            }
            GameObject bottom = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            bottom.name = "Bottom";
            UnityEngine.Object.DestroyImmediate(bottom.GetComponent<Collider>());
            bottom.transform.SetParent(model.transform, false);
            bottom.transform.localPosition = new Vector3(0f, 0.003f, 0f);
            bottom.transform.localScale = new Vector3(2f * rb + t, 0.003f, 2f * rb + t); // Unity's cylinder is 2 m high
            bottom.GetComponent<MeshRenderer>().sharedMaterial = paper;
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
        /// <summary>
        /// Spec 036: the graybox seitan spit as the wrapper's "Model" (SpitArtRules, the base on the counter top at the origin): a Steel
        /// base plate reaching forward under the tray, the heat panel's Charcoal frame at the back, the skewer and the motor housing on
        /// top. The cone, the panel's glowing face, the tray and the knob are the scene's (they turn, glow or are aimed at). Front on +Z like
        /// the kit (the scene turns it to the cook).
        /// </summary>
        private static GameObject BuildSpit(Transform parent)
        {
            var model = new GameObject("Model");
            model.transform.SetParent(parent, false);
            Material steel = GrayboxMaterial("SpitSteel", Palette.Steel);
            Material frame = GrayboxMaterial("SpitFrame", Palette.Charcoal);
            Vector3 F(Vector3 station) => new Vector3(-station.x, station.y, -station.z); // the wrapper's front is +Z (x mirrors too)
            Box(model, "Base", F(SpitArtRules.BaseCentre), SpitArtRules.BaseSize, steel);
            Box(model, "Panel", F(SpitArtRules.PanelCentre), SpitArtRules.PanelSize, frame);
            Box(model, "Housing", F(SpitArtRules.HousingCentre), SpitArtRules.HousingSize, steel);
            Box(model, "Skewer", F(SpitArtRules.SkewerCentre), SpitArtRules.SkewerSize, steel);
            return model;
        }

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
