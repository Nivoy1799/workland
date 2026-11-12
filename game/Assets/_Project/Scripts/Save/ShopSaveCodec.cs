using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using Worktown.Game.Climb;
using Worktown.Game.Cooking;
using Worktown.Game.Shop;
using Worktown.Sim.Economy;

namespace Worktown.Game.Save
{
    /// <summary>
    /// Spec 053: the save file's text. UTF-8 JSON through JsonUtility (as GameSettings), indented, a few KB. Loading checks, in order:
    /// the size (<see cref="SaveError.TooLarge"/> past <see cref="MaxBytes"/>), JSON (<see cref="SaveError.NotJson"/>), the format name
    /// (<see cref="SaveError.WrongFormat"/>), the version (<see cref="SaveError.NewerVersion"/> above what this build knows), the
    /// identity (<see cref="SaveError.Invalid"/>: the save id, the times); then the migrations run and out-of-range values are clamped
    /// and noted (the balance 0 to EconomyLimits.MaxBalance, counts 0 to the slot's capacity, hygiene 0 to 100, the day 1 or more, the
    /// stage to this build's). Unknown slots and unlock ids are ignored by the game but kept in the save. Pure apart from JsonUtility.
    /// </summary>
    public sealed class ShopSaveCodec
    {
        /// <summary>A save file larger than this (1 MB) is refused unread.</summary>
        public const int MaxBytes = 1024 * 1024;
        public const string ShelfPrefix = "shelf:";
        public const string FridgePrefix = "fridge:";
        public const string StockPrefix = "stock:";

        /// <summary>This build's codec (spec 037: the v1 to v2 money step; spec 062a: format version 3, the v2 to v3 layout step).</summary>
        public static readonly ShopSaveCodec Default = new ShopSaveCodec(SaveMigrations.Current);

        private readonly SaveMigrations _migrations;

        public ShopSaveCodec(SaveMigrations migrations)
        {
            _migrations = migrations ?? throw new ArgumentNullException(nameof(migrations));
        }

        /// <summary>The format version this codec writes.</summary>
        public int Version => _migrations.Latest;

        /// <summary>The indented JSON of <paramref name="save"/> (at <see cref="Version"/>).</summary>
        public string ToJson(ShopSave save)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            save.format = ShopSave.Format;
            save.formatVersion = Version;
            return JsonUtility.ToJson(save, true);
        }

        /// <summary>Parses, checks, migrates and clamps a save's text.</summary>
        public SaveLoadResult TryParse(string text)
        {
            if (string.IsNullOrEmpty(text)) return SaveLoadResult.Fail(SaveError.NotJson);
            if (text.Length > MaxBytes || Encoding.UTF8.GetByteCount(text) > MaxBytes) return SaveLoadResult.Fail(SaveError.TooLarge);
            string trimmed = text.Trim();
            if (trimmed.Length < 2 || trimmed[0] != '{' || trimmed[trimmed.Length - 1] != '}') return SaveLoadResult.Fail(SaveError.NotJson);
            ShopSave save;
            try
            {
                save = JsonUtility.FromJson<ShopSave>(trimmed);
            }
            catch (Exception)
            {
                return SaveLoadResult.Fail(SaveError.NotJson);
            }
            if (save == null) return SaveLoadResult.Fail(SaveError.NotJson);
            if (save.format != ShopSave.Format) return SaveLoadResult.Fail(SaveError.WrongFormat);
            if (save.formatVersion > Version) return SaveLoadResult.Fail(SaveError.NewerVersion);
            if (save.formatVersion < SaveMigrations.FirstVersion || !IsSaveId(save.saveId) || !IsTime(save.createdUtc) || !IsTime(save.savedUtc))
                return SaveLoadResult.Fail(SaveError.Invalid);
            try
            {
                save = _migrations.Run(save);
            }
            catch (Exception)
            {
                return SaveLoadResult.Fail(SaveError.Invalid);
            }
            var notes = new List<string>();
            Clamp(save, notes);
            return new SaveLoadResult(save, SaveError.None, notes);
        }

        /// <summary>32 hex digits.</summary>
        public static bool IsSaveId(string id)
        {
            if (id == null || id.Length != 32) return false;
            foreach (char c in id)
                if (!Uri.IsHexDigit(c)) return false;
            return true;
        }

        public static bool IsTime(string text) => TryTime(text, out _);

        /// <summary>An ISO 8601 time as UTC; false when it is not one.</summary>
        public static bool TryTime(string text, out DateTime utc)
        {
            utc = default;
            if (string.IsNullOrEmpty(text)) return false;
            if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime t))
                return false;
            utc = t.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(t, DateTimeKind.Utc) : t.ToUniversalTime();
            return true;
        }

        /// <summary>The "O" text of a time (UTC).</summary>
        public static string TimeText(DateTime utc) => utc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

        // ---- stock slots ----

        /// <summary>The save's slot name of stock line <paramref name="line"/>: "shelf:potato", "fridge:tomato", "stock:cups" (empty for no line).</summary>
        public static string SlotOf(int line)
        {
            if (!StockCatalog.IsValid(line)) return string.Empty;
            string id = StockCatalog.Goods[line].Id;
            if (Contains(StockLayout.Shelf, id)) return ShelfPrefix + id;
            if (Contains(StockLayout.Fridge, id)) return FridgePrefix + id;
            return StockPrefix + id;
        }

        /// <summary>The stock line of a save's slot name, or -1 (an unknown slot).</summary>
        public static int LineOfSlot(string slot)
        {
            if (string.IsNullOrEmpty(slot)) return -1;
            for (int i = 0; i < StockCatalog.Count; i++)
                if (string.Equals(SlotOf(i), slot, StringComparison.Ordinal)) return i;
            return -1;
        }

        private static bool Contains(IReadOnlyList<string> list, string id)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] == id) return true;
            return false;
        }

        // ---- clamping ----

        private static void Clamp(ShopSave s, List<string> notes)
        {
            s.climb ??= new ShopSave.ClimbPart();
            s.money ??= new ShopSave.MoneyPart();
            s.clock ??= new ShopSave.ClockPart();
            s.stats ??= new ShopSave.StatsPart();
            s.stock ??= new ShopSave.StockPart[0];
            s.menu ??= new ShopSave.MenuPart[0];
            s.layout ??= new ShopSave.LayoutPart();
            s.climb.unlocks ??= new string[0];
            s.shopName = string.IsNullOrEmpty(s.shopName) ? FoodCornerRules.ShopName : s.shopName;
            s.gameVersion ??= string.Empty;
            s.buildHash ??= string.Empty;

            byte stage = ShopStageRules.ClampSaved(s.climb.stage);
            if (stage != s.climb.stage) notes.Add("stage " + s.climb.stage + " -> " + stage);
            s.climb.stage = stage;
            if (Array.IndexOf(s.climb.unlocks, FoodCornerRules.StartUnlock) < 0)
            {
                var unlocks = new List<string>(s.climb.unlocks) { FoodCornerRules.StartUnlock };
                s.climb.unlocks = unlocks.ToArray();
                notes.Add("unlock " + FoodCornerRules.StartUnlock + " added");
            }

            // Spec 057: the restaurant's fields. A move is pending only at the food corner; the rent owed is at most the cap of the largest
            // crew (D40: the cap follows the crew at each service end); the window holds at most 8 values of 1.0 to 5.0 stars; unknown
            // menuOff ids are kept (a newer build's recipe).
            s.climb.starWindow ??= new int[0];
            s.climb.menuOff ??= new string[0];
            s.climb.crewStarts ??= new int[0];
            int[] crew = RestaurantRules.PushCrew(s.climb.crewStarts, 0); // 057 review: at most 3, each 1 to 16
            if (crew.Length != s.climb.crewStarts.Length || !System.Linq.Enumerable.SequenceEqual(crew, s.climb.crewStarts))
                notes.Add("crewStarts " + string.Join(",", s.climb.crewStarts) + " -> " + string.Join(",", crew));
            s.climb.crewStarts = crew;
            if (s.climb.movePending && s.climb.stage != ShopStageRules.FoodCorner)
            {
                notes.Add("movePending at stage " + s.climb.stage + " -> false");
                s.climb.movePending = false;
            }
            s.climb.rentOwed = Range("rentOwed", s.climb.rentOwed, 0L, MaxRentOwed, notes);
            if (s.climb.starWindow.Length > GateRules.Kept)
            {
                notes.Add("starWindow " + s.climb.starWindow.Length + " values -> the newest " + GateRules.Kept);
                var newest = new int[GateRules.Kept];
                Array.Copy(s.climb.starWindow, s.climb.starWindow.Length - GateRules.Kept, newest, 0, GateRules.Kept);
                s.climb.starWindow = newest;
            }
            for (int i = 0; i < s.climb.starWindow.Length; i++)
                s.climb.starWindow[i] = (int)Range("starWindow[" + i + "]", s.climb.starWindow[i], GateRules.MinTenths, GateRules.MaxTenths, notes);

            s.money.balance = Range("balance", s.money.balance, 0L, EconomyLimits.MaxBalance, notes);
            s.money.earnedTotal = Range("earnedTotal", s.money.earnedTotal, 0L, long.MaxValue, notes);
            s.money.spentTotal = Range("spentTotal", s.money.spentTotal, 0L, long.MaxValue, notes);
            s.clock.day = (int)Range("day", s.clock.day, 1L, int.MaxValue, notes);
            s.clock.servicesPlayed = (int)Range("servicesPlayed", s.clock.servicesPlayed, 0L, int.MaxValue, notes);
            s.clock.playSeconds = Range("playSeconds", s.clock.playSeconds, 0L, long.MaxValue, notes);
            s.hygienePercent = (int)Range("hygienePercent", s.hygienePercent, 0L, 100L, notes);
            s.stats.guestsServed = (int)Range("guestsServed", s.stats.guestsServed, 0L, int.MaxValue, notes);
            s.stats.guestsMissed = (int)Range("guestsMissed", s.stats.guestsMissed, 0L, int.MaxValue, notes);
            s.stats.starSum = (int)Range("starSum", s.stats.starSum, 0L, int.MaxValue, notes);
            s.stats.bestServiceEarned = Range("bestServiceEarned", s.stats.bestServiceEarned, 0L, long.MaxValue, notes);

            // Spec 037: a menu price outside its board's bounds or off its step grid is put back on it (MenuPricing.Normalize).
            foreach (ShopSave.MenuPart part in s.menu)
            {
                if (part == null) continue;
                int line = Worktown.Game.Npc.MenuPrices.LineOfId(part.id);
                if (line < 0) continue; // unknown: the board ignores it; SaveService keeps it for the next save (037 review)
                long normal = MenuPricing.Normalize(Worktown.Game.Npc.MenuPrices.Reference(line), part.cents);
                if (normal != part.cents) notes.Add("menu " + part.id + " " + part.cents + " -> " + normal);
                part.cents = normal;
            }

            // Spec 059: the office part: a special that names no dish line is none; the history keeps its newest 8 records, each in range.
            ClampOffice(s, notes);

            // Spec 062a: the layout's values in range (LayoutCodec); a piece of an unknown type stays in the file.
            if (s.layout.pieces != null && s.layout.pieces.Length > 0)
                s.layout = Worktown.Game.Layout.LayoutCodec.ToPart(
                    Worktown.Game.Layout.LayoutCodec.ToLayout(s.layout, Worktown.Sim.Layout.PieceCatalog.CreateDefault(), notes));

            foreach (ShopSave.StockPart part in s.stock)
            {
                if (part == null) continue;
                int line = LineOfSlot(part.slot);
                if (line < 0) continue; // unknown: ignored, kept for the next save
                part.count = (int)Range(part.slot, part.count, 0L, StockCatalog.Goods[line].Capacity, notes);
            }
        }

        /// <summary>Spec 059: the office part in range (ServiceRecord.Of clamps each record; ServiceHistory keeps the newest 8).</summary>
        private static void ClampOffice(ShopSave s, List<string> notes)
        {
            s.office ??= new ShopSave.OfficePart();
            s.office.special ??= string.Empty;
            s.office.history ??= new ShopSave.RecordPart[0];
            if (s.office.special.Length > 0 && Worktown.Game.Office.SpecialRules.FromLineId(s.office.special) == Worktown.Game.Npc.Menu.None)
            {
                notes.Add("office special " + s.office.special + " -> none");
                s.office.special = string.Empty;
            }
            Worktown.Game.Office.ServiceRecord[] records = SaveService.RecordsOf(s.office);
            int given = 0;
            foreach (ShopSave.RecordPart p in s.office.history) if (p != null) given++;
            if (given > records.Length) notes.Add("office history " + given + " records -> the newest " + records.Length);
            ShopSave.RecordPart[] parts = SaveService.PartsOf(records);
            int skip = s.office.history.Length - parts.Length;
            for (int i = 0; i < parts.Length; i++)
            {
                ShopSave.RecordPart was = i + skip >= 0 && i + skip < s.office.history.Length ? s.office.history[i + skip] : null;
                if (was != null && !SaveService.SameRecord(was, parts[i])) notes.Add("office history day " + was.day + " clamped");
            }
            s.office.history = parts;
        }

        /// <summary>Spec 057: the most rent a save may owe: the owed cap (two services) of the largest crew (16 players, D40).</summary>
        public static long MaxRentOwed { get; } = RestaurantRules.OwedCap(RestaurantRules.Due(Worktown.Sim.Economy.EconomyCatalog.CreateDefault(),
            FoodCornerRules.DefaultClimb, RestaurantRules.Crew(FoodCornerRules.DefaultClimb, RestaurantRules.MaxPlayers), null));

        private static long Range(string what, long value, long min, long max, List<string> notes)
        {
            long clamped = value < min ? min : value > max ? max : value;
            if (clamped != value) notes.Add(what + " " + value + " -> " + clamped);
            return clamped;
        }
    }
}
