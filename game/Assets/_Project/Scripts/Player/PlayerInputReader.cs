using UnityEngine;
using UnityEngine.InputSystem;

namespace Worktown.Game.Player
{
    /// <summary>
    /// Owner-only input from the project's default <c>Player</c> action map (spec 010): Move, Look,
    /// Sprint, Crouch. Keyboard, mouse and gamepad come with the template bindings.
    /// Action-style mouse: cursor locked while playing, Esc or Alt frees it, a click locks it again.
    /// </summary>
    public sealed class PlayerInputReader : MonoBehaviour
    {
        [SerializeField] private InputActionAsset _actions;

        private InputAction _move;
        private InputAction _look;
        private InputAction _sprint;
        private InputAction _crouch;
        private bool _active;
        private bool _crouchToggled;

        public Vector2 Move => _active ? _move.ReadValue<Vector2>() : Vector2.zero;
        public bool Sprint => _active && _sprint.IsPressed();
        /// <summary>Crouch toggles on each press (C / gamepad east).</summary>
        public bool Crouch => _active && _crouchToggled;
        /// <summary>Camera look delta this frame; zero while the cursor is free.</summary>
        public Vector2 Look => _active && Cursor.lockState == CursorLockMode.Locked ? _look.ReadValue<Vector2>() : Vector2.zero;
        public bool IsGamepadLook => _look != null && _look.activeControl != null && _look.activeControl.device is Gamepad;
        /// <summary>Mouse wheel this frame (positive = zoom in).</summary>
        public float Zoom => _active && Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;

        /// <summary>Called by the owner's player when it spawns.</summary>
        public void Activate()
        {
            if (_active || _actions == null) return;
            InputActionMap map = _actions.FindActionMap("Player", true);
            _move = map.FindAction("Move", true);
            _look = map.FindAction("Look", true);
            _sprint = map.FindAction("Sprint", true);
            _crouch = map.FindAction("Crouch", true);
            map.Enable();
            _active = true;
            LockCursor(true);
        }

        public void Deactivate()
        {
            if (!_active) return;
            _active = false;
            LockCursor(false);
        }

        private void Update()
        {
            if (!_active) return;
            if (_crouch.WasPressedThisFrame()) _crouchToggled = !_crouchToggled;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.escapeKey.wasPressedThisFrame || keyboard.leftAltKey.wasPressedThisFrame))
                LockCursor(false);
            else if (Cursor.lockState != CursorLockMode.Locked && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                LockCursor(true);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && _active) LockCursor(false);
        }

        private static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void OnDestroy()
        {
            Deactivate();
        }
    }
}
