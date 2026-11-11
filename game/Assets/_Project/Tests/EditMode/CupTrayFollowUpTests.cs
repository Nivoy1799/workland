using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Worktown.Game.Art;
using Worktown.Game.Cooking;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;
using Object = UnityEngine.Object;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// 042 follow-ups (2026-11-11): a cup at rest on a tap's spot stands on the drip tray (not in it), and a drawn level eases towards
    /// the level to show instead of snapping (another player's cup, a round trip late).
    /// </summary>
    public class CupTrayFollowUpTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/Graybox.unity";
        private const string CatalogPath = "Assets/_Project/Art/ArtCatalog.asset";
        private const string FoodPrefabPath = "Assets/_Project/Prefabs/Food.prefab";

        /// <summary>
        /// The whole chain a cup at rest is drawn by: the server's rest pose on the spot (PickupService.PlanPlace with the Food prefab's
        /// collider bottom), the Food's Visual child, and the cup model's lowest point. Its bottom is the drip tray's top (the model's
        /// "Drip tray" box), within 3 mm, under every tap.
        /// </summary>
        [Test]
        public void BuiltScene_ACupAtRestOnItsSpot_StandsOnTheDripTray()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ArtCatalog>(CatalogPath);
            Assert.IsNotNull(catalog, CatalogPath);
            Assert.IsTrue(catalog.TryGet(ArtKey.PropCup, out GameObject cupPrefab), "the cup model");
            float cupBottom = LowestPoint(cupPrefab);
            var food = AssetDatabase.LoadAssetAtPath<GameObject>(FoodPrefabPath);
            Assert.IsNotNull(food, FoodPrefabPath);
            var box = food.GetComponent<BoxCollider>();
            Assert.IsNotNull(box, "the Food's collider (Pickup.BottomOffset reads it)");
            float bottomOffset = (box.size.y * 0.5f - box.center.y) * food.transform.localScale.y;
            Transform visual = food.transform.Find("Visual");
            Assert.IsNotNull(visual, "the Food's Visual child");
            float visualY = visual.localPosition.y * food.transform.localScale.y;

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                int checkedSpots = 0;
                foreach (PlaceSurface surface in All<PlaceSurface>(scene))
                {
                    if (!surface.IsCupSpot) continue;
                    Station dispenser = surface.GetComponentInParent<Station>();
                    Transform tray = Find(dispenser.transform, "Drip tray");
                    Assert.IsNotNull(tray, "the dispenser model's drip tray");
                    float trayTop = tray.GetComponent<Renderer>().bounds.max.y;
                    Vector3 player = dispenser.transform.position + dispenser.transform.rotation * new Vector3(0f, 0f, -0.9f);
                    player.y = 0f;
                    Assert.AreEqual(PickupOutcome.Ok, PickupService.PlanPlace(new PlaceSpots(), true, surface.Index, surface, PlaceTurn.Pack(0, 0),
                        player, false, Quaternion.identity, bottomOffset, null, out _, out Pose rest), surface.name);
                    float drawnBottom = rest.position.y + visualY + cupBottom;
                    TestContext.WriteLine(surface.name + ": tray top " + trayTop.ToString("F4") + ", spot " + surface.SpotWorldPosition(0).y.ToString("F4")
                        + ", rest " + rest.position.y.ToString("F4") + ", cup bottom " + drawnBottom.ToString("F4") + " (offset " + bottomOffset.ToString("F4")
                        + ", visual " + visualY.ToString("F4") + ", model " + cupBottom.ToString("F4") + ")");
                    Assert.AreEqual(trayTop, drawnBottom, 0.003f, surface.name + ": the cup's bottom on the tray (tray top " + trayTop.ToString("F3")
                        + ", cup bottom " + drawnBottom.ToString("F3") + ")");
                    checkedSpots++;
                }
                Assert.AreEqual(DrinkRules.TapCount, checkedSpots);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void EaseFill_GlidesToTheBucket_AndKeepsUpWithAPour()
        {
            Assert.AreEqual(0.55f, DrinkArtRules.EaseFill(0.55f, 0.9f, 0f), 1e-6f, "no time: no move");
            Assert.AreEqual(0.55f + DrinkArtRules.FillEaseSpeed * 0.05f, DrinkArtRules.EaseFill(0.55f, 0.9f, 0.05f), 1e-5f, "a step towards it");
            Assert.AreEqual(0.9f, DrinkArtRules.EaseFill(0.85f, 0.9f, 0.05f), 1e-6f, "never past it");
            Assert.AreEqual(0.4f, DrinkArtRules.EaseFill(0.5f, 0.4f, 0.1f), 1e-6f, "down as well (a refused pour)");
            Assert.AreEqual(0.7f, DrinkArtRules.EaseFill(float.NaN, 0.7f, 0.1f), 1e-6f);

            // A jump from a low cup (drawn 0.55) to a right one takes well under a quarter second at 60 fps.
            float shown = DrinkRules.DrawnFill(DrinkRules.LevelLow), target = DrinkRules.DrawnFill(DrinkRules.LevelRight);
            int frames = 0;
            while (shown != target && frames < 600)
            {
                shown = DrinkArtRules.EaseFill(shown, target, 1f / 60f);
                frames++;
            }
            Assert.Greater(frames, 3, "not a snap");
            Assert.Less(frames / 60f, 0.25f, "and not slow");

            // A pour (200 ml/s into 400 ml) never runs ahead of the drawn level by more than one frame's step.
            float pourPerSecond = Worktown.Sim.Cooking.Drinks.PourRate / Worktown.Sim.Cooking.Drinks.CupMl;
            Assert.Less(pourPerSecond, DrinkArtRules.FillEaseSpeed, "the ease is faster than a pour");
            shown = 0f;
            float poured = 0f;
            for (int i = 0; i < 60; i++)
            {
                poured += pourPerSecond / 60f;
                shown = DrinkArtRules.EaseFill(shown, poured, 1f / 60f);
                Assert.AreEqual(poured, shown, 1e-5f, "frame " + i);
            }
        }

        private static float LowestPoint(GameObject prefab)
        {
            GameObject copy = Object.Instantiate(prefab);
            try
            {
                copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                bool any = false;
                float min = 0f;
                foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
                {
                    float y = r.bounds.min.y;
                    if (!any || y < min) min = y;
                    any = true;
                }
                Assert.IsTrue(any, prefab.name + " has renderers");
                return min;
            }
            finally { Object.DestroyImmediate(copy); }
        }

        private static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = Find(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        private static List<T> All<T>(Scene scene) where T : Component
        {
            var found = new List<T>();
            foreach (GameObject root in scene.GetRootGameObjects()) found.AddRange(root.GetComponentsInChildren<T>(true));
            return found;
        }
    }
}
