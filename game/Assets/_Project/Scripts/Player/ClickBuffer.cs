namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 027b, pure: clicks are never swallowed (docs/feel.md, spec section 6). The client keeps its requests at least
    /// <see cref="Interval"/> apart (the server ignores faster ones, so a too-fast click used to vanish). A click inside the
    /// interval is queued instead (at most one: more clicks inside the same interval merge into it) and released by
    /// <see cref="Poll"/> as soon as the interval has passed. What the queued click does is decided when it is sent, so it
    /// acts on what the player sees then. The server's own rate limit is unchanged.
    /// </summary>
    public sealed class ClickBuffer
    {
        private float _lastSent = float.NegativeInfinity;

        public ClickBuffer(float interval)
        {
            Interval = interval > 0f ? interval : 0f;
        }

        /// <summary>Seconds between two requests.</summary>
        public float Interval { get; }

        /// <summary>A click is waiting to be sent.</summary>
        public bool HasQueued { get; private set; }

        /// <summary>A click at <paramref name="now"/>: true means send it now; false means it is queued.</summary>
        public bool Click(float now)
        {
            if (now - _lastSent >= Interval)
            {
                _lastSent = now;
                HasQueued = false;
                return true;
            }
            HasQueued = true;
            return false;
        }

        /// <summary>Every frame: true (once) when the queued click may go out now.</summary>
        public bool Poll(float now)
        {
            if (!HasQueued || now - _lastSent < Interval) return false;
            HasQueued = false;
            _lastSent = now;
            return true;
        }

        /// <summary>Another request (a knife stroke, a key) went out: the next click waits for the interval too.</summary>
        public void NoteSent(float now)
        {
            if (now > _lastSent) _lastSent = now;
        }

        /// <summary>Drops a queued click (a menu opened, the view changed).</summary>
        public void Clear() => HasQueued = false;
    }
}
