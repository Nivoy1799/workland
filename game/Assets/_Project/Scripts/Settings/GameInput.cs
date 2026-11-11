using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace Worktown.Game.Settings
{
    /// <summary>
    /// Spec 048: reads a <see cref="ControlAction"/> from the devices through the player's bindings
    /// (<see cref="GameSettings.CurrentBindings"/>): its key on the keyboard or its button on the gamepad. Thin; the mapping is
    /// ControlBindings. The fixed inputs (mouse buttons, wheel, triggers, sticks, d-pad) are still read where they are used.
    /// </summary>
    public static class GameInput
    {
        /// <summary>The action's key or button went down this frame.</summary>
        public static bool Pressed(ControlAction action)
        {
            ButtonControl key = KeyControl(action);
            ButtonControl pad = PadControl(action);
            return (key != null && key.wasPressedThisFrame) || (pad != null && pad.wasPressedThisFrame);
        }

        /// <summary>The action's key or button is down.</summary>
        public static bool Held(ControlAction action)
        {
            ButtonControl key = KeyControl(action);
            ButtonControl pad = PadControl(action);
            return (key != null && key.isPressed) || (pad != null && pad.isPressed);
        }

        /// <summary>
        /// The keyboard's walk direction (x right, y forward; -1..1 per axis): the four bound keys, and the arrow keys, which always walk.
        /// </summary>
        public static Vector2 KeyboardMove()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return Vector2.zero;
            float x = Axis(ControlAction.MoveRight, keyboard.rightArrowKey) - Axis(ControlAction.MoveLeft, keyboard.leftArrowKey);
            float y = Axis(ControlAction.MoveForward, keyboard.upArrowKey) - Axis(ControlAction.MoveBack, keyboard.downArrowKey);
            return new Vector2(x, y);
        }

        /// <summary>The Input System control of a pad button (null for None).</summary>
        public static GamepadButton? ToGamepadButton(PadButton button)
        {
            switch (button)
            {
                case PadButton.South: return GamepadButton.South;
                case PadButton.East: return GamepadButton.East;
                case PadButton.West: return GamepadButton.West;
                case PadButton.North: return GamepadButton.North;
                case PadButton.LeftShoulder: return GamepadButton.LeftShoulder;
                case PadButton.RightShoulder: return GamepadButton.RightShoulder;
                case PadButton.LeftStick: return GamepadButton.LeftStick;
                case PadButton.RightStick: return GamepadButton.RightStick;
                case PadButton.Select: return GamepadButton.Select;
                default: return null;
            }
        }

        private static float Axis(ControlAction action, ButtonControl arrow)
        {
            ButtonControl key = KeyControl(action);
            return (key != null && key.isPressed) || arrow.isPressed ? 1f : 0f;
        }

        private static ButtonControl KeyControl(ControlAction action)
        {
            Key key = GameSettings.CurrentBindings.KeyOf(action);
            Keyboard keyboard = Keyboard.current;
            if (key == Key.None || keyboard == null) return null;
            return keyboard[key];
        }

        private static ButtonControl PadControl(ControlAction action)
        {
            GamepadButton? button = ToGamepadButton(GameSettings.CurrentBindings.PadOf(action));
            Gamepad gamepad = Gamepad.current;
            if (!button.HasValue || gamepad == null) return null;
            return gamepad[button.Value];
        }
    }
}
