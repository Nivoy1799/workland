namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 041, pure: turns the mouse wheel into whole turn steps (one per notch). Positive is the wheel rolled away from you; a
    /// touchpad's small values add up to a step.
    /// Spec 048 (041 review): the size of a notch comes from the Input System's scroll setting (<see cref="CountsPerNotch"/>: 1 when
    /// the deltas are uniform across platforms, else the platform's own, 120 on Windows). Without one (the parameterless constructor)
    /// it guesses per frame as before: a value of 10 or more is read in 120-count notches, a smaller one as notches.
    /// </summary>
    public sealed class WheelSteps
    {
        private const float LargeNotch = 120f;
        private const float LargeThreshold = 10f;

        private readonly float _countsPerNotch; // 0: guess per frame
        private float _notches;

        /// <summary>Guesses the notch size per frame (see the class).</summary>
        public WheelSteps()
        {
        }

        /// <summary>A known notch size in wheel counts (anything not above 0 guesses, as the parameterless constructor).</summary>
        public WheelSteps(float countsPerNotch)
        {
            _countsPerNotch = countsPerNotch > 0f && !float.IsInfinity(countsPerNotch) ? countsPerNotch : 0f;
        }

        /// <summary>
        /// The counts of one notch: 1 with the Input System's "uniform across all platforms" scroll deltas (its default, a range of
        /// -1..1), else the platform's own range: 120 on Windows, 1 on macOS and Linux.
        /// </summary>
        public static float CountsPerNotch(bool uniformAcrossPlatforms, bool windows) => uniformAcrossPlatforms ? 1f : windows ? LargeNotch : 1f;

        /// <summary>Feeds one frame's wheel value; returns the whole steps it completes (signed).</summary>
        public int Update(float scroll)
        {
            if (float.IsNaN(scroll) || float.IsInfinity(scroll) || scroll == 0f) return 0;
            float unit = _countsPerNotch > 0f ? _countsPerNotch : System.Math.Abs(scroll) >= LargeThreshold ? LargeNotch : 1f;
            _notches += scroll / unit;
            int steps = (int)_notches; // towards zero
            _notches -= steps;
            return steps;
        }

        public void Reset() => _notches = 0f;
    }
}
