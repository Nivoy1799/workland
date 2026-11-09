namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 027c, server only: the rate gate for one vessel's two heat SyncVars. A new value goes out only when it differs
    /// from the last one sent, and at most <see cref="MaxPerSecond"/> times per second of ticks; a change that comes
    /// sooner waits for a later call (KitchenService calls every tick). Pure, so the rate is unit-tested.
    /// </summary>
    public sealed class HeatSync
    {
        /// <summary>Updates per second at most (spec 027c default).</summary>
        public const int MaxPerSecond = 4;

        private bool _sent;
        private uint _sentTick;

        /// <summary>The progress byte last sent.</summary>
        public byte Progress { get; private set; }
        /// <summary>The zone byte last sent.</summary>
        public byte Zone { get; private set; }

        /// <summary>Ticks between two sends at <paramref name="tickRate"/> (at least one).</summary>
        public static uint MinTicks(double tickRate)
        {
            if (!(tickRate > 0d)) return 1u;
            double ticks = System.Math.Ceiling(tickRate / MaxPerSecond);
            return ticks < 1d ? 1u : (uint)ticks;
        }

        /// <summary>
        /// True when (<paramref name="progress"/>, <paramref name="zone"/>) should be sent now: it differs from what was
        /// sent and the last send is at least <see cref="MinTicks"/> ago (or the tick counter went backwards, a restart).
        /// Then it counts as sent.
        /// </summary>
        public bool ShouldSend(uint tick, double tickRate, byte progress, byte zone)
        {
            if (_sent && progress == Progress && zone == Zone) return false;
            if (_sent && tick >= _sentTick && tick - _sentTick < MinTicks(tickRate)) return false;
            _sent = true;
            _sentTick = tick;
            Progress = progress;
            Zone = zone;
            return true;
        }

        /// <summary>Forget what was sent (a vessel object that is reused starts fresh).</summary>
        public void Reset()
        {
            _sent = false;
            _sentTick = 0;
            Progress = 0;
            Zone = 0;
        }
    }
}
