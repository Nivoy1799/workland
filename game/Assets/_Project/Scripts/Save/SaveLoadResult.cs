using System;
using System.Collections.Generic;

namespace Worktown.Game.Save
{
    /// <summary>
    /// Spec 053: a load's outcome: the save (null on an error), the error, what was clamped (the caller logs it), the file it came from
    /// and whether that was the <c>.bak</c> copy. Immutable.
    /// </summary>
    public sealed class SaveLoadResult
    {
        private static readonly IReadOnlyList<string> NoNotes = Array.Empty<string>();

        public SaveLoadResult(ShopSave save, SaveError error, IReadOnlyList<string> notes = null, string path = null, bool fromBackup = false)
        {
            Error = save == null && error == SaveError.None ? SaveError.Invalid : error;
            Save = Error == SaveError.None ? save : null;
            Notes = notes ?? NoNotes;
            Path = path ?? string.Empty;
            FromBackup = fromBackup;
        }

        public ShopSave Save { get; }
        public SaveError Error { get; }
        public IReadOnlyList<string> Notes { get; }
        public string Path { get; }
        public bool FromBackup { get; }
        public bool Ok => Error == SaveError.None;

        /// <summary>The same outcome, read from <paramref name="path"/> (its <c>.bak</c> when <paramref name="fromBackup"/>).</summary>
        public SaveLoadResult At(string path, bool fromBackup) => new SaveLoadResult(Save, Error, Notes, path, fromBackup);

        public static SaveLoadResult Fail(SaveError error, string path = null) => new SaveLoadResult(null, error, null, path);
    }
}
