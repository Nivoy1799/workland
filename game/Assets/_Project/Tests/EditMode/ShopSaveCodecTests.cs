using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Save;
using Worktown.Game.Shop;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 053 AC2: the save's text: a full round trip, each error from a fixture, clamping, a fake migration.</summary>
    public class ShopSaveCodecTests
    {
        private const string Id = "0123456789abcdef0123456789abcdef";
        private const string Time1 = "2026-11-11T10:00:00.0000000Z";
        private const string Time2 = "2026-11-11T12:30:00.0000000Z";

        /// <summary>A version 1 save with every field set.</summary>
        public static ShopSave Full()
        {
            var s = new ShopSave
            {
                gameVersion = "0.1.0",
                buildHash = "a1b2c3d4e5f60718",
                saveId = Id,
                createdUtc = Time1,
                savedUtc = Time2,
                shopName = "Your food corner",
                worldSeed = 0xF00DCAFE12345678UL,
                hygienePercent = 87,
            };
            s.climb.stage = 1;
            s.climb.unlocks = new[] { "dish_fries", "future_unlock" };
            s.climb.goalReached = true;
            s.money.balance = 3105;
            s.money.earnedTotal = 1840;
            s.money.spentTotal = 1610;
            s.clock.day = 4;
            s.clock.servicesPlayed = 3;
            s.clock.playSeconds = 1234;
            s.stock = new[]
            {
                new ShopSave.StockPart { slot = "shelf:potato", count = 9 },
                new ShopSave.StockPart { slot = "fridge:tomato", count = 2 },
                new ShopSave.StockPart { slot = "garage:wrench", count = 3 },
            };
            s.stats.guestsServed = 11;
            s.stats.guestsMissed = 2;
            s.stats.starSum = 40;
            s.stats.bestServiceEarned = 2300;
            return s;
        }

        private const string Minimal = "{\"format\":\"worktown-shop-save\",\"formatVersion\":1,\"saveId\":\"" + Id
            + "\",\"createdUtc\":\"" + Time1 + "\",\"savedUtc\":\"" + Time1 + "\"}";
        private const string Version99 = "{\"format\":\"worktown-shop-save\",\"formatVersion\":99,\"saveId\":\"" + Id
            + "\",\"createdUtc\":\"" + Time1 + "\",\"savedUtc\":\"" + Time1 + "\"}";
        private const string Broken = "{\"format\":\"worktown-shop-save\",\"formatVersion\":1,\"money\":{\"balance\":";
        private const string Garbage = "this is not a save";
        private const string OtherFormat = "{\"format\":\"some-other-game\",\"formatVersion\":1}";
        private const string BadId = "{\"format\":\"worktown-shop-save\",\"formatVersion\":1,\"saveId\":\"nope\",\"createdUtc\":\"" + Time1
            + "\",\"savedUtc\":\"" + Time1 + "\"}";
        private const string BadTime = "{\"format\":\"worktown-shop-save\",\"formatVersion\":1,\"saveId\":\"" + Id
            + "\",\"createdUtc\":\"yesterday\",\"savedUtc\":\"" + Time1 + "\"}";

        private static string Huge()
        {
            var sb = new StringBuilder("{\"format\":\"worktown-shop-save\",\"shopName\":\"");
            sb.Append('x', 2 * 1024 * 1024);
            return sb.Append("\"}").ToString();
        }

        [Test]
        public void AFullSave_RoundTripsEqual()
        {
            ShopSave a = Full();
            string json = ShopSaveCodec.Default.ToJson(a);
            Assert.Less(Encoding.UTF8.GetByteCount(json), 8 * 1024, "a save stays small (< 8 KB)");
            StringAssert.Contains("\n", json, "indented");
            SaveLoadResult r = ShopSaveCodec.Default.TryParse(json);
            Assert.IsTrue(r.Ok, r.Error.ToString());
            CollectionAssert.IsEmpty(r.Notes, "nothing to clamp");
            Assert.AreEqual(json, ShopSaveCodec.Default.ToJson(r.Save), "every field came back");
            Assert.AreEqual(0xF00DCAFE12345678UL, r.Save.worldSeed);
            Assert.AreEqual(3105, r.Save.money.balance);
            Assert.AreEqual(4, r.Save.clock.day);
            Assert.IsTrue(r.Save.climb.goalReached);
            Assert.AreEqual(87, r.Save.hygienePercent);
            Assert.AreEqual("garage:wrench", r.Save.stock[2].slot, "an unknown slot is kept for the next save");
            Assert.AreEqual(3, r.Save.stock[2].count);
            CollectionAssert.Contains(r.Save.climb.unlocks, "future_unlock", "an unknown unlock is kept");
        }

        [Test]
        public void AMinimalSave_LoadsWithItsDefaults()
        {
            SaveLoadResult r = ShopSaveCodec.Default.TryParse(Minimal);
            Assert.IsTrue(r.Ok, r.Error.ToString());
            Assert.AreEqual(1, r.Save.climb.stage);
            Assert.AreEqual(1, r.Save.clock.day);
            Assert.AreEqual("Your food corner", r.Save.shopName);
            CollectionAssert.Contains(r.Save.climb.unlocks, "dish_fries");
        }

        [Test]
        public void EachError_ComesFromItsFixture()
        {
            Assert.AreEqual(SaveError.NewerVersion, ShopSaveCodec.Default.TryParse(Version99).Error);
            Assert.AreEqual(SaveError.NotJson, ShopSaveCodec.Default.TryParse(Broken).Error);
            Assert.AreEqual(SaveError.NotJson, ShopSaveCodec.Default.TryParse(Garbage).Error);
            Assert.AreEqual(SaveError.NotJson, ShopSaveCodec.Default.TryParse(string.Empty).Error);
            Assert.AreEqual(SaveError.WrongFormat, ShopSaveCodec.Default.TryParse(OtherFormat).Error);
            Assert.AreEqual(SaveError.TooLarge, ShopSaveCodec.Default.TryParse(Huge()).Error);
            Assert.AreEqual(SaveError.Invalid, ShopSaveCodec.Default.TryParse(BadId).Error);
            Assert.AreEqual(SaveError.Invalid, ShopSaveCodec.Default.TryParse(BadTime).Error);
            Assert.IsNull(ShopSaveCodec.Default.TryParse(Version99).Save, "no save on an error");
        }

        [Test]
        public void OutOfRangeValues_AreClampedAndNoted()
        {
            ShopSave s = Full();
            s.money.balance = -50;
            s.hygienePercent = 140;
            s.clock.day = 0;
            s.climb.stage = 7;
            s.stock[0].count = 500;
            s.stats.guestsServed = -3;
            SaveLoadResult r = ShopSaveCodec.Default.TryParse(JsonUtility.ToJson(s));
            Assert.IsTrue(r.Ok, r.Error.ToString());
            Assert.AreEqual(0, r.Save.money.balance);
            Assert.AreEqual(100, r.Save.hygienePercent);
            Assert.AreEqual(1, r.Save.clock.day);
            Assert.AreEqual(2, r.Save.climb.stage, "spec 057 re-pin: the highest stage this build has (the first restaurant)");
            Assert.AreEqual(StockCatalog.CapacityOf("potato"), r.Save.stock[0].count, "up to the slot's capacity");
            Assert.AreEqual(0, r.Save.stats.guestsServed);
            Assert.GreaterOrEqual(r.Notes.Count, 6, "each clamp is noted for the log");

            s = Full();
            s.money.balance = long.MaxValue;
            r = ShopSaveCodec.Default.TryParse(JsonUtility.ToJson(s));
            Assert.AreEqual(Worktown.Sim.Economy.EconomyLimits.MaxBalance, r.Save.money.balance);
        }

        [Test]
        public void AFakeMigrationStep_RunsOnceAndInOrder()
        {
            var ran = new List<string>();
            var migrations = new SaveMigrations(new (int, Func<ShopSave, ShopSave>)[]
            {
                (1, s => { ran.Add("1->2"); s.money.balance *= 100; return s; }),
                (2, s => { ran.Add("2->3"); s.shopName = s.shopName + "!"; return s; }),
            });
            Assert.AreEqual(3, migrations.Latest);
            var codec = new ShopSaveCodec(migrations);
            ShopSave v1 = Full();
            v1.formatVersion = 1;
            SaveLoadResult r = codec.TryParse(JsonUtility.ToJson(v1));
            Assert.IsTrue(r.Ok, r.Error.ToString());
            CollectionAssert.AreEqual(new[] { "1->2", "2->3" }, ran);
            Assert.AreEqual(310500, r.Save.money.balance);
            Assert.AreEqual(3, r.Save.formatVersion);
            ran.Clear();
            ShopSave v2 = Full();
            v2.formatVersion = 2;
            codec.TryParse(JsonUtility.ToJson(v2));
            CollectionAssert.AreEqual(new[] { "2->3" }, ran, "a version 2 save skips the first step");
            Assert.Throws<ArgumentException>(() => new SaveMigrations(new (int, Func<ShopSave, ShopSave>)[] { (2, s => s) }), "steps start at 1");
            Assert.AreEqual(4, ShopSaveCodec.Default.Version, "spec 057 re-pin: this build has three steps (v1 to v2, cents; v2 to v3, the layout; v3 to v4, the restaurant)");
            string v5 = codec.ToJson(Full()).Replace("\"formatVersion\": 3", "\"formatVersion\": 5");
            Assert.AreEqual(SaveError.NewerVersion, ShopSaveCodec.Default.TryParse(v5).Error, "a v5 save is newer here");
        }

        [Test]
        public void SlotNames_MapToStockLines()
        {
            int potato = StockCatalog.IndexOf("potato");
            Assert.AreEqual("shelf:potato", ShopSaveCodec.SlotOf(potato));
            Assert.AreEqual(potato, ShopSaveCodec.LineOfSlot("shelf:potato"));
            Assert.AreEqual(-1, ShopSaveCodec.LineOfSlot("shelf:moon"));
            for (int i = 0; i < StockCatalog.Count; i++) Assert.AreEqual(i, ShopSaveCodec.LineOfSlot(ShopSaveCodec.SlotOf(i)), "line " + i);
        }
    }
}
