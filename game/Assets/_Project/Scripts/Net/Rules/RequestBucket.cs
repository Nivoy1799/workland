namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Review of spec 027b (orchestrator decision), server side, pure: the rate limit for one player's requests as a token
    /// bucket. It holds at most <see cref="Burst"/> tokens and gains one every <see cref="PickupRules.ServerRequestInterval"/>
    /// seconds; each request takes one. Requests the client sent far enough apart but that arrive bunched (network jitter,
    /// a reliable resend) all pass, while the average stays capped at one per interval (plus the burst). A knife stroke is
    /// a request like any other.
    /// </summary>
    public sealed class RequestBucket
    {
        /// <summary>Tokens a player can save up.</summary>
        public const float Burst = 2f;

        private readonly float _interval;
        private float _tokens = Burst;
        private float _last = float.NaN;

        public RequestBucket() : this(PickupRules.ServerRequestInterval)
        {
        }

        public RequestBucket(float interval)
        {
            _interval = interval > 0f ? interval : PickupRules.ServerRequestInterval;
        }

        /// <summary>Tokens available at the last request (for tests).</summary>
        public float Tokens => _tokens;

        /// <summary>A request at <paramref name="now"/> (seconds, never decreasing): true when it may be carried out.</summary>
        public bool TryTake(float now)
        {
            if (float.IsNaN(now)) return false;
            if (!float.IsNaN(_last) && now > _last)
            {
                _tokens += (now - _last) / _interval;
                if (_tokens > Burst) _tokens = Burst;
            }
            if (float.IsNaN(_last) || now > _last) _last = now;
            if (_tokens < 1f - 1e-4f) return false; // float rounding of the refill must not cost a token
            _tokens -= 1f;
            return true;
        }
    }
}
