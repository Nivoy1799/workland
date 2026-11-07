using System;
using Unity.Cinemachine;
using UnityEngine;
using Worktown.Game.Net;

namespace Worktown.Game.Player
{
    /// <summary>
    /// Binds the scene's two Cinemachine cameras to the local player. Third person (spec 010): orbit with
    /// mouse or right stick, zoom with the wheel, Cinemachine keeps walls out of the view. First person
    /// (spec 018, default): the camera sits at the player's eyes, mouse or stick pan and tilt it. V or the
    /// right stick press switches; the live camera gets the higher priority and the brain blends.
    /// Client only; only the owner calls <see cref="Bind"/>.
    /// </summary>
    public sealed class PlayerCameraRig : MonoBehaviour
    {
        private const float MouseSensitivity = 0.12f;   // degrees per mouse count (same in both views)
        private const float StickSensitivity = 160f;    // degrees per second at full tilt
        private const float ZoomStep = 0.0012f;         // radial scale per wheel count
        private const int LivePriority = 20;
        private const int IdlePriority = 10;

        [SerializeField] private CinemachineCamera _thirdPerson;
        [SerializeField] private CinemachineCamera _firstPerson;
        [Tooltip("Anchor in front of the main camera where the held food is drawn in first person.")]
        [SerializeField] private Transform _viewHold;

        private CinemachineOrbitalFollow _orbit;
        private CinemachinePanTilt _panTilt;
        private PlayerInputReader _input;

        /// <summary>The view the local player looks through.</summary>
        public ViewMode Mode { get; private set; } = ViewMode.FirstPerson;
        /// <summary>Raised after every change of <see cref="Mode"/>, including the start mode on bind.</summary>
        public event Action<ViewMode> ModeChanged;

        /// <summary>True once the local player has bound the cameras.</summary>
        public bool IsBound => _input != null;
        public bool IsFirstPerson => IsBound && Mode == ViewMode.FirstPerson;
        public Transform ViewHold => _viewHold;

        /// <summary>
        /// Points both cameras at the local player: third person orbits the smoothed graphic, first person
        /// sits at the eyes. Starts in the <c>-view</c> mode, else the saved one, else first person.
        /// </summary>
        public void Bind(Transform followTarget, Transform eyes, PlayerInputReader input)
        {
            if (_thirdPerson == null || _firstPerson == null)
            {
                Debug.LogError("[Camera] PlayerCameraRig needs a third-person and a first-person CinemachineCamera");
                return;
            }
            _thirdPerson.Follow = followTarget;
            _thirdPerson.LookAt = followTarget;
            _firstPerson.Follow = eyes != null ? eyes : followTarget;
            _orbit = _thirdPerson.GetComponent<CinemachineOrbitalFollow>();
            _panTilt = _firstPerson.GetComponent<CinemachinePanTilt>();
            _input = input;

            // Start looking where the body faces.
            float yaw = followTarget.eulerAngles.y;
            if (_orbit != null) _orbit.HorizontalAxis.Value = Wrap(yaw, _orbit.HorizontalAxis.Range);
            if (_panTilt != null)
            {
                _panTilt.PanAxis.Value = Wrap(yaw, _panTilt.PanAxis.Range);
                _panTilt.TiltAxis.Value = 0f;
            }

            ViewMode? launch = null;
            NetBootstrap bootstrap = FindFirstObjectByType<NetBootstrap>();
            if (bootstrap != null) launch = bootstrap.Args.View;
            ViewMode? saved = ViewRules.FromSaved(PlayerPrefs.GetInt(ViewRules.SavedModeKey, -1));
            SetMode(ViewRules.StartMode(launch, saved), keepLook: false, save: false);
        }

        private void SetMode(ViewMode mode, bool keepLook, bool save)
        {
            if (keepLook && _orbit != null && _panTilt != null)
            {
                // Keep the look direction: same yaw, first person starts level.
                if (mode == ViewMode.FirstPerson)
                {
                    _panTilt.PanAxis.Value = Wrap(_orbit.HorizontalAxis.Value, _panTilt.PanAxis.Range);
                    _panTilt.TiltAxis.Value = 0f;
                }
                else
                {
                    _orbit.HorizontalAxis.Value = Wrap(_panTilt.PanAxis.Value, _orbit.HorizontalAxis.Range);
                }
            }
            _firstPerson.Priority = mode == ViewMode.FirstPerson ? LivePriority : IdlePriority;
            _thirdPerson.Priority = mode == ViewMode.ThirdPerson ? LivePriority : IdlePriority;
            Mode = mode;
            if (save)
            {
                PlayerPrefs.SetInt(ViewRules.SavedModeKey, (int)mode);
                PlayerPrefs.Save();
            }
            ModeChanged?.Invoke(mode);
        }

        private void LateUpdate()
        {
            if (_input == null) return;
            if (_input.ConsumeToggleView()) SetMode(ViewRules.Toggle(Mode), keepLook: true, save: true);

            Vector2 look = _input.Look;
            if (_input.IsGamepadLook) look *= StickSensitivity * Time.deltaTime;
            else look *= MouseSensitivity;

            if (Mode == ViewMode.FirstPerson)
            {
                if (_panTilt == null) return;
                _panTilt.PanAxis.Value = Wrap(_panTilt.PanAxis.Value + look.x, _panTilt.PanAxis.Range);
                _panTilt.TiltAxis.Value = ViewRules.ClampPitch(_panTilt.TiltAxis.Value - look.y);
                return;
            }

            if (_orbit == null) return;
            _orbit.HorizontalAxis.Value = Wrap(_orbit.HorizontalAxis.Value + look.x, _orbit.HorizontalAxis.Range);
            _orbit.VerticalAxis.Value = Mathf.Clamp(_orbit.VerticalAxis.Value - look.y,
                _orbit.VerticalAxis.Range.x, _orbit.VerticalAxis.Range.y);
            // The wheel zooms only in third person.
            _orbit.RadialAxis.Value = Mathf.Clamp(_orbit.RadialAxis.Value - _input.Zoom * ZoomStep,
                _orbit.RadialAxis.Range.x, _orbit.RadialAxis.Range.y);
        }

        private static float Wrap(float value, Vector2 range)
        {
            float size = range.y - range.x;
            if (size <= 0f || float.IsNaN(value) || float.IsInfinity(value)) return value;
            while (value > range.y) value -= size;
            while (value < range.x) value += size;
            return value;
        }
    }
}
