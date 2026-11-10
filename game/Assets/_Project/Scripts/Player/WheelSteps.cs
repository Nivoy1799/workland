namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 041, pure: turns the mouse wheel into whole turn steps (one per notch). A notch is 120 counts on some platforms and 1 on
    /// others (the Input System's scroll setting); a frame's value of 10 or more is read in 120-count notches, a smaller one as
    /// notches, so a touchpad's small values add up to a step. Positive is the wheel rolled away from you.
    /// </summary>
    public sealed class WheelSteps
    {
        private const float LargeNotch = 120f;
        private const float LargeThreshold = 10f;

        private float _notches;

        /// <summary>Feeds one frame's wheel value; returns the whole steps it completes (signed).</summary>
        public int Update(float scroll)
        {
            if (float.IsNaN(scroll) || float.IsInfinity(scroll) || scroll == 0f) return 0;
            float unit = System.Math.Abs(scroll) >= LargeThreshold ? LargeNotch : 1f;
            _notches += scroll / unit;
            int steps = (int)_notches; // towards zero
            _notches -= steps;
            return steps;
        }

        public void Reset() => _notches = 0f;
    }
}
