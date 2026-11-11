using UnityEngine;
using UnityEngine.InputSystem;
using Worktown.Game.Settings;

namespace Worktown.Game.Player
{
    /// <summary>
    /// Owner-only input from the project's default <c>Player</c> action map (spec 010): Move, Look,
    /// Sprint, Crouch. Keyboard, mouse and gamepad come with the template bindings.
    /// Action-style mouse: cursor locked while playing, Esc or Alt frees it, a click locks it again.
    /// Spec 048: the keys and gamepad buttons come from the player's bindings (Settings.GameInput): the keyboard walks with its four
    /// bound keys and the arrows, the Move action keeps only its gamepad stick; run, sneak, jump and the view switch read their
    /// bound key or button. Look stays the action's (mouse delta, right stick).
    /// </summary>
    public sealed class PlayerInputReader : MonoBehaviour
    {
        [SerializeField] private InputActionAsset _actions;

        private InputAction _move;
        private InputAction _look;
        private bool _active;
        private bool _crouchToggled;
        private bool _jumpPressed;
        private bool _togglePressed;
        private int _uiOpenCount;
        private int _lastUiClosedFrame = -1;

        /// <summary>Spec 011: a menu or banner is open, so the cursor stays free and clicks go to the UI.</summary>
        public bool IsUiOpen => _uiOpenCount > 0;
        /// <summary>Spec 048 (review): how many menus are open now (the lobby subtracts its own).</summary>
        public int UiOpenCount => _uiOpenCount;

        public Vector2 Move => _active && !IsUiOpen ? Vector2.ClampMagnitude(_move.ReadValue<Vector2>() + GameInput.KeyboardMove(), 1f) : Vector2.zero;
        public bool Sprint => _active && GameInput.Held(ControlAction.Sprint);
        /// <summary>Crouch toggles on each press (C / gamepad east).</summary>
        public bool Crouch => _active && _crouchToggled;
        /// <summary>Spec 012: true while the emote wheel uses the mouse, so the camera stays still.</summary>
        public bool SuppressLook { get; set; }

        /// <summary>Camera look delta this frame; zero while the cursor is free or look is suppressed.</summary>
        public Vector2 Look => _active && !SuppressLook && Cursor.lockState == CursorLockMode.Locked ? _look.ReadValue<Vector2>() : Vector2.zero;
        public bool IsGamepadLook => _look != null && _look.activeControl != null && _look.activeControl.device is Gamepad;
        /// <summary>Mouse wheel this frame (positive = rolled away): the zoom in third person; spec 041: the turn in first person
        /// (WheelSteps, read by the PickupInteractor).</summary>
        public float Zoom => _active && Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;

        /// <summary>Spec 048 (041 review): the wheel counts of one notch under the Input System's scroll setting (WheelSteps.CountsPerNotch).
        /// Main thread only.</summary>
        public static float WheelCountsPerNotch() => WheelSteps.CountsPerNotch(
            InputSystem.settings == null || InputSystem.settings.scrollDeltaBehavior == InputSettings.ScrollDeltaBehavior.UniformAcrossAllPlatforms,
            Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor);

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
            // Spec 048: quick chat and the emote wheel by their bound keys and buttons (T, G, LB).
            return !GameInput.Held(ControlAction.QuickChat) && !GameInput.Held(ControlAction.EmoteWheel);
        }

        /// <summary>Spec 011: menus call this when they open (true) and close (false).</summary>
        public void SetUiOpen(bool open)
        {
            _uiOpenCount = Mathf.Max(0, _uiOpenCount + (open ? 1 : -1));
            if (!open && !IsUiOpen) _lastUiClosedFrame = Time.frameCount;
            if (IsUiOpen) LockCursor(false);
            // The title screen keeps its free cursor (_active is false there). The real fix for the Esc-on-close bug is
            // CaptureGate.FreesCursor skipping the closing frame; this branch only locks again on close while playing.
            else if (CaptureGate.LocksOnClose(_active, IsUiOpen)) LockCursor(true);
        }

        /// <summary>Called by the owner's player when it spawns.</summary>
        public void Activate()
        {
            if (_active || _actions == null) return;
            InputActionMap map = _actions.FindActionMap("Player", true);
            _move = map.FindAction("Move", true);
            _look = map.FindAction("Look", true);
            // Spec 048: the keyboard walks by the bound keys (GameInput.KeyboardMove); the action keeps its gamepad stick only.
            _move.bindingMask = InputBinding.MaskByGroup("Gamepad");
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

        /// <summary>
        /// Spec 045: a scripted run (a tour, its observer, a load-test bot) drives the player through intents only. The reader stops and
        /// the Player action map is disabled, so the Input System keeps no action monitors on the mouse and keyboard of a window nobody
        /// uses (a 043 cook run's observer logged 22 NullReferenceExceptions inside the Input System's mouse state monitors).
        /// </summary>
        public void DisableForScript()
        {
            Deactivate();
            if (_actions != null) _actions.FindActionMap("Player", true).Disable();
        }

        private void Update()
        {
            if (!_active) return;
            // Spec 048 (review): no jump or crouch from a key pressed in a menu (a rebind capture, the lobby).
            if (CaptureGate.GameKeysLive(IsUiOpen, UI.SettingsView.AnyOpen))
            {
                if (GameInput.Pressed(ControlAction.Crouch)) _crouchToggled = !_crouchToggled;
                if (GameInput.Pressed(ControlAction.Jump)) _jumpPressed = true;
            }
            if (GameInput.Pressed(ControlAction.ToggleView)) _togglePressed = true;

            Keyboard keyboard = Keyboard.current;

            bool escOrAlt = keyboard != null && (keyboard.escapeKey.wasPressedThisFrame || keyboard.leftAltKey.wasPressedThisFrame);
            // Follow-up 2026-11-11 (spec 048): not the Esc that just closed the settings over the shop (that close locked it again).
            if (CaptureGate.FreesCursor(escOrAlt, _lastUiClosedFrame, Time.frameCount))
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
