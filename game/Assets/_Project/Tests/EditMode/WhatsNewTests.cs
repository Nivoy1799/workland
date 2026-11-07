using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.UI;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 011 AC1 and AC2.</summary>
    public class WhatsNewTests
    {
        private const string Json = @"{ ""entries"": [
            { ""id"": ""a"", ""date"": ""2026-11-06"", ""title"": ""A"", ""items"": [""x""], ""test"": [""t""] },
            { ""id"": ""c"", ""date"": ""2026-11-07"", ""title"": ""C"", ""items"": [], ""test"": [] },
            { ""id"": ""b"", ""date"": ""2026-11-05"", ""title"": ""B"", ""items"": [], ""test"": [] }
        ] }";

        [Test]
        public void Parse_ReadsEntries()
        {
            IReadOnlyList<WhatsNewEntry> entries = WhatsNewRules.Parse(Json);
            Assert.AreEqual(3, entries.Count);
            Assert.AreEqual("A", entries[0].title);
            Assert.AreEqual("x", entries[0].items[0]);
            Assert.AreEqual("t", entries[0].test[0]);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("not json")]
        [TestCase("{ \"entries\": 5 }")]
        public void Parse_BadJson_GivesEmptyList(string json)
        {
            Assert.AreEqual(0, WhatsNewRules.Parse(json).Count);
        }

        [Test]
        public void Parse_SkipsEntriesWithoutId()
        {
            Assert.AreEqual(0, WhatsNewRules.Parse("{ \"entries\": [ { \"title\": \"no id\" } ] }").Count);
        }

        [Test]
        public void Newest_IsLatestDate()
        {
            Assert.AreEqual("c", WhatsNewRules.Newest(WhatsNewRules.Parse(Json)).id);
            Assert.IsNull(WhatsNewRules.Newest(new List<WhatsNewEntry>()));
        }

        [Test]
        public void ShowOnStart_OnlyForUnseenBuild()
        {
            WhatsNewEntry newest = WhatsNewRules.Newest(WhatsNewRules.Parse(Json));
            Assert.IsTrue(WhatsNewRules.ShouldShowOnStart(newest, null));
            Assert.IsTrue(WhatsNewRules.ShouldShowOnStart(newest, "a"));
            Assert.IsFalse(WhatsNewRules.ShouldShowOnStart(newest, "c"));
            Assert.IsFalse(WhatsNewRules.ShouldShowOnStart(null, null));
        }

        [Test]
        public void ShippedFile_HasEntriesFor007_009_010_011()
        {
            var asset = Resources.Load<TextAsset>("WhatsNew");
            Assert.IsNotNull(asset, "Resources/WhatsNew.json missing");
            IReadOnlyList<WhatsNewEntry> entries = WhatsNewRules.Parse(asset.text);
            var ids = new List<string>();
            foreach (WhatsNewEntry e in entries)
            {
                ids.Add(e.id);
                Assert.IsNotEmpty(e.title, e.id);
                Assert.IsNotEmpty(e.items, e.id + " has no items");
                Assert.IsNotEmpty(e.test, e.id + " has nothing to test");
            }
            foreach (string spec in new[] { "spec-007", "spec-009", "spec-010", "spec-011" })
                Assert.IsTrue(ids.Exists(id => id.EndsWith(spec)), "missing entry for " + spec);
        }

        [Test]
        public void ShippedFile_HasTheSpec019Entry_WithTestSteps()
        {
            IReadOnlyList<WhatsNewEntry> entries = WhatsNewRules.Parse(Resources.Load<TextAsset>("WhatsNew").text);
            WhatsNewEntry entry = null;
            foreach (WhatsNewEntry e in entries)
                if (e.id == "2026-11-07-spec-019") entry = e;
            Assert.IsNotNull(entry, "spec 019 AC10");
            Assert.AreEqual("2026-11-07", entry.date);
            Assert.GreaterOrEqual(entry.test.Length, 3);
        }

        [Test]
        public void ShippedFile_HasTheSpec020Entry_WithTestSteps()
        {
            IReadOnlyList<WhatsNewEntry> entries = WhatsNewRules.Parse(Resources.Load<TextAsset>("WhatsNew").text);
            WhatsNewEntry entry = null;
            foreach (WhatsNewEntry e in entries)
                if (e.id == "2026-11-07-spec-020") entry = e;
            Assert.IsNotNull(entry, "spec 020 AC10");
            Assert.AreEqual("2026-11-07", entry.date);
            Assert.GreaterOrEqual(entry.test.Length, 3);
            Assert.AreNotSame(entry, WhatsNewRules.Newest(entries), "spec 021 is newer");
        }

        [Test]
        public void ShippedFile_HasTheSpec021Entry_WithTestSteps()
        {
            IReadOnlyList<WhatsNewEntry> entries = WhatsNewRules.Parse(Resources.Load<TextAsset>("WhatsNew").text);
            WhatsNewEntry entry = null;
            foreach (WhatsNewEntry e in entries)
                if (e.id == "2026-11-07-spec-021") entry = e;
            Assert.IsNotNull(entry, "spec 021 AC10");
            Assert.AreEqual("2026-11-07", entry.date);
            Assert.GreaterOrEqual(entry.test.Length, 3);
            Assert.AreNotSame(entry, WhatsNewRules.Newest(entries), "spec 022 is newer");
        }

        [Test]
        public void ShippedFile_HasTheSpec022Entry_WithTestSteps_AsTheNewest()
        {
            IReadOnlyList<WhatsNewEntry> entries = WhatsNewRules.Parse(Resources.Load<TextAsset>("WhatsNew").text);
            WhatsNewEntry entry = null;
            foreach (WhatsNewEntry e in entries)
                if (e.id == "2026-11-08-spec-022") entry = e;
            Assert.IsNotNull(entry, "spec 022 AC9");
            Assert.AreEqual("2026-11-08", entry.date);
            Assert.GreaterOrEqual(entry.test.Length, 3);
            Assert.AreSame(entry, WhatsNewRules.Newest(entries), "the newest entry, so the banner shows it");
        }
    }
}
