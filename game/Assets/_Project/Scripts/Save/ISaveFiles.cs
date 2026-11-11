using System;
using System.Collections.Generic;

namespace Worktown.Game.Save
{
    /// <summary>
    /// Spec 053: the file steps the save store needs, so tests run it on a fake (a write that throws midway, a corrupt file) and the
    /// game on the disk (<see cref="DiskSaveFiles"/>). Paths are full paths. Nothing here deletes a file.
    /// </summary>
    public interface ISaveFiles
    {
        bool Exists(string path);
        string ReadAllText(string path);
        /// <summary>The file's size in bytes (053 review: checked before it is read).</summary>
        long Length(string path);
        /// <summary>Writes (creates or overwrites) <paramref name="path"/>; may throw midway.</summary>
        void WriteAllText(string path, string text);
        /// <summary>
        /// Puts <paramref name="source"/> in the place of <paramref name="target"/>, keeping the old target as <paramref name="backup"/>
        /// (overwriting an older backup); when <paramref name="target"/> does not exist yet, a plain move.
        /// </summary>
        void Replace(string source, string target, string backup);
        /// <summary>The files of <paramref name="folder"/> matching <paramref name="pattern"/> (a missing folder: none).</summary>
        IReadOnlyList<string> List(string folder, string pattern);
        DateTime LastWriteUtc(string path);
        void EnsureFolder(string folder);
    }
}
