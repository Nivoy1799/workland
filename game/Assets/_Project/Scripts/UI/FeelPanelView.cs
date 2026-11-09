#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Worktown.Game.Player;

namespace Worktown.Game.UI
{
    /// <summary>
    /// Spec 027b (docs/feel.md 9), development builds only (the whole file is compiled out of release builds, and it adds
    /// itself at runtime, so no scene references it): the live tuning panel. F2 opens it (the What's-new banner moves to
    /// Shift+F2 in development builds). A slider for every <see cref="FeelSettings"/> number, a head-bob toggle, "Preset A"
    /// and "Preset B" to compare (the human answers "A or B?"), and "Save", which writes feel.json next to the player's
    /// log and logs the path, so the choice can be committed as the new defaults. The title shows the active preset
    /// ("custom" once a slider moved).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class FeelPanelView : MonoBehaviour
    {
        public const string FileName = "feel.json";

        /// <summary>One slider: the FeelSettings field it edits and its range.</summary>
        public readonly struct SliderSpec
        {
            public SliderSpec(string field, string label, float min, float max, Func<FeelSettings, float> get, Action<FeelSettings, float> set)
            {
                Field = field;
                Label = label;
                Min = min;
                Max = max;
                Get = get;
                Set = set;
            }

            public string Field { get; }
            public string Label { get; }
            public float Min { get; }
            public float Max { get; }
            public Func<FeelSettings, float> Get { get; }
            public Action<FeelSettings, float> Set { get; }
        }

        /// <summary>Every number of <see cref="FeelSettings"/> (FeelPanelTests checks none is missing).</summary>
        public static readonly IReadOnlyList<SliderSpec> Sliders = new[]
        {
            new SliderSpec(nameof(FeelSettings.ConfirmTimeout), "Confirm timeout (s)", 0.1f, 1f, s => s.ConfirmTimeout, (s, v) => s.ConfirmTimeout = v),
            new SliderSpec(nameof(FeelSettings.PickupSeconds), "Pick-up arc (s)", 0f, 0.5f, s => s.PickupSeconds, (s, v) => s.PickupSeconds = v),
            new SliderSpec(nameof(FeelSettings.PickupLift), "Pick-up lift (m)", 0f, 0.3f, s => s.PickupLift, (s, v) => s.PickupLift = v),
            new SliderSpec(nameof(FeelSettings.PlaceSeconds), "Place ease (s)", 0f, 0.5f, s => s.PlaceSeconds, (s, v) => s.PlaceSeconds = v),
            new SliderSpec(nameof(FeelSettings.PlaceSettle), "Place settle", 0f, 0.15f, s => s.PlaceSettle, (s, v) => s.PlaceSettle = v),
            new SliderSpec(nameof(FeelSettings.DropSeconds), "Drop into pot (s)", 0f, 0.5f, s => s.DropSeconds, (s, v) => s.DropSeconds = v),
            new SliderSpec(nameof(FeelSettings.DropHeight), "Drop height (m)", 0f, 0.3f, s => s.DropHeight, (s, v) => s.DropHeight = v),
            new SliderSpec(nameof(FeelSettings.AimCone), "Aim cone (deg)", 0f, 15f, s => s.AimCone, (s, v) => s.AimCone = v),
            new SliderSpec(nameof(FeelSettings.StickMargin), "Stick margin", 0f, 0.8f, s => s.StickMargin, (s, v) => s.StickMargin = v),
            new SliderSpec(nameof(FeelSettings.HeldBonus), "Held-item bonus", 0f, 0.8f, s => s.HeldBonus, (s, v) => s.HeldBonus = v),
            new SliderSpec(nameof(FeelSettings.HighlightMinSeconds), "Highlight minimum (s)", 0f, 0.3f, s => s.HighlightMinSeconds, (s, v) => s.HighlightMinSeconds = v),
            new SliderSpec(nameof(FeelSettings.ThirdPersonDamping), "3rd person follow damping (s)", 0f, 0.4f, s => s.ThirdPersonDamping, (s, v) => s.ThirdPersonDamping = v),
            new SliderSpec(nameof(FeelSettings.ComposerDamping), "3rd person aim damping (s)", 0f, 0.4f, s => s.ComposerDamping, (s, v) => s.ComposerDamping = v),
            new SliderSpec(nameof(FeelSettings.ThirdPersonDistance), "3rd person distance (m)", 1.5f, 7f, s => s.ThirdPersonDistance, (s, v) => s.ThirdPersonDistance = v),
            new SliderSpec(nameof(FeelSettings.ThirdPersonHeight), "3rd person look height (m)", 0.6f, 2f, s => s.ThirdPersonHeight, (s, v) => s.ThirdPersonHeight = v),
            new SliderSpec(nameof(FeelSettings.ThirdPersonPitch), "3rd person pitch (deg)", -5f, 45f, s => s.ThirdPersonPitch, (s, v) => s.ThirdPersonPitch = v),
            new SliderSpec(nameof(FeelSettings.HeadBobAmount), "Head bob (m)", 0f, 0.05f, s => s.HeadBobAmount, (s, v) => s.HeadBobAmount = v),
        };

        private VisualElement _box;
        private Label _title;
        private Toggle _headBob;
        private readonly List<Slider> _sliders = new List<Slider>();
        private int _shownVersion = -1;
        private bool _open;
        private bool _reportedOpen;
        private PlayerInputReader _input;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Application.isBatchMode) return;
            foreach (HudView hud in FindObjectsByType<HudView>(FindObjectsSortMode.None))
                if (hud.GetComponent<FeelPanelView>() == null) hud.gameObject.AddComponent<FeelPanelView>();
        }

        /// <summary>Where Save writes: next to the player's log (the editor's log in the editor), else persistent data.</summary>
        public static string SavePath()
        {
            string log = Application.consoleLogPath;
            string dir = !string.IsNullOrEmpty(log) ? Path.GetDirectoryName(log) : null;
            if (string.IsNullOrEmpty(dir)) dir = Application.persistentDataPath;
            return Path.Combine(dir, FileName);
        }

        /// <summary>Writes the current settings to <see cref="SavePath"/>; returns the path (null on failure, logged).</summary>
        public static string Save()
        {
            string path = SavePath();
            try
            {
                File.WriteAllText(path, FeelSettings.Current.ToJson());
                Debug.Log("[Feel] saved preset '" + FeelSettings.Current.Preset + "' to " + path);
                return path;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning("[Feel] could not save " + path + ": " + e.Message);
                return null;
            }
        }

        private void Start()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            _box = new VisualElement { name = "feel-panel" };
            _box.AddToClassList("panel");
            IStyle box = _box.style;
            box.position = Position.Absolute;
            box.right = 12;
            box.top = 12;
            box.width = 360;
            box.display = DisplayStyle.None;

            _title = new Label();
            _title.AddToClassList("debug-title");
            _box.Add(_title);

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.Add(new Button(() => FeelSettings.Use(FeelSettings.PresetA())) { text = "Preset A" });
            buttons.Add(new Button(() => FeelSettings.Use(FeelSettings.PresetB())) { text = "Preset B" });
            buttons.Add(new Button(() => Save()) { text = "Save" });
            _box.Add(buttons);

            var scroll = new ScrollView();
            scroll.style.maxHeight = 520;
            foreach (SliderSpec spec in Sliders)
            {
                SliderSpec captured = spec;
                var slider = new Slider(spec.Label, spec.Min, spec.Max) { showInputField = true };
                slider.RegisterValueChangedCallback(e => Edit(s => captured.Set(s, e.newValue)));
                _sliders.Add(slider);
                scroll.Add(slider);
            }
            _headBob = new Toggle("Head bob (1st person)");
            _headBob.RegisterValueChangedCallback(e => Edit(s => s.HeadBob = e.newValue));
            scroll.Add(_headBob);
            _box.Add(scroll);
            _box.Add(new Label("F2 closes. Values apply at once."));
            root.Add(_box);
        }

        private static void Edit(Action<FeelSettings> change)
        {
            FeelSettings s = FeelSettings.Current.Clone();
            change(s);
            s.Preset = FeelSettings.CustomName;
            FeelSettings.Use(s);
        }

        private void Update()
        {
            if (_box == null) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f2Key.wasPressedThisFrame && !keyboard.shiftKey.isPressed)
            {
                _open = !_open;
                _box.style.display = _open ? DisplayStyle.Flex : DisplayStyle.None;
                ReportOpen(_open);
            }
            if (_open && _shownVersion != FeelSettings.Version) Refresh();
        }

        private void Refresh()
        {
            _shownVersion = FeelSettings.Version;
            FeelSettings s = FeelSettings.Current;
            _title.text = "Feel (F2): preset " + s.Preset;
            for (int i = 0; i < _sliders.Count; i++) _sliders[i].SetValueWithoutNotify(Sliders[i].Get(s));
            _headBob.SetValueWithoutNotify(s.HeadBob);
        }

        /// <summary>Frees the cursor while the panel is open, so the sliders can be used.</summary>
        private void ReportOpen(bool open)
        {
            if (open == _reportedOpen) return;
            if (_input == null) _input = FindFirstObjectByType<PlayerInputReader>();
            if (_input == null) return;
            _reportedOpen = open;
            _input.SetUiOpen(open);
        }

        private void OnDisable()
        {
            _box?.RemoveFromHierarchy();
            _box = null;
            ReportOpen(false);
        }
    }
}
#endif
