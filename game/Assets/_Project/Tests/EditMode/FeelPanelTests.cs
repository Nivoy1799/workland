using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Audio;
using Worktown.Game.Cooking;
using Worktown.Game.Player;
using Worktown.Game.UI;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 027b AC6: the F2 feel panel exists only in development builds (the whole file sits inside the spec 027 dev
    /// guard, it adds itself at runtime so no scene or release code refers to it), it covers every FeelSettings number,
    /// and Save writes the current values as JSON. Plus the new sounds (section 8).
    /// </summary>
    public class FeelPanelTests
    {
        private const string DevGuard = "#if DEVELOPMENT_BUILD || UNITY_EDITOR";
        private static string Scripts => Path.Combine(Application.dataPath, "_Project", "Scripts");
        private static string PanelFile => Path.Combine(Scripts, "UI", "FeelPanelView.cs");

        [Test]
        public void PanelFile_IsWhollyInsideTheDevGuard()
        {
            string[] lines = File.ReadAllLines(PanelFile).Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
            Assert.AreEqual(DevGuard, lines[0], "the first line opens the guard");
            Assert.AreEqual("#endif", lines[lines.Length - 1], "the last line closes it");
            int depth = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("#if")) depth++;
                else if (lines[i].StartsWith("#endif")) depth--;
                else if (lines[i].StartsWith("#else") || lines[i].StartsWith("#elif")) Assert.Greater(depth, 1, "no release branch at line " + (i + 1));
                if (i < lines.Length - 1) Assert.Greater(depth, 0, "line " + (i + 1) + " is outside the guard");
            }
        }

        [Test]
        public void NothingOutsideTheGuard_RefersToThePanel()
        {
            foreach (string path in Directory.GetFiles(Scripts, "*.cs", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(path) == "FeelPanelView.cs") continue;
                var guards = new Stack<bool>();
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.StartsWith("#if ")) { guards.Push(line == DevGuard); continue; }
                    if (line.StartsWith("#elif") || line.StartsWith("#else")) { if (guards.Count > 0) { guards.Pop(); guards.Push(false); } continue; }
                    if (line.StartsWith("#endif")) { if (guards.Count > 0) guards.Pop(); continue; }
                    if (!line.Contains("FeelPanelView") || line.StartsWith("///") || line.StartsWith("//")) continue;
                    Assert.IsTrue(guards.Contains(true), Path.GetFileName(path) + ":" + (i + 1) + " refers to the panel outside the guard");
                }
            }
        }

        [Test]
        public void Panel_HasASliderForEveryNumber()
        {
            string[] numbers = typeof(FeelSettings).GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Where(f => f.FieldType == typeof(float)).Select(f => f.Name).ToArray();
            CollectionAssert.AreEquivalent(numbers, FeelPanelView.Sliders.Select(s => s.Field).ToArray());
            FeelSettings a = FeelSettings.PresetA(), b = FeelSettings.PresetB();
            foreach (FeelPanelView.SliderSpec spec in FeelPanelView.Sliders)
            {
                Assert.That(spec.Get(a), Is.InRange(spec.Min, spec.Max), spec.Field + " preset A fits its slider");
                Assert.That(spec.Get(b), Is.InRange(spec.Min, spec.Max), spec.Field + " preset B fits its slider");
                FeelSettings s = FeelSettings.PresetA();
                spec.Set(s, spec.Max);
                Assert.AreEqual(spec.Max, spec.Get(s), 1e-6f, spec.Field + " slider writes its field");
            }
        }

        [Test]
        public void Save_WritesTheCurrentSettingsAsJson()
        {
            FeelSettings before = FeelSettings.Current;
            string path = FeelPanelView.SavePath();
            bool existed = File.Exists(path);
            string old = existed ? File.ReadAllText(path) : null;
            try
            {
                FeelSettings.Use(FeelSettings.PresetB());
                Assert.AreEqual(path, FeelPanelView.Save());
                FeelSettings saved = FeelSettings.FromJson(File.ReadAllText(path));
                Assert.IsNotNull(saved);
                Assert.AreEqual(FeelSettings.PresetBName, saved.Preset);
                Assert.AreEqual(9f, saved.AimCone, 1e-6f);
                Assert.AreEqual(FeelPanelView.FileName, Path.GetFileName(path));
            }
            finally
            {
                FeelSettings.Use(before);
                if (existed) File.WriteAllText(path, old);
                else if (File.Exists(path)) File.Delete(path);
            }
        }

        // ---- Sounds (section 8) ----

        [TestCase(VesselKind.Pan, SoundMaterial.Metal)]
        [TestCase(VesselKind.Pot, SoundMaterial.Metal)]
        [TestCase(VesselKind.Plate, SoundMaterial.Ceramic)]
        [TestCase(VesselKind.Bowl, SoundMaterial.Ceramic)]
        [TestCase(VesselKind.Board, SoundMaterial.Wood)]
        public void Cookware_HasItsMaterial(VesselKind kind, SoundMaterial material)
        {
            Assert.AreEqual(material, SoundRules.MaterialOf(kind));
        }

        [Test]
        public void PlaceSound_FollowsTheItem_ThenTheSurface()
        {
            Assert.AreEqual(SoundKey.PlaceMetal, SoundRules.PlaceSound(SoundMaterial.Metal, false));
            Assert.AreEqual(SoundKey.PlaceCeramic, SoundRules.PlaceSound(SoundMaterial.Ceramic, true));
            Assert.AreEqual(SoundKey.PlaceFood, SoundRules.PlaceSound(SoundMaterial.Food, false));
            Assert.AreEqual(SoundKey.PlaceWood, SoundRules.PlaceSound(SoundMaterial.Wood, true));
            Assert.AreEqual(SoundKey.PlaceMetal, SoundRules.PlaceSound(null, true), "a box on a burner");
            Assert.AreEqual(SoundKey.PlaceWood, SoundRules.PlaceSound(null, false), "a box on a worktop");
            Assert.IsNull(SoundRules.MaterialOf((Component)null));
        }

        [Test]
        public void NewSounds_AreOneShots_AndShort()
        {
            foreach (SoundKey key in new[] { SoundKey.Pick, SoundKey.PlaceWood, SoundKey.PlaceMetal, SoundKey.PlaceCeramic, SoundKey.PlaceFood, SoundKey.Nope, SoundKey.Button })
            {
                Assert.IsFalse(ProceduralSounds.IsLoop(key), key.ToString());
                float seconds = ProceduralSounds.Samples(key).Length / (float)ProceduralSounds.SampleRate;
                Assert.That(seconds, Is.InRange(0.03f, 0.5f), key.ToString());
            }
        }

        [Test]
        public void OldSoundValues_StayWhereTheyWere()
        {
            Assert.AreEqual(0, (int)SoundKey.Chop);
            Assert.AreEqual(1, (int)SoundKey.Tick);
            Assert.AreEqual(2, (int)SoundKey.Sizzle);
            Assert.AreEqual(3, (int)SoundKey.Bubble);
        }

        [Test]
        public void OutlineColour_IsInteract_OrGreyWhenTheClickDoesNothing()
        {
            Assert.AreEqual(Worktown.Game.Art.Palette.Interact, InteractionHighlight.OutlineColorFor(Worktown.Game.Net.Rules.InteractionAction.PickUp));
            Assert.AreEqual(Worktown.Game.Art.Palette.Interact, InteractionHighlight.OutlineColorFor(Worktown.Game.Net.Rules.InteractionAction.Place));
            Assert.AreEqual(InteractionHighlight.RefuseColor, InteractionHighlight.OutlineColorFor(Worktown.Game.Net.Rules.InteractionAction.None));
            Assert.AreEqual(InteractionHighlight.OutlineMaxScale, InteractionHighlight.OutlineScaleFor(0.01f), 1e-6f, "small things get the most");
            Assert.AreEqual(InteractionHighlight.OutlineMinScale, InteractionHighlight.OutlineScaleFor(5f), 1e-6f, "a counter gets the least");
        }
    }
}
