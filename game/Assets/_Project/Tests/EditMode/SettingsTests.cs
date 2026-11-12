using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using Worktown.Game.Art;
using Worktown.Game.Audio;
using Worktown.Game.Cooking;
using Worktown.Game.Npc;
using Worktown.Game.Player;
using Worktown.Game.Net.Rules;
using Worktown.Game.Settings;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 048 AC2: the settings save and load (SettingsStore over a fake PlayerPrefs; a scripted run never reads or writes them) and
    /// apply (GameSettings.Use hands the volumes, ring colours and bindings on; SettingsRules says what the engine gets: frame cap,
    /// vsync, window mode, field of view, look speed, quality, labels). Also the 041 review's wheel notch from the scroll setting.
    /// </summary>
    public class SettingsTests
    {
        private sealed class FakePrefs : ISettingsPrefs
        {
            public readonly Dictionary<string, string> Values = new Dictionary<string, string>();
            public int Writes;
            public bool Fail;

            public string GetString(string key, string fallback) => Values.TryGetValue(key, out string v) ? v : fallback;

            public bool SetString(string key, string value)
            {
                if (Fail) return false;
                Writes++;
                Values[key] = value;
                return true;
            }
        }

        [TearDown]
        public void ResetSettings() => GameSettings.Use(GameSettings.Defaults());

        // ---- Defaults and ranges ----

        [Test]
        public void Defaults_AreComfortable_AndLeaveTheGameAsItWas()
        {
            GameSettings d = GameSettings.Defaults();
            Assert.AreEqual(QualityPreset.High, d.Quality, "the project's own render settings");
            Assert.IsFalse(d.DisplaySet, "the window the game started with");
            Assert.IsTrue(d.VSync);
            Assert.AreEqual(120, d.FrameCap, "no more 500 fps while hosting (045)");
            Assert.AreEqual(ViewRules.FieldOfView, d.FieldOfView, "the scene's first-person lens");
            Assert.IsFalse(d.MotionBlur);
            Assert.IsFalse(d.HeadBob, "docs/feel.md 5: off by default");
            Assert.AreEqual(1f, d.MasterVolume);
            Assert.AreEqual(1f, d.MouseSensitivity);
            Assert.IsFalse(d.InvertY);
            Assert.AreEqual(string.Empty, d.Bindings);
            Assert.AreEqual(1f, d.UiScale);
            Assert.IsFalse(d.ColourBlindRings);
            Assert.IsTrue(d.Labels);
        }

        [Test]
        public void Sanitized_ClampsEveryValue_AndFixesBrokenOnes()
        {
            var s = new GameSettings
            {
                Quality = (QualityPreset)42,
                Window = (WindowMode)(-1),
                ResolutionWidth = 100,
                ResolutionHeight = 50,
                FrameCap = 10,
                FieldOfView = 150f,
                MasterVolume = 2f,
                EffectsVolume = -1f,
                AmbienceVolume = float.NaN,
                UiVolume = float.PositiveInfinity,
                MouseSensitivity = 0f,
                GamepadSensitivity = 99f,
                UiScale = 5f,
                Bindings = null
            };
            GameSettings c = GameSettings.Sanitized(s);
            Assert.AreEqual(QualityPreset.High, c.Quality);
            Assert.AreEqual(WindowMode.Borderless, c.Window);
            Assert.AreEqual(0, c.ResolutionWidth, "too small: as started");
            Assert.AreEqual(0, c.ResolutionHeight);
            Assert.AreEqual(GameSettings.MinFrameCap, c.FrameCap);
            Assert.AreEqual(GameSettings.MaxFieldOfView, c.FieldOfView);
            Assert.AreEqual(1f, c.MasterVolume);
            Assert.AreEqual(0f, c.EffectsVolume);
            Assert.AreEqual(1f, c.AmbienceVolume, "NaN: the default");
            Assert.AreEqual(1f, c.UiVolume, "infinity: the default");
            Assert.AreEqual(GameSettings.MinSensitivity, c.MouseSensitivity);
            Assert.AreEqual(GameSettings.MaxSensitivity, c.GamepadSensitivity);
            Assert.AreEqual(GameSettings.MaxUiScale, c.UiScale);
            Assert.AreEqual(string.Empty, c.Bindings);
            Assert.AreEqual(150f, s.FieldOfView, "the original is not touched");

            Assert.AreEqual(0, GameSettings.Sanitized(new GameSettings { FrameCap = 0 }).FrameCap, "0 is no cap");
            Assert.AreEqual(GameSettings.MaxFrameCap, GameSettings.Sanitized(new GameSettings { FrameCap = 9999 }).FrameCap);
            Assert.AreEqual(GameSettings.MinFieldOfView, GameSettings.Sanitized(new GameSettings { FieldOfView = float.NaN }).FieldOfView);
            Assert.IsNotNull(GameSettings.Sanitized(null));
        }

        // ---- Save and load ----

        [Test]
        public void Json_RoundTrips_AndMissingFieldsKeepTheirDefaults()
        {
            var s = new GameSettings { FieldOfView = 90f, VSync = false, FrameCap = 144, InvertY = true, Labels = false, UiScale = 1.25f,
                ColourBlindRings = true, MasterVolume = 0.4f, Bindings = "Jump=E,South;Use=Space,None", Window = WindowMode.Windowed };
            GameSettings back = GameSettings.FromJson(s.ToJson());
            Assert.IsNotNull(back);
            Assert.AreEqual(s.ToJson(), back.ToJson());

            GameSettings partial = GameSettings.FromJson("{\"FieldOfView\":85.0}");
            Assert.AreEqual(85f, partial.FieldOfView);
            Assert.IsTrue(partial.Labels, "a field the save does not have keeps its default");
            Assert.AreEqual(120, partial.FrameCap);
            Assert.AreEqual(100f, GameSettings.FromJson("{\"FieldOfView\":400.0}").FieldOfView, "loaded values are sanitized");

            Assert.IsNull(GameSettings.FromJson(null));
            Assert.IsNull(GameSettings.FromJson(" "));
            Assert.IsNull(GameSettings.FromJson("not json"));
        }

        [Test]
        public void Store_SavesAndLoads_ThePlayersSettings()
        {
            var prefs = new FakePrefs();
            var store = new SettingsStore(prefs, persistent: true);
            Assert.AreEqual(GameSettings.Defaults().ToJson(), store.Load().ToJson(), "nothing saved yet: the defaults");

            var s = new GameSettings { FieldOfView = 95f, MouseSensitivity = 1.5f, Bindings = "Turn=R,North" };
            Assert.IsTrue(store.Save(s));
            Assert.AreEqual(1, prefs.Writes);
            Assert.IsTrue(prefs.Values.ContainsKey(SettingsRules.PrefsKey));
            GameSettings loaded = new SettingsStore(prefs, persistent: true).Load();
            Assert.AreEqual(95f, loaded.FieldOfView);
            Assert.AreEqual(1.5f, loaded.MouseSensitivity);
            Assert.AreEqual("Turn=R,North", loaded.Bindings);

            prefs.Values[SettingsRules.PrefsKey] = "{broken";
            Assert.AreEqual(GameSettings.Defaults().ToJson(), store.Load().ToJson(), "a broken save: the defaults");
            prefs.Fail = true;
            Assert.IsFalse(store.Save(s), "a failed write says so");
        }

        [Test]
        public void Store_InAScriptedRun_NeverReadsOrWrites()
        {
            var prefs = new FakePrefs();
            string mine = new GameSettings { FieldOfView = 100f, Labels = false }.ToJson();
            prefs.Values[SettingsRules.PrefsKey] = mine;
            var store = new SettingsStore(prefs, persistent: false);
            Assert.IsFalse(store.Persistent);
            Assert.AreEqual(GameSettings.Defaults().ToJson(), store.Load().ToJson(), "a tour runs on the defaults, not this PC's settings");
            Assert.IsFalse(store.Save(new GameSettings { FieldOfView = 70f }));
            Assert.AreEqual(0, prefs.Writes);
            Assert.AreEqual(mine, prefs.Values[SettingsRules.PrefsKey], "the player's saved settings are unchanged");
        }

        [Test]
        public void ScriptedRuns_AreTheTours_TheirObservers_AndTheBots()
        {
            Assert.IsTrue(SettingsRules.UsesSavedSettings(LaunchArgs.Parse(new[] { "Worktown.exe" })));
            Assert.IsTrue(SettingsRules.UsesSavedSettings(LaunchArgs.Parse(new[] { "Worktown.exe", "-host" })));
            Assert.IsTrue(SettingsRules.UsesSavedSettings(null));
            Assert.IsFalse(SettingsRules.UsesSavedSettings(LaunchArgs.Parse(new[] { "Worktown.exe", "-tour", "menu" })));
            Assert.IsFalse(SettingsRules.UsesSavedSettings(LaunchArgs.Parse(new[] { "Worktown.exe", "-connect", "127.0.0.1", "-tour", "cook" })));
            Assert.IsFalse(SettingsRules.UsesSavedSettings(LaunchArgs.Parse(new[] { "Worktown.exe", "-host", "-observe", "cook" })));
            Assert.IsFalse(SettingsRules.UsesSavedSettings(LaunchArgs.Parse(new[] { "Worktown.exe", "-batchmode", "-connect", "x", "-bot" })));
            Assert.IsFalse(SettingsRules.UsesSavedSettings(LaunchArgs.Parse(new[] { "Worktown.exe", "-host", "-metrics", "m.jsonl" })), "048 review: a load-test or perf-check host");
        }

        // ---- Apply ----

        [Test]
        public void Use_MakesASanitizedCopyCurrent_AndBumpsTheVersion()
        {
            int before = GameSettings.Version;
            var s = new GameSettings { FieldOfView = 300f };
            GameSettings.Use(s);
            Assert.Greater(GameSettings.Version, before);
            Assert.AreEqual(GameSettings.MaxFieldOfView, GameSettings.Current.FieldOfView);
            Assert.AreNotSame(s, GameSettings.Current);
            s.FieldOfView = 80f;
            Assert.AreEqual(GameSettings.MaxFieldOfView, GameSettings.Current.FieldOfView, "a later edit of the caller's copy changes nothing");
        }

        [Test]
        public void Use_AppliesTheVolumesPerSoundGroup()
        {
            GameSettings.Use(new GameSettings { EffectsVolume = 0.5f, AmbienceVolume = 0.25f, UiVolume = 0f });
            Assert.AreEqual(SoundCatalog.BaseGainOf(SoundKey.Chop) * 0.5f, SoundCatalog.GainOf(SoundKey.Chop), 1e-5f);
            Assert.AreEqual(SoundCatalog.BaseGainOf(SoundKey.Fryer) * 0.25f, SoundCatalog.GainOf(SoundKey.Fryer), 1e-5f);
            Assert.AreEqual(0f, SoundCatalog.GainOf(SoundKey.Perfect));
            GameSettings.Use(GameSettings.Defaults());
            Assert.AreEqual(SoundCatalog.BaseGainOf(SoundKey.Chop), SoundCatalog.GainOf(SoundKey.Chop));
        }

        [Test]
        public void SoundGroups_HumsAreAmbience_CuesAreUi_TheRestEffects()
        {
            Assert.AreEqual(SoundCategory.Ambience, SoundRules.CategoryOf(SoundKey.Fryer));
            Assert.AreEqual(SoundCategory.Ambience, SoundRules.CategoryOf(SoundKey.MotorHum));
            Assert.AreEqual(SoundCategory.Ui, SoundRules.CategoryOf(SoundKey.Perfect));
            Assert.AreEqual(SoundCategory.Ui, SoundRules.CategoryOf(SoundKey.Nope));
            Assert.AreEqual(SoundCategory.Ui, SoundRules.CategoryOf(SoundKey.Coin));
            Assert.AreEqual(SoundCategory.Effects, SoundRules.CategoryOf(SoundKey.Chop));
            Assert.AreEqual(SoundCategory.Effects, SoundRules.CategoryOf(SoundKey.Sizzle));
            AudioMix.Set(float.NaN, -2f, 7f);
            Assert.AreEqual(1f, AudioMix.Of(SoundCategory.Effects));
            Assert.AreEqual(0f, AudioMix.Of(SoundCategory.Ambience));
            Assert.AreEqual(1f, AudioMix.Of(SoundCategory.Ui));
        }

        [Test]
        public void Use_SwitchesTheRingColours_ToTheColourBlindSet()
        {
            int before = RingPalette.Version;
            GameSettings.Use(new GameSettings { ColourBlindRings = true });
            Assert.AreSame(RingPalette.ColourBlind, RingPalette.Current);
            Assert.Greater(RingPalette.Version, before, "the ring views rebuild");
            Assert.AreEqual(Palette.SafeSkyBlue, HeatRingRules.FillColor(HeatZone.Good));
            Assert.AreEqual(Palette.SafeYellow, HeatRingRules.FillColor(HeatZone.Perfect));
            Assert.AreEqual(Palette.SafeOrange, HeatRingRules.FillColor(HeatZone.Overdone));
            Assert.AreEqual(Palette.SafeVermillion, HeatRingRules.FillColor(HeatZone.Burnt));
            Assert.AreEqual(Palette.Cream, HeatRingRules.FillColor(HeatZone.Cooking));
            Assert.AreEqual(Palette.SafeSkyBlue, PatienceRingRules.FillColor(0.9f));
            Assert.AreEqual(Palette.SafeYellow, PatienceRingRules.FillColor(0.4f));
            Assert.AreEqual(Palette.SafeOrange, PatienceRingRules.FillColor(0.1f));

            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            HeatRingRules.Build(1f, HeatZone.Perfect, v, c, t);
            CollectionAssert.Contains(c, Palette.SafeSkyBlue, "the good window's mark");
            CollectionAssert.DoesNotContain(c, Palette.Ground);

            GameSettings.Use(GameSettings.Defaults());
            Assert.AreSame(RingPalette.Standard, RingPalette.Current);
            Assert.AreEqual(Palette.Ground, HeatRingRules.FillColor(HeatZone.Good));
            Assert.AreEqual(Palette.Ground, PatienceRingRules.FillColor(0.9f));
        }

        [Test]
        public void ColourBlindSet_KeepsEveryZoneApart_InLightnessToo()
        {
            RingPalette p = RingPalette.ColourBlind;
            Color[] zones = { p.Good, p.Perfect, p.Overdone, p.Burnt };
            for (int i = 0; i < zones.Length; i++)
            for (int j = i + 1; j < zones.Length; j++)
                Assert.AreNotEqual(zones[i], zones[j]);
            // Lightness steps down from perfect to burnt, so the worse zones read darker without any hue.
            Assert.Greater(Luma(p.Perfect), Luma(p.Overdone));
            Assert.Greater(Luma(p.Overdone), Luma(p.Burnt));
            Assert.AreNotEqual(RingPalette.Standard.Good, p.Good);
        }

        [Test]
        public void Use_ParsesTheBindings_ForTheReaders()
        {
            ControlBindings b = ControlBindings.Defaults();
            b.Rebind(ControlAction.Jump, Key.E);
            GameSettings.Use(new GameSettings { Bindings = b.Serialize() });
            Assert.AreEqual(Key.E, GameSettings.CurrentBindings.KeyOf(ControlAction.Jump));
            Assert.AreEqual(Key.Space, GameSettings.CurrentBindings.KeyOf(ControlAction.Use));
            GameSettings.Use(GameSettings.Defaults());
            Assert.AreEqual(Key.Space, GameSettings.CurrentBindings.KeyOf(ControlAction.Jump));
        }

        [Test]
        public void FrameCap_AndVsync_ForTheEngine()
        {
            Assert.AreEqual(120, SettingsRules.TargetFrameRate(120));
            Assert.AreEqual(-1, SettingsRules.TargetFrameRate(0), "no cap: the platform's own");
            Assert.AreEqual(1, SettingsRules.VSyncCount(true));
            Assert.AreEqual(0, SettingsRules.VSyncCount(false));
            Assert.AreEqual(3, SettingsRules.FrameCapIndex(120));
            Assert.AreEqual(SettingsRules.FrameCaps.Length - 1, SettingsRules.FrameCapIndex(0));
            Assert.AreEqual(1, SettingsRules.FrameCapIndex(59), "the nearest offered cap");
            Assert.AreEqual("No cap", SettingsRules.FrameCapText(0));
            Assert.AreEqual("144 fps", SettingsRules.FrameCapText(144));
        }

        [Test]
        public void WindowModes_MapBothWays()
        {
            Assert.AreEqual(FullScreenMode.FullScreenWindow, SettingsRules.ToFullScreenMode(WindowMode.Borderless));
            Assert.AreEqual(FullScreenMode.ExclusiveFullScreen, SettingsRules.ToFullScreenMode(WindowMode.Fullscreen));
            Assert.AreEqual(FullScreenMode.Windowed, SettingsRules.ToFullScreenMode(WindowMode.Windowed));
            foreach (WindowMode mode in new[] { WindowMode.Borderless, WindowMode.Fullscreen, WindowMode.Windowed })
                Assert.AreEqual(mode, SettingsRules.FromFullScreenMode(SettingsRules.ToFullScreenMode(mode)));
            Assert.AreEqual(WindowMode.Borderless, SettingsRules.FromFullScreenMode(FullScreenMode.MaximizedWindow));
        }

        [Test]
        public void FieldOfView_FirstPersonFromTheSlider_ThirdPersonTenNarrower()
        {
            Assert.AreEqual(70f, SettingsRules.FirstPersonFov(70f));
            Assert.AreEqual(60f, SettingsRules.ThirdPersonFov(70f), "the scene's third-person lens at the default");
            Assert.AreEqual(100f, SettingsRules.FirstPersonFov(130f));
            Assert.AreEqual(90f, SettingsRules.ThirdPersonFov(100f));
            Assert.AreEqual(70f, SettingsRules.FirstPersonFov(float.NaN));
        }

        [Test]
        public void LookSpeed_FollowsSensitivity_AndInvertY()
        {
            var d = GameSettings.Defaults();
            Vector2 mouse = SettingsRules.LookDegrees(new Vector2(10f, 10f), false, 0.016f, d);
            Assert.AreEqual(1.2f, mouse.x, 1e-4f, "the spec 010 speed at sensitivity 1");
            Assert.AreEqual(1.2f, mouse.y, 1e-4f);
            var fast = new GameSettings { MouseSensitivity = 2f, InvertY = true };
            Vector2 inverted = SettingsRules.LookDegrees(new Vector2(10f, 10f), false, 0.016f, fast);
            Assert.AreEqual(2.4f, inverted.x, 1e-4f);
            Assert.AreEqual(-2.4f, inverted.y, 1e-4f);
            Vector2 stick = SettingsRules.LookDegrees(new Vector2(1f, 0f), true, 0.5f, new GameSettings { GamepadSensitivity = 0.5f });
            Assert.AreEqual(40f, stick.x, 1e-4f, "160 degrees a second at full tilt, half speed, half a second");
        }

        [Test]
        public void QualityPresets_HighIsTheProjectsOwn()
        {
            Assert.AreEqual(1f, SettingsRules.RenderScale(QualityPreset.High, 1f));
            Assert.AreEqual(0.9f, SettingsRules.RenderScale(QualityPreset.High, 0.9f), "048 review: the asset's own scale, not a literal 1");
            Assert.IsNull(SettingsRules.ShadowDistance(QualityPreset.High));
            Assert.IsNull(SettingsRules.ShadowCascades(QualityPreset.High));
            Assert.Less(SettingsRules.RenderScale(QualityPreset.Low, 1f), SettingsRules.RenderScale(QualityPreset.Medium, 1f));
            Assert.Less(SettingsRules.ShadowDistance(QualityPreset.Low).Value, SettingsRules.ShadowDistance(QualityPreset.Medium).Value);
            Assert.AreEqual(1, SettingsRules.ShadowCascades(QualityPreset.Low));
        }

        [Test]
        public void Resolutions_EachOnce_LargestFirst()
        {
            List<Vector2Int> list = SettingsRules.UniqueResolutions(new[]
            {
                new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(1920, 1080), new Vector2Int(320, 200),
                new Vector2Int(1920, 1200)
            });
            CollectionAssert.AreEqual(new[] { new Vector2Int(1920, 1200), new Vector2Int(1920, 1080), new Vector2Int(1280, 720) }, list);
            Assert.IsEmpty(SettingsRules.UniqueResolutions(null));
            Assert.AreEqual("As started", SettingsRules.ResolutionText(0, 0));
            Assert.AreEqual("1920 x 1080", SettingsRules.ResolutionText(1920, 1080));
        }

        [Test]
        public void LabelsOff_HidesTheNames_NotTheOrders()
        {
            Assert.IsFalse(LabelRules.ShownWithLabels(LabelKind.Sign, false));
            Assert.IsFalse(LabelRules.ShownWithLabels(LabelKind.Item, false));
            Assert.IsTrue(LabelRules.ShownWithLabels(LabelKind.Bubble, false), "orders");
            Assert.IsTrue(LabelRules.ShownWithLabels(LabelKind.Rail, false));
            Assert.IsTrue(LabelRules.ShownWithLabels(LabelKind.Button, false));
            foreach (LabelKind kind in (LabelKind[])System.Enum.GetValues(typeof(LabelKind)))
                Assert.IsTrue(LabelRules.ShownWithLabels(kind, true), kind + " with labels on");
        }

        // ---- 041 review: the wheel's notch from the Input System's scroll setting ----

        [Test]
        public void WheelNotch_ComesFromTheScrollSetting()
        {
            Assert.AreEqual(1f, WheelSteps.CountsPerNotch(true, true), "uniform deltas: -1..1 on every platform");
            Assert.AreEqual(120f, WheelSteps.CountsPerNotch(false, true), "Windows' own range");
            Assert.AreEqual(1f, WheelSteps.CountsPerNotch(false, false), "macOS and Linux");

            var uniform = new WheelSteps(1f);
            Assert.AreEqual(12, uniform.Update(12f), "a known notch size never guesses: 12 uniform notches are 12 steps");
            var windows = new WheelSteps(120f);
            Assert.AreEqual(0, windows.Update(1f), "a 120th of a notch");
            Assert.AreEqual(1, windows.Update(119f), "adds up to one");
            Assert.AreEqual(-1, windows.Update(-120f));
            var guess = new WheelSteps(0f);
            Assert.AreEqual(1, guess.Update(120f), "no size: guesses as before");
            Assert.AreEqual(1, guess.Update(1f));
        }

        private static float Luma(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
    }
}
