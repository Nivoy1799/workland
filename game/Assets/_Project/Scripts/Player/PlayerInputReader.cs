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
        private InputAction _jump;
        private bool _active;
        private bool _crouchToggled;
        private bool _jumpPressed;
        private bool _togglePressed;
        private int _uiOpenCount;

        /// <summary>Spec 011: a menu or banner is open, so the cursor stays free and clicks go to the UI.</summary>
        public bool IsUiOpen => _uiOpenCount > 0;

        public Vector2 Move => _active && !IsUiOpen ? _move.ReadValue<Vector2>() : Vector2.zero;
        public bool Sprint => _active && _sprint.IsPressed();
        /// <summary>Crouch toggles on each press (C / gamepad east).</summary>
        public bool Crouch => _active && _crouchToggled;
        /// <summary>Spec 012: true while the emote wheel uses the mouse, so the camera stays still.</summary>
        public bool SuppressLook { get; set; }

        /// <summary>Camera look delta this frame; zero while the cursor is free or look is suppressed.</summary>
        public Vector2 Look => _active && !SuppressLook && Cursor.lockState == CursorLockMode.Locked ? _look.ReadValue<Vector2>() : Vector2.zero;
        public bool IsGamepadLook => _look != null && _look.activeControl != null && _look.activeControl.device is Gamepad;
        /// <summary>Mouse wheel this frame (positive = zoom in).</summary>
        public float Zoom => _active && Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;

        /// <summary>True once per Jump press (Space / gamepad south); the next call returns false
        /// until the button is pressed again. Read once per network tick.</summary>
        public bool ConsumeJump()
        {
            bool pressed = _jumpPressed;
            _jumpPressed = false;
            return _active && pressed;
        }

        /// <summary>Spec 018: true once per V (keyboard) or right stick press (gamepad). False while a menu or
        /// banner is open, or while quick chat (T) or the emote wheel (G / LB) is held. Read once per frame.</summary>
        public bool ConsumeToggleView()
        {
            bool pressed = _togglePressed;
            _togglePressed = false;
            if (!_active || !pressed || IsUiOpen || SuppressLook) return false;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.tKey.isPressed || keyboard.gKey.isPressed)) return false;
            Gamepad gamepad = Gamepad.current;
            if (gamepad != null && gamepad.leftShoulder.isPressed) return false;
            return true;
        }

        /// <summary>Spec 011: menus call this when they open (true) and close (false).</summary>
        public void SetUiOpen(bool open)
        {
            _uiOpenCount = Mathf.Max(0, _uiOpenCount + (open ? 1 : -1));
            if (IsUiOpen) LockCursor(false);
            else if (_active) LockCursor(true);
        }

        /// <summary>Called by the owner's player when it spawns.</summary>
        public void Activate()
        {
            if (_active || _actions == null) return;
            InputActionMap map = _actions.FindActionMap("Player", true);
            _move = map.FindAction("Move", true);
            _look = map.FindAction("Look", true);
            _sprint = map.FindAction("Sprint", true);
            _crouch = map.FindAction("Crouch", true);
            _jump = map.FindAction("Jump", true);
            map.Enable();
            _active = true;
            if (!IsUiOpen) LockCursor(true);
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
            if (_jump.WasPressedThisFrame()) _jumpPressed = true;

            Keyboard keyboard = Keyboard.current;
            Gamepad gamepad = Gamepad.current;
            if ((keyboard != null && keyboard.vKey.wasPressedThisFrame)
                || (gamepad != null && gamepad.rightStickButton.wasPressedThisFrame))
                _togglePressed = true;

            if (keyboard != null && (keyboard.escapeKey.wasPressedThisFrame || keyboard.leftAltKey.wasPressedThisFrame))
                LockCursor(false);
            else if (!IsUiOpen && Cursor.lockState != CursorLockMode.Locked && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
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
