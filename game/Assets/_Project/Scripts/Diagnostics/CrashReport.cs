using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Worktown.Game.Diagnostics
{
    /// <summary>
    /// Spec 049, pure: the words of the local crash and exception log (CrashLog). One entry per exception: when (UTC), the build
    /// (version and the spec 047 build hash), the kind (an unhandled exception on any thread, an exception Unity logged, an unobserved
    /// task, a host tick stall), the thread, the message and the whole stack. The same exception repeating is written
    /// <see cref="MaxRepeats"/> times, then only counted (<see cref="Admit"/>), so a per-frame exception cannot fill the disk; the file
    /// starts over above <see cref="MaxBytes"/> (the old one is kept once as <c>.old</c>).
    /// </summary>
    public static class CrashReport
    {
        public const string DefaultFileName = "crash.log";
        /// <summary>One file at most this big (then it is moved to .old and a new one starts).</summary>
        public const long MaxBytes = 2L * 1024 * 1024;
        /// <summary>The same exception (kind, message, first stack line) is written this often per session.</summary>
        public const int MaxRepeats = 5;
        public const string EntryStart = "=== Worktown crash log entry ===";

        /// <summary>
        /// The marker a session leaves while it runs ("running") and on a clean quit ("clean"). The next start writes a line when the
        /// last session never wrote "clean" (a native crash, a hang that was killed, the power): <see cref="PreviousSessionText"/>.
        /// </summary>
        public const string MarkerRunning = "running";
        public const string MarkerClean = "clean";

        /// <summary>One entry, ready to append (ends with a blank line). Never throws on null parts.</summary>
        public static string Entry(DateTime utc, string version, string buildHash, string kind, string thread, string message, string stack)
        {
            var sb = new StringBuilder(256 + (stack?.Length ?? 0));
            sb.Append(EntryStart).Append('\n');
            sb.Append("time: ").Append(utc.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append(" UTC\n");
            sb.Append("build: ").Append(Or(version, "unknown")).Append(" hash ").Append(Or(buildHash, "unknown")).Append('\n');
            sb.Append("kind: ").Append(Or(kind, "exception")).Append('\n');
            sb.Append("thread: ").Append(Or(thread, "unknown")).Append('\n');
            sb.Append("message: ").Append(OneLine(message)).Append('\n');
            sb.Append("stack:\n");
            string body = string.IsNullOrWhiteSpace(stack) ? "(no stack)" : stack.Replace("\r\n", "\n").TrimEnd('\n');
            foreach (string line in body.Split('\n')) sb.Append("  ").Append(line).Append('\n');
            sb.Append('\n');
            return sb.ToString();
        }

        /// <summary>The thread as the log names it: "main" or "not main", the managed id, the name, and the pool or background flag.</summary>
        public static string ThreadText(bool isMain, int managedId, string name, bool isPool, bool isBackground)
        {
            var sb = new StringBuilder(48);
            sb.Append(isMain ? "main" : "not main").Append(" #").Append(managedId);
            if (!string.IsNullOrEmpty(name)) sb.Append(" \"").Append(name).Append('"');
            if (isPool) sb.Append(" pool");
            else if (isBackground) sb.Append(" background");
            return sb.ToString();
        }

        /// <summary>What decides "the same exception": the kind, the message and the first stack line.</summary>
        public static string Key(string kind, string message, string stack)
        {
            string first = string.Empty;
            if (!string.IsNullOrEmpty(stack))
            {
                string trimmed = stack.TrimStart();
                int nl = trimmed.IndexOf('\n');
                first = nl < 0 ? trimmed : trimmed.Substring(0, nl);
            }
            return (kind ?? string.Empty) + "|" + OneLine(message) + "|" + first.Trim();
        }

        /// <summary>
        /// Counts <paramref name="key"/> in <paramref name="seen"/> and says whether this one is written (the first
        /// <see cref="MaxRepeats"/>). The caller owns the dictionary (one per session) and its lock.
        /// </summary>
        public static bool Admit(Dictionary<string, int> seen, string key)
        {
            if (seen == null) return true;
            string k = key ?? string.Empty;
            seen.TryGetValue(k, out int count);
            seen[k] = count + 1;
            return count < MaxRepeats;
        }

        /// <summary>The closing line of a session: how many repeats were only counted (empty when none).</summary>
        public static string SuppressedText(Dictionary<string, int> seen)
        {
            if (seen == null) return string.Empty;
            int extra = 0;
            foreach (int count in seen.Values) if (count > MaxRepeats) extra += count - MaxRepeats;
            return extra > 0 ? "(" + extra + " repeats of logged exceptions were counted, not written)\n\n" : string.Empty;
        }

        /// <summary>A file of <paramref name="size"/> bytes starts over before the next entry.</summary>
        public static bool NeedsRotation(long size) => size >= MaxBytes;

        /// <summary>The marker text while a session runs: "running" and when it started.</summary>
        public static string RunningMarker(DateTime utc, string version) =>
            MarkerRunning + " " + utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC, " + Or(version, "unknown");

        /// <summary>Null when the previous session ended cleanly (or there was none), else the line for the log.</summary>
        public static string PreviousSessionText(string marker, string unityCrashFolder)
        {
            if (string.IsNullOrWhiteSpace(marker)) return null;
            string text = marker.Trim();
            if (!text.StartsWith(MarkerRunning, StringComparison.Ordinal)) return null;
            return "The previous session (started " + text.Substring(MarkerRunning.Length).Trim() + ") did not quit cleanly: a native crash, a"
                + " hang that was killed, or the power. Unity's own report of a native crash is in " + Or(unityCrashFolder, "its Crashes folder") + ".";
        }

        private static string Or(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

        private static string OneLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "(none)";
            return text.Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
