using System;
using UnityEngine;
using Worktown.Game.Art;
using Worktown.Game.Audio;

namespace Worktown.Game.Settings
{
    /// <summary>
    /// Spec 048: the player's own settings, saved on this PC (PlayerPrefs, SettingsStore) and applied live. Graphics (a quality preset,
    /// the resolution and window mode, vsync, a frame cap, the field of view, motion blur, head bob), audio (master, effects, ambience,
    /// UI), controls (mouse and gamepad sensitivity, invert Y, the key and button bindings) and comfort (UI scale, colour-blind friendly
    /// ring colours, world labels on or off). Client display and input only: nothing here is a server rule.
    /// <see cref="Current"/> is what the game reads; <see cref="Use"/> makes a sanitized copy current, bumps <see cref="Version"/> (the
    /// SettingsApplier and the camera rig re-apply on a change) and hands the shared parts on (AudioMix, RingPalette, the bindings).
    /// Plain fields so JsonUtility can store them.
    /// </summary>
    [Serializable]
    public sealed class GameSettings
    {
        public const float MinFieldOfView = 70f;
        public const float MaxFieldOfView = 100f;
        public const float MinSensitivity = 0.2f;
        public const float MaxSensitivity = 3f;
        public const float MinUiScale = 0.75f;
        public const float MaxUiScale = 1.5f;
        /// <summary>The highest frame cap (0 is no cap).</summary>
        public const int MaxFrameCap = 500;
        public const int MinFrameCap = 30;

        // ---- Graphics ----
        public QualityPreset Quality = QualityPreset.High;
        /// <summary>The player picked a resolution or window mode in the settings; until then the game keeps the window it started
        /// with (Unity's own launch settings).</summary>
        public bool DisplaySet;
        public int ResolutionWidth;
        public int ResolutionHeight;
        public WindowMode Window = WindowMode.Borderless;
        public bool VSync = true;
        /// <summary>Frames per second at most (0 = no cap). Also holds while hosting: FishNet's own 500 fps cap is replaced (045).</summary>
        public int FrameCap = 120;
        /// <summary>First-person vertical field of view in degrees (third person follows, 10 degrees narrower).</summary>
        public float FieldOfView = MinFieldOfView;
        public bool MotionBlur;
        public bool HeadBob;

        // ---- Audio (0..1) ----
        public float MasterVolume = 1f;
        public float EffectsVolume = 1f;
        public float AmbienceVolume = 1f;
        public float UiVolume = 1f;

        // ---- Controls ----
        /// <summary>A factor on the spec 010 mouse speed (1 = 0.12 degrees per count).</summary>
        public float MouseSensitivity = 1f;
        /// <summary>A factor on the right stick's turn speed (1 = 160 degrees per second).</summary>
        public float GamepadSensitivity = 1f;
        public bool InvertY;
        /// <summary>ControlBindings.Serialize (only the changed entries; empty = the spec 041 mapping).</summary>
        public string Bindings = string.Empty;

        // ---- Comfort ----
        public float UiScale = 1f;
        public bool ColourBlindRings;
        /// <summary>Station signs and item names in the world (off: the spec 040 test; orders, the rail and buttons still show).</summary>
        public bool Labels = true;

        private static GameSettings _current = new GameSettings();
        private static ControlBindings _bindings = ControlBindings.Defaults();

        /// <summary>The settings the game uses now.</summary>
        public static GameSettings Current => _current;
        /// <summary>The key and button bindings of <see cref="Current"/> (parsed once per <see cref="Use"/>).</summary>
        public static ControlBindings CurrentBindings => _bindings;
        /// <summary>Bumped by every <see cref="Use"/>.</summary>
        public static int Version { get; private set; }

        public static GameSettings Defaults() => new GameSettings();

        public GameSettings Clone() => (GameSettings)MemberwiseClone();

        /// <summary>Makes a sanitized copy current and hands the volumes, the ring colours and the bindings on.</summary>
        public static void Use(GameSettings settings)
        {
            _current = Sanitized(settings);
            _bindings = ControlBindings.Parse(_current.Bindings);
            AudioMix.Set(_current.EffectsVolume, _current.AmbienceVolume, _current.UiVolume);
            RingPalette.Use(_current.ColourBlindRings);
            Version++;
        }

        /// <summary>A copy with every value inside its range (a hand-edited save or an old format never breaks the game).</summary>
        public static GameSettings Sanitized(GameSettings settings)
        {
            GameSettings s = settings != null ? settings.Clone() : Defaults();
            GameSettings d = Defaults();
            if (!Enum.IsDefined(typeof(QualityPreset), s.Quality)) s.Quality = d.Quality;
            if (!Enum.IsDefined(typeof(WindowMode), s.Window)) s.Window = d.Window;
            if (s.ResolutionWidth < 640 || s.ResolutionHeight < 360 || s.ResolutionWidth > 16384 || s.ResolutionHeight > 16384)
            {
                s.ResolutionWidth = 0;
                s.ResolutionHeight = 0;
            }
            if (s.FrameCap != 0) s.FrameCap = s.FrameCap < MinFrameCap ? MinFrameCap : s.FrameCap > MaxFrameCap ? MaxFrameCap : s.FrameCap;
            s.FieldOfView = Range(s.FieldOfView, MinFieldOfView, MaxFieldOfView, d.FieldOfView);
            s.MasterVolume = Range(s.MasterVolume, 0f, 1f, d.MasterVolume);
            s.EffectsVolume = Range(s.EffectsVolume, 0f, 1f, d.EffectsVolume);
            s.AmbienceVolume = Range(s.AmbienceVolume, 0f, 1f, d.AmbienceVolume);
            s.UiVolume = Range(s.UiVolume, 0f, 1f, d.UiVolume);
            s.MouseSensitivity = Range(s.MouseSensitivity, MinSensitivity, MaxSensitivity, d.MouseSensitivity);
            s.GamepadSensitivity = Range(s.GamepadSensitivity, MinSensitivity, MaxSensitivity, d.GamepadSensitivity);
            s.UiScale = Range(s.UiScale, MinUiScale, MaxUiScale, d.UiScale);
            s.Bindings ??= string.Empty;
            return s;
        }

        public string ToJson() => JsonUtility.ToJson(this);

        /// <summary>The saved text back; null for empty or broken text (never throws). Missing fields keep their defaults.</summary>
        public static GameSettings FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                GameSettings parsed = Defaults();
                JsonUtility.FromJsonOverwrite(json, parsed);
                return Sanitized(parsed);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static float Range(float value, float min, float max, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return fallback;
            return value < min ? min : value > max ? max : value;
        }
    }
}
