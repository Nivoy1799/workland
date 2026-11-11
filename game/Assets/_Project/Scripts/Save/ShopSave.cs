using System;

namespace Worktown.Game.Save
{
    /// <summary>
    /// Spec 053: what the host's save file holds: the business, not the players (D33). A [Serializable] DTO for JsonUtility (like
    /// GameSettings): plain public fields, nested parts as classes that always exist. Money in the game's whole-dollar unit (D23; inbox
    /// question 17 may move it to cents with a "v1 to v2" migration step). Validated, clamped and migrated by <see cref="ShopSaveCodec"/>.
    /// </summary>
    [Serializable]
    public sealed class ShopSave
    {
        public const string Format = "worktown-shop-save";

        public string format = Format;
        public int formatVersion = 1;
        /// <summary>Informational: Application.version and the 047 build hash of the build that wrote it.</summary>
        public string gameVersion = string.Empty;
        public string buildHash = string.Empty;
        /// <summary>32 hex characters, random at New shop; the file name holds its first 12.</summary>
        public string saveId = string.Empty;
        /// <summary>ISO 8601 ("O"), from an injected clock.</summary>
        public string createdUtc = string.Empty;
        public string savedUtc = string.Empty;
        /// <summary>"Your food corner" (fixed until O6 decides a name filter).</summary>
        public string shopName = string.Empty;
        /// <summary>Random at New shop; the economy of each loaded day is seeded with worldSeed ^ day.</summary>
        public ulong worldSeed;
        public ClimbPart climb = new ClimbPart();
        public MoneyPart money = new MoneyPart();
        public ClockPart clock = new ClockPart();
        public StockPart[] stock = new StockPart[0];
        public int hygienePercent = 100;
        public StatsPart stats = new StatsPart();

        [Serializable]
        public sealed class ClimbPart
        {
            /// <summary>The climb stage (1, the food corner: the only stage of this build).</summary>
            public int stage = 1;
            /// <summary>The 052 ids owned (["dish_fries"] at the start); unknown ids are kept for the next save.</summary>
            public string[] unlocks = new string[0];
            public bool goalReached;
        }

        [Serializable]
        public sealed class MoneyPart
        {
            /// <summary>The one shop wallet (pillar 2).</summary>
            public long balance;
            public long earnedTotal;
            public long spentTotal;
        }

        [Serializable]
        public sealed class ClockPart
        {
            /// <summary>The next day to play (1 or more); a save written during a service loads at the start of that day's service.</summary>
            public int day = 1;
            public int servicesPlayed;
            public long playSeconds;
        }

        [Serializable]
        public sealed class StockPart
        {
            /// <summary>"shelf:potato", "fridge:tomato", "stock:cups" (ShopSaveCodec.SlotOf); an unknown slot is kept for the next save.</summary>
            public string slot = string.Empty;
            public int count;
        }

        [Serializable]
        public sealed class StatsPart
        {
            public int guestsServed;
            public int guestsMissed;
            public int starSum;
            public long bestServiceEarned;
        }
    }
}
