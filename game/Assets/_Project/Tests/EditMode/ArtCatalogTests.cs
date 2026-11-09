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
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var catalog = AssetDatabase.LoadAssetAtPath<ArtCatalog>(CatalogPath);
                // Spec 027a: the shop floor plan. Crates 1.24 m apart from x -0.12, then the two stoves, the serving counter (in
                // the front counter at z 11.85, 0.7 m deep, opposite the plate shelf) and the plate shelf; the back line at z 14.3.
                float[] fixedX = { 6.64f, 9.14f, 11.64f, 11.64f };
                int crates = DemoIngredients.All.Count;
                int count = 0;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Station station in root.GetComponentsInChildren<Station>(true))
                    {
                        count++;
                        float x = station.Index < crates ? -0.12f + station.Index * 1.24f : fixedX[station.Index - crates];
                        // Spec 026: the body is as high as the furniture (0.90 m, the counter 1.05 m; was 1.0 m).
                        float height = station.Kind == StationKind.Counter ? ArtSizeTable.ServingCounterHeight : ArtSizeTable.WorktopHeight;
                        var expected = new Vector3(x, height * 0.5f, station.Kind == StationKind.Counter ? 11.85f : 14.3f);
                        Transform body = station.transform.Find("Body");
                        Assert.IsNotNull(body, station.name + " lost its Body");
                        Assert.AreEqual(Vector3.zero, body.localPosition, station.name);
                        var box = body.GetComponent<BoxCollider>();
                        Assert.IsNotNull(box, station.name + " lost its BoxCollider");
                        Assert.AreEqual(Vector3.one, box.size);
                        Assert.AreEqual(new Vector3(1.2f, height, station.Kind == StationKind.Counter ? 0.7f : 1.2f), body.localScale); // spec 027a: the counter is 0.7 m deep
                        Vector3 colliderCenter = box.bounds.center;
                        Assert.AreEqual(0f, Vector3.Distance(expected, colliderCenter), 0.001f, station.name + " moved");
                        Assert.IsNull(body.GetComponent<MeshRenderer>(), station.name + " still shows the cube");

                        Transform visual = station.transform.Find("Art_" + ArtCatalogRules.StationKey(station.Kind));
                        Assert.IsNotNull(visual, station.name + " has no catalog visual");
                        Assert.IsTrue(catalog.TryGet(ArtCatalogRules.StationKey(station.Kind), out GameObject expectedPrefab));
                        Assert.AreSame(expectedPrefab, PrefabUtility.GetCorrespondingObjectFromSource(visual.gameObject),
                            station.name + " uses the wrong wrapper");
                        var renderers = visual.GetComponentsInChildren<Renderer>();
                        Assert.Greater(renderers.Length, 0);
                        Bounds b = renderers[0].bounds;
                        foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
                        Assert.AreEqual(colliderCenter.x, b.center.x, 0.05f, station.name + " visual off centre in X");
                        Assert.AreEqual(colliderCenter.z, b.center.z, 0.05f, station.name + " visual off centre in Z");
                    }
                }
                Assert.AreEqual(DemoIngredients.All.Count + 4, count, "crates plus two stoves, the counter and the plate shelf");
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
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
