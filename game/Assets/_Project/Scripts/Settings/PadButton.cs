namespace Worktown.Game.Settings
{
    /// <summary>
    /// Spec 048: the gamepad buttons an action can be bound to (the triggers and the d-pad are fixed: spec 041 and quick chat).
    /// Stored by name.
    /// </summary>
    public enum PadButton
    {
        None,
        /// <summary>A (Xbox) / Cross.</summary>
        South,
        /// <summary>B / Circle.</summary>
        East,
        /// <summary>X / Square.</summary>
        West,
        /// <summary>Y / Triangle.</summary>
        North,
        LeftShoulder,
        RightShoulder,
        LeftStick,
        RightStick,
        Select
    }
}
