using System;

namespace Worktown.Game.Save
{
    /// <summary>
    /// Spec 053: what the host's save file holds: the business, not the players (D33). A [Serializable] DTO for JsonUtility (like
    /// GameSettings): plain public fields, nested parts as classes that always exist. Spec 037 (D39 revises D23): money in cents, format
    /// version 2 (a version 1 save held the old scale's whole dollars; SaveMigrations.V1ToV2 converts it when it loads), and the menu
    /// board's prices (<see cref="menu"/>). Spec 062a: format version 3, the kitchen's layout (<see cref="layout"/>). Spec 057: format version 4, the restaurant's climb fields (<see cref="ClimbPart"/>). Spec 059: format version 5, the office terminal (<see cref="office"/>). Validated, clamped and
    /// migrated by <see cref="ShopSaveCodec"/>.
    /// </summary>
    [Serializable]
    public sealed class ShopSave
    {
        public const string Format = "worktown-shop-save";

        public string format = Format;
        public int formatVersion = 6; // spec 062b: v6 (the stored pieces' records, the door sign and the pallet)
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
        /// <summary>
        /// Spec 062a (format version 3): the kitchen's furniture (LayoutCodec turns it into a ShopLayout). A version 2 save gets the default
        /// layout (SaveMigrations.V2ToV3). Unknown piece types are kept in the file but not spawned.
        /// </summary>
        public LayoutPart layout = new LayoutPart();
        /// <summary>Spec 059 (format version 5): the office terminal's special of the day and its last services (at most 8, newest last).</summary>
        public OfficePart office = new OfficePart();

        [Serializable]
        public sealed class OfficePart
        {
            /// <summary>The special's MenuPrices line id ("falafel_wrap"); empty for none (a drink or an unknown id reads as none).</summary>
            public string special = string.Empty;
            public RecordPart[] history = new RecordPart[0];
        }

        /// <summary>Spec 059: one service of the office terminal's Results (Office.ServiceRecord).</summary>
        [Serializable]
        public sealed class RecordPart
        {
            public int day = 1;
            public int came;
            public int served;
            public int missed;
            public long earned;
            public long tips;
            /// <summary>Tenths of a star, -1 when the service had no guest to count.</summary>
            public int starsTenths = -1;
            /// <summary>Whole seconds, -1 when nobody was served.</summary>
            public int waitSeconds = -1;
            /// <summary>Six counts in Office.ResultsRules order (the restaurant dishes, the drinks, other dishes).</summary>
            public int[] sold = new int[0];
            /// <summary>Spec 058's town share (permille), -1 while unknown.</summary>
            public int rivalPermille = -1;
        }

        [Serializable]
        public sealed class ClimbPart
        {
            /// <summary>The climb stage (1, the food corner; spec 057: 2, the first restaurant).</summary>
            public int stage = 1;
            /// <summary>The 052 ids owned (["dish_fries"] at the start); unknown ids are kept for the next save.</summary>
            public string[] unlocks = new string[0];
            public bool goalReached;
            /// <summary>Spec 057: the rent was paid during a service that was saved before it ended; the crew moves in when the save loads.</summary>
            public bool movePending;
            /// <summary>Spec 057: rent owed in cents (RentRules), paid first at the next service end.</summary>
            public long rentOwed;
            /// <summary>Spec 057: the stage 2 star window, each service's average in tenths (10 to 50), newest last, at most 8.</summary>
            public int[] starWindow = new int[0];
            /// <summary>Spec 057: the owned recipe ids (052 purchase ids) that are off the menu; unknown ids are kept.</summary>
            public string[] menuOff = new string[0];
            /// <summary>
            /// 057 review: the players connected at the last service starts (at most 3, newest last, 1 to 16). One-time prices and the stage's
            /// goal use the largest of them and the players connected now, so a load or a short drop never makes them solo prices.
            /// </summary>
            public int[] crewStarts = new int[0];
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

        /// <summary>Spec 062a: a shop layout as data (also the format of Resources/Layouts/default.json).</summary>
        [Serializable]
        public sealed class LayoutPart
        {
            /// <summary>The layout data's version (ShopLayout.CurrentVersion).</summary>
            public int version = 1;
            public PiecePart[] pieces = new PiecePart[0];
            /// <summary>Spec 062b: ids of pieces that are owned but not placed (on the storage pallet).</summary>
            public int[] stored = new int[0];
            /// <summary>Spec 062b (save v6): the stored pieces' records (type, length, the pose they were stored from).</summary>
            public PiecePart[] storedPieces = new PiecePart[0];
            public HomePart[] homes = new HomePart[0];
            /// <summary>The next free piece id (ids are never reused).</summary>
            public int nextId = 1;
        }

        /// <summary>Spec 062a: one placed piece (millimetres and quarter turns; tier and paid cents are reserved for spec 062c).</summary>
        [Serializable]
        public sealed class PiecePart
        {
            public int id;
            public string type = string.Empty;
            public int xMm;
            public int zMm;
            public int turns;
            public int lengthMm;
            public int tier = 1;
            public long paidCents;
        }

        /// <summary>Spec 062a: where an item (cookware, a tool) lies at the start: spot of local surface of piece.</summary>
        [Serializable]
        public sealed class HomePart
        {
            public string item = string.Empty;
            public int piece;
            public int local;
            public int spot;
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
