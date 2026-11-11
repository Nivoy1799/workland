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
    /// starts over above <see cref="MaxBytes"/> (the old one is kept once as <c>.old</c>). 049 review: a session writes at most
    /// <see cref="MaxEntries"/> entries and <see cref="MaxKeys"/> different exceptions, then one <see cref="CapText"/> line and nothing
    /// more (<see cref="WithinCap"/>); the player's own folders are written as <c>%TEMP%</c> and <c>%USERPROFILE%</c> (<see cref="Scrub"/>).
    /// </summary>
    public static class CrashReport
    {
        public const string DefaultFileName = "crash.log";
        /// <summary>One file at most this big (then it is moved to .old and a new one starts).</summary>
        public const long MaxBytes = 2L * 1024 * 1024;
        /// <summary>The same exception (kind, message, first stack line) is written this often per session.</summary>
        public const int MaxRepeats = 5;
        /// <summary>049 review: at most this many different exceptions (keys) per session.</summary>
        public const int MaxKeys = 256;
        /// <summary>049 review: at most this many entries per session.</summary>
        public const int MaxEntries = 1000;
        /// <summary>049 review: the one line written when the session's cap is reached; nothing is written after it.</summary>
        public const string CapText = "(the crash log cap of this session was reached: further entries suppressed)\n\n";
        public const string UserProfileToken = "%USERPROFILE%";
        public const string TempToken = "%TEMP%";
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

        /// <summary>
        /// 049 review, the session cap: false once <paramref name="written"/> entries reached <see cref="MaxEntries"/>, or when
        /// <paramref name="key"/> is new while <see cref="MaxKeys"/> keys are already in <paramref name="seen"/>. Changes nothing: the
        /// caller asks this before <see cref="Admit"/> (which then never grows <paramref name="seen"/> past the cap).
        /// </summary>
        public static bool WithinCap(Dictionary<string, int> seen, string key, int written)
        {
            if (written >= MaxEntries) return false;
            if (seen == null) return true;
            return seen.Count < MaxKeys || seen.ContainsKey(key ?? string.Empty);
        }

        /// <summary>The closing line of a session: how many repeats were only counted (empty when none).</summary>
        public static string SuppressedText(Dictionary<string, int> seen) => SuppressedText(seen, 0);

        /// <summary>
        /// The closing line of a session: how many repeats were only counted and how many entries came after the cap (empty when
        /// none).
        /// </summary>
        public static string SuppressedText(Dictionary<string, int> seen, int afterCap)
        {
            int extra = 0;
            if (seen != null)
                foreach (int count in seen.Values) if (count > MaxRepeats) extra += count - MaxRepeats;
            if (extra == 0 && afterCap <= 0) return string.Empty;
            var sb = new StringBuilder(112);
            sb.Append('(');
            if (extra > 0) sb.Append(extra).Append(" repeats of logged exceptions were counted, not written");
            if (afterCap > 0) sb.Append(extra > 0 ? "; " : string.Empty).Append(afterCap).Append(" entries came after the cap, not written");
            sb.Append(")\n\n");
            return sb.ToString();
        }

        /// <summary>
        /// 049 review: <paramref name="text"/> with the player's own folders replaced, so a crash log names no account: the temp folder
        /// by <see cref="TempToken"/> (first: it is usually inside the profile, and may be in its short 8.3 form), then the user profile
        /// by <see cref="UserProfileToken"/>. Either slash and any case match, only as a whole folder name (a profile "C:\Users\jo"
        /// leaves "C:\Users\joan" alone). Empty folders and folders shorter than 3 characters (a root) are skipped; null gives "".
        /// </summary>
        public static string Scrub(string text, string userProfile, string tempPath)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            text = ReplaceFolder(text, tempPath, TempToken);
            return ReplaceFolder(text, userProfile, UserProfileToken);
        }

        private static string ReplaceFolder(string text, string folder, string token)
        {
            if (string.IsNullOrWhiteSpace(folder)) return text;
            string trimmed = folder.Trim().TrimEnd('\\', '/');
            if (trimmed.Length < 3) return text;
            text = ReplaceWhole(text, trimmed.Replace('/', '\\'), token);
            return ReplaceWhole(text, trimmed.Replace('\\', '/'), token);
        }

        private static string ReplaceWhole(string text, string folder, string token)
        {
            int at = text.IndexOf(folder, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return text;
            var sb = new StringBuilder(text.Length);
            int from = 0;
            while (at >= 0)
            {
                int end = at + folder.Length;
                bool whole = end >= text.Length || !ContinuesName(text[end]);
                sb.Append(text, from, at - from);
                if (whole) sb.Append(token);
                else sb.Append(text, at, folder.Length);
                from = end;
                at = text.IndexOf(folder, from, StringComparison.OrdinalIgnoreCase);
            }
            sb.Append(text, from, text.Length - from);
            return sb.ToString();
        }

        /// <summary>A character that would make the match only the start of a longer folder name.</summary>
        private static bool ContinuesName(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '~';

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
