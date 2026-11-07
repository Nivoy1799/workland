using Unity.Cinemachine;
using UnityEngine;

namespace Worktown.Game.Player
{
    /// <summary>
    /// Binds the scene's single Cinemachine third-person camera to the local player (spec 010):
    /// orbit with mouse or right stick, zoom with the wheel. Cinemachine does the follow damping
    /// and keeps walls out of the view (Deoccluder). Only the owner calls <see cref="Bind"/>.
    /// </summary>
    public sealed class PlayerCameraRig : MonoBehaviour
    {
        private const float MouseSensitivity = 0.12f;   // degrees per mouse count
        private const float StickSensitivity = 160f;    // degrees per second at full tilt
        private const float ZoomStep = 0.0012f;         // radial scale per wheel count

        private CinemachineOrbitalFollow _orbit;
        private PlayerInputReader _input;

        public bool IsBound => _orbit != null;

        /// <summary>Points the scene camera at the smoothed graphic of the local player.</summary>
        public void Bind(Transform followTarget, PlayerInputReader input)
        {
            var cam = FindFirstObjectByType<CinemachineCamera>();
            if (cam == null)
            {
                Debug.LogError("[Camera] no CinemachineCamera in the scene");
                return;
            }
            cam.Follow = followTarget;
            cam.LookAt = followTarget;
            _orbit = cam.GetComponent<CinemachineOrbitalFollow>();
            _input = input;
            // Start behind the player.
            if (_orbit != null) _orbit.HorizontalAxis.Value = followTarget.eulerAngles.y;
        }

        private void LateUpdate()
        {
            if (_orbit == null || _input == null) return;

            Vector2 look = _input.Look;
            if (_input.IsGamepadLook) look *= StickSensitivity * Time.deltaTime;
            else look *= MouseSensitivity;

            _orbit.HorizontalAxis.Value = Wrap(_orbit.HorizontalAxis.Value + look.x, _orbit.HorizontalAxis.Range);
            _orbit.VerticalAxis.Value = Mathf.Clamp(_orbit.VerticalAxis.Value - look.y,
                _orbit.VerticalAxis.Range.x, _orbit.VerticalAxis.Range.y);
            _orbit.RadialAxis.Value = Mathf.Clamp(_orbit.RadialAxis.Value - _input.Zoom * ZoomStep,
                _orbit.RadialAxis.Range.x, _orbit.RadialAxis.Range.y);
        }

        private static float Wrap(float value, Vector2 range)
        {
            float size = range.y - range.x;
            if (size <= 0f) return value;
            while (value > range.y) value -= size;
            while (value < range.x) value += size;
            return value;
        }
    }
}
