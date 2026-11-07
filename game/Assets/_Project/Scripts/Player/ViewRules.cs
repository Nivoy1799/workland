using UnityEngine;

namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 018: numbers and small pure decisions for the first-person view. Client side only; nothing
    /// here changes what the server allows.
    /// </summary>
    public static class ViewRules
    {
        /// <summary>Eye anchor height above the feet while standing, in metres.</summary>
        public const float EyeHeight = 1.6f;
        /// <summary>Eye anchor height while crouched (the capsule is 1.2 m then).</summary>
        public const float CrouchEyeHeight = 1.0f;
        /// <summary>Eye anchor in front of the body centre, so the view starts at the face.</summary>
        public const float EyeForward = 0.1f;
        /// <summary>How fast the eyes move between the standing and crouched height (m/s).</summary>
        public const float EyeHeightSpeed = 6f;

        public const float FieldOfView = 70f;
        public const float MinPitch = -80f;
        public const float MaxPitch = 80f;
        /// <summary>Near clip plane of the main camera, so held food and nearby counters do not clip.</summary>
        public const float NearClip = 0.05f;
        /// <summary>Length of the aim ray from the camera centre, in metres.</summary>
        public const float AimRange = 3f;
        /// <summary>Camera blend between the two views (open question 3).</summary>
        public const float BlendSeconds = 0.2f;

        /// <summary>Where the held food's visual sits in front of the first-person camera (right, down, forward).</summary>
        public static readonly Vector3 ViewHoldOffset = new Vector3(0.35f, -0.3f, 0.55f);

        /// <summary>PlayerPrefs key of the last chosen view (open question 2, local only).</summary>
        public const string SavedModeKey = "worktown.view";

        public static ViewMode Toggle(ViewMode mode)
        {
            return mode == ViewMode.FirstPerson ? ViewMode.ThirdPerson : ViewMode.FirstPerson;
        }

        /// <summary>Clamps a first-person pitch (tilt) to <see cref="MinPitch"/> .. <see cref="MaxPitch"/>;
        /// NaN or infinite becomes 0 (level).</summary>
        public static float ClampPitch(float pitch)
        {
            if (float.IsNaN(pitch) || float.IsInfinity(pitch)) return 0f;
            return Mathf.Clamp(pitch, MinPitch, MaxPitch);
        }

        /// <summary>Start view: the launch argument wins, then the saved view, then first person.</summary>
        public static ViewMode StartMode(ViewMode? launchArg, ViewMode? saved)
        {
            if (launchArg.HasValue) return launchArg.Value;
            if (saved.HasValue) return saved.Value;
            return ViewMode.FirstPerson;
        }

        /// <summary>Reads a stored PlayerPrefs value; anything that is not a known mode is "nothing saved".</summary>
        public static ViewMode? FromSaved(int stored)
        {
            if (stored == (int)ViewMode.FirstPerson) return ViewMode.FirstPerson;
            if (stored == (int)ViewMode.ThirdPerson) return ViewMode.ThirdPerson;
            return null;
        }

        /// <summary>Parses the <c>-view</c> value: "first" or "third" (case-insensitive).</summary>
        public static bool TryParse(string value, out ViewMode mode)
        {
            mode = ViewMode.FirstPerson;
            if (value == null) return false;
            switch (value.Trim().ToLowerInvariant())
            {
                case "first":
                    mode = ViewMode.FirstPerson;
                    return true;
                case "third":
                    mode = ViewMode.ThirdPerson;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Eye height for the stance.</summary>
        public static float EyeHeightFor(bool crouched)
        {
            return crouched ? CrouchEyeHeight : EyeHeight;
        }
    }
}
