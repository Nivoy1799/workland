using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Worktown.Game.Art;
using Worktown.Game.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 015: the art swap layer, its catalog asset and the built scene.</summary>
    public class ArtCatalogTests
    {
        private const string CatalogPath = "Assets/_Project/Art/ArtCatalog.asset";
        private const string WrapperFolder = "Assets/_Project/Prefabs/Art";
        private const string ScenePath = "Assets/_Project/Scenes/Graybox.unity";

        private static List<(ArtKey Key, GameObject Prefab)> Pairs(params (ArtKey, GameObject)[] items) =>
            new List<(ArtKey Key, GameObject Prefab)>(items);

        // AC3
        [Test]
        public void TryGet_ReturnsAssignedPrefab()
        {
            var prefab = new GameObject("p");
            try
            {
                var entries = Pairs((ArtKey.Road, prefab));
                Assert.IsTrue(ArtCatalogRules.TryGet(entries, ArtKey.Road, out GameObject found));
                Assert.AreSame(prefab, found);
            }
            finally { UnityEngine.Object.DestroyImmediate(prefab); }
        }

        [Test]
        public void TryGet_MissingOrUnassignedKey_ReturnsFalse()
        {
            var entries = Pairs((ArtKey.Road, null));
            Assert.IsFalse(ArtCatalogRules.TryGet(entries, ArtKey.Road, out GameObject unassigned));
            Assert.IsNull(unassigned);
            Assert.IsFalse(ArtCatalogRules.TryGet(entries, ArtKey.BuildingShop, out _));
        }

        [Test]
        public void MissingKeys_ReportsKeysWithoutPrefab()
        {
            var prefab = new GameObject("p");
            try
            {
                var entries = Pairs((ArtKey.Road, prefab), (ArtKey.BuildingShop, null));
                List<ArtKey> missing = ArtCatalogRules.MissingKeys(entries,
                    new[] { ArtKey.Road, ArtKey.BuildingShop, ArtKey.PropFence });
                CollectionAssert.AreEqual(new[] { ArtKey.BuildingShop, ArtKey.PropFence }, missing);
            }
            finally { UnityEngine.Object.DestroyImmediate(prefab); }
        }

        [Test]
        public void DuplicateKeys_ReportsEachDuplicateOnce()
        {
            var a = new GameObject("a");
            var b = new GameObject("b");
            try
            {
                var entries = Pairs((ArtKey.Road, a), (ArtKey.Road, b), (ArtKey.Road, a), (ArtKey.PropFence, a));
                CollectionAssert.AreEqual(new[] { ArtKey.Road }, ArtCatalogRules.DuplicateKeys(entries));
                Assert.IsEmpty(ArtCatalogRules.DuplicateKeys(Pairs((ArtKey.Road, a), (ArtKey.PropFence, b))));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(a);
                UnityEngine.Object.DestroyImmediate(b);
            }
        }

        [Test]
        public void StationKey_CoversEveryStationKindWithADistinctKey()
        {
            var seen = new HashSet<ArtKey>();
            foreach (StationKind kind in Enum.GetValues(typeof(StationKind)))
            {
                ArtKey key = ArtCatalogRules.StationKey(kind);
                StringAssert.StartsWith("Station", key.ToString(), kind.ToString());
                Assert.IsTrue(seen.Add(key), "duplicate key for " + kind);
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => ArtCatalogRules.StationKey((StationKind)200));
            // Spec 021: explicit, the stove keeps the spec 015 stove model, the shelf the plain cabinet.
            Assert.AreEqual(ArtKey.StationPan, ArtCatalogRules.StationKey(StationKind.Stove));
            Assert.AreEqual(ArtKey.StationPlatingTable, ArtCatalogRules.StationKey(StationKind.PlateShelf));
            Assert.AreEqual(ArtKey.StationCounter, ArtCatalogRules.StationKey(StationKind.Counter));
            Assert.AreEqual(ArtKey.StationCrate, ArtCatalogRules.StationKey(StationKind.Crate));
            Assert.AreEqual(ArtKey.StationFridge, ArtCatalogRules.StationKey(StationKind.Fridge)); // spec 028b
            Assert.AreEqual(ArtKey.StationShelf, ArtCatalogRules.StationKey(StationKind.Shelf));
        }

        // AC4
        [Test]
        public void Catalog_HasAnEntryForEveryStationKind()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ArtCatalog>(CatalogPath);
            Assert.IsNotNull(catalog, "ArtCatalog.asset missing; run Worktown/Build Graybox Scene");
            foreach (StationKind kind in Enum.GetValues(typeof(StationKind)))
            {
                ArtKey key = ArtCatalogRules.StationKey(kind);
                Assert.IsTrue(catalog.TryGet(key, out GameObject prefab) && prefab != null, "no art for " + kind);
            }
            Assert.IsEmpty(ArtCatalogRules.DuplicateKeys(catalog.ToPairs()));
        }

        // AC5
        [Test]
        public void BuiltScene_StationsKeepCollidersAndPositionAndShowCatalogArt()
        {
            Scene scene = LayoutScene.Open();
            try
            {
                var catalog = AssetDatabase.LoadAssetAtPath<ArtCatalog>(CatalogPath);
                // Spec 027a: the shop floor plan; spec 028b: the dry shelf and the fridge where the last two 027a crates stood
                // (x 2.36 and 3.60, 1.8 m high, 0.5 and 0.62 m deep, their backs on the back line's back at z 14.9), then the two
                // stoves, the serving counter (in the front counter at z 11.85, 0.7 m deep, opposite the plate shelf) and the plate
                // shelf; the back line at z 14.3.
                // Spec 029: the fryer (index 6, 0.8 m wide) after the prep run moved the stoves, the counter and the shelf 0.84 m right.
                // Spec 030: the griddle (index 7, 1.02 m wide) between the prep run (now one board wide) and the fryer; nothing else moved.
                // Spec 031: the assembly board (index 8, 0.9 m wide) after the plating worktop (now 0.74 m) moved the plate shelf and the
                // counter 0.42 m right.
                // Spec 032: the register (index 9) on the front counter, 1.15 m right of the serving counter, on its top.
                // Spec 033: the drink dispenser (index 10) on the front counter, 1.15 m left of the serving counter, on its top.
                // Spec 034: the ordering terminal (index 11) on the back wall over the landing worktop (x 1.11), hanging at 1.13 to 1.59 m.
                // Spec 035: the dish return (index 12) on the front counter right of the register (x 13.79), the sink (13) after the plate
                // shelf (x 12.88, where the bowl's worktop stood), the trash can (14) at the front counter's west end by the staff gap
                // (x 0.76) and the bin (15) outside the staff gap (x -2.0, z 13.4).
                // Spec 036: the seitan spit (16) on the front counter opposite the heat block, between the stoves (x 8.10), its body 0.1 m
                // towards the street (z 11.75) so the catch tray and the knob stand on the counter in front of it.
                // Spec 037: the menu board (17) hangs above the front counter between the serving counter and the register (x 12.215),
                // over the counter's cook half (z 12.10), 1.85 to 2.35 m high.
                float[] fixedX = { 2.36f, 3.60f, 7.48f, 8.72f, 11.64f, 11.64f, 6.44f, 5.49f, 10.55f, 12.79f, 10.49f, 1.11f, // spec 029b: stove B and the plates moved left
                    13.79f, 12.88f, 0.76f, -2.0f, 8.10f, 12.215f };
                StationKind[] kinds = { StationKind.Shelf, StationKind.Fridge, StationKind.Stove, StationKind.Stove, StationKind.Counter, StationKind.PlateShelf,
                    StationKind.Fryer, StationKind.Griddle, StationKind.Assembly, StationKind.Register, StationKind.DrinkDispenser, StationKind.Terminal,
                    StationKind.DishReturn, StationKind.Sink, StationKind.TrashCan, StationKind.Bin, StationKind.Spit, StationKind.MenuBoard };
                int count = 0;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Station station in root.GetComponentsInChildren<Station>(true))
                    {
                        count++;
                        int index = station.Id - 1; // spec 062a: the default layout's station ids are the old indices + 1
                        Assert.AreEqual(kinds[index], station.Kind, station.name);
                        bool stock = StockLayout.IsStock(station.Kind);
                        float x = fixedX[index];
                        // Spec 026: the body is as high as the furniture (0.90 m, the counter 1.05 m; was 1.0 m); spec 028b: the stock 1.80 m.
                        bool register = station.Kind == StationKind.Register; // spec 032: it stands on the front counter
                        bool drinks = station.Kind == StationKind.DrinkDispenser; // spec 033: so does the drink dispenser
                        bool terminal = station.Kind == StationKind.Terminal; // spec 034: it hangs on the back wall
                        bool board = station.Kind == StationKind.MenuBoard; // spec 037: it hangs above the front counter
                        bool cleaning = Worktown.Game.Shop.CleaningRules.IsCleaningStation(station.Kind); // spec 035
                        bool spit = station.Kind == StationKind.Spit; // spec 036: on the front counter
                        Vector3 clean = spit ? new Vector3(SpitArtRules.Width, SpitArtRules.Height, SpitArtRules.Depth)
                            : CleaningArtRules.SizeOf(station.Kind);
                        cleaning |= spit; // the spit's box is its own size (above), like the cleaning stations'
                        float height = station.Kind == StationKind.Counter ? ArtSizeTable.ServingCounterHeight
                            : register ? RegisterArtRules.Height : drinks ? DrinkArtRules.Height : terminal ? TerminalArtRules.Height
                            : board ? MenuBoardArtRules.Height : cleaning ? clean.y : stock ? ArtSizeTable.StockHeight : ArtSizeTable.WorktopHeight;
                        float depth = station.Kind == StationKind.Counter ? 0.7f : register ? RegisterArtRules.Depth : drinks ? DrinkArtRules.Depth
                            : terminal ? TerminalArtRules.Depth : board ? MenuBoardArtRules.Depth : cleaning ? clean.z : stock ? StockArt.DepthOf(station.Kind) : 1.2f;
                        float z = board ? 12.10f : spit ? 11.75f : station.Kind == StationKind.Counter || register || drinks || station.Kind == StationKind.DishReturn
                            || station.Kind == StationKind.TrashCan ? 11.85f : station.Kind == StationKind.Bin ? 13.4f : terminal ? 14.92f - depth * 0.5f
                            : stock ? 14.9f - depth * 0.5f : 14.3f;
                        bool fryer = station.Kind == StationKind.Fryer;
                        float width = fryer ? FryerArt.Width : station.Kind == StationKind.Griddle ? GriddleArtRules.Width
                            : station.Kind == StationKind.Assembly ? AssemblyArtRules.Width : register ? RegisterArtRules.Width
                            : drinks ? DrinkArtRules.Width : terminal ? TerminalArtRules.Width : board ? MenuBoardArtRules.Width : cleaning ? clean.x : 1.2f;
                        // Spec 029: the fryer's collider stops under its oil wells (the aim finds a lowered basket).
                        float colliderHeight = fryer ? FryerArt.BodyColliderTop : height;
                        float bottom = register || drinks || spit ? ArtSizeTable.ServingCounterHeight
                            : terminal ? TerminalArtRules.CentreHeight - TerminalArtRules.Height * 0.5f
                            : board ? MenuBoardArtRules.CentreHeight - MenuBoardArtRules.Height * 0.5f
                            : cleaning ? CleaningArtRules.BottomOf(station.Kind) : 0f;
                        var expected = new Vector3(x, bottom + colliderHeight * 0.5f, z);
                        Transform body = station.transform.Find("Body");
                        Assert.IsNotNull(body, station.name + " lost its Body");
                        Assert.AreEqual(Vector3.zero, body.localPosition, station.name);
                        var box = body.GetComponent<BoxCollider>();
                        Assert.IsNotNull(box, station.name + " lost its BoxCollider");
                        Assert.AreEqual(fryer ? new Vector3(1f, colliderHeight / height, 1f) : Vector3.one, box.size);
                        Assert.AreEqual(new Vector3(width, height, depth), body.localScale); // spec 027a: the counter is 0.7 m deep
                        Vector3 colliderCenter = box.bounds.center;
                        Assert.AreEqual(0f, Vector3.Distance(expected, colliderCenter), 0.001f, station.name + " moved");
                        Assert.IsNull(body.GetComponent<MeshRenderer>(), station.name + " still shows the cube");

                        Transform visual = station.transform.Find("Art_" + ArtCatalogRules.StationKey(station.Kind));
                        Assert.IsNotNull(visual, station.name + " has no catalog visual");
                        Assert.IsTrue(catalog.TryGet(ArtCatalogRules.StationKey(station.Kind), out GameObject expectedPrefab));
                        Assert.AreSame(expectedPrefab, PrefabUtility.GetCorrespondingObjectFromSource(visual.gameObject),
                            station.name + " uses the wrong wrapper");
                        // Spec 028b: the fridge's open doors stand out of its body; its cabinet is what fills the body.
                        Transform measured = stock ? visual.Find("Model/Cabinet") : visual;
                        Assert.IsNotNull(measured, station.name + " has no cabinet");
                        var renderers = measured.GetComponentsInChildren<Renderer>();
                        Assert.Greater(renderers.Length, 0);
                        Bounds b = renderers[0].bounds;
                        foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
                        Assert.AreEqual(colliderCenter.x, b.center.x, 0.05f, station.name + " visual off centre in X");
                        Assert.AreEqual(colliderCenter.z, b.center.z, 0.05f, station.name + " visual off centre in Z");
                        if (stock) Assert.AreEqual(box.bounds.size.y, b.size.y, 0.01f, station.name + " cabinet as high as its body");
                    }
                }
                Assert.AreEqual(18, count, "the twelve before, (spec 035) the dish return, the sink, the trash can and the bin, (spec 036) the spit, (spec 037) the menu board");
            }
            finally { LayoutScene.Close(scene); }
        }

        // AC7
        [Test]
        public void WrapperPrefabs_HaveNoCollidersOrScripts()
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { WrapperFolder });
            Assert.IsNotEmpty(guids);
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Assert.IsEmpty(root.GetComponentsInChildren<Collider>(true), path + " has a collider");
                    Assert.IsEmpty(root.GetComponentsInChildren<MonoBehaviour>(true), path + " has a script");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        }
    }
}
