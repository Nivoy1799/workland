namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 020, pure: tells a click from a hold or a drag. A click is a press and release within
    /// <see cref="MaxSeconds"/> with less than <see cref="MaxCounts"/> of mouse movement in between; it fires on
    /// the release. A press that turned into a cut stroke (spec 019) or began while the cursor was free is
    /// cancelled by the caller (<see cref="Cancel"/>) and never clicks. Client side only.
    /// </summary>
    public sealed class ClickGesture
    {
        public const float MaxSeconds = 0.3f;
        /// <summary>Mouse counts (path length) a press may move and still be a click.</summary>
        public const float MaxCounts = 15f;

        private bool _down;
        private bool _void;
        private float _start;
        private float _moved;

        /// <summary>True while the button is held (since the press this gesture saw).</summary>
        public bool IsPressed => _down;

        /// <summary>
        /// Feeds one frame: whether the button is held, this frame's mouse movement in counts and the time in
        /// seconds. Returns true exactly on the release of a click.
        /// </summary>
        public bool Update(bool buttonHeld, float deltaX, float deltaY, float now)
        {
            if (buttonHeld && !_down)
            {
                // The press frame: movement before the press does not count.
                _down = true;
                _void = false;
                _start = now;
                _moved = 0f;
                return false;
            }
            if (!_down) return false;

            if (float.IsNaN(deltaX) || float.IsNaN(deltaY) || float.IsInfinity(deltaX) || float.IsInfinity(deltaY))
                _void = true;
            else
                _moved += (float)System.Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
            if (_moved >= MaxCounts || now - _start > MaxSeconds) _void = true;
            if (buttonHeld) return false;

            _down = false;
            return !_void;
        }

        /// <summary>The current press can no longer become a click (it is a cut stroke, or the UI had it).</summary>
        public void Cancel()
        {
            if (_down) _void = true;
        }

        public void Reset()
        {
            _down = false;
            _void = false;
            _moved = 0f;
        }
    }
}
