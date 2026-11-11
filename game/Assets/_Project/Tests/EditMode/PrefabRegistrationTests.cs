using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Worktown.Game.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// 030 review: every prefab the KitchenService spawns or respawns at runtime (the food, the knife, the spatula, each piece of
    /// cookware, and anything a later spec adds as a field) is registered with FishNet in DefaultPrefabObjects, so a spawn never
    /// fails on a client that does not know the prefab. Reads every object field of the scene's KitchenService, so a new prefab
    /// field is covered without changing this test.
    /// </summary>
    public class PrefabRegistrationTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/Graybox.unity";
        private const string DefaultPrefabsPath = "Assets/DefaultPrefabObjects.asset";

        [Test]
        public void EveryPrefabTheKitchenSpawns_IsRegisteredWithFishNet()
        {
            var registered = new HashSet<string>();
            Object asset = AssetDatabase.LoadMainAssetAtPath(DefaultPrefabsPath);
            Assert.IsNotNull(asset, DefaultPrefabsPath);
            SerializedProperty list = new SerializedObject(asset).FindProperty("_prefabs");
            Assert.IsNotNull(list, "DefaultPrefabObjects._prefabs");
            for (int i = 0; i < list.arraySize; i++)
            {
                Object prefab = list.GetArrayElementAtIndex(i).objectReferenceValue;
                if (prefab != null) registered.Add(AssetDatabase.GetAssetPath(prefab));
            }

            Scene scene = LayoutScene.Open();
            try
            {
                KitchenService kitchen = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                    if (kitchen == null) kitchen = root.GetComponentInChildren<KitchenService>(true);
                Assert.IsNotNull(kitchen, "no KitchenService in the scene");

                var spawned = new List<string>();
                SerializedProperty p = new SerializedObject(kitchen).GetIterator();
                while (p.Next(true))
                {
                    if (p.propertyType != SerializedPropertyType.ObjectReference || p.objectReferenceValue == null) continue;
                    string path = AssetDatabase.GetAssetPath(p.objectReferenceValue);
                    if (!path.EndsWith(".prefab")) continue; // scene objects (stations, services) are not spawned
                    spawned.Add(p.propertyPath + " " + path);
                    Assert.IsTrue(registered.Contains(path), p.propertyPath + " (" + path + ") is not in " + DefaultPrefabsPath);
                }
                TestContext.WriteLine(string.Join("\n", spawned));
                Assert.GreaterOrEqual(spawned.Count, 4, "at least the food, the knife, the spatula and the cookware");
                Assert.IsTrue(spawned.Exists(s => s.StartsWith("_foodPrefab")), "the food"); // 031b (review of 031): by name too
                Assert.IsTrue(spawned.Exists(s => s.StartsWith("_knifePrefab")), "the knife");
                Assert.IsTrue(spawned.Exists(s => s.StartsWith("_spatulaPrefab")), "the spatula");
                Assert.IsTrue(spawned.Exists(s => s.StartsWith("_vesselPrefabs")), "the cookware");

                // Spec 034: the supply service spawns the delivery boxes; every prefab it holds is registered too.
                Worktown.Game.Shop.SupplyService supply = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                    if (supply == null) supply = root.GetComponentInChildren<Worktown.Game.Shop.SupplyService>(true);
                Assert.IsNotNull(supply, "no SupplyService in the scene");
                var supplied = new List<string>();
                SerializedProperty q = new SerializedObject(supply).GetIterator();
                while (q.Next(true))
                {
                    if (q.propertyType != SerializedPropertyType.ObjectReference || q.objectReferenceValue == null) continue;
                    string path = AssetDatabase.GetAssetPath(q.objectReferenceValue);
                    if (!path.EndsWith(".prefab")) continue;
                    supplied.Add(q.propertyPath + " " + path);
                    Assert.IsTrue(registered.Contains(path), q.propertyPath + " (" + path + ") is not in " + DefaultPrefabsPath);
                }
                TestContext.WriteLine(string.Join("\n", supplied));
                Assert.IsTrue(supplied.Exists(s => s.StartsWith("_boxPrefab")), "the delivery box");
                Assert.IsNotNull(kitchen.GetType().GetField("_supply", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .GetValue(kitchen), "the kitchen draws from the supply's stock");
                Assert.IsTrue(spawned.Exists(s => s.StartsWith("_clothPrefab")), "spec 035: the wipe cloth (respawned at home)");

                // Spec 035: the cleaning service spawns the dirty dishes and the trash bags; every prefab it holds is registered too.
                Worktown.Game.Shop.CleaningService cleaning = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                    if (cleaning == null) cleaning = root.GetComponentInChildren<Worktown.Game.Shop.CleaningService>(true);
                Assert.IsNotNull(cleaning, "no CleaningService in the scene");
                var cleaned = new List<string>();
                SerializedProperty c = new SerializedObject(cleaning).GetIterator();
                while (c.Next(true))
                {
                    if (c.propertyType != SerializedPropertyType.ObjectReference || c.objectReferenceValue == null) continue;
                    string path = AssetDatabase.GetAssetPath(c.objectReferenceValue);
                    if (!path.EndsWith(".prefab")) continue;
                    cleaned.Add(c.propertyPath + " " + path);
                    Assert.IsTrue(registered.Contains(path), c.propertyPath + " (" + path + ") is not in " + DefaultPrefabsPath);
                }
                TestContext.WriteLine(string.Join("\n", cleaned));
                Assert.IsTrue(cleaned.Exists(s => s.StartsWith("_dirtyDishPrefab")), "the dirty dish");
                Assert.IsTrue(cleaned.Exists(s => s.StartsWith("_trashBagPrefab")), "the trash bag");
                Assert.IsNotNull(kitchen.GetType().GetField("_cleaning", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .GetValue(kitchen), "the kitchen's plates are counted by the cleaning service");
            }
            finally { LayoutScene.Close(scene); }
        }
    }
}
