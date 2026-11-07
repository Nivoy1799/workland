namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 019: turns the mouse into knife strokes. While it is armed (knife in the hands, aiming at a board
    /// with food) and the left button is held, downward mouse movement is summed; at
    /// <see cref="StrokeCounts"/> one stroke fires. The next stroke needs the button released or
    /// <see cref="RearmCounts"/> of upward movement first. Pure and client side: the server only gets
    /// "one stroke", never the mouse data.
    /// </summary>
    public sealed class CutGesture
    {
        /// <summary>Downward mouse counts for one stroke.</summary>
        public const float StrokeCounts = 40f;
        /// <summary>Upward mouse counts that re-arm the gesture after a stroke (or release the button).</summary>
        public const float RearmCounts = 20f;

        private float _down;
        private float _up;
        private bool _waiting;

        /// <summary>True between a stroke and the re-arm (release or upward movement).</summary>
        public bool IsWaitingForRearm => _waiting;

        /// <summary>
        /// Feeds one frame. <paramref name="deltaY"/> is the mouse movement in counts, positive up (the Input
        /// System's convention). Returns true exactly when a stroke fires.
        /// </summary>
        public bool Update(bool armed, bool buttonHeld, float deltaY)
        {
            if (!armed || !buttonHeld)
            {
                Reset();
                return false;
            }
            if (float.IsNaN(deltaY) || float.IsInfinity(deltaY)) return false;

            if (_waiting)
            {
                if (deltaY > 0f) _up += deltaY;
                if (_up >= RearmCounts)
                {
                    _waiting = false;
                    _down = 0f;
                    _up = 0f;
                }
                return false;
            }

            // Net downward movement since the last stroke; moving up first does not bank a stroke.
            _down = System.Math.Max(0f, _down - deltaY);
            if (_down < StrokeCounts) return false;
            _waiting = true;
            _down = 0f;
            _up = 0f;
            return true;
        }

        public void Reset()
        {
            _down = 0f;
            _up = 0f;
            _waiting = false;
        }
    }
}
