using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Worktown.Game.Settings
{
    /// <summary>
    /// Spec 048, pure: what the settings mean for the engine and how the settings screen words them. The SettingsApplier, the camera rig
    /// and the input readers only carry these out.
    /// </summary>
    public static class SettingsRules
    {
        /// <summary>The PlayerPrefs key holding <see cref="GameSettings.ToJson"/>.</summary>
        public const string PrefsKey = "worktown.settings";

        /// <summary>The spec 010 mouse speed in degrees per count at sensitivity 1 (both views).</summary>
        public const float MouseDegreesPerCount = 0.12f;
        /// <summary>The spec 010 stick speed in degrees per second at full tilt and sensitivity 1.</summary>
        public const float StickDegreesPerSecond = 160f;
        /// <summary>The third-person camera's field of view at the first-person default (70): the scene's 60.</summary>
        public const float ThirdPersonFovOffset = -10f;
        /// <summary>Motion blur strength when it is on (URP, camera only).</summary>
        public const float MotionBlurIntensity = 0.35f;

        /// <summary>The frame caps the settings offer (0 = no cap).</summary>
        public static readonly int[] FrameCaps = { 30, 60, 90, 120, 144, 165, 240, 0 };

        /// <summary>
        /// Spec 038b's lesson: a scripted dev run (<c>-tour</c>, <c>-observe</c>: the visual check; <c>-bot</c>, and a host or server with
        /// <c>-metrics</c>: the load test and spec 049's perf check) neither reads nor writes the player's saved settings. It runs on the
        /// defaults, so its pictures and numbers never depend on this PC's settings, and anything it changes stays in memory.
        /// 048 review: from the parsed command line (Net.Rules.LaunchArgs), not a second parse; null = a normal start.
        /// </summary>
        public static bool UsesSavedSettings(Net.Rules.LaunchArgs args)
        {
            if (args == null) return true;
            return args.Tour.Length == 0 && args.Observe.Length == 0 && !args.IsBot && args.MetricsPath.Length == 0;
        }

        /// <summary>Application.targetFrameRate for a cap (-1, the platform's own, for no cap).</summary>
        public static int TargetFrameRate(int cap) => cap > 0 ? cap : -1;

        /// <summary>QualitySettings.vSyncCount: every refresh with vsync, else none.</summary>
        public static int VSyncCount(bool vsync) => vsync ? 1 : 0;

        public static FullScreenMode ToFullScreenMode(WindowMode mode)
        {
            switch (mode)
            {
                case WindowMode.Fullscreen: return FullScreenMode.ExclusiveFullScreen;
                case WindowMode.Windowed: return FullScreenMode.Windowed;
                default: return FullScreenMode.FullScreenWindow;
            }
        }

        public static WindowMode FromFullScreenMode(FullScreenMode mode)
        {
            switch (mode)
            {
                case FullScreenMode.ExclusiveFullScreen: return WindowMode.Fullscreen;
                case FullScreenMode.Windowed: return WindowMode.Windowed;
                default: return WindowMode.Borderless;
            }
        }

        /// <summary>The first-person camera's vertical field of view (70..100).</summary>
        public static float FirstPersonFov(float fov) =>
            float.IsNaN(fov) ? GameSettings.MinFieldOfView : Mathf.Clamp(fov, GameSettings.MinFieldOfView, GameSettings.MaxFieldOfView);

        /// <summary>The third-person camera's: the first-person one, 10 degrees narrower (60 at the default).</summary>
        public static float ThirdPersonFov(float fov) => FirstPersonFov(fov) + ThirdPersonFovOffset;

        /// <summary>
        /// The camera turn in degrees for this frame's look input: a mouse delta (counts) times the mouse speed, or a stick
        /// (-1..1) times the stick speed and <paramref name="deltaTime"/>; with invert Y the vertical turn flips.
        /// </summary>
        public static Vector2 LookDegrees(Vector2 raw, bool gamepad, float deltaTime, GameSettings settings)
        {
            settings ??= GameSettings.Current;
            Vector2 look = gamepad
                ? raw * (StickDegreesPerSecond * settings.GamepadSensitivity * deltaTime)
                : raw * (MouseDegreesPerCount * settings.MouseSensitivity);
            if (settings.InvertY) look.y = -look.y;
            return look;
        }

        /// <summary>The render pipeline's render scale for a preset (048 review: High = the pipeline asset's own <paramref name="original"/>).</summary>
        public static float RenderScale(QualityPreset preset, float original) =>
            preset == QualityPreset.Low ? 0.7f : preset == QualityPreset.Medium ? 0.85f : original;

        /// <summary>The shadow distance in metres for a preset (High = the project's 50); null keeps the asset's own.</summary>
        public static float? ShadowDistance(QualityPreset preset) => preset == QualityPreset.Low ? 15f : preset == QualityPreset.Medium ? 30f : (float?)null;

        /// <summary>The shadow cascades for a preset (High keeps the asset's 4).</summary>
        public static int? ShadowCascades(QualityPreset preset) => preset == QualityPreset.Low ? 1 : preset == QualityPreset.Medium ? 2 : (int?)null;

        /// <summary>The resolutions to offer: each width and height once, the largest first, none below 640 x 360.</summary>
        public static List<Vector2Int> UniqueResolutions(IEnumerable<Vector2Int> sizes)
        {
            var list = new List<Vector2Int>();
            if (sizes != null)
                foreach (Vector2Int size in sizes)
                    if (size.x >= 640 && size.y >= 360 && !list.Contains(size)) list.Add(size);
            list.Sort((a, b) => a.x != b.x ? b.x.CompareTo(a.x) : b.y.CompareTo(a.y));
            return list;
        }

        /// <summary>The index of <paramref name="cap"/> in <see cref="FrameCaps"/>, or of the nearest one.</summary>
        public static int FrameCapIndex(int cap)
        {
            int best = 0;
            for (int i = 0; i < FrameCaps.Length; i++)
            {
                if (FrameCaps[i] == cap) return i;
                if (cap > 0 && FrameCaps[i] > 0 && Math.Abs(FrameCaps[i] - cap) < Math.Abs(FrameCaps[best] - cap)) best = i;
            }
            return cap == 0 ? FrameCaps.Length - 1 : best;
        }

        // ---- Words ----

        public static string PageTitle(SettingsPage page) => page.ToString();

        public static string FrameCapText(int cap) => cap > 0 ? cap + " fps" : "No cap";

        public static string WindowText(WindowMode mode) =>
            mode == WindowMode.Fullscreen ? "Full screen" : mode == WindowMode.Windowed ? "Window" : "Borderless";

        public static string ResolutionText(int width, int height) => width > 0 && height > 0 ? width + " x " + height : "As started";

        public static string PercentText(float share) => Mathf.RoundToInt(Mathf.Clamp01(share) * 100f) + " %";

        public static string ActionText(ControlAction action)
        {
            switch (action)
            {
                case ControlAction.MoveForward: return "Walk forward";
                case ControlAction.MoveBack: return "Walk back";
                case ControlAction.MoveLeft: return "Walk left";
                case ControlAction.MoveRight: return "Walk right";
                case ControlAction.Sprint: return "Run";
                case ControlAction.Crouch: return "Sneak";
                case ControlAction.Jump: return "Jump";
                case ControlAction.ToggleView: return "First / third person";
                case ControlAction.Use: return "Use (third person)";
                case ControlAction.PutDown: return "Put down";
                case ControlAction.Turn: return "Turn one step";
                case ControlAction.QuickChat: return "Quick chat (hold)";
                case ControlAction.EmoteWheel: return "Emote wheel (hold)";
                case ControlAction.Ping: return "Ping";
                case ControlAction.Players: return "Players in the shop";
                default: return action.ToString();
            }
        }

        /// <summary>A key as the settings show it: "W", "Left Shift", "1", "-" for none.</summary>
        public static string KeyText(Key key)
        {
            if (key == Key.None) return "-";
            string name = key.ToString();
            if (name.StartsWith("Digit", StringComparison.Ordinal) && name.Length == 6) return name.Substring(5);
            return SplitWords(name);
        }

        public static string PadText(PadButton button)
        {
            switch (button)
            {
                case PadButton.None: return "-";
                case PadButton.South: return "A";
                case PadButton.East: return "B";
                case PadButton.West: return "X";
                case PadButton.North: return "Y";
                case PadButton.LeftShoulder: return "LB";
                case PadButton.RightShoulder: return "RB";
                case PadButton.LeftStick: return "Left stick press";
                case PadButton.RightStick: return "Right stick press";
                case PadButton.Select: return "View / Select";
                default: return button.ToString();
            }
        }

        /// <summary>The line after a rebind: what changed, and a swap's other action by name.</summary>
        public static string RebindText(ControlAction action, RebindResult result, string newControl, string oldControl)
        {
            string name = ActionText(action);
            switch (result.Outcome)
            {
                case RebindOutcome.Bound: return name + ": " + newControl;
                case RebindOutcome.Cleared: return name + ": no key now";
                case RebindOutcome.Unchanged: return name + " already uses " + newControl;
                case RebindOutcome.Reserved: return newControl + " is kept for the game's menus. Pick another one.";
                case RebindOutcome.Swapped:
                    return newControl + " was " + ActionText(result.Other ?? action) + ": swapped, " + ActionText(result.Other ?? action)
                        + " now uses " + oldControl;
                default: return string.Empty;
            }
        }

        private static string SplitWords(string name)
        {
            var sb = new System.Text.StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1])) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
