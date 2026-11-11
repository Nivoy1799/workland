namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Spec 045, server side, pure: disconnect a client after many TooFast replies in a row. Every request the rate limit
    /// (<see cref="RequestBucket"/>) refuses is one more in the streak; any request it lets through ends the streak. At
    /// <see cref="MaxTooFastInARow"/> the guard trips once and stays tripped (the caller kicks the client).
    /// Why this many: an honest client spaces its requests by <see cref="PickupRules.ClientRequestInterval"/> (about 0.24 s), so
    /// after a network stall its queued requests arrive bunched and the bucket's <see cref="RequestBucket.Burst"/> passes, the
    /// rest is refused: 40 in a row needs a stall of about 10 s, longer than a connection survives in play. A flood trips it
    /// within a fraction of a second, because the bucket lets one through only every <see cref="PickupRules.ServerRequestInterval"/>:
    /// 40 refusals in that time is about 200 requests per second. A client a little faster than allowed is only throttled (the
    /// bucket's job): every refusal is one small answer and no validation work.
    /// </summary>
    public sealed class SpamGuard
    {
        /// <summary>TooFast replies in a row that disconnect the client.</summary>
        public const int MaxTooFastInARow = 40;

        private readonly int _limit;

        public SpamGuard() : this(MaxTooFastInARow)
        {
        }

        public SpamGuard(int limit)
        {
            _limit = limit > 0 ? limit : MaxTooFastInARow;
        }

        /// <summary>TooFast replies since the last request that passed.</summary>
        public int Streak { get; private set; }

        /// <summary>The limit was reached; the client is (being) disconnected.</summary>
        public bool Tripped { get; private set; }

        /// <summary>
        /// One request was answered: <paramref name="tooFast"/> when the rate limit refused it. True exactly once, for the refusal
        /// that reaches the limit (kick now); false before and after.
        /// </summary>
        public bool Note(bool tooFast)
        {
            if (Tripped) return false;
            if (!tooFast)
            {
                Streak = 0;
                return false;
            }
            Streak++;
            if (Streak < _limit) return false;
            Tripped = true;
            return true;
        }

        /// <summary>A new life of the player object (FishNet may pool it): no streak, not tripped.</summary>
        public void Reset()
        {
            Streak = 0;
            Tripped = false;
        }
    }
}
