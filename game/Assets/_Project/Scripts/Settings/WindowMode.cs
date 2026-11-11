namespace Worktown.Game.Settings
{
    /// <summary>Spec 048: how the game window shows (stored by value in the saved settings, so new modes are appended).</summary>
    public enum WindowMode
    {
        /// <summary>A borderless window over the whole screen (the default).</summary>
        Borderless,
        /// <summary>Exclusive full screen.</summary>
        Fullscreen,
        /// <summary>A normal window.</summary>
        Windowed
    }
}
