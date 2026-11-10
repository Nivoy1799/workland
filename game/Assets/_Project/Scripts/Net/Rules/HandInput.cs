namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Spec 041: the inputs of the hands, whatever device gives them (<see cref="ControlRules"/> turns each into its one meaning).
    /// Gamepad: the right trigger is <see cref="LeftClick"/> and <see cref="HoldLeft"/>, the left trigger <see cref="RightClick"/>,
    /// Y <see cref="Wheel"/> (one turn step), X the hidden <see cref="KeyQ"/> alias.
    /// </summary>
    public enum HandInput : byte
    {
        /// <summary>A short left click (press and release).</summary>
        LeftClick,
        /// <summary>The left button held down (a drag with the knife, a squeeze, a pour, a scrub, a shave, a wipe).</summary>
        HoldLeft,
        /// <summary>The right button pressed.</summary>
        RightClick,
        /// <summary>One notch of the mouse wheel (either way).</summary>
        Wheel,
        /// <summary>E pressed.</summary>
        KeyE,
        /// <summary>Q pressed: a hidden alias of the right click (keyboard-only players; not in any prompt).</summary>
        KeyQ,
    }
}
