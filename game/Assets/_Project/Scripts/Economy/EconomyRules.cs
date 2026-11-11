namespace Worktown.Game.Economy
{
    /// <summary>Spec 009 constants and pure mappings. Values answered by the human on 2026-11-06.</summary>
    public static class EconomyRules
    {
        /// <summary>Demo starting money in whole dollars (open question 1). Spec 053: the open kitchen's shop wallet gets it once (it was
        /// per player); a food corner starts with its scaled 052 starting money instead (Climb.FoodCornerRules.StartingMoney).</summary>
        public const long StartingGrant = 500;

        /// <summary>Spec 053 (pillar 2, the money belongs to the group): the one shop account every player shares.</summary>
        public const string ShopAccount = "shop";

        /// <summary>Real seconds per in-game hour = one economy tick (open question 2).</summary>
        public const float SecondsPerHour = 60f;

        /// <summary>NPC population of the town (spec 008 default answer: 120 NPCs).</summary>
        public const int Population = 120;

        /// <summary>The server ticks to this hour on start, so demand exists from the first minute.</summary>
        public const int StartHour = 8;

        /// <summary>Seed for the economy RNG; same seed, same demand curve.</summary>
        public const ulong Seed = 1;

        /// <summary>Spec 022: a pay share of 100 % (no order factor).</summary>
        public const int FullPayPermille = 1000;

        /// <summary>
        /// Spec 022: <paramref name="payout"/> times <paramref name="permille"/> / 1000 in whole dollars, rounded half up;
        /// never below 0. Used for the customer's order factor on a sale (no new money source: it scales a sim sale).
        /// </summary>
        public static long ApplyPermille(long payout, int permille)
        {
            if (payout <= 0 || permille <= 0) return 0;
            return (payout * permille + 500) / 1000;
        }

        /// <summary>Spec 053: every player's money steps go to the one shop account (<see cref="ShopAccount"/>); it was "p" + id per player.</summary>
        public static string AccountId(int clientId)
        {
            return ShopAccount;
        }

        /// <summary>Spec 053: the economy tick a shown day starts at: day <paramref name="day"/> (1 or more) is (day - 1) × 24 hours on.</summary>
        public static long DayOffsetTicks(long day) => (System.Math.Max(1L, day) - 1L) * 24L;

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
