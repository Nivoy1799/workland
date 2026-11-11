using System;

namespace Worktown.Game.Save
{
    /// <summary>
    /// Spec 053: what the host's save file holds: the business, not the players (D33). A [Serializable] DTO for JsonUtility (like
    /// GameSettings): plain public fields, nested parts as classes that always exist. Spec 037 (D39 revises D23): money in cents, format
    /// version 2 (a version 1 save held the old scale's whole dollars; SaveMigrations.V1ToV2 converts it when it loads), and the menu
    /// board's prices (<see cref="menu"/>). Validated, clamped and migrated by <see cref="ShopSaveCodec"/>.
    /// </summary>
    [Serializable]
    public sealed class ShopSave
    {
        public const string Format = "worktown-shop-save";

        public string format = Format;
        public int formatVersion = 2;
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
        /// <summary>Spec 037: the menu board's prices that differ from their reference (a line id and cents); unknown ids are ignored.</summary>
        public MenuPart[] menu = new MenuPart[0];

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
            /// <summary>The one shop wallet (pillar 2), in cents (spec 037).</summary>
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
        public sealed class MenuPart
        {
            /// <summary>A MenuPrices line id ("fries", "falafel_wrap", "lemonade").</summary>
            public string id = string.Empty;
            /// <summary>Its price on the board, in cents.</summary>
            public long cents;
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
