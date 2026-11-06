namespace Worktown.Game.Economy
{
    /// <summary>Spec 009 constants and pure mappings. Values answered by the human on 2026-11-06.</summary>
    public static class EconomyRules
    {
        /// <summary>Demo starting money per player in whole dollars (open question 1).</summary>
        public const long StartingGrant = 500;

        /// <summary>Real seconds per in-game hour = one economy tick (open question 2).</summary>
        public const float SecondsPerHour = 60f;

        /// <summary>NPC population of the town (spec 008 default answer: 120 NPCs).</summary>
        public const int Population = 120;

        /// <summary>The server ticks to this hour on start, so demand exists from the first minute.</summary>
        public const int StartHour = 8;

        /// <summary>Seed for the economy RNG; same seed, same demand curve.</summary>
        public const ulong Seed = 1;

        public static string AccountId(int clientId)
        {
            return "p" + clientId;
        }

        /// <summary>"Day 1, 08:00" for tick 8; one tick is one hour, day 1 starts at tick 0.</summary>
        public static string ClockText(long tick)
        {
            if (tick < 0) tick = 0;
            long day = tick / 24 + 1;
            long hour = tick % 24;
            return "Day " + day + ", " + hour.ToString("00") + ":00";
        }

        /// <summary>"$1,234" style money text (whole dollars, D23).</summary>
        public static string Money(long dollars)
        {
            return (dollars < 0 ? "-$" : "$") + System.Math.Abs(dollars).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
