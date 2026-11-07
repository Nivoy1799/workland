namespace Worktown.Game.Social
{
    /// <summary>
    /// Token bucket for social actions (spec 012): a short burst is fine, spam is refused.
    /// Pure: the caller passes the time, so tests do not depend on a clock.
    /// </summary>
    public sealed class SocialRateLimiter
    {
        private float _tokens = SocialRules.Burst;
        private float _lastTime = float.NaN;

        public bool TryConsume(float now)
        {
            if (!float.IsNaN(_lastTime) && now > _lastTime)
            {
                _tokens += (now - _lastTime) / SocialRules.RefillSeconds;
                if (_tokens > SocialRules.Burst) _tokens = SocialRules.Burst;
            }
            _lastTime = now;
            if (_tokens < 1f) return false;
            _tokens -= 1f;
            return true;
        }
    }
}
