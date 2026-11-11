using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UIElements;
using Worktown.Game.Art;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;
using Worktown.Game.Player;
using Worktown.Game.Settings;

namespace Worktown.Game.UI
{
    /// <summary>
    /// Spec 048: the settings screen, from the title screen (Settings) and in the shop (Esc). Four pages: Graphics (quality, resolution,
    /// window mode, vsync, frame cap, field of view, motion blur, head bob), Audio (master, effects, ambience, UI), Controls (mouse and
    /// gamepad sensitivity, invert Y, every action's key and gamepad button: click one, then press the new key or button; a key that
    /// another action has swaps the two and says so) and Comfort (UI scale, colour-blind friendly ring colours, world labels).
    /// Every change applies at once (GameSettings.Use; the SettingsApplier and the readers follow); Back saves (SettingsApplier.Save,
    /// nothing in a scripted run). Thin: the rules and words are SettingsRules and ControlBindings. <see cref="Press"/>,
    /// <see cref="ShowPage"/> and <see cref="Rebind"/> are the menu tour's way in (the same actions as clicks and key presses).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class SettingsView : MonoBehaviour
    {
        public const string BackId = "settings-back";
        public const string ResetId = "settings-reset";

        [SerializeField] private NetBootstrap _bootstrap;
        [SerializeField] private StyleSheet _style;

        private readonly Dictionary<string, Action> _actions = new Dictionary<string, Action>();
        private readonly Dictionary<SettingsPage, VisualElement> _pages = new Dictionary<SettingsPage, VisualElement>();
        private readonly Dictionary<SettingsPage, Button> _tabs = new Dictionary<SettingsPage, Button>();
        private readonly List<Action> _refreshers = new List<Action>();
        private readonly Dictionary<ControlAction, (Button Key, Button Pad)> _bindingButtons = new Dictionary<ControlAction, (Button, Button)>();
        private VisualElement _root;
        private Label _message;
        private ScrollView _scroll;
        private SettingsPage _page = SettingsPage.Graphics;
        private bool _open;
        private bool _inShop;
        private Action _onClose;
        private int _shownVersion = -1;
        private ControlAction? _capture;
        private bool _capturePad;
        private bool _reportedOpen;
        private PlayerInputReader _input;

        public bool IsOpen => _open;
        public string CurrentPage => _page.ToString();

        public static SettingsView Find() => FindFirstObjectByType<SettingsView>();

        private void Start()
        {
            if (Application.isBatchMode) return;
            VisualElement documentRoot = GetComponent<UIDocument>().rootVisualElement;
            if (_style != null && !documentRoot.styleSheets.Contains(_style)) documentRoot.styleSheets.Add(_style);
            BuildUi();
            documentRoot.Add(_root);
            Show(_root, false);
        }

        private void OnDisable()
        {
            _root?.RemoveFromHierarchy();
            _root = null;
            Report(false);
        }

        // ---- Opening and closing ----

        /// <summary>Opens the screen on its first page; <paramref name="inShop"/>: over the shop (the cursor is freed while it shows).</summary>
        public void Open(bool inShop, Action onClose = null)
        {
            if (_root == null) return;
            _open = true;
            _inShop = inShop;
            _onClose = onClose;
            _capture = null;
            _message.text = SettingsApplier.Persistent ? string.Empty : "Scripted run: changes are not stored.";
            _root.BringToFront();
            ShowPage(SettingsPage.Graphics);
            Refresh();
            Show(_root, true);
            Debug.Log("[Settings] open" + (inShop ? " (in the shop)" : string.Empty));
        }

        /// <summary>Back: saves (not in a scripted run) and closes.</summary>
        public void Close()
        {
            if (!_open) return;
            _open = false;
            _capture = null;
            SettingsApplier.Save();
            Show(_root, false);
            Action done = _onClose;
            _onClose = null;
            done?.Invoke();
        }

        /// <summary>The menu tour (and tests): runs the button's action, as a click would. False for an unknown id.</summary>
        public bool Press(string id)
        {
            if (!_actions.ContainsKey(id)) return false;
            Debug.Log("[Settings] press " + id);
            _actions[id]();
            return true;
        }

        /// <summary>The menu tour: scrolls the page (0 the top, 1 the end).</summary>
        public void ScrollTo(float share)
        {
            if (_scroll == null) return;
            Scroller bar = _scroll.verticalScroller;
            bar.value = bar.lowValue + Mathf.Clamp01(share) * (bar.highValue - bar.lowValue);
        }

        public void ShowPage(SettingsPage page)
        {
            _page = page;
            foreach (KeyValuePair<SettingsPage, VisualElement> pair in _pages) Show(pair.Value, pair.Key == page);
            foreach (KeyValuePair<SettingsPage, Button> pair in _tabs)
                pair.Value.EnableInClassList("settings-tab-on", pair.Key == page);
        }

        /// <summary>Binds a key, as pressing it while waiting for one does (the menu tour). Shows what changed; true if anything did.</summary>
        public bool Rebind(ControlAction action, Key key)
        {
            ControlBindings bindings = GameSettings.CurrentBindings.Clone();
            string old = SettingsRules.KeyText(bindings.KeyOf(action));
            RebindResult result = bindings.Rebind(action, key);
            return Finish(action, result, bindings, SettingsRules.KeyText(key), old);
        }

        /// <summary>Binds a gamepad button, as pressing it while waiting for one does.</summary>
        public bool Rebind(ControlAction action, PadButton button)
        {
            ControlBindings bindings = GameSettings.CurrentBindings.Clone();
            string old = SettingsRules.PadText(bindings.PadOf(action));
            RebindResult result = bindings.Rebind(action, button);
            return Finish(action, result, bindings, SettingsRules.PadText(button), old);
        }

        private bool Finish(ControlAction action, RebindResult result, ControlBindings bindings, string newText, string oldText)
        {
            _capture = null;
            _message.text = SettingsRules.RebindText(action, result, newText, oldText);
            Debug.Log("[Settings] rebind " + action + ": " + result.Outcome + (result.Other.HasValue ? " with " + result.Other.Value : string.Empty));
            if (!result.Changed) return false;
            Change(s => s.Bindings = bindings.Serialize());
            SettingsApplier.Save();
            return true;
        }

        // ---- Every frame ----

        private void Update()
        {
            if (_root == null) return;
            Keyboard keyboard = Keyboard.current;
            bool esc = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
            if (_open && _capture.HasValue)
            {
                Capture(keyboard, esc);
                esc = false; // Esc cancelled the capture
            }
            bool inGame = _bootstrap != null && _bootstrap.Flow.State == JoinState.InGame;
            if (esc)
            {
                if (_open) Close();
                else if (inGame) Open(true);
            }
            if (_open && _inShop && !inGame) Close(); // left the shop while it showed
            Report(_open && _inShop);
            if (_open && _shownVersion != GameSettings.Version) Refresh();
        }

        private void Capture(Keyboard keyboard, bool esc)
        {
            ControlAction action = _capture.Value;
            if (esc)
            {
                _capture = null;
                _message.text = "Not changed.";
                return;
            }
            if (keyboard != null && keyboard.backspaceKey.wasPressedThisFrame)
            {
                if (_capturePad) Rebind(action, PadButton.None);
                else Rebind(action, Key.None);
                return;
            }
            if (_capturePad)
            {
                Gamepad gamepad = Gamepad.current;
                if (gamepad == null) return;
                foreach (PadButton button in (PadButton[])Enum.GetValues(typeof(PadButton)))
                {
                    var control = GameInput.ToGamepadButton(button);
                    if (control.HasValue && gamepad[control.Value].wasPressedThisFrame)
                    {
                        Rebind(action, button);
                        return;
                    }
                }
                return;
            }
            if (keyboard == null) return;
            foreach (KeyControl key in keyboard.allKeys)
            {
                if (key == null || !key.wasPressedThisFrame) continue;
                Rebind(action, key.keyCode);
                return;
            }
        }

        private void BeginCapture(ControlAction action, bool pad)
        {
            _capture = action;
            _capturePad = pad;
            _message.text = (pad ? "Press a gamepad button for " : "Press a key for ") + SettingsRules.ActionText(action)
                + "   (Esc: keep it, Backspace: none)";
        }

        /// <summary>Frees the cursor while the screen shows over the shop.</summary>
        private void Report(bool open)
        {
            if (open == _reportedOpen) return;
            if (_input == null) _input = FindFirstObjectByType<PlayerInputReader>();
            if (_input == null) return;
            _reportedOpen = open;
            _input.SetUiOpen(open);
        }

        // ---- Changing ----

        /// <summary>Applies one change at once (a sanitized copy becomes current; the readers and the applier follow).</summary>
        private static void Change(Action<GameSettings> edit)
        {
            GameSettings s = GameSettings.Current.Clone();
            edit(s);
            GameSettings.Use(s);
        }

        private void Refresh()
        {
            _shownVersion = GameSettings.Version;
            foreach (Action refresh in _refreshers) refresh();
        }

        // ---- Building ----

        private void BuildUi()
        {
            _root = new VisualElement { name = "settings" };
            _root.AddToClassList("settings-root");
            var box = new VisualElement();
            box.AddToClassList("panel");
            box.AddToClassList("settings-box");
            _root.Add(box);

            var heading = new Label("Settings");
            heading.AddToClassList("menu-heading");
            box.Add(heading);

            var tabs = new VisualElement();
            tabs.AddToClassList("settings-tabs");
            box.Add(tabs);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("settings-scroll");
            box.Add(scroll);
            _scroll = scroll;
            foreach (SettingsPage page in (SettingsPage[])Enum.GetValues(typeof(SettingsPage)))
            {
                SettingsPage p = page;
                Button tab = MakeButton("tab-" + page.ToString().ToLowerInvariant(), SettingsRules.PageTitle(page), () => ShowPage(p));
                tab.AddToClassList("settings-tab");
                tabs.Add(tab);
                _tabs[page] = tab;
                var content = new VisualElement();
                _pages[page] = content;
                scroll.Add(content);
            }
            BuildGraphics(_pages[SettingsPage.Graphics]);
            BuildAudio(_pages[SettingsPage.Audio]);
            BuildControls(_pages[SettingsPage.Controls]);
            BuildComfort(_pages[SettingsPage.Comfort]);

            _message = new Label { enableRichText = false };
            _message.AddToClassList("settings-message");
            box.Add(_message);
            var row = new VisualElement();
            row.AddToClassList("menu-row");
            Button reset = MakeButton(ResetId, "Reset this page", ResetPage);
            reset.AddToClassList("menu-button");
            row.Add(reset);
            Button back = MakeButton(BackId, "Back", Close);
            back.AddToClassList("menu-button");
            row.Add(back);
            box.Add(row);
        }

        private void BuildGraphics(VisualElement page)
        {
            var qualities = new List<string> { "Low", "Medium", "High" };
            AddDropdown(page, "Quality", qualities, () => (int)GameSettings.Current.Quality, i => Change(s => s.Quality = (QualityPreset)i));

            List<Vector2Int> sizes = Resolutions();
            var sizeNames = new List<string> { SettingsRules.ResolutionText(0, 0) };
            foreach (Vector2Int size in sizes) sizeNames.Add(SettingsRules.ResolutionText(size.x, size.y));
            AddDropdown(page, "Resolution", sizeNames, () =>
            {
                GameSettings s = GameSettings.Current;
                int index = sizes.IndexOf(new Vector2Int(s.ResolutionWidth, s.ResolutionHeight));
                return index + 1; // 0 = as started
            }, i => Change(s =>
            {
                s.DisplaySet = true;
                s.ResolutionWidth = i > 0 ? sizes[i - 1].x : 0;
                s.ResolutionHeight = i > 0 ? sizes[i - 1].y : 0;
            }));

            var windows = new List<string>();
            foreach (WindowMode mode in (WindowMode[])Enum.GetValues(typeof(WindowMode))) windows.Add(SettingsRules.WindowText(mode));
            AddDropdown(page, "Window", windows, () => (int)GameSettings.Current.Window, i => Change(s =>
            {
                s.DisplaySet = true;
                s.Window = (WindowMode)i;
            }));

            AddToggle(page, "Vsync", () => GameSettings.Current.VSync, v => Change(s => s.VSync = v));
            var caps = new List<string>();
            foreach (int cap in SettingsRules.FrameCaps) caps.Add(SettingsRules.FrameCapText(cap));
            AddDropdown(page, "Frame cap", caps, () => SettingsRules.FrameCapIndex(GameSettings.Current.FrameCap),
                i => Change(s => s.FrameCap = SettingsRules.FrameCaps[i]));
            Note(page, "The frame cap holds when vsync is off (and while hosting a shop).");
            AddSlider(page, "Field of view", GameSettings.MinFieldOfView, GameSettings.MaxFieldOfView, () => GameSettings.Current.FieldOfView,
                v => Change(s => s.FieldOfView = Mathf.Round(v)), v => Mathf.RoundToInt(v) + " degrees");
            AddToggle(page, "Motion blur", () => GameSettings.Current.MotionBlur, v => Change(s => s.MotionBlur = v));
            AddToggle(page, "Head bob (first person)", () => GameSettings.Current.HeadBob, v => Change(s => s.HeadBob = v));
        }

        private void BuildAudio(VisualElement page)
        {
            AddVolume(page, "Master", () => GameSettings.Current.MasterVolume, v => Change(s => s.MasterVolume = v));
            AddVolume(page, "Effects", () => GameSettings.Current.EffectsVolume, v => Change(s => s.EffectsVolume = v));
            AddVolume(page, "Ambience", () => GameSettings.Current.AmbienceVolume, v => Change(s => s.AmbienceVolume = v));
            AddVolume(page, "UI and cues", () => GameSettings.Current.UiVolume, v => Change(s => s.UiVolume = v));
        }

        private void BuildControls(VisualElement page)
        {
            AddSlider(page, "Mouse sensitivity", GameSettings.MinSensitivity, GameSettings.MaxSensitivity,
                () => GameSettings.Current.MouseSensitivity, v => Change(s => s.MouseSensitivity = Round2(v)), v => v.ToString("0.00") + "x");
            AddSlider(page, "Gamepad sensitivity", GameSettings.MinSensitivity, GameSettings.MaxSensitivity,
                () => GameSettings.Current.GamepadSensitivity, v => Change(s => s.GamepadSensitivity = Round2(v)), v => v.ToString("0.00") + "x");
            AddToggle(page, "Invert Y", () => GameSettings.Current.InvertY, v => Change(s => s.InvertY = v));

            var header = new VisualElement();
            header.AddToClassList("settings-bind-row");
            header.Add(Text("Action", "settings-bind-name"));
            header.Add(Text("Keyboard", "settings-bind-head"));
            header.Add(Text("Gamepad", "settings-bind-head"));
            page.Add(header);
            foreach (ControlAction action in ControlBindings.Actions)
            {
                ControlAction a = action;
                var row = new VisualElement();
                row.AddToClassList("settings-bind-row");
                row.Add(Text(SettingsRules.ActionText(action), "settings-bind-name"));
                string id = "bind-" + action.ToString().ToLowerInvariant();
                Button key = MakeButton(id + "-key", string.Empty, () => BeginCapture(a, false));
                key.AddToClassList("settings-bind");
                Button pad = MakeButton(id + "-pad", string.Empty, () => BeginCapture(a, true));
                pad.AddToClassList("settings-bind");
                row.Add(key);
                row.Add(pad);
                page.Add(row);
                _bindingButtons[action] = (key, pad);
            }
            _refreshers.Add(() =>
            {
                ControlBindings b = GameSettings.CurrentBindings;
                foreach (KeyValuePair<ControlAction, (Button Key, Button Pad)> pair in _bindingButtons)
                {
                    pair.Value.Key.text = SettingsRules.KeyText(b.KeyOf(pair.Key));
                    pair.Value.Pad.text = SettingsRules.PadText(b.PadOf(pair.Key));
                }
            });
            Note(page, "Fixed: left click does it, right click puts down, the wheel turns; the arrow keys walk too. Gamepad: RT and LT are "
                + "the left and right click, the left stick walks, the right stick looks, the d-pad sends quick chat.");
        }

        private void BuildComfort(VisualElement page)
        {
            AddSlider(page, "UI scale", GameSettings.MinUiScale, GameSettings.MaxUiScale, () => GameSettings.Current.UiScale,
                v => Change(s => s.UiScale = Mathf.Round(v * 20f) / 20f), v => Mathf.RoundToInt(v * 100f) + " %");
            AddToggle(page, "Colour-blind friendly ring colours", () => GameSettings.Current.ColourBlindRings, v => Change(s => s.ColourBlindRings = v));
            // The four ring colours as they show now: good, perfect, overdone, burnt.
            var swatches = new VisualElement();
            swatches.AddToClassList("settings-swatches");
            string[] names = { "good", "perfect", "overdone", "burnt" };
            var cells = new VisualElement[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                var cell = new VisualElement();
                cell.AddToClassList("settings-swatch");
                var label = new Label(names[i]);
                label.AddToClassList("settings-swatch-text");
                cell.Add(label);
                swatches.Add(cell);
                cells[i] = cell;
            }
            page.Add(swatches);
            _refreshers.Add(() =>
            {
                RingPalette p = RingPalette.Current;
                Color[] colours = { p.Good, p.Perfect, p.Overdone, p.Burnt };
                for (int i = 0; i < cells.Length; i++) cells[i].style.backgroundColor = colours[i];
            });
            AddToggle(page, "Labels in the world (station and item names)", () => GameSettings.Current.Labels, v => Change(s => s.Labels = v));
            Note(page, "With labels off, orders, the ticket rail and the stove buttons still show.");
        }

        private void ResetPage()
        {
            GameSettings d = GameSettings.Defaults();
            switch (_page)
            {
                case SettingsPage.Graphics:
                    Change(s =>
                    {
                        s.Quality = d.Quality;
                        s.VSync = d.VSync;
                        s.FrameCap = d.FrameCap;
                        s.FieldOfView = d.FieldOfView;
                        s.MotionBlur = d.MotionBlur;
                        s.HeadBob = d.HeadBob; // the resolution and window stay as they are
                    });
                    break;
                case SettingsPage.Audio:
                    Change(s =>
                    {
                        s.MasterVolume = d.MasterVolume;
                        s.EffectsVolume = d.EffectsVolume;
                        s.AmbienceVolume = d.AmbienceVolume;
                        s.UiVolume = d.UiVolume;
                    });
                    break;
                case SettingsPage.Controls:
                    Change(s =>
                    {
                        s.MouseSensitivity = d.MouseSensitivity;
                        s.GamepadSensitivity = d.GamepadSensitivity;
                        s.InvertY = d.InvertY;
                        s.Bindings = d.Bindings;
                    });
                    break;
                default:
                    Change(s =>
                    {
                        s.UiScale = d.UiScale;
                        s.ColourBlindRings = d.ColourBlindRings;
                        s.Labels = d.Labels;
                    });
                    break;
            }
            _capture = null;
            _message.text = SettingsRules.PageTitle(_page) + ": back to the defaults.";
        }

        // ---- Small builders ----

        private Button MakeButton(string id, string text, Action action)
        {
            _actions[id] = action;
            return new Button(action) { text = text, name = id };
        }

        private static Label Text(string text, string cls)
        {
            var label = new Label(text);
            label.AddToClassList(cls);
            return label;
        }

        private static void Note(VisualElement page, string text) => page.Add(Text(text, "settings-note"));

        private static VisualElement Row(VisualElement page, string name)
        {
            var row = new VisualElement();
            row.AddToClassList("settings-row");
            row.Add(Text(name, "settings-name"));
            page.Add(row);
            return row;
        }

        private void AddToggle(VisualElement page, string name, Func<bool> get, Action<bool> set)
        {
            VisualElement row = Row(page, name);
            var toggle = new Toggle();
            toggle.AddToClassList("settings-toggle");
            toggle.RegisterValueChangedCallback(e => { if (e.newValue != get()) set(e.newValue); });
            row.Add(toggle);
            _refreshers.Add(() => toggle.SetValueWithoutNotify(get()));
        }

        private void AddDropdown(VisualElement page, string name, List<string> choices, Func<int> get, Action<int> set)
        {
            VisualElement row = Row(page, name);
            var field = new DropdownField(choices, 0);
            field.AddToClassList("settings-field");
            field.RegisterValueChangedCallback(e =>
            {
                int index = choices.IndexOf(e.newValue);
                if (index >= 0 && index != get()) set(index);
            });
            row.Add(field);
            _refreshers.Add(() =>
            {
                int index = get();
                field.SetValueWithoutNotify(index >= 0 && index < choices.Count ? choices[index] : choices[0]);
            });
        }

        private void AddSlider(VisualElement page, string name, float min, float max, Func<float> get, Action<float> set, Func<float, string> text)
        {
            VisualElement row = Row(page, name);
            var slider = new Slider(min, max);
            slider.AddToClassList("settings-slider");
            var value = Text(string.Empty, "settings-value");
            slider.RegisterValueChangedCallback(e =>
            {
                value.text = text(e.newValue);
                if (!Mathf.Approximately(e.newValue, get())) set(e.newValue);
            });
            row.Add(slider);
            row.Add(value);
            _refreshers.Add(() =>
            {
                float v = get();
                slider.SetValueWithoutNotify(v);
                value.text = text(v);
            });
        }

        private void AddVolume(VisualElement page, string name, Func<float> get, Action<float> set) =>
            AddSlider(page, name, 0f, 1f, get, v => set(Mathf.Round(v * 100f) / 100f), SettingsRules.PercentText);

        private static float Round2(float v) => Mathf.Round(v * 100f) / 100f;

        private static List<Vector2Int> Resolutions()
        {
            var sizes = new List<Vector2Int>();
            foreach (Resolution r in Screen.resolutions) sizes.Add(new Vector2Int(r.width, r.height));
            return SettingsRules.UniqueResolutions(sizes);
        }

        private static void Show(VisualElement element, bool visible)
        {
            if (element == null) return;
            DisplayStyle wanted = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (element.style.display != wanted) element.style.display = wanted;
        }
    }
}
