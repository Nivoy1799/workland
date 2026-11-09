using Worktown.Sim.Cooking;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 016: one food item in a pan or pot. Server only. Counts only the ticks while the stove is on
    /// (the stove can be switched on and off with food inside). Temperature and ticks never leave the
    /// server (D21); clients see bucket labels.
    /// </summary>
    public sealed class HeatSession
    {
        private uint _cookedTicks;
        private uint _runningSince;
        private bool _running;

        public HeatSession(Item food, string operationId, int? tempC, bool stoveOn, uint tick)
        {
            Food = food;
            OperationId = operationId;
            TempC = tempC;
            if (stoveOn) Start(tick);
        }

        public Item Food { get; }
        /// <summary>"fry", "saute" or "boil".</summary>
        public string OperationId { get; }
        /// <summary>Pan temperature in Celsius; null for the pot (boil has a fixed temperature).</summary>
        public int? TempC { get; }
        public bool IsRunning => _running;
        /// <summary>Spec 027c: when this food reaches each heat zone (built once from the sim when it goes in). Server only.</summary>
        public HeatTimeline Timeline { get; internal set; }

        /// <summary>The stove was switched on at <paramref name="tick"/>.</summary>
        public void Start(uint tick)
        {
            if (_running) return;
            _running = true;
            _runningSince = tick;
        }

        /// <summary>The stove was switched off at <paramref name="tick"/>: keep what has cooked so far.</summary>
        public void Stop(uint tick)
        {
            if (!_running) return;
            _cookedTicks += CookingTime.ElapsedTicks(_runningSince, tick);
            _running = false;
        }

        /// <summary>Ticks spent cooking up to <paramref name="tick"/>, over all on-periods.</summary>
        public uint CookedTicks(uint tick)
        {
            return _running ? _cookedTicks + CookingTime.ElapsedTicks(_runningSince, tick) : _cookedTicks;
        }
    }
}
