using System;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Worktown.Game.Art;
using Worktown.Game.Cooking;
using Object = UnityEngine.Object;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 017 AC4: food models in the art catalog, their wrappers, the tint rules and FoodVisual.</summary>
    public class FoodArtCatalogTests
    {
        private const string CatalogPath = "Assets/_Project/Art/ArtCatalog.asset";
        private const string FoodPrefabPath = "Assets/_Project/Prefabs/Food.prefab";
        private const string ScenePath = "Assets/_Project/Scenes/Graybox.unity";

        private static readonly byte[] CutShapes =
            { FoodLook.ShapeHalves, FoodLook.ShapeSlices, FoodLook.ShapeSticks, FoodLook.ShapeCubes };

        private static readonly ArtKey[] Containers = { ArtKey.FoodPlate, ArtKey.FoodBowl, ArtKey.FoodPan, ArtKey.FoodPot };

        private static ArtCatalog Catalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ArtCatalog>(CatalogPath);
            Assert.IsNotNull(catalog, "ArtCatalog.asset missing; run Worktown/Build Graybox Scene");
            return catalog;
        }

        private static GameObject Prefab(ArtCatalog catalog, ArtKey key)
        {
            Assert.IsTrue(catalog.TryGet(key, out GameObject prefab) && prefab != null, "no catalog model for " + key);
            return prefab;
        }

        [Test]
        public void EveryDemoIngredient_HasAWholeModel()
        {
            ArtCatalog catalog = Catalog();
            Assert.AreEqual(DemoIngredients.All.Count, FoodArtRules.IngredientCount, "a colour per demo ingredient");
            for (byte i = 0; i < DemoIngredients.All.Count; i++)
            {
                Assert.IsTrue(FoodArtRules.TryGetPiece(Look(i, FoodLook.ShapeWhole), out FoodPiece piece), DemoIngredients.All[i].Id);
                Assert.AreEqual(1, piece.Count);
                Prefab(catalog, piece.Key);
            }
        }

        [Test]
        public void EveryShape_HasAModelOrTheGenericPiece()
        {
            ArtCatalog catalog = Catalog();
            foreach (byte shape in CutShapes)
            {
                Prefab(catalog, FoodArtRules.GenericPiece(shape));
                for (byte i = 0; i < DemoIngredients.All.Count; i++)
                {
                    foreach (FoodLook look in Variants(i, shape))
                    {
                        Assert.IsTrue(FoodArtRules.TryGetPiece(look, out FoodPiece piece), look.ToString());
                        Assert.That(piece.Count, Is.InRange(1, 6));
                        Prefab(catalog, piece.Key);
                    }
                }
            }
        }

        [Test]
        public void PlateBowlPanAndPot_HaveModels()
        {
            ArtCatalog catalog = Catalog();
            foreach (ArtKey key in Containers) Prefab(catalog, key);
            Assert.IsEmpty(ArtCatalogRules.DuplicateKeys(catalog.ToPairs()));
        }

        [Test]
        public void FoodWrappers_HaveNoCollidersOrScripts_AndFitTheFootprint()
        {
            ArtCatalog catalog = Catalog();
            foreach (ArtKey key in Enum.GetValues(typeof(ArtKey)))
            {
                if (!key.ToString().StartsWith("Food")) continue;
                GameObject prefab = Prefab(catalog, key);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                try
                {
                    Assert.IsEmpty(instance.GetComponentsInChildren<Collider>(true), key + " has a collider");
                    Assert.IsEmpty(instance.GetComponentsInChildren<MonoBehaviour>(true), key + " has a script");
                    Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
                    Assert.IsNotEmpty(renderers, key + " has no renderer");
                    Bounds b = renderers[0].bounds;
                    foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
                    float limit = key == ArtKey.FoodPan || key == ArtKey.FoodPot ? 0.6f
                        : key == ArtKey.FoodPlate || key == ArtKey.FoodBowl ? 0.35f : 0.25f;
                    Assert.LessOrEqual(Mathf.Max(b.size.x, b.size.y, b.size.z), limit + 0.001f, key + " is too big");
                    Assert.AreEqual(0f, b.min.y, 0.002f, key + " pivot is not at the bottom");
                }
                finally { Object.DestroyImmediate(instance); }
            }
        }

        [Test]
        public void UnknownIngredientOrShape_HasNoPiece_SoTheCubeShows()
        {
            Assert.IsFalse(FoodArtRules.TryGetPiece(Look(FoodLook.UnknownIngredient, FoodLook.ShapeWhole), out _));
            Assert.IsFalse(FoodArtRules.TryGetPiece(Look(0, 99), out _));
        }

        [Test]
        public void PotatoSticks_BecomeFriesOnlyOnceFriedToMedium()
        {
            ArtKey KeyOf(FoodLook look)
            {
                Assert.IsTrue(FoodArtRules.TryGetPiece(look, out FoodPiece piece));
                return piece.Key;
            }

            Assert.AreEqual(ArtKey.FoodPieceStick, KeyOf(Look(0, FoodLook.ShapeSticks)), "raw sticks are not fries");
            Assert.AreEqual(ArtKey.FoodPieceStick, KeyOf(Look(0, FoodLook.ShapeSticks, FoodLook.DonenessShort, FoodLook.HeatFried)));
            Assert.AreEqual(ArtKey.FoodPieceStick, KeyOf(Look(0, FoodLook.ShapeSticks, FoodLook.DonenessLong, FoodLook.HeatBoiled)));
            Assert.AreEqual(ArtKey.FoodFries, KeyOf(Look(0, FoodLook.ShapeSticks, FoodLook.DonenessMedium, FoodLook.HeatFried)));
            Assert.AreEqual(ArtKey.FoodFries, KeyOf(Look(0, FoodLook.ShapeSticks, FoodLook.DonenessLong, FoodLook.HeatFried)));
            Assert.AreEqual(ArtKey.FoodPieceStick, KeyOf(Look(2, FoodLook.ShapeSticks, FoodLook.DonenessLong, FoodLook.HeatFried)),
                "only potato sticks become fries");
        }

        [Test]
        public void HandHeldPiles_FitTheFootprint()
        {
            var root = new GameObject("PileBoundsTest");
            try
            {
                var visual = root.AddComponent<FoodVisual>();
                var so = new SerializedObject(visual);
                so.FindProperty("_catalog").objectReferenceValue = Catalog();
                so.ApplyModifiedPropertiesWithoutUndo();
                var shapes = new[] { FoodLook.ShapeWhole, FoodLook.ShapeHalves, FoodLook.ShapeSlices, FoodLook.ShapeSticks, FoodLook.ShapeCubes };
                for (byte i = 0; i < DemoIngredients.All.Count; i++)
                {
                    foreach (byte shape in shapes)
                    {
                        foreach (FoodLook look in Variants(i, shape))
                        {
                            visual.ShowFood(look, Array.Empty<FoodLook>());
                            Bounds b = ActiveBounds(root);
                            Assert.LessOrEqual(Mathf.Max(b.size.x, b.size.y, b.size.z), 0.25f, look + " is larger than 0.25 m");
                        }
                    }
                }
                // A full plate stays within the plate (0.35 m).
                var part = Look(0, FoodLook.ShapeCubes);
                visual.ShowFood(new FoodLook(0, 0, 0, 0, FoodLook.KindPlated, 4), new[] { part, part, part, part });
                Bounds plate = ActiveBounds(root);
                Assert.LessOrEqual(Mathf.Max(plate.size.x, plate.size.z), 0.35f + 0.001f, "a full plate");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static FoodLook Look(byte ingredient, byte shape, byte doneness = 0, byte heat = 0) =>
            new FoodLook(ingredient, shape, doneness, heat, FoodLook.KindSingle, 0);

        /// <summary>Raw, and fried long (where some models change, e.g. potato sticks to fries).</summary>
        private static FoodLook[] Variants(byte ingredient, byte shape) => new[]
        {
            Look(ingredient, shape), Look(ingredient, shape, FoodLook.DonenessLong, FoodLook.HeatFried)
        };

        private static Bounds ActiveBounds(GameObject root)
        {
            bool any = false;
            var bounds = new Bounds();
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(false))
            {
                if (!any) bounds = r.bounds;
                else bounds.Encapsulate(r.bounds);
                any = true;
            }
            Assert.IsTrue(any, "nothing drawn");
            return bounds;
        }

        [Test]
        public void Tint_DarkensWithDoneness_AndFriedTurnsGolden()
        {
            Assert.AreEqual(0f, FoodArtRules.DarkeningOf(FoodLook.DonenessRaw));
            Assert.AreEqual(0.10f, FoodArtRules.DarkeningOf(FoodLook.DonenessShort), 1e-6f);
            Assert.AreEqual(0.25f, FoodArtRules.DarkeningOf(FoodLook.DonenessMedium), 1e-6f);
            Assert.AreEqual(0.45f, FoodArtRules.DarkeningOf(FoodLook.DonenessLong), 1e-6f);

            var raw = new FoodLook(0, FoodLook.ShapeSticks, FoodLook.DonenessRaw, FoodLook.HeatNone, 0, 0);
            Assert.AreEqual(Color.white, FoodArtRules.Tint(raw, false), "own colours stay untouched while raw");
            float previous = float.MaxValue;
            for (byte d = FoodLook.DonenessShort; d <= FoodLook.DonenessLong; d++)
            {
                var fried = new FoodLook(0, FoodLook.ShapeSticks, d, FoodLook.HeatFried, 0, 0);
                var boiled = new FoodLook(0, FoodLook.ShapeSticks, d, FoodLook.HeatBoiled, 0, 0);
                Color f = FoodArtRules.Tint(fried, true);
                Color b = FoodArtRules.Tint(boiled, true);
                float brightness = f.r + f.g + f.b;
                Assert.Less(brightness, previous, "darker with every bucket");
                previous = brightness;
                Assert.Less(f.b / f.r, b.b / b.r, "fried is more golden (less blue) than boiled");
            }
        }

        [Test]
        public void FoodPrefab_KeepsItsColliderAndHasAVisualWithTheCubeAsFallback()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FoodPrefabPath);
            Assert.IsNotNull(prefab);
            var box = prefab.GetComponent<BoxCollider>();
            Assert.IsNotNull(box);
            Assert.AreEqual(Vector3.one * 0.4f, box.size);
            Assert.AreEqual(1, prefab.GetComponentsInChildren<Collider>(true).Length, "only the pickup collider");
            var visual = prefab.GetComponentInChildren<FoodVisual>(true);
            Assert.IsNotNull(visual, "Food prefab has no FoodVisual");
            var so = new SerializedObject(visual);
            Assert.AreSame(Catalog(), so.FindProperty("_catalog").objectReferenceValue);
            Assert.IsNotNull(so.FindProperty("_fallback").objectReferenceValue, "no fallback cube");
            Component food = prefab.GetComponent(typeof(Food)); // typed as Component: no FishNet reference here
            Assert.IsNotNull(food);
            Assert.AreSame(visual, new SerializedObject(food).FindProperty("_visual").objectReferenceValue);
        }

        [Test]
        public void FoodVisual_ShowsModels_FallsBackToTheCube_AndReusesInstances()
        {
            var root = new GameObject("FoodVisualTest");
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var visual = root.AddComponent<FoodVisual>();
                var so = new SerializedObject(visual);
                so.FindProperty("_catalog").objectReferenceValue = Catalog();
                so.FindProperty("_fallback").objectReferenceValue = cube.GetComponent<Renderer>();
                so.ApplyModifiedPropertiesWithoutUndo();
                Renderer fallback = cube.GetComponent<Renderer>();

                var potato = new FoodLook(0, FoodLook.ShapeWhole, 0, 0, FoodLook.KindSingle, 0);
                visual.ShowFood(potato, Array.Empty<FoodLook>());
                Assert.AreEqual(1, ActiveChildren(root));
                Assert.IsFalse(fallback.enabled, "the model replaces the cube");

                var cubes = new FoodLook(2, FoodLook.ShapeCubes, 0, 0, FoodLook.KindSingle, 0);
                visual.ShowFood(cubes, Array.Empty<FoodLook>());
                Assert.AreEqual(6, ActiveChildren(root), "a pile of six cubes");

                var parts = new[] { new FoodLook(0, FoodLook.ShapeSticks, 2, 1, 0, 0), new FoodLook(2, FoodLook.ShapeSlices, 0, 0, 0, 0) };
                var plate = new FoodLook(0, 0, 0, 0, FoodLook.KindPlated, 2);
                visual.ShowFood(plate, parts);
                Assert.AreEqual(1 + 1 + 3, ActiveChildren(root), "plate, fries, three tomato slices");

                int total = root.transform.childCount;
                for (int i = 0; i < 3; i++)
                {
                    visual.ShowFood(cubes, Array.Empty<FoodLook>());
                    visual.ShowFood(plate, parts);
                }
                Assert.AreEqual(total, root.transform.childCount, "rebuilds reuse pooled instances");

                visual.ShowFood(FoodLook.Unknown, Array.Empty<FoodLook>());
                Assert.AreEqual(0, ActiveChildren(root));
                Assert.IsTrue(fallback.enabled, "unknown looks show the cube");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(cube);
            }
        }

        [Test]
        public void BuiltScene_AssemblyAndHeatStationsHaveAToolAndAContentAnchor()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                int found = 0;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Station station in root.GetComponentsInChildren<Station>(true))
                    {
                        Component contents = station.GetComponent(typeof(StationContents));
                        if (contents == null) continue;
                        found++;
                        var visual = new SerializedObject(contents).FindProperty("_visual").objectReferenceValue as FoodVisual;
                        Assert.IsNotNull(visual, station.name + " has no content anchor");
                        Assert.AreSame(station.transform, visual.transform.parent);
                        Assert.IsNotNull(station.transform.Find("Tool_Food" + ToolName(station.Kind)), station.name + " has no tool model");
                        Assert.Greater(visual.transform.localPosition.y, 0f, station.name + " contents sit below the top");
                    }
                }
                Assert.AreEqual(4, found, "pan, pot, plating table, bowl");
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        private static string ToolName(StationKind kind)
        {
            switch (kind)
            {
                case StationKind.Pan: return "Pan";
                case StationKind.Pot: return "Pot";
                case StationKind.PlatingTable: return "Plate";
                case StationKind.Bowl: return "Bowl";
                default: return "?";
            }
        }

        private static int ActiveChildren(GameObject root)
        {
            int count = 0;
            foreach (Transform child in root.transform)
                if (child.gameObject.activeSelf) count++;
            return count;
        }
    }
}
