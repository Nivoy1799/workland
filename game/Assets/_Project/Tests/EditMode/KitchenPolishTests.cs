using System;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Worktown.Game.Art;
using Worktown.Game.Audio;
using Worktown.Game.Net;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 023: generated sounds, burner test, worktop and stove art, steam on the cookware.</summary>
    public class KitchenPolishTests
    {
        private const string CatalogPath = "Assets/_Project/Art/ArtCatalog.asset";
        private const string ScenePath = "Assets/_Project/Scenes/Graybox.unity";
        private const string FurnitureFolder = "Assets/ThirdParty/Kenney/FurnitureKit/";

        // AC2 (changed by the orchestrator: sounds are generated, no files, so no licence row)
        [Test]
        public void EverySoundKey_HasAClip()
        {
            var catalog = new SoundCatalog();
            foreach (SoundKey key in Enum.GetValues(typeof(SoundKey)))
            {
                Assert.IsTrue(catalog.TryGet(key, out AudioClip clip), key.ToString());
                Assert.IsNotNull(clip, key.ToString());
                Assert.Greater(clip.length, 0.03f, key.ToString());
                Assert.AreEqual(1, clip.channels);
                UnityEngine.Object.DestroyImmediate(clip);
            }
            Assert.IsFalse(catalog.TryGet((SoundKey)200, out _));
        }

        [Test]
        public void Samples_AreDeterministic_ModestAndNotSilent()
        {
            foreach (SoundKey key in Enum.GetValues(typeof(SoundKey)))
            {
                float[] a = ProceduralSounds.Samples(key);
                float[] b = ProceduralSounds.Samples(key);
                CollectionAssert.AreEqual(a, b, key + " must not change between runs");
                float peak = 0f;
                foreach (float v in a) peak = Mathf.Max(peak, Mathf.Abs(v));
                Assert.Greater(peak, 0.05f, key + " is silent");
                Assert.LessOrEqual(peak, 0.8f, key + " is too loud");
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => ProceduralSounds.Samples((SoundKey)200));
        }

        [Test]
        public void Loops_AreSizzleAndBubble_AndOneShotsAreNot()
        {
            Assert.IsTrue(ProceduralSounds.IsLoop(SoundKey.Sizzle));
            Assert.IsTrue(ProceduralSounds.IsLoop(SoundKey.Bubble));
            Assert.IsFalse(ProceduralSounds.IsLoop(SoundKey.Chop));
            Assert.IsFalse(ProceduralSounds.IsLoop(SoundKey.Tick));
        }

        [Test]
        public void BubbleLoop_EndsQuiet_SoTheLoopHasNoClick()
        {
            float[] s = ProceduralSounds.Bubble();
            for (int i = s.Length - 100; i < s.Length; i++) Assert.AreEqual(0f, s[i], 0.0001f);
        }

        [Test]
        public void SoundCatalog_GainsAreModest()
        {
            Assert.LessOrEqual(SoundCatalog.MasterVolume, 0.6f);
            foreach (SoundKey key in Enum.GetValues(typeof(SoundKey)))
                Assert.That(SoundCatalog.GainOf(key), Is.InRange(0.1f, 1f));
        }

        [Test]
        public void BurnerRules_IsOver_UsesFootprintMarginAndHeight()
        {
            var size = new Vector2(0.42f, 0.42f);
            Assert.IsTrue(BurnerRules.IsOver(new Vector3(0f, 0.9f, 0f), size, 0.9f));
            Assert.IsTrue(BurnerRules.IsOver(new Vector3(0.25f, 0.9f, -0.25f), size, 0.9f), "within the margin");
            Assert.IsFalse(BurnerRules.IsOver(new Vector3(0.4f, 0.9f, 0f), size, 0.9f), "beside the burner");
            Assert.IsFalse(BurnerRules.IsOver(new Vector3(0f, 1.3f, 0f), size, 0.9f), "held up in the air");
        }

        // AC3
        [Test]
        public void Catalog_MapsWorktopAndStoveToFurnitureKitModels()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ArtCatalog>(CatalogPath);
            foreach (ArtKey key in new[] { ArtKey.Worktop, ArtKey.StationPan })
            {
                Assert.IsTrue(catalog.TryGet(key, out GameObject wrapper), key.ToString());
                Assert.IsEmpty(wrapper.GetComponentsInChildren<Collider>(true), key + " has a collider");
                Assert.IsEmpty(wrapper.GetComponentsInChildren<MonoBehaviour>(true), key + " has a script");
                GameObject model = PrefabUtility.GetCorrespondingObjectFromSource(wrapper.transform.Find("Model").gameObject);
                StringAssert.StartsWith(FurnitureFolder, AssetDatabase.GetAssetPath(model), key + " is not a Furniture Kit model");
            }
        }

        [Test]
        public void BuiltScene_WorktopsShowTheStretchedCabinet_AndKeepTheirColliders()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                int worktops = 0;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (PlaceSurface surface in root.GetComponentsInChildren<PlaceSurface>(true))
                    {
                        if (!surface.name.StartsWith("Worktop")) continue;
                        worktops++;
                        var box = surface.GetComponent<BoxCollider>();
                        Assert.IsNotNull(box, surface.name);
                        Transform visual = surface.transform.Find("Art_" + ArtKey.Worktop);
                        Assert.IsNotNull(visual, surface.name + " has no cabinet");
                        Assert.AreEqual(box.size.x, visual.localScale.x, 0.001f);
                        Assert.AreEqual(box.size.y, visual.localScale.y, 0.001f);
                        Assert.AreEqual(box.size.z, visual.localScale.z, 0.001f);
                        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>();
                        Bounds b = renderers[0].bounds;
                        foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
                        Assert.AreEqual(box.bounds.size.x, b.size.x, 0.02f, surface.name + " width");
                        Assert.AreEqual(box.bounds.size.y, b.size.y, 0.02f, surface.name + " height");
                    }
                }
                Assert.AreEqual(5, worktops);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void BuiltScene_PansAndPotsHaveSteam_PlatesDoNot()
        {
            foreach (string kind in new[] { "Pan", "Pot", "Plate", "Board", "Bowl" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/" + kind + ".prefab");
                Assert.IsNotNull(prefab, kind);
                bool steam = prefab.GetComponent<VesselSteam>() != null;
                Assert.AreEqual(kind == "Pan" || kind == "Pot", steam, kind);
            }
            Assert.LessOrEqual(VesselSteam.MaxParticles, 30);
        }
    }
}
