using System;
using System.IO;
using System.Text;

namespace Worktown.Game.Diagnostics
{
    /// <summary>
    /// Spec 049: appends crash log entries to one local file, from any thread (a lock; the file is opened for each entry and shared, so
    /// a second game on the same PC can append too). Starts over above CrashReport.MaxBytes (the old file kept as .old). Never throws:
    /// a crash log that cannot be written must not crash the game.
    /// </summary>
    public sealed class CrashFileSink : ICrashSink
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private readonly object _lock = new object();

        public CrashFileSink(string path)
        {
            FilePath = path ?? string.Empty;
        }

        public string FilePath { get; }

        /// <summary>Entries that could not be written (a locked or read-only file).</summary>
        public int Failures { get; private set; }

        public void Write(string entry)
        {
            if (string.IsNullOrEmpty(entry) || FilePath.Length == 0) return;
            lock (_lock)
            {
                try
                {
                    string dir = Path.GetDirectoryName(FilePath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    var info = new FileInfo(FilePath);
                    if (info.Exists && CrashReport.NeedsRotation(info.Length))
                    {
                        string old = FilePath + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(FilePath, old);
                    }
                    byte[] bytes = Utf8.GetBytes(entry);
                    using (var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                        stream.Write(bytes, 0, bytes.Length);
                }
                catch (Exception)
                {
                    Failures++;
                }
            }
        }
    }
}
