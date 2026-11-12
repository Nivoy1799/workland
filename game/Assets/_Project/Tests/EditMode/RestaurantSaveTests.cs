using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Climb;
using Worktown.Game.Layout;
using Worktown.Game.Save;
using Worktown.Sim.Layout;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 057 AC8: save format version 4. A version 1 fixture (053's format: the food corner, the goal reached) migrates with the new
    /// fields' defaults and the goal board; a version 3 save (062a) gets the goal board by the backfill; a version 4 save with every new field
    /// round-trips equal; stage 3 clamps to 2; the codec clamps the new fields; version 99 is newer.
    /// </summary>
    public class RestaurantSaveTests
    {
        private const string Id = "0123456789abcdef0123456789abcdef";
        private const string Time1 = "2026-11-11T10:00:00.0000000Z";

        /// <summary>053's format (version 1): the corner, the rent goal reached ($34,500 of the old whole dollars).</summary>
        private const string V1GoalReached = "{\"format\":\"worktown-shop-save\",\"formatVersion\":1,\"saveId\":\"" + Id
            + "\",\"createdUtc\":\"" + Time1 + "\",\"savedUtc\":\"" + Time1 + "\",\"shopName\":\"Your food corner\","
            + "\"climb\":{\"stage\":1,\"unlocks\":[\"dish_fries\"],\"goalReached\":true},\"money\":{\"balance\":34500},"
            + "\"clock\":{\"day\":9,\"servicesPlayed\":8}}";

        private const string Version99 = "{\"format\":\"worktown-shop-save\",\"formatVersion\":99,\"saveId\":\"" + Id
            + "\",\"createdUtc\":\"" + Time1 + "\",\"savedUtc\":\"" + Time1 + "\"}";

        /// <summary>A version 4 save with every new field set.</summary>
        public static ShopSave FullV4()
        {
            ShopSave s = ShopSaveCodecTests.Full();
            s.shopName = GoalBoardRules.RestaurantName;
            s.climb.stage = 2;
            s.climb.unlocks = new[] { "dish_fries", "rent_restaurant", "dish_falafel_wrap", "dish_lemonade", "future_unlock" };
            s.climb.goalReached = false;
            s.climb.rentOwed = 3050;
            s.climb.starWindow = new[] { 31, 36, 40, 28, 35, 50 };
            s.climb.menuOff = new[] { "dish_lemonade", "dish_from_a_newer_build" };
            s.layout = LayoutCodec.ToPart(DefaultLayout.Load());
            return s;
        }

        [Test]
        public void AVersion1Save_MigratesToVersion4_WithTheDefaults()
        {
            SaveLoadResult r = ShopSaveCodec.Default.TryParse(V1GoalReached);
            Assert.IsTrue(r.Ok, r.Error.ToString());
            Assert.AreEqual(4, r.Save.formatVersion);
            Assert.AreEqual(1, r.Save.climb.stage);
            Assert.IsTrue(r.Save.climb.goalReached);
            Assert.AreEqual(30000, r.Save.money.balance, "037's cents: the old $34,500 is $300.00");
            Assert.IsFalse(r.Save.climb.movePending);
            Assert.AreEqual(0, r.Save.climb.rentOwed);
            CollectionAssert.IsEmpty(r.Save.climb.starWindow);
            CollectionAssert.IsEmpty(r.Save.climb.menuOff);
            Assert.AreEqual(9, r.Save.clock.day, "everything else is kept");
            Assert.AreEqual(1, r.Save.layout.pieces.Count(p => p.type == PieceCatalog.GoalBoard), "the goal board is in the layout");
        }

        [Test]
        public void AVersion3Save_GetsTheGoalBoard()
        {
            ShopSave v3 = ShopSaveCodecTests.Full();
            ShopLayout standard = DefaultLayout.Load();
            ShopLayout withoutBoard = standard.WithPieces(standard.Pieces.Where(p => p.TypeId != PieceCatalog.GoalBoard).ToList());
            v3.layout = LayoutCodec.ToPart(withoutBoard);
            string json = ShopSaveCodec.Default.ToJson(v3).Replace("\"formatVersion\": 4", "\"formatVersion\": 3");
            SaveLoadResult r = ShopSaveCodec.Default.TryParse(json);
            Assert.IsTrue(r.Ok, r.Error.ToString());
            ShopSave.PiecePart board = r.Save.layout.pieces.Single(p => p.type == PieceCatalog.GoalBoard);
            LayoutPiece model = standard.Pieces.Single(p => p.TypeId == PieceCatalog.GoalBoard);
            Assert.AreEqual(model.XMm, board.xMm, "at its default place (free)");
            Assert.AreEqual(model.ZMm, board.zMm);
            Assert.AreEqual(withoutBoard.NextId, board.id, "a new id, never a reused one");
            Assert.AreEqual(withoutBoard.NextId + 1, r.Save.layout.nextId);
            CollectionAssert.AreEqual(new[] { PieceCatalog.GoalBoard }, SaveMigrations.AddedInV4.ToArray());
        }

        [Test]
        public void AVersion4Save_WithEveryNewField_RoundTripsEqual()
        {
            string json = ShopSaveCodec.Default.ToJson(FullV4());
            SaveLoadResult r = ShopSaveCodec.Default.TryParse(json);
            Assert.IsTrue(r.Ok, r.Error.ToString());
            CollectionAssert.IsEmpty(r.Notes, "nothing to clamp");
            Assert.AreEqual(json, ShopSaveCodec.Default.ToJson(r.Save), "every field came back");
            Assert.AreEqual(2, r.Save.climb.stage);
            Assert.AreEqual(3050, r.Save.climb.rentOwed);
            CollectionAssert.AreEqual(new[] { 31, 36, 40, 28, 35, 50 }, r.Save.climb.starWindow);
            CollectionAssert.Contains(r.Save.climb.menuOff, "dish_from_a_newer_build", "an unknown id is kept");
            CollectionAssert.AreEqual(new[] { "dish_from_a_newer_build" }, SaveService.UnknownMenuOff(r.Save).ToArray());
        }

        [Test]
        public void Stage3_ClampsTo2_AndTheNewFieldsAreClamped()
        {
            ShopSave s = FullV4();
            s.climb.stage = 3;
            s.climb.movePending = true;
            s.climb.rentOwed = long.MaxValue;
            s.climb.starWindow = new[] { 99, 5, 60, 30, 30, 30, 30, 30, 30 };
            SaveLoadResult r = ShopSaveCodec.Default.TryParse(JsonUtility.ToJson(s));
            Assert.IsTrue(r.Ok, r.Error.ToString());
            Assert.AreEqual(2, r.Save.climb.stage);
            Assert.IsFalse(r.Save.climb.movePending, "a move is pending only at the food corner");
            Assert.AreEqual(ShopSaveCodec.MaxRentOwed, r.Save.climb.rentOwed);
            Assert.AreEqual(40000, ShopSaveCodec.MaxRentOwed, "two rents of a 16-player crew ($200 each)");
            Assert.AreEqual(8, r.Save.climb.starWindow.Length, "the newest 8");
            Assert.AreEqual(GateRules.MinTenths, r.Save.climb.starWindow[0], "the oldest (99) dropped; 5 -> 10");
            Assert.AreEqual(GateRules.MaxTenths, r.Save.climb.starWindow[1], "60 -> 50");
            Assert.IsTrue(r.Save.climb.starWindow.All(v => v >= GateRules.MinTenths && v <= GateRules.MaxTenths));
            Assert.GreaterOrEqual(r.Notes.Count, 4);
            s.climb.rentOwed = -5;
            Assert.AreEqual(0, ShopSaveCodec.Default.TryParse(JsonUtility.ToJson(s)).Save.climb.rentOwed);
        }

        [Test]
        public void Version99_IsNewer()
        {
            Assert.AreEqual(SaveError.NewerVersion, ShopSaveCodec.Default.TryParse(Version99).Error);
            string v5 = ShopSaveCodec.Default.ToJson(FullV4()).Replace("\"formatVersion\": 4", "\"formatVersion\": 5");
            Assert.AreEqual(SaveError.NewerVersion, ShopSaveCodec.Default.TryParse(v5).Error);
        }

        [Test]
        public void TheLoadedMenu_IsTheOwnedRecipes_LessMenuOff()
        {
            ShopSave s = FullV4();
            Assert.AreEqual(0b1001, ShopStage.OwnedOf(FoodCornerRules.DefaultClimb, s.climb.unlocks), "the falafel wrap and the drinks");
            Assert.AreEqual(0b0001, ShopStage.MenuOf(FoodCornerRules.DefaultClimb, 0b1001, s.climb.menuOff), "the drinks are off");
            CollectionAssert.AreEqual(new[] { "dish_lemonade" }, ShopStage.MenuOffOf(FoodCornerRules.DefaultClimb, 0b1001, 0b0001));
        }
    }
}
