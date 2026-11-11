namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Spec 045 (045 review: a sliding window), server side, pure: disconnect a client that keeps sending faster than the rate
    /// limit (<see cref="RequestBucket"/>) allows. Every TooFast reply is counted in the second it happened; when the TooFast replies
    /// of the last <see cref="WindowSeconds"/> seconds reach <see cref="MaxTooFastInWindow"/> (30 per second sustained) the guard trips
    /// once and stays tripped (the caller kicks the client unless <see cref="ShouldKick"/> says no). Requests that pass do not reset
    /// anything, so a flood that the bucket lets through one request every <see cref="PickupRules.ServerRequestInterval"/> still adds up.
    /// Why these numbers: the bucket passes about 5 requests per second, so an honest masher at 10 clicks per second gets about 5 TooFast
    /// per second (50 per window); a network stall bunches the requests sent meanwhile into one burst (a 10 s stall: about 40 at the
    /// client's own spacing, about 100 at 10 per second). How many a stall queues depends on the client's request spacing, not on the
    /// stall alone: the longest stall a connection survives (FishNet's remote timeout, 60 s on the ServerManager and ClientManager
    /// defaults; Tugboat's own 1800 s socket timeout is only the fallback) queues about 250 at the client's own spacing, under 300,
    /// but a client that kept sending 10 per second through all 60 s would queue about 600 and trip it. A flood of 50 per second trips it within about 7 s, 1000 per second within a third of a second.
    /// Allocation-free after construction: a ring of per-second counts.
    /// </summary>
    public sealed class SpamGuard
    {
        /// <summary>TooFast replies within the window that disconnect the client.</summary>
        public const int MaxTooFastInWindow = 300;

        /// <summary>The sliding window, in whole seconds.</summary>
        public const int WindowSeconds = 10;

        private readonly int _limit;
        private readonly int[] _counts = new int[WindowSeconds];
        private readonly long[] _seconds = new long[WindowSeconds];

        public SpamGuard() : this(MaxTooFastInWindow)
        {
        }

        public SpamGuard(int limit)
        {
            _limit = limit > 0 ? limit : MaxTooFastInWindow;
            Reset();
        }

        /// <summary>The limit was reached; the client is (being) disconnected.</summary>
        public bool Tripped { get; private set; }

        /// <summary>TooFast replies counted in the window that ends at the last <see cref="Note"/>.</summary>
        public int InWindow { get; private set; }

        /// <summary>
        /// One request was answered at <paramref name="now"/> (seconds, never decreasing): <paramref name="tooFast"/> when the rate
        /// limit refused it. True exactly once, for the refusal that reaches the limit (kick now); false before and after.
        /// </summary>
        public bool Note(bool tooFast, double now)
        {
            if (Tripped || double.IsNaN(now)) return false;
            long second = (long)System.Math.Floor(now);
            int slot = (int)(((second % WindowSeconds) + WindowSeconds) % WindowSeconds);
            if (_seconds[slot] != second)
            {
                _seconds[slot] = second;
                _counts[slot] = 0;
            }
            if (tooFast) _counts[slot]++;
            int sum = 0;
            for (int i = 0; i < WindowSeconds; i++)
                if (_seconds[i] > second - WindowSeconds && _seconds[i] <= second) sum += _counts[i];
            InWindow = sum;
            if (!tooFast || sum < _limit) return false;
            Tripped = true;
            return true;
        }

        /// <summary>045 review: whether a tripped guard kicks the client; never the host's own player (<paramref name="isLocal"/>).</summary>
        public static bool ShouldKick(bool tripped, bool isLocal) => tripped && !isLocal;

        /// <summary>A new life of the player object (FishNet may pool it): nothing counted, not tripped.</summary>
        public void Reset()
        {
            Tripped = false;
            InWindow = 0;
            for (int i = 0; i < WindowSeconds; i++)
            {
                _counts[i] = 0;
                _seconds[i] = long.MinValue;
            }
        }
    }
}
