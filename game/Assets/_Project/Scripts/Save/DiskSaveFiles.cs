using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Worktown.Game.Save
{
    /// <summary>Spec 053: <see cref="ISaveFiles"/> on the disk (UTF-8 without a BOM; File.Replace keeps the old file as the backup).</summary>
    public sealed class DiskSaveFiles : ISaveFiles
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        public bool Exists(string path) => File.Exists(path);

        public string ReadAllText(string path) => File.ReadAllText(path, Utf8);

        public void WriteAllText(string path, string text)
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, Utf8))
            {
                writer.Write(text);
                writer.Flush();
                stream.Flush(true); // on the disk before the replace
            }
        }

        public void Replace(string source, string target, string backup)
        {
            if (File.Exists(target)) File.Replace(source, target, backup, true);
            else File.Move(source, target);
        }

        public IReadOnlyList<string> List(string folder, string pattern) =>
            Directory.Exists(folder) ? Directory.GetFiles(folder, pattern) : Array.Empty<string>();

        public DateTime LastWriteUtc(string path) => File.GetLastWriteTimeUtc(path);

        public void EnsureFolder(string folder) => Directory.CreateDirectory(folder);
    }
}
