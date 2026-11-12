using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Layout;
using Worktown.Game.Npc;
using Worktown.Game.Office;
using Worktown.Game.Save;
using Worktown.Sim.Layout;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 059 AC7: the history keeps 8 and compares with up to 4; the office part survives a save round trip; a version 4 save migrates
    /// to the empty defaults and gains the office terminal; clamps on load; version 99 is newer.
    /// </summary>
    public class ServiceHistoryTests
    {
        private static ServiceRecord Rec(int day, int served) => ServiceRecord.Of(day, served + 1, served, 1, served * 400L, 30, 40, 35, new[] { served });

        [Test]
        public void TheHistory_Keeps8_AndComparesWithUpTo4()
        {
            ServiceRecord[] history = new ServiceRecord[0];
            for (int day = 1; day <= 11; day++) history = ServiceHistory.Push(history, Rec(day, day));
            Assert.AreEqual(ServiceHistory.Kept, history.Length);
            Assert.AreEqual(4, history[0].Day, "the oldest dropped");
            Assert.IsTrue(ServiceHistory.TryLast(history, out ServiceRecord last));
            Assert.AreEqual(11, last.Day);
            Assert.IsTrue(ServiceHistory.TryUsual(history, r => r.Served, out int usual));
            Assert.AreEqual(9, usual, "the mean of days 7 to 10 (8.5 rounds up), not the older ones");
            Assert.IsFalse(ServiceHistory.TryUsual(new[] { Rec(1, 5) }, r => r.Served, out _), "nothing before the last one");
            Assert.IsTrue(ServiceHistory.TryUsual(new[] { Rec(1, 5), Rec(2, 9) }, r => r.Served, out usual));
            Assert.AreEqual(5, usual);
            Assert.IsFalse(ServiceHistory.TryUsual(new[] { ServiceRecord.Of(1, 0, 0, 0, 0, 0, -1, -1, null), Rec(2, 3) }, r => r.WaitSeconds, out _),
                "an unknown wait is left out");
            Assert.AreEqual(3, ServiceHistory.Newest(history, 3).Length);
            Assert.AreEqual(9, ServiceHistory.Newest(history, 3)[0].Day);
            CollectionAssert.IsEmpty(ServiceHistory.Newest(null));
        }

        [Test]
        public void ARecord_ClampsItsFields()
        {
            ServiceRecord r = ServiceRecord.Of(0, -3, 70000, 5, -10, long.MaxValue, -7, -1, new[] { 300, -1, 4, 5, 6, 7, 8 }, 5000);
            Assert.AreEqual(1, r.Day);
            Assert.AreEqual(0, r.Came);
            Assert.AreEqual(short.MaxValue, r.Served);
            Assert.AreEqual(0, r.Earned);
            Assert.AreEqual(int.MaxValue, r.Tips);
            Assert.AreEqual(ServiceRecord.Unknown, r.StarsTenths);
            Assert.AreEqual(ServiceRecord.Unknown, r.WaitSeconds);
            Assert.AreEqual(1000, r.RivalPermille);
            CollectionAssert.AreEqual(new[] { 255, 0, 4, 5, 6, 7 }, Enumerable.Range(0, 6).Select(r.SoldAt).ToArray(), "six counts, 0 to 255");
            Assert.AreEqual(0, r.SoldAt(6));
            Assert.AreEqual(r, ServiceRecord.Of(0, -3, 70000, 5, -10, long.MaxValue, -7, -1, new[] { 300, -1, 4, 5, 6, 7, 8 }, 5000));
        }

        [Test]
        public void TheOfficePart_SurvivesASave()
        {
            ShopSave save = ShopSaveCodecTests.Full();
            var history = new List<ServiceRecord>();
            for (int day = 1; day <= 3; day++) history.Add(ServiceRecord.Of(day, 12, 10, 2, 8400, 600, 38, 41, new[] { 7, 0, 0, 3, 5, 0 }));
            save.office = SaveService.OfficePartOf(Menu.FalafelWrap, history);
            SaveLoadResult r = ShopSaveCodec.Default.TryParse(ShopSaveCodec.Default.ToJson(save));
            Assert.IsTrue(r.Ok, r.Error.ToString());
            CollectionAssert.IsEmpty(r.Notes);
            Assert.AreEqual("falafel_wrap", r.Save.office.special);
            Assert.AreEqual(Menu.FalafelWrap, SpecialRules.FromLineId(r.Save.office.special));
            CollectionAssert.AreEqual(history, SaveService.RecordsOf(r.Save.office));
            Assert.AreEqual(6, r.Save.formatVersion); // spec 062b: v6
        }

        [Test]
        public void ALoad_Clamps()
        {
            ShopSave save = ShopSaveCodecTests.Full();
            var parts = new List<ShopSave.RecordPart>();
            for (int day = 1; day <= 11; day++) parts.Add(new ShopSave.RecordPart { day = day, came = 5, served = 4, sold = new[] { 4 } });
            parts[10].served = -4;
            save.office = new ShopSave.OfficePart { special = "lemonade", history = parts.ToArray() };
            SaveLoadResult r = ShopSaveCodec.Default.TryParse(ShopSaveCodec.Default.ToJson(save));
            Assert.IsTrue(r.Ok, r.Error.ToString());
            Assert.AreEqual(string.Empty, r.Save.office.special, "a drink is no special");
            Assert.AreEqual(ServiceHistory.Kept, r.Save.office.history.Length);
            Assert.AreEqual(4, r.Save.office.history[0].day, "the newest 8");
            Assert.AreEqual(0, r.Save.office.history[7].served);
            Assert.IsTrue(r.Notes.Any(n => n.StartsWith("office special")), string.Join("; ", r.Notes));
            Assert.IsTrue(r.Notes.Any(n => n.StartsWith("office history 11 records")), string.Join("; ", r.Notes));
            Assert.IsTrue(r.Notes.Any(n => n == "office history day 11 clamped"), string.Join("; ", r.Notes));
        }

        [Test]
        public void AVersion4Save_GetsTheDefaults_AndTheOfficeTerminal()
        {
            ShopSave v4 = ShopSaveCodecTests.Full();
            ShopLayout standard = DefaultLayout.Load();
            ShopLayout without = standard.WithPieces(standard.Pieces.Where(p => p.TypeId != PieceCatalog.OfficeTerminal).ToList());
            v4.layout = LayoutCodec.ToPart(without);
            v4.office = new ShopSave.OfficePart { special = "falafel_wrap", history = new[] { new ShopSave.RecordPart { day = 3 } } };
            string json = ShopSaveCodec.Default.ToJson(v4).Replace("\"formatVersion\": 6", "\"formatVersion\": 4");
            SaveLoadResult r = ShopSaveCodec.Default.TryParse(json);
            Assert.IsTrue(r.Ok, r.Error.ToString());
            Assert.AreEqual(6, r.Save.formatVersion); // spec 062b: v6
            Assert.AreEqual(string.Empty, r.Save.office.special, "a version 4 save had no office part: the empty defaults");
            CollectionAssert.IsEmpty(r.Save.office.history);
            ShopSave.PiecePart office = r.Save.layout.pieces.Single(p => p.type == PieceCatalog.OfficeTerminal);
            LayoutPiece model = standard.Pieces.Single(p => p.TypeId == PieceCatalog.OfficeTerminal);
            Assert.AreEqual((model.XMm, model.ZMm), (office.xMm, office.zMm), "at its default place (free)");
            Assert.AreEqual(without.NextId, office.id, "a new id");
            CollectionAssert.AreEqual(new[] { PieceCatalog.OfficeTerminal }, SaveMigrations.AddedInV5.ToArray());
            Assert.AreEqual(6, ShopSaveCodec.Default.Version); // spec 062b: v6
        }

        [Test]
        public void Version99_IsNewer()
        {
            string json = ShopSaveCodec.Default.ToJson(ShopSaveCodecTests.Full()).Replace("\"formatVersion\": 6", "\"formatVersion\": 99");
            Assert.AreEqual(SaveError.NewerVersion, ShopSaveCodec.Default.TryParse(json).Error);
            string v6 = ShopSaveCodec.Default.ToJson(ShopSaveCodecTests.Full()).Replace("\"formatVersion\": 6", "\"formatVersion\": 7");
            Assert.AreEqual(SaveError.NewerVersion, ShopSaveCodec.Default.TryParse(v6).Error);
            Assert.IsNotNull(JsonUtility.FromJson<ShopSave>("{}").office, "a part that always exists");
        }
    }
}
