using System;
using System.Collections.Generic;
using System.IO;

namespace Worktown.Game.Save
{
    /// <summary>
    /// Spec 053: the host's save files in one folder (one file per shop: <c>shop-&lt;first 12 of the save id&gt;.json</c>). Writes are
    /// atomic: the text goes to <c>&lt;file&gt;.tmp</c>, which then replaces the file, the old one kept as <c>&lt;file&gt;.bak</c>; a
    /// write that fails leaves the last good file. A load that finds the file cut off or broken (NotJson, Invalid) tries the <c>.bak</c>.
    /// Files are never deleted. <see cref="Newest"/> picks by savedUtc (ties: the file name, ordinal); a broken file counts by its write
    /// time, so a broken newest save shows its error instead of an older shop. Only the host (the server) uses it; a scripted run without
    /// <c>-save-dir</c> has no folder at all (<see cref="FolderFor"/>), so it never reads or writes the player's saves.
    /// </summary>
    public sealed class SaveStore
    {
        public const string FilePrefix = "shop-";
        public const string Extension = ".json";
        public const string TempSuffix = ".tmp";
        public const string BackupSuffix = ".bak";
        public const int IdChars = 12;
        public const string FolderName = "saves";

        private readonly ISaveFiles _files;
        private readonly ShopSaveCodec _codec;
        private readonly Func<DateTime> _utcNow;

        public SaveStore(string folder, ISaveFiles files, ShopSaveCodec codec, Func<DateTime> utcNow)
        {
            Folder = string.IsNullOrEmpty(folder) ? null : folder;
            _files = files ?? throw new ArgumentNullException(nameof(files));
            _codec = codec ?? throw new ArgumentNullException(nameof(codec));
            _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        /// <summary>The save folder, or null: no slot (nothing is read or written).</summary>
        public string Folder { get; }
        public bool HasSlot => Folder != null;
        public ShopSaveCodec Codec => _codec;

        /// <summary>
        /// The folder of a run: <c>-save-dir</c> when given; none for a scripted run without it (the tours, the observer, bots, the 049
        /// test flags: never the player's saves); else <c>persistentDataPath/saves</c> (on Windows
        /// <c>%USERPROFILE%\AppData\LocalLow\&lt;company&gt;\Worktown\saves</c>; the company is still "DefaultCompany", O1).
        /// </summary>
        public static string FolderFor(string saveDir, bool scripted, string persistentDataPath)
        {
            if (!string.IsNullOrWhiteSpace(saveDir)) return saveDir;
            if (scripted || string.IsNullOrEmpty(persistentDataPath)) return null;
            return Path.Combine(persistentDataPath, FolderName);
        }

        /// <summary>The file of save id <paramref name="saveId"/> (null without a slot).</summary>
        public string PathFor(string saveId)
        {
            if (!HasSlot || string.IsNullOrEmpty(saveId)) return null;
            string id = saveId.Length > IdChars ? saveId.Substring(0, IdChars) : saveId;
            return Path.Combine(Folder, FilePrefix + id.ToLowerInvariant() + Extension);
        }

        /// <summary>Whether a new shop with <paramref name="saveId"/> would land on an existing file (or its backup).</summary>
        public bool IsTaken(string saveId)
        {
            string path = PathFor(saveId);
            return path != null && (_files.Exists(path) || _files.Exists(path + BackupSuffix));
        }

        /// <summary>New shop: writes <paramref name="save"/> as a new file; false (nothing written) when its file exists already.</summary>
        public bool CreateNew(ShopSave save, out string error)
        {
            error = string.Empty;
            if (save == null || !HasSlot) { error = "no save folder"; return false; }
            if (IsTaken(save.saveId)) { error = "a save with that id exists"; return false; }
            return Write(save, out error);
        }

        /// <summary>Writes <paramref name="save"/> atomically (savedUtc from the clock); false and the old file kept on any failure.</summary>
        public bool Write(ShopSave save, out string error)
        {
            error = string.Empty;
            string path = save != null ? PathFor(save.saveId) : null;
            if (path == null) { error = HasSlot ? "no save id" : "no save folder"; return false; }
            string before = save.savedUtc;
            try
            {
                save.savedUtc = ShopSaveCodec.TimeText(_utcNow());
                if (string.IsNullOrEmpty(save.createdUtc)) save.createdUtc = save.savedUtc;
                string text = _codec.ToJson(save);
                _files.EnsureFolder(Folder);
                string temp = path + TempSuffix;
                _files.WriteAllText(temp, text);
                _files.Replace(temp, path, path + BackupSuffix);
                return true;
            }
            catch (Exception e)
            {
                save.savedUtc = before;
                error = e.GetType().Name + ": " + e.Message;
                return false;
            }
        }

        /// <summary>Loads <paramref name="path"/>; a file cut off or broken (NotJson, Invalid) loads from its <c>.bak</c> when that one is good.</summary>
        public SaveLoadResult Load(string path)
        {
            SaveLoadResult main = Read(path);
            if (main.Ok || (main.Error != SaveError.NotJson && main.Error != SaveError.Invalid)) return main.At(path, false);
            SaveLoadResult backup = Read(path + BackupSuffix);
            return backup.Ok ? backup.At(path, true) : main.At(path, false);
        }

        private SaveLoadResult Read(string path)
        {
            if (path == null || !_files.Exists(path)) return SaveLoadResult.Fail(SaveError.NotJson, path);
            try
            {
                // 053 review: a huge file is refused unread.
                if (_files.Length(path) > ShopSaveCodec.MaxBytes) return SaveLoadResult.Fail(SaveError.TooLarge, path);
                return _codec.TryParse(_files.ReadAllText(path));
            }
            catch (Exception)
            {
                return SaveLoadResult.Fail(SaveError.NotJson, path); // unreadable: the same as broken, the .bak is tried
            }
        }

        /// <summary>The shop files of the folder (the .tmp and .bak copies are not shops), in ordinal order.</summary>
        public List<string> Files()
        {
            var list = new List<string>();
            if (!HasSlot) return list;
            foreach (string path in _files.List(Folder, FilePrefix + "*" + Extension))
            {
                string name = Path.GetFileName(path);
                if (name.StartsWith(FilePrefix, StringComparison.Ordinal) && name.EndsWith(Extension, StringComparison.Ordinal)) list.Add(path);
            }
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        /// <summary>
        /// The newest shop: by savedUtc, ties by the file name (ordinal, the first wins); a broken file counts by its write time, so it can
        /// be the newest (Continue then shows its error). Null when the folder has no shop file.
        /// </summary>
        public SaveLoadResult Newest()
        {
            SaveLoadResult best = null;
            DateTime bestTime = DateTime.MinValue;
            foreach (string path in Files())
            {
                SaveLoadResult r = Load(path);
                DateTime time;
                if (!r.Ok || !ShopSaveCodec.TryTime(r.Save.savedUtc, out time))
                {
                    try { time = _files.LastWriteUtc(path); }
                    catch (Exception) { time = DateTime.MinValue; }
                }
                if (best == null || time > bestTime)
                {
                    best = r;
                    bestTime = time;
                }
            }
            return best;
        }

        /// <summary>The title screen's hasSave: a shop file that parses exists.</summary>
        public bool HasSave()
        {
            foreach (string path in Files())
                if (Load(path).Ok) return true;
            return false;
        }
    }
}
