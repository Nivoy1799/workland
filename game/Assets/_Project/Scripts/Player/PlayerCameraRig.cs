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
        private CinemachineRotationComposer _composer;
        private PlayerInputReader _input;
        // Spec 027b: the feel numbers last applied, and the optional first-person head bob.
        private int _feelVersion = -1;
        private CinemachineCameraOffset _bob;
        private Transform _eyes;
        private Vector3 _lastEyes;
        private float _bobPhase;
        private float _bobSpeed;

        /// <summary>The view the local player looks through.</summary>
        public ViewMode Mode { get; private set; } = ViewMode.FirstPerson;
        /// <summary>Raised after every change of <see cref="Mode"/>, including the start mode on bind.</summary>
        public event Action<ViewMode> ModeChanged;

        /// <summary>True once the local player has bound the cameras.</summary>
        public bool IsBound => _input != null;
        public bool IsFirstPerson => IsBound && Mode == ViewMode.FirstPerson;
        public Transform ViewHold => _viewHold;

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        /// <summary>Spec 027, development builds only: while set, look and view come from the script, not the devices.
        /// Set by the owner's PlayerController.</summary>
        public PlayerController.IIntentSource IntentSource { get; set; }
#endif

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
            _composer = _thirdPerson.GetComponent<CinemachineRotationComposer>();
            _input = input;
            _eyes = eyes;
            ApplyFeel();

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
            ApplyFeel();
            UpdateHeadBob();
            Vector2 look;
            float zoom;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (IntentSource != null)
            {
                // Spec 027: scripted look in degrees, no sensitivity; a scripted view change is never saved.
                ViewMode? wanted = IntentSource.View;
                if (wanted.HasValue && wanted.Value != Mode) SetMode(wanted.Value, keepLook: true, save: false);
                look = IntentSource.ConsumeLook();
                zoom = 0f;
            }
            else
#endif
            {
                if (_input.ConsumeToggleView()) SetMode(ViewRules.Toggle(Mode), keepLook: true, save: true);
                look = _input.Look;
                if (_input.IsGamepadLook) look *= StickSensitivity * Time.deltaTime;
                else look *= MouseSensitivity;
                zoom = _input.Zoom;
            }

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
            _orbit.RadialAxis.Value = Mathf.Clamp(_orbit.RadialAxis.Value - zoom * ZoomStep,
                _orbit.RadialAxis.Range.x, _orbit.RadialAxis.Range.y);
        }

        /// <summary>
        /// Spec 027b: the third-person numbers from <see cref="FeelSettings.Current"/> (distance, look height, pitch,
        /// follow and aim damping), applied on bind and again whenever the F2 panel changes them. Mouse smoothing stays off.
        /// </summary>
        private void ApplyFeel()
        {
            if (_feelVersion == FeelSettings.Version) return;
            _feelVersion = FeelSettings.Version;
            FeelSettings feel = FeelSettings.Current;
            if (_orbit != null)
            {
                _orbit.Radius = feel.ThirdPersonDistance;
                _orbit.TargetOffset = new Vector3(0f, feel.ThirdPersonHeight, 0f);
                _orbit.VerticalAxis.Center = feel.ThirdPersonPitch;
                _orbit.VerticalAxis.Value = Mathf.Clamp(feel.ThirdPersonPitch, _orbit.VerticalAxis.Range.x, _orbit.VerticalAxis.Range.y);
                _orbit.TrackerSettings.PositionDamping = Vector3.one * feel.ThirdPersonDamping;
            }
            if (_composer != null)
            {
                _composer.TargetOffset = new Vector3(0f, feel.ThirdPersonHeight, 0f);
                _composer.Damping = Vector2.one * feel.ComposerDamping;
            }
            if (feel.HeadBob && _bob == null && _firstPerson != null)
            {
                _bob = _firstPerson.GetComponent<CinemachineCameraOffset>();
                if (_bob == null) _bob = _firstPerson.gameObject.AddComponent<CinemachineCameraOffset>();
            }
        }

        /// <summary>Spec 027b, docs/feel.md 5: an optional, very soft first-person head bob (off by default), one dip per
        /// step at the walking cadence.</summary>
        private void UpdateHeadBob()
        {
            if (_bob == null) return;
            FeelSettings feel = FeelSettings.Current;
            float dt = Time.deltaTime;
            Vector3 eyes = _eyes != null ? _eyes.position : Vector3.zero;
            Vector3 delta = eyes - _lastEyes;
            _lastEyes = eyes;
            delta.y = 0f;
            float speed = dt > 0f ? Mathf.Min(delta.magnitude / dt, 10f) : 0f;
            _bobSpeed = CharacterAnimRules.SmoothSpeed(_bobSpeed, speed, dt);
            _bobPhase += dt * Mathf.PI * CharacterAnimRules.TargetCadence(_bobSpeed);
            float strength = Mathf.Clamp01(_bobSpeed / MovementRules.WalkSpeed);
            bool on = feel.HeadBob && IsFirstPerson && _bobSpeed > CharacterAnimRules.WalkStopSpeed;
            _bob.Offset = on ? new Vector3(0f, -feel.HeadBobAmount * strength * Mathf.Abs(Mathf.Sin(_bobPhase)), 0f) : Vector3.zero;
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
