using System;
using System.Collections.Generic;
using System.Threading;

namespace Worktown.Game.Diagnostics
{
    /// <summary>
    /// Spec 049: turns exceptions from any thread into crash log entries (CrashReport.Entry) and hands them to a sink. Thread-safe:
    /// CrashLog calls it from Unity's threaded log callback, the AppDomain's unhandled-exception event (any thread, also a dying one) and
    /// the unobserved-task event. The clock is injected (the tests), the build hash can arrive later (it is computed after start).
    /// 049 review: the session cap (CrashReport.WithinCap: one cap line, then only counted) and the scrub of the player's folders
    /// (<see cref="SetPrivateFolders"/>, CrashReport.Scrub) on every message and stack before it reaches the sink.
    /// </summary>
    public sealed class CrashRecorder
    {
        public const string KindUnhandled = "unhandled exception";
        public const string KindUnhandledTerminating = "unhandled exception (the runtime ends the process)";
        public const string KindLogged = "exception (logged)";
        public const string KindAssert = "assert";
        public const string KindTask = "unobserved task exception";

        private readonly ICrashSink _sink;
        private readonly Func<DateTime> _utcNow;
        private readonly int _mainThreadId;
        private readonly Dictionary<string, int> _seen = new Dictionary<string, int>();
        private readonly object _lock = new object();
        private int _written;
        private bool _capped;
        private int _afterCap;
        private string _userProfile = string.Empty;
        private string _tempPath = string.Empty;

        public CrashRecorder(ICrashSink sink, string version, string buildHash, int mainThreadId, Func<DateTime> utcNow)
        {
            _sink = sink;
            Version = version ?? string.Empty;
            BuildHash = buildHash ?? string.Empty;
            _mainThreadId = mainThreadId;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public string Version { get; }
        /// <summary>The spec 047 build hash (set once known).</summary>
        public string BuildHash { get; set; }
        /// <summary>Entries handed to the sink this session (the cap line not counted).</summary>
        public int Written => _written;
        /// <summary>The session cap was reached (CrashReport.MaxEntries or MaxKeys): the cap line was written, nothing after it.</summary>
        public bool Capped => _capped;

        /// <summary>
        /// The player's own folders (the user profile, the temp folder), written as %USERPROFILE% and %TEMP%. Set once, before the
        /// first entry (CrashLog's install); empty = no scrub.
        /// </summary>
        public void SetPrivateFolders(string userProfile, string tempPath)
        {
            lock (_lock)
            {
                _userProfile = userProfile ?? string.Empty;
                _tempPath = tempPath ?? string.Empty;
            }
        }

        /// <summary>An exception object on the calling thread (unhandled, or from a task).</summary>
        public void RecordException(string kind, object exceptionObject)
        {
            var e = exceptionObject as Exception;
            string message = e != null ? e.GetType().FullName + ": " + e.Message : exceptionObject?.ToString() ?? "(null)";
            // ToString has the inner exceptions and the whole stack of each.
            string stack = e != null ? e.ToString() : string.Empty;
            Write(kind, message, stack);
        }

        /// <summary>One of Unity's log messages (Exception and Assert only; everything else is ignored, fast).</summary>
        public void RecordLog(string condition, string stackTrace, bool isException, bool isAssert)
        {
            if (!isException && !isAssert) return;
            Write(isException ? KindLogged : KindAssert, condition, stackTrace);
        }

        /// <summary>Anything else worth an entry (a host tick stall, a session that did not end cleanly).</summary>
        public void RecordNote(string kind, string message, string details) => Write(kind, message, details);

        /// <summary>The session's last words (how many repeats were only counted).</summary>
        public void CloseSession()
        {
            string text;
            lock (_lock) text = CrashReport.SuppressedText(_seen, _afterCap);
            if (text.Length > 0) _sink?.Write(text);
        }

        private void Write(string kind, string message, string stack)
        {
            try
            {
                string profile, temp;
                bool capLine = false;
                lock (_lock)
                {
                    if (_capped)
                    {
                        _afterCap++;
                        return;
                    }
                    string key = CrashReport.Key(kind, message, stack);
                    if (!CrashReport.WithinCap(_seen, key, _written))
                    {
                        _capped = true;
                        _afterCap++;
                        capLine = true;
                    }
                    else
                    {
                        if (!CrashReport.Admit(_seen, key)) return;
                        _written++;
                    }
                    profile = _userProfile;
                    temp = _tempPath;
                }
                if (capLine)
                {
                    _sink?.Write(CrashReport.CapText);
                    return;
                }
                Thread t = Thread.CurrentThread;
                string thread = CrashReport.ThreadText(t.ManagedThreadId == _mainThreadId, t.ManagedThreadId, t.Name, t.IsThreadPoolThread,
                    t.IsBackground);
                _sink?.Write(CrashReport.Entry(_utcNow(), Version, BuildHash, kind, thread, CrashReport.Scrub(message, profile, temp),
                    CrashReport.Scrub(stack, profile, temp)));
            }
            catch (Exception)
            {
                // Never let the crash log throw (it runs inside exception handlers).
            }
        }
    }
}
