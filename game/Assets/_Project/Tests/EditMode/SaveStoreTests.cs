using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Worktown.Game.Save;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 053 AC3: the save store on fake files and a fixed clock: atomic writes, the .bak, the newest, never overwriting.</summary>
    public class SaveStoreTests
    {
        private const string Folder = "C:/saves";

        /// <summary>In-memory files; a write can be made to throw midway (after part of the text landed).</summary>
        private sealed class FakeFiles : ISaveFiles
        {
            public readonly Dictionary<string, string> Files = new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly Dictionary<string, DateTime> Times = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            public bool ThrowOnWrite;
            public bool ThrowOnReplace;
            public int Deletes;
            private DateTime _now = new DateTime(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc);

            public int Reads;
            public long? FakeLength;

            public bool Exists(string path) => Files.ContainsKey(path);

            public string ReadAllText(string path)
            {
                Reads++;
                return Files.TryGetValue(path, out string t) ? t : throw new FileNotFoundException(path);
            }

            public long Length(string path) => FakeLength ?? (Files.TryGetValue(path, out string t) ? t.Length : 0);

            public void WriteAllText(string path, string text)
            {
                if (ThrowOnWrite)
                {
                    Files[path] = text.Substring(0, text.Length / 2); // half of it landed
                    throw new IOException("disk full");
                }
                Files[path] = text;
                Times[path] = _now = _now.AddSeconds(1);
            }

            public void Replace(string source, string target, string backup)
            {
                if (ThrowOnReplace) throw new IOException("locked");
                if (Files.TryGetValue(target, out string old))
                {
                    Files[backup] = old;
                    Times[backup] = Times[target];
                }
                Files[target] = Files[source];
                Times[target] = Times[source];
                Files.Remove(source);
                Times.Remove(source);
            }

            public IReadOnlyList<string> List(string folder, string pattern)
            {
                var list = new List<string>();
                foreach (string path in Files.Keys)
                    if (path.StartsWith(folder, StringComparison.Ordinal) && path.EndsWith(".json", StringComparison.Ordinal)) list.Add(path);
                return list;
            }

            public DateTime LastWriteUtc(string path) => Times.TryGetValue(path, out DateTime t) ? t : DateTime.MinValue;
            public void EnsureFolder(string folder) { }
        }

        private static DateTime _clock;
        private static DateTime Now() => _clock;

        private static SaveStore Store(FakeFiles files) => new SaveStore(Folder, files, ShopSaveCodec.Default, Now);

        private static ShopSave Shop(string id, long balance)
        {
            ShopSave s = ShopSaveCodecTests.Full();
            s.saveId = id;
            s.money.balance = balance;
            return s;
        }

        private const string IdA = "aaaaaaaaaaaa00000000000000000000";
        private const string IdB = "bbbbbbbbbbbb00000000000000000000";

        [SetUp]
        public void SetClock() => _clock = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void TheFile_IsNamedByTheFirst12OfTheId_AndTheClockStampsIt()
        {
            var files = new FakeFiles();
            SaveStore store = Store(files);
            Assert.AreEqual(Path.Combine(Folder, "shop-aaaaaaaaaaaa.json"), store.PathFor(IdA));
            ShopSave s = Shop(IdA, 100);
            Assert.IsTrue(store.Write(s, out string error), error);
            Assert.AreEqual("2026-11-11T12:00:00.0000000Z", s.savedUtc, "savedUtc from the injected clock");
            Assert.IsTrue(store.Load(store.PathFor(IdA)).Ok);
            Assert.IsFalse(files.Exists(store.PathFor(IdA) + SaveStore.TempSuffix), "the temp file was moved into place");
        }

        [Test]
        public void AWriteThatThrowsMidway_LeavesTheOldFileReadable()
        {
            var files = new FakeFiles();
            SaveStore store = Store(files);
            Assert.IsTrue(store.Write(Shop(IdA, 100), out _));
            files.ThrowOnWrite = true;
            Assert.IsFalse(store.Write(Shop(IdA, 999), out string error));
            StringAssert.Contains("disk full", error);
            SaveLoadResult r = store.Load(store.PathFor(IdA));
            Assert.IsTrue(r.Ok);
            Assert.AreEqual(100, r.Save.money.balance, "the last good file");
            files.ThrowOnWrite = false;
            files.ThrowOnReplace = true;
            Assert.IsFalse(store.Write(Shop(IdA, 555), out _));
            Assert.AreEqual(100, store.Load(store.PathFor(IdA)).Save.money.balance, "a failed replace keeps it too");
        }

        [Test]
        public void ACorruptFile_LoadsFromItsBak()
        {
            var files = new FakeFiles();
            SaveStore store = Store(files);
            Assert.IsTrue(store.Write(Shop(IdA, 100), out _));
            Assert.IsTrue(store.Write(Shop(IdA, 200), out _)); // the 100 one is the .bak now
            string path = store.PathFor(IdA);
            files.Files[path] = "{\"format\":\"worktown-sh"; // cut off
            SaveLoadResult r = store.Load(path);
            Assert.IsTrue(r.Ok);
            Assert.IsTrue(r.FromBackup);
            Assert.AreEqual(100, r.Save.money.balance);
            files.Files[path + SaveStore.BackupSuffix] = "garbage";
            Assert.AreEqual(SaveError.NotJson, store.Load(path).Error, "both broken: the file's own error");
        }

        [Test]
        public void ANewerVersion_IsNotHiddenByTheBak()
        {
            var files = new FakeFiles();
            SaveStore store = Store(files);
            Assert.IsTrue(store.Write(Shop(IdA, 100), out _));
            Assert.IsTrue(store.Write(Shop(IdA, 200), out _));
            string path = store.PathFor(IdA);
            ShopSave newer = Shop(IdA, 300);
            newer.formatVersion = 99;
            files.Files[path] = UnityEngine.JsonUtility.ToJson(newer);
            Assert.AreEqual(SaveError.NewerVersion, store.Load(path).Error);
        }

        [Test]
        public void TheNewest_IsBySavedUtc_ThenByFileName()
        {
            var files = new FakeFiles();
            SaveStore store = Store(files);
            Assert.IsNull(store.Newest());
            Assert.IsFalse(store.HasSave());
            Assert.IsTrue(store.Write(Shop(IdB, 2), out _)); // 12:00
            _clock = _clock.AddHours(1);
            Assert.IsTrue(store.Write(Shop(IdA, 1), out _)); // 13:00
            Assert.AreEqual(1, store.Newest().Save.money.balance, "the later savedUtc");
            Assert.IsTrue(store.Write(Shop(IdB, 3), out _)); // also 13:00: a tie
            Assert.AreEqual(1, store.Newest().Save.money.balance, "a tie goes to the first file name (ordinal)");
            Assert.IsTrue(store.HasSave());
            // A broken newest file (its write time is the latest) is the newest: Continue shows its error.
            files.Files[store.PathFor(IdB)] = "garbage";
            files.Files.Remove(store.PathFor(IdB) + SaveStore.BackupSuffix);
            files.Times[store.PathFor(IdB)] = new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);
            Assert.IsFalse(store.Newest().Ok);
            Assert.IsTrue(store.HasSave(), "the other shop still loads");
        }

        [Test]
        public void NewShop_NeverOverwritesAnExistingFile()
        {
            var files = new FakeFiles();
            SaveStore store = Store(files);
            Assert.IsTrue(store.CreateNew(Shop(IdA, 100), out _));
            Assert.IsTrue(store.IsTaken(IdA));
            Assert.IsFalse(store.CreateNew(Shop(IdA, 5), out string error), error);
            Assert.AreEqual(100, store.Load(store.PathFor(IdA)).Save.money.balance, "the existing shop is untouched");
            Assert.IsFalse(store.IsTaken(IdB));
            Assert.IsTrue(store.CreateNew(Shop(IdB, 7), out _));
            Assert.AreEqual(2, store.Files().Count);
        }

        [Test]
        public void AHugeFile_IsRefusedUnread()
        {
            var files = new FakeFiles();
            SaveStore store = Store(files);
            Assert.IsTrue(store.Write(Shop(IdA, 100), out _));
            files.FakeLength = ShopSaveCodec.MaxBytes + 1L;
            files.Reads = 0;
            Assert.AreEqual(SaveError.TooLarge, store.Load(store.PathFor(IdA)).Error);
            Assert.AreEqual(0, files.Reads, "the size is checked before the file is read");
        }

        [Test]
        public void ANewerVersion_AsTheOnlyFile_StillOffersContinue()
        {
            var files = new FakeFiles();
            SaveStore store = Store(files);
            ShopSave newer = Shop(IdA, 300);
            newer.formatVersion = 99;
            files.Files[store.PathFor(IdA)] = UnityEngine.JsonUtility.ToJson(newer);
            files.Times[store.PathFor(IdA)] = _clock;
            Assert.IsFalse(store.HasSave(), "nothing this build can load");
            SaveLoadResult newest = store.Newest();
            Assert.AreEqual(SaveError.NewerVersion, newest.Error);
            Assert.IsTrue(UI.MenuRules.OffersContinue(store.HasSave(), newest.Error), "Continue shows, so its card can say why");
            Assert.IsFalse(UI.MenuRules.OffersContinue(false, SaveError.NotJson), "a broken file alone offers nothing");
            Assert.IsFalse(UI.MenuRules.OffersContinue(false, SaveError.None));
        }

        [Test]
        public void ScriptedRuns_HaveNoSlot_UnlessTheyBringAFolder()
        {
            Assert.IsNull(SaveStore.FolderFor(string.Empty, true, "C:/data"), "a tour, bot or test run never uses the player's saves");
            Assert.AreEqual("D:/tour/saves", SaveStore.FolderFor("D:/tour/saves", true, "C:/data"));
            Assert.AreEqual(Path.Combine("C:/data", "saves"), SaveStore.FolderFor(string.Empty, false, "C:/data"));
            var store = new SaveStore(null, new FakeFiles(), ShopSaveCodec.Default, Now);
            Assert.IsFalse(store.HasSlot);
            Assert.IsFalse(store.Write(Shop(IdA, 1), out _));
            Assert.IsFalse(store.HasSave());
            Assert.IsNull(store.Newest());
        }
    }
}
