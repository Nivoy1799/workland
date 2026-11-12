using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Worktown.Game.Layout;
using Worktown.Sim.Layout;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 062a: the built scene as a game sees it after the server spawned the kitchen. Opens Graybox.unity (additively) and instantiates
    /// the default layout offline into it (LayoutSpawner.InstantiateOffline: the pieces configured and registered, the items on their
    /// homes), so every built-scene test finds the stations and surfaces where a game has them. <see cref="Close"/> closes the scene (the
    /// pieces with it) and clears the LayoutRegistry.
    /// </summary>
    public static class LayoutScene
    {
        public const string ScenePath = "Assets/_Project/Scenes/Graybox.unity";
        public const string PrefabsPath = "Assets/_Project/Prefabs/Pieces/PiecePrefabs.asset";

        public static PiecePrefabs Prefabs
        {
            get
            {
                var prefabs = AssetDatabase.LoadAssetAtPath<PiecePrefabs>(PrefabsPath);
                Assert.IsNotNull(prefabs, "the piece prefabs (" + PrefabsPath + "): run Worktown/Build Graybox Scene");
                return prefabs;
            }
        }

        /// <summary>Opens the scene with the default layout spawned offline.</summary>
        public static Scene Open() => Open(DefaultLayout.Load());

        /// <summary>Opens the scene with <paramref name="layout"/> spawned offline.</summary>
        public static Scene Open(ShopLayout layout)
        {
            LayoutRegistry.Clear();
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            List<GameObject> made = LayoutSpawner.InstantiateOffline(layout, Prefabs, scene);
            Assert.Greater(made.Count, 0, "the layout spawned nothing");
            return scene;
        }

        public static void Close(Scene scene)
        {
            if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
            LayoutRegistry.Clear();
        }
    }
}
