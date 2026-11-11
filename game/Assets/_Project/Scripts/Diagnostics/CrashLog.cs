using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;
using Worktown.Game.Settings;

namespace Worktown.Game.Diagnostics
{
    /// <summary>
    /// Spec 049: the local crash and exception log, in every player build (not the Editor). Installed before the first scene loads; it
    /// records, with the build (version and the spec 047 build hash) and the thread:
    /// every exception Unity logs (from any thread, Application.logMessageReceivedThreaded), every unhandled exception on any thread
    /// (AppDomain.UnhandledException: also a FishNet or Steam background thread, the finalizer; the runtime may end the process right
    /// after, so it is written at once), unobserved task exceptions, the host's tick stalls (TickWatchdog) and, at the next start, a
    /// session that did not quit cleanly (a native crash: Unity's own crash report folder is named).
    /// The file: <c>-crash-log path</c>, else <c>crash.log</c> in the player's data folder (Application.persistentDataPath). It stays on
    /// this PC: an opt-in upload is an OPEN decision (ICrashSink is the seam).
    /// </summary>
    public static class CrashLog
    {
        private static CrashRecorder _recorder;
        private static string _markerPath;

        /// <summary>The log file of this session (empty before the install, and in the Editor).</summary>
        public static string FilePath { get; private set; } = string.Empty;

        /// <summary>The recorder of this session, or null (the Editor).</summary>
        public static CrashRecorder Recorder => _recorder;

        /// <summary>A note for the log (the host's tick stall); does nothing before the install.</summary>
        public static void Note(string kind, string message, string details) => _recorder?.RecordNote(kind, message, details);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
#if !UNITY_EDITOR
            if (_recorder != null) return;
            LaunchArgs args = LaunchArgs.Parse(Environment.GetCommandLineArgs());
            string dataDir = Application.persistentDataPath;
            FilePath = args.CrashLogPath.Length > 0 ? Path.GetFullPath(args.CrashLogPath) : Path.Combine(dataDir, CrashReport.DefaultFileName);
            var sink = new CrashFileSink(FilePath);
            _recorder = new CrashRecorder(sink, Application.version, string.Empty, Thread.CurrentThread.ManagedThreadId, () => DateTime.UtcNow);
            Application.logMessageReceivedThreaded += OnLog;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
            TaskScheduler.UnobservedTaskException += OnUnobservedTask;
            Application.quitting += OnQuitting;
            try
            {
                _recorder.BuildHash = BuildHash.Compute(Application.version, ProtocolSurface.Lines(typeof(CrashLog).Assembly));
            }
            catch (Exception e)
            {
                _recorder.BuildHash = "unknown (" + e.GetType().Name + ")";
            }
            // Only a player's own session leaves the marker: scripted runs (tours, bots, the perf check) are killed on purpose.
            if (args.CrashLogPath.Length == 0 && SettingsRules.UsesSavedSettings(args)) CheckPreviousSession(dataDir);
            Debug.Log("[Crash] log " + FilePath + " (build " + Application.version + " hash " + _recorder.BuildHash + ")");
#endif
        }

        private static void CheckPreviousSession(string dataDir)
        {
            try
            {
                _markerPath = Path.Combine(dataDir, "session.marker");
                string before = File.Exists(_markerPath) ? File.ReadAllText(_markerPath) : null;
                string unityCrashes = Path.Combine(Path.GetTempPath(), Application.companyName, Application.productName, "Crashes");
                string line = CrashReport.PreviousSessionText(before, unityCrashes);
                if (line != null) _recorder.RecordNote("previous session", line, string.Empty);
                File.WriteAllText(_markerPath, CrashReport.RunningMarker(DateTime.UtcNow, Application.version));
            }
            catch (Exception)
            {
                _markerPath = null; // a read-only data folder: no marker, the log still works
            }
        }

        private static void OnLog(string condition, string stackTrace, LogType type) =>
            _recorder?.RecordLog(condition, stackTrace, type == LogType.Exception, type == LogType.Assert);

        private static void OnUnhandled(object sender, UnhandledExceptionEventArgs e) =>
            _recorder?.RecordException(e.IsTerminating ? CrashRecorder.KindUnhandledTerminating : CrashRecorder.KindUnhandled, e.ExceptionObject);

        private static void OnUnobservedTask(object sender, UnobservedTaskExceptionEventArgs e) =>
            _recorder?.RecordException(CrashRecorder.KindTask, e.Exception);

        private static void OnQuitting()
        {
            _recorder?.CloseSession();
            try
            {
                if (_markerPath != null) File.WriteAllText(_markerPath, CrashReport.MarkerClean);
            }
            catch (Exception)
            {
                // nothing to do: the next start reports this session as not clean
            }
        }
    }
}
