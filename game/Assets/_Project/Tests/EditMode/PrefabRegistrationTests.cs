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

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
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
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
