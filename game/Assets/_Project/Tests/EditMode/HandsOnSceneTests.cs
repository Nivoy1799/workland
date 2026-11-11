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
    /// Spec 019/021: the generated scene has a knife next to the cutting board (normal pickups; the knife with the Knife marker and its
    /// model), a knob and a button per burner on the stoves, and the Food Kit models in the catalog. Network types
    /// are looked up with typeof, so it does not depend on FishNet's types.
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
        public void BuiltScene_OneKnifeNextToTheCuttingBoard_AndTheServiceCanReplaceThem()
        {
            Scene scene = LayoutScene.Open();
            try
            {
                List<Knife> knives = All<Knife>(scene);
                List<Component> boards = VesselTestKit.AllOf(scene, VesselTestKit.VesselType).FindAll(v => VesselTestKit.KindOf(v) == VesselKind.Board);
                Assert.AreEqual(1, boards.Count, "spec 021: one cutting board, lying on a worktop");
                Assert.AreEqual(1, knives.Count, "one knife");
                Knife knife = knives[0];
                Assert.IsNotNull(knife.GetComponent(PickupType));
                Assert.Less(Vector3.Distance(knife.transform.position, boards[0].transform.position), 1f, "next to the board");
                Assert.IsNotNull(boards[0].GetComponent(PickupType), "the board is carried like the knife");

                KitchenService service = All<KitchenService>(scene)[0];
                var so = new SerializedObject(service);
                Assert.IsNotNull(so.FindProperty("_knifePrefab").objectReferenceValue, "no knife prefab to respawn");
                SerializedProperty prefabs = so.FindProperty("_vesselPrefabs");
                var kinds = new List<VesselKind>();
                for (int i = 0; i < prefabs.arraySize; i++)
                {
                    var prefab = prefabs.GetArrayElementAtIndex(i).objectReferenceValue as Component;
                    Assert.IsNotNull(prefab, "cookware prefab " + i);
                    kinds.Add(VesselTestKit.KindOf(VesselTestKit.VesselOn(prefab.gameObject)));
                }
                CollectionAssert.AreEquivalent(new[] { VesselKind.Board, VesselKind.Pan, VesselKind.Pot, VesselKind.Plate, VesselKind.Bowl }, kinds);
            }
            finally { LayoutScene.Close(scene); }
        }

        [Test]
        public void BuiltScene_StovesHaveAKnobAndAButtonPerBurnerInFrontOfThem()
        {
            Scene scene = LayoutScene.Open();
            try
            {
                var options = new Dictionary<int, List<int>>();
                foreach (StationButton button in All<StationButton>(scene))
                {
                    Station station = button.Station;
                    Assert.IsNotNull(station, button.name + " has no station");
                    Assert.IsTrue(button.transform.IsChildOf(station.transform));
                    if (station.Kind == StationKind.Fryer || station.Kind == StationKind.Griddle || station.Kind == StationKind.Spit)
                        continue; // spec 029/030/036: their knobs (FloorPlanTests)
                    Assert.AreEqual(StationKind.Stove, station.Kind, button.name + " on " + station.Kind);
                    var box = button.GetComponent<BoxCollider>();
                    Assert.IsNotNull(box, button.name + " has no collider to aim at");
                    Bounds body = station.transform.Find("Body").GetComponent<BoxCollider>().bounds;
                    Assert.Less(box.bounds.max.z, body.min.z + 0.001f, button.name + " must sit in front of the station body");
                    if (!options.TryGetValue(station.Id, out List<int> list)) options[station.Id] = list = new List<int>();
                    list.Add(button.Option);
                    Assert.IsNotNull(station.GetComponent<StoveButtonVisual>(), station.name + " has no button lights");
                    Assert.IsNotNull(station.GetComponent(typeof(StationContents)), station.name + " has no synced flags");
                }
                Assert.AreEqual(2, options.Count, "two stoves");
                foreach (List<int> list in options.Values)
                    CollectionAssert.AreEquivalent(new[]
                    {
                        StationRules.StoveOption(0, StationRules.StoveSwitch), StationRules.StoveOption(0, StationRules.PanMode),
                        StationRules.StoveOption(1, StationRules.StoveSwitch), StationRules.StoveOption(1, StationRules.PanMode)
                    }, list);
            }
            finally { LayoutScene.Close(scene); }
        }
    }
}
