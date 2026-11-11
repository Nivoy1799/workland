using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Worktown.Game.Climb;
using Worktown.Game.Economy;
using Worktown.Game.Npc;
using Worktown.Game.Save;
using Worktown.Game.Shop;
using Worktown.Sim.Climb;
using Worktown.Sim.Economy;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 053 AC8, the save and continue path (SaveService's pure parts over the real economy, stock room and cleaning room, the store
    /// on real files in a temp folder): three guests served, the service ended, the file written; its balance, day, stock, hygiene and
    /// stats equal the live values; applied to a fresh game they come back, with no grant.
    /// </summary>
    public class SaveServiceTests
    {
        private string _folder;

        [SetUp]
        public void MakeFolder()
        {
            _folder = Path.Combine(Path.GetTempPath(), "worktown-save-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
        }

        [TearDown]
        public void RemoveFolder()
        {
            if (!string.IsNullOrEmpty(_folder) && _folder.Contains("worktown-save-test-") && Directory.Exists(_folder))
                Directory.Delete(_folder, true);
        }

        [Test]
        public void ServeThreeGuests_EndTheService_TheFileHoldsTheLiveValues_AndAFreshGameGetsThemBack()
        {
            EconomyCatalog catalog = EconomyCatalog.CreateDefault();
            long starting = FoodCornerRules.StartingMoney(catalog, ClimbCatalog.CreateDefault());
            var clock = new DateTime(2026, 11, 11, 18, 0, 0, DateTimeKind.Utc);
            var store = new SaveStore(_folder, new DiskSaveFiles(), ShopSaveCodec.Default, () => clock);
            ShopSave save = SaveService.NewSave("c0ffee00c0ffee00c0ffee00c0ffee00", 99UL, starting, ShopSaveCodec.TimeText(clock), "0.1.0", "hash");
            Assert.IsTrue(store.CreateNew(save, out string created), created);

            // The live game: the shop opens with the starting money (no grant), three guests pay, three potatoes are used.
            TownEconomy economy = TownEconomy.CreateGame(SaveService.SeedOf(save));
            Assert.IsTrue(economy.TryOpenAccount(EconomyRules.ShopAccount, save.money.balance));
            var room = new StockRoom();
            room.RestoreCounts(SaveService.CountsOf(save));
            int potato = StockCatalog.IndexOf("potato");
            var summary = ShiftSummary.Empty;
            foreach (long bill in new long[] { 420, 455, 390 })
            {
                Assert.AreEqual(Cooking.CookOutcome.Ok, room.Take(potato));
                Assert.IsTrue(economy.TryPayGuest(EconomyRules.AccountId(1), bill, out long paid));
                summary = summary.WithServed(4, paid);
            }
            summary = summary.WithMissed(1);
            var cleaning = new CleaningRoom();
            cleaning.RestoreHygiene(83, 0);

            // The service ends: its numbers into the save, the live values captured, the file written.
            SaveService.AddService(save, summary);
            int[] counts = new int[StockCatalog.Count];
            room.ReadCounts(counts);
            long liveBalance = economy.BalanceOf(EconomyRules.ShopAccount);
            long nextDay = AutosaveRules.NextDay(false, 1, true, 1, 1);
            SaveService.Fill(save, liveBalance, liveBalance - starting, 0, nextDay, 360, counts, null, cleaning.Hygiene,
                ShopStageRules.FoodCorner, false);
            clock = clock.AddMinutes(6);
            Assert.IsTrue(store.Write(save, out string error), error);
            Assert.IsTrue(File.Exists(store.PathFor(save.saveId) + SaveStore.BackupSuffix), "the first file is the .bak now");

            SaveLoadResult loaded = store.Newest();
            Assert.IsTrue(loaded.Ok, loaded.Error.ToString());
            ShopSave file = loaded.Save;
            Assert.AreEqual(starting + 420 + 455 + 390, file.money.balance);
            Assert.AreEqual(liveBalance, file.money.balance);
            Assert.AreEqual(2, file.clock.day, "the next day to play");
            Assert.AreEqual(1, file.clock.servicesPlayed);
            Assert.AreEqual(counts[potato], SaveService.CountsOf(file)[potato]);
            Assert.AreEqual(StockCatalog.CapacityOf("potato") / 2 - 3, SaveService.CountsOf(file)[potato]);
            Assert.AreEqual(83, file.hygienePercent);
            Assert.AreEqual(3, file.stats.guestsServed);
            Assert.AreEqual(1, file.stats.guestsMissed);
            Assert.AreEqual(12, file.stats.starSum);
            Assert.AreEqual(420 + 455 + 390, file.stats.bestServiceEarned);
            Assert.AreEqual("2026-11-11T18:06:00.0000000Z", file.savedUtc);

            // A fresh server applies it: the balance with no grant, the stock, the hygiene, the day.
            TownEconomy fresh = TownEconomy.CreateGame(SaveService.SeedOf(file));
            Assert.IsTrue(fresh.TryOpenAccount(EconomyRules.ShopAccount, file.money.balance));
            Assert.IsFalse(fresh.TryOpenAccount(EconomyRules.AccountId(1), EconomyRules.StartingGrant), "the first player's spawn adds nothing");
            Assert.AreEqual(liveBalance, fresh.BalanceOf(EconomyRules.ShopAccount));
            var freshRoom = new StockRoom();
            freshRoom.RestoreCounts(SaveService.CountsOf(file));
            int[] freshCounts = new int[StockCatalog.Count];
            freshRoom.ReadCounts(freshCounts);
            CollectionAssert.AreEqual(counts, freshCounts);
            var freshCleaning = new CleaningRoom();
            freshCleaning.RestoreHygiene(file.hygienePercent, 0);
            Assert.AreEqual(83, freshCleaning.Hygiene);
            Assert.AreEqual("Day 2, 08:00", EconomyRules.ClockText(fresh.Tick + EconomyRules.DayOffsetTicks(file.clock.day)));
            Assert.AreEqual(99UL ^ 2UL, SaveService.SeedOf(file), "the day's economy is seeded worldSeed ^ day");
        }

        [Test]
        public void ANewShop_StartsAtDayOne_WithItsStartingMoneyAndTheStartingStock()
        {
            ShopSave s = SaveService.NewSave("c0ffee00c0ffee00c0ffee00c0ffee00", 5UL, 2875, "2026-11-11T18:00:00.0000000Z", "0.1.0", "h");
            Assert.AreEqual(1, s.clock.day);
            Assert.AreEqual(2875, s.money.balance);
            Assert.AreEqual(1, s.climb.stage);
            CollectionAssert.AreEqual(new[] { "dish_fries" }, s.climb.unlocks);
            Assert.AreEqual("Your food corner", s.shopName);
            Assert.AreEqual(StockCatalog.Count, s.stock.Length);
            Assert.AreEqual(StockCatalog.CapacityOf("potato") / 2, SaveService.CountsOf(s)[StockCatalog.IndexOf("potato")]);
            Assert.IsTrue(ShopSaveCodec.Default.TryParse(ShopSaveCodec.Default.ToJson(s)).Ok);
        }

        [Test]
        public void UnknownSlots_AreKeptForTheNextSave()
        {
            var unknown = new List<ShopSave.StockPart> { new ShopSave.StockPart { slot = "garage:wrench", count = 3 } };
            ShopSave.StockPart[] parts = SaveService.StockParts(new int[StockCatalog.Count], unknown);
            Assert.AreEqual(StockCatalog.Count + 1, parts.Length);
            Assert.AreEqual("garage:wrench", parts[parts.Length - 1].slot);
            int[] counts = SaveService.CountsOf(new ShopSave { stock = parts });
            Assert.AreEqual(StockCatalog.Count, counts.Length, "an unknown slot changes no line");
        }
    }
}
