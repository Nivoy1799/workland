using System;
using UnityEngine;

namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 027b: every feel number in one place (docs/feel.md), with presets A (this spec's defaults) and B (smoother and
    /// heavier: tweens about 1.6x longer, a 9 degree cone, more camera damping, a slightly wider third-person framing).
    /// Client visuals only: nothing here is a server rule (reach, aim range, place grid, rate limits stay where they are).
    /// <see cref="Current"/> is what the game reads; the F2 panel (development builds) changes it live and bumps
    /// <see cref="Version"/>, so readers that cache (the camera rig) re-apply it. Plain fields so JsonUtility can save them
    /// (feel.json).
    /// </summary>
    [Serializable]
    public sealed class FeelSettings
    {
        public const string PresetAName = "A";
        public const string PresetBName = "B";
        public const string CustomName = "custom";
        /// <summary>How much longer preset B's tweens are than A's.</summary>
        public const float PresetBTweenScale = 1.6f;

        [Tooltip("s: a predicted visual without a confirming state change flies back after this (from the last relevant change).")]
        public float ConfirmTimeout = 0.35f;
        [Tooltip("s: pick-up arc into the hand.")]
        public float PickupSeconds = 0.15f;
        [Tooltip("m: how high the pick-up arc lifts in its middle.")]
        public float PickupLift = 0.08f;
        [Tooltip("s: ease onto the place spot.")]
        public float PlaceSeconds = 0.12f;
        [Tooltip("Fraction of the travel the item sinks past the spot before it settles (0.03 = 3 %).")]
        public float PlaceSettle = 0.03f;
        [Tooltip("s: food dropping into a pot, pan, board or plate.")]
        public float DropSeconds = 0.12f;
        [Tooltip("m: height above the contents the food drops from.")]
        public float DropHeight = 0.12f;
        [Tooltip("Degrees: interactables within this cone around the crosshair can be the target.")]
        public float AimCone = 6f;
        [Tooltip("The current target stays until another one scores this much better (0.25 = 25 %).")]
        public float StickMargin = 0.25f;
        [Tooltip("Score bonus for cookware that takes what you hold (0.4 = its angle counts 40 % less).")]
        public float HeldBonus = 0.4f;
        [Tooltip("s: a target (highlight, prompt) shows at least this long; no single-frame flicker.")]
        public float HighlightMinSeconds = 0.1f;
        [Tooltip("s: third-person follow damping (position).")]
        public float ThirdPersonDamping = 0.08f;
        [Tooltip("s: third-person aim damping (composer).")]
        public float ComposerDamping = 0.05f;
        [Tooltip("m: third-person camera distance from the look point.")]
        public float ThirdPersonDistance = 3.2f;
        [Tooltip("m: height of the third-person look point above the feet.")]
        public float ThirdPersonHeight = 1.3f;
        [Tooltip("Degrees: default third-person camera pitch above the look point.")]
        public float ThirdPersonPitch = 12f;
        [Tooltip("First-person head bob (docs/feel.md 5: at most very soft, off by default).")]
        public bool HeadBob;
        [Tooltip("m: head bob height at walking speed.")]
        public float HeadBobAmount = 0.015f;
        [Tooltip("Which preset these values came from (A, B or custom once a slider moved).")]
        public string Preset = PresetAName;

        private static FeelSettings _current = PresetA();

        /// <summary>The settings the game uses now (a copy is handed in by <see cref="Use"/>).</summary>
        public static FeelSettings Current => _current;
        /// <summary>Bumped by every <see cref="Use"/>; readers that apply values once compare it.</summary>
        public static int Version { get; private set; }

        /// <summary>Makes a sanitized copy of <paramref name="settings"/> the current one.</summary>
        public static void Use(FeelSettings settings)
        {
            _current = Sanitized(settings ?? PresetA());
            Version++;
        }

        /// <summary>This spec's defaults.</summary>
        public static FeelSettings PresetA() => new FeelSettings();

        /// <summary>The smoother, heavier variant.</summary>
        public static FeelSettings PresetB()
        {
            FeelSettings b = PresetA();
            b.PickupSeconds *= PresetBTweenScale;
            b.PlaceSeconds *= PresetBTweenScale;
            b.DropSeconds *= PresetBTweenScale;
            b.AimCone = 9f;
            b.ThirdPersonDamping = 0.12f;
            b.ComposerDamping = 0.08f;
            b.ThirdPersonDistance = 3.8f;
            b.ThirdPersonPitch = 15f;
            b.ThirdPersonHeight = 1.35f;
            b.Preset = PresetBName;
            return b;
        }

        public FeelSettings Clone() => (FeelSettings)MemberwiseClone();

        /// <summary>
        /// A copy with every number finite and inside a sane range (a hand-edited feel.json or a slider at its end can
        /// never make a tween divide by zero or the camera sit inside the head).
        /// </summary>
        public static FeelSettings Sanitized(FeelSettings settings)
        {
            FeelSettings s = settings != null ? settings.Clone() : PresetA();
            FeelSettings d = PresetA();
            s.ConfirmTimeout = Range(s.ConfirmTimeout, 0.05f, 3f, d.ConfirmTimeout);
            s.PickupSeconds = Range(s.PickupSeconds, 0f, 1f, d.PickupSeconds);
            s.PickupLift = Range(s.PickupLift, 0f, 0.5f, d.PickupLift);
            s.PlaceSeconds = Range(s.PlaceSeconds, 0f, 1f, d.PlaceSeconds);
            s.PlaceSettle = Range(s.PlaceSettle, 0f, 0.2f, d.PlaceSettle);
            s.DropSeconds = Range(s.DropSeconds, 0f, 1f, d.DropSeconds);
            s.DropHeight = Range(s.DropHeight, 0f, 0.5f, d.DropHeight);
            s.AimCone = Range(s.AimCone, 0f, 20f, d.AimCone);
            s.StickMargin = Range(s.StickMargin, 0f, 0.9f, d.StickMargin);
            s.HeldBonus = Range(s.HeldBonus, 0f, 0.9f, d.HeldBonus);
            s.HighlightMinSeconds = Range(s.HighlightMinSeconds, 0f, 0.5f, d.HighlightMinSeconds);
            s.ThirdPersonDamping = Range(s.ThirdPersonDamping, 0f, 1f, d.ThirdPersonDamping);
            s.ComposerDamping = Range(s.ComposerDamping, 0f, 1f, d.ComposerDamping);
            s.ThirdPersonDistance = Range(s.ThirdPersonDistance, 1.5f, 8f, d.ThirdPersonDistance);
            s.ThirdPersonHeight = Range(s.ThirdPersonHeight, 0.5f, 2.2f, d.ThirdPersonHeight);
            s.ThirdPersonPitch = Range(s.ThirdPersonPitch, -10f, 60f, d.ThirdPersonPitch);
            s.HeadBobAmount = Range(s.HeadBobAmount, 0f, 0.05f, d.HeadBobAmount);
            if (string.IsNullOrEmpty(s.Preset)) s.Preset = CustomName;
            return s;
        }

        public string ToJson() => JsonUtility.ToJson(this, true);

        /// <summary>Parses feel.json; null for empty or broken text (never throws).</summary>
        public static FeelSettings FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                FeelSettings parsed = JsonUtility.FromJson<FeelSettings>(json);
                return parsed != null ? Sanitized(parsed) : null;
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
