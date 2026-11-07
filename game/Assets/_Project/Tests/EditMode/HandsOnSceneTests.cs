using System.Collections.Generic;
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
    /// <summary>
    /// Spec 019: the generated scene has a knife next to every board (a normal pickup with the Knife marker and its
    /// model), the stove buttons on the pan and the pot, and the Food Kit models in the catalog. Network types
    /// are looked up with typeof, so this test assembly needs no FishNet reference.
    /// </summary>
    public class HandsOnSceneTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/Graybox.unity";
        private const string CatalogPath = "Assets/_Project/Art/ArtCatalog.asset";
        private const string KnifePrefabPath = "Assets/_Project/Prefabs/Knife.prefab";
        private static readonly System.Type PickupType = typeof(StationRules).Assembly.GetType("Worktown.Game.Net.Pickup");

        private static List<T> All<T>(Scene scene) where T : Component
        {
            var found = new List<T>();
            foreach (GameObject root in scene.GetRootGameObjects()) found.AddRange(root.GetComponentsInChildren<T>(true));
            return found;
        }

        [Test]
        public void Catalog_HasTheCuttingBoardAndTheKnife()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ArtCatalog>(CatalogPath);
            Assert.IsNotNull(catalog);
            foreach (ArtKey key in new[] { ArtKey.ToolCuttingBoard, ArtKey.ToolKnife })
            {
                Assert.IsTrue(catalog.TryGet(key, out GameObject prefab) && prefab != null, "no model for " + key);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                try
                {
                    Assert.IsEmpty(instance.GetComponentsInChildren<Collider>(true), key + " has a collider");
                    Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
                    Assert.IsNotEmpty(renderers);
                    Bounds b = renderers[0].bounds;
                    foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
                    Assert.LessOrEqual(Mathf.Max(b.size.x, b.size.y, b.size.z), 0.501f, key + " is too big");
                    if (key == ArtKey.ToolKnife) Assert.GreaterOrEqual(b.size.z, b.size.x, "the knife lies along Z");
                }
                finally { Object.DestroyImmediate(instance); }
            }
        }

        [Test]
        public void KnifePrefab_IsAPickupWithTheMarkerAndAModel()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(KnifePrefabPath);
            Assert.IsNotNull(prefab, "Knife.prefab missing; run Worktown/Build Graybox Scene");
            Assert.IsNotNull(prefab.GetComponent<Knife>());
            Assert.IsNotNull(prefab.GetComponent(PickupType));
            Assert.IsNotNull(prefab.GetComponent<BoxCollider>());
            Assert.IsNull(prefab.GetComponent(typeof(Food)), "the knife is no food: stations refuse it as NotFood");
            Assert.IsNotEmpty(prefab.transform.Find("Visual").GetComponentsInChildren<Renderer>(), "no knife model");
        }

        [Test]
        public void BuiltScene_EveryBoardHasOneKnifeInReach_AndTheServiceCanReplaceIt()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                List<Knife> knives = All<Knife>(scene);
                int boards = 0;
                foreach (Station station in All<Station>(scene))
                {
                    if (station.Kind != StationKind.Board) continue;
                    boards++;
                    Knife knife = knives.Find(k => k.BoardIndex == station.Index);
                    Assert.IsNotNull(knife, station.name + " has no knife");
                    Assert.IsNotNull(knife.GetComponent(PickupType));
                    Assert.Less(Vector3.Distance(knife.transform.position, station.transform.position), 1f, "next to the board");
                    Assert.Greater(knife.transform.position.y, station.transform.position.y, "on top of the board station");
                    Assert.IsNotNull(station.GetComponent(typeof(StationContents)), "the board shows its food");
                }
                Assert.AreEqual(1, boards);
                Assert.AreEqual(boards, knives.Count, "never more knives than boards");

                KitchenService service = All<KitchenService>(scene)[0];
                Assert.IsNotNull(new SerializedObject(service).FindProperty("_knifePrefab").objectReferenceValue, "no knife prefab to respawn");
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void BuiltScene_StovesHaveButtonsInFrontOfThem()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var options = new Dictionary<StationKind, List<int>>();
                foreach (StationButton button in All<StationButton>(scene))
                {
                    Station station = button.Station;
                    Assert.IsNotNull(station, button.name + " has no station");
                    Assert.IsTrue(button.transform.IsChildOf(station.transform));
                    Assert.IsTrue(StationRules.IsHeat(station.Kind), button.name + " on " + station.Kind);
                    var box = button.GetComponent<BoxCollider>();
                    Assert.IsNotNull(box, button.name + " has no collider to aim at");
                    Bounds body = station.transform.Find("Body").GetComponent<BoxCollider>().bounds;
                    Assert.Less(box.bounds.max.z, body.min.z + 0.001f, button.name + " must sit in front of the station body");
                    if (!options.TryGetValue(station.Kind, out List<int> list)) options[station.Kind] = list = new List<int>();
                    list.Add(button.Option);
                    Assert.IsNotNull(station.GetComponent<StoveButtonVisual>(), station.name + " has no button lights");
                }
                CollectionAssert.AreEquivalent(new[] { StationRules.StoveSwitch, StationRules.PanMode }, options[StationKind.Pan]);
                CollectionAssert.AreEquivalent(new[] { StationRules.StoveSwitch }, options[StationKind.Pot]);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
