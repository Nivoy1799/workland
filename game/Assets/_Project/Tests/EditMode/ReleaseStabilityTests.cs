using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NUnit.Framework;
using Worktown.Game.Dev;
using Worktown.Game.Diagnostics;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 049: the crash and exception log (AC4: a forced exception on a background thread is written with the build hash and its
    /// stack), its repeat limit and file size, the previous-session check, the host's tick-stall rule, the new launch flags and the
    /// profiling tour's numbers.
    /// </summary>
    public class ReleaseStabilityTests
    {
        private static readonly DateTime Noon = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "worktown-049-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(_dir) && Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void ThrowDeep(string message) => throw new InvalidOperationException(message);

        [Test]
        public void AForcedException_OnABackgroundThread_IsWrittenWithTheBuildHashAndTheStack()
        {
            string path = Path.Combine(_dir, "crash.log");
            var recorder = new CrashRecorder(new CrashFileSink(path), "0.49", "0123456789abcdef", Thread.CurrentThread.ManagedThreadId, () => Noon);
            var thread = new Thread(() =>
            {
                try
                {
                    ThrowDeep("forced 049");
                }
                catch (Exception e)
                {
                    recorder.RecordException(CrashRecorder.KindUnhandled, e);
                }
            }) { Name = "fishnet-like", IsBackground = true };
            thread.Start();
            Assert.IsTrue(thread.Join(5000));

            string log = File.ReadAllText(path);
            StringAssert.StartsWith(CrashReport.EntryStart, log);
            StringAssert.Contains("time: 2026-11-11 12:00:00.000 UTC", log);
            StringAssert.Contains("build: 0.49 hash 0123456789abcdef", log);
            StringAssert.Contains("kind: " + CrashRecorder.KindUnhandled, log);
            StringAssert.Contains("thread: not main #", log);
            StringAssert.Contains("\"fishnet-like\" background", log);
            StringAssert.Contains("message: System.InvalidOperationException: forced 049", log);
            StringAssert.Contains("ThrowDeep", log, "the whole stack");
            Assert.AreEqual(1, recorder.Written);
        }

        [Test]
        public void ALoggedException_OnTheMainThread_AndNothingElse()
        {
            var sink = new ListSink();
            var recorder = new CrashRecorder(sink, "0.49", "hash", Thread.CurrentThread.ManagedThreadId, () => Noon);
            recorder.RecordLog("a normal line", "stack", false, false);
            Assert.AreEqual(0, sink.Entries.Count, "Log, Warning and Error lines are not crashes");
            recorder.RecordLog("NullReferenceException: x", "Foo.Bar () (at Foo.cs:1)", true, false);
            Assert.AreEqual(1, sink.Entries.Count);
            StringAssert.Contains("thread: main #", sink.Entries[0]);
            StringAssert.Contains("kind: " + CrashRecorder.KindLogged, sink.Entries[0]);
            StringAssert.Contains("  Foo.Bar () (at Foo.cs:1)", sink.Entries[0]);
            recorder.RecordLog("Assertion failed", "", false, true);
            StringAssert.Contains("(no stack)", sink.Entries[1]);
        }

        [Test]
        public void TheSameException_IsWrittenFiveTimes_ThenCounted()
        {
            var sink = new ListSink();
            var recorder = new CrashRecorder(sink, "v", "h", -1, () => Noon);
            for (int i = 0; i < CrashReport.MaxRepeats + 2; i++) recorder.RecordLog("same", "A.B ()\nC.D ()", true, false);
            recorder.RecordLog("other", "A.B ()", true, false);
            Assert.AreEqual(CrashReport.MaxRepeats + 1, sink.Entries.Count);
            recorder.CloseSession();
            StringAssert.Contains("2 repeats", sink.Entries[sink.Entries.Count - 1]);
            Assert.AreEqual(string.Empty, CrashReport.SuppressedText(new Dictionary<string, int> { { "k", 1 } }));
        }

        [Test]
        public void TheFile_StartsOverAboveTheLimit_KeepingTheOldOne()
        {
            string path = Path.Combine(_dir, "big.log");
            File.WriteAllText(path, new string('x', (int)CrashReport.MaxBytes));
            var sink = new CrashFileSink(path);
            sink.Write("new entry\n");
            Assert.AreEqual("new entry\n", File.ReadAllText(path));
            Assert.IsTrue(File.Exists(path + ".old"));
            Assert.IsFalse(CrashReport.NeedsRotation(CrashReport.MaxBytes - 1));
            new CrashFileSink(string.Empty).Write("nothing");
            Assert.AreEqual(0, sink.Failures);
        }

        [Test]
        public void PreviousSession_IsReportedOnlyWhenItDidNotQuitCleanly()
        {
            Assert.IsNull(CrashReport.PreviousSessionText(null, "C:/x"));
            Assert.IsNull(CrashReport.PreviousSessionText(CrashReport.MarkerClean, "C:/x"));
            string running = CrashReport.RunningMarker(Noon, "0.49");
            string text = CrashReport.PreviousSessionText(running, "C:/Temp/Crashes");
            StringAssert.Contains("2026-11-11 12:00:00 UTC, 0.49", text);
            StringAssert.Contains("C:/Temp/Crashes", text);
        }

        [Test]
        public void ThreadText_NamesTheThread()
        {
            Assert.AreEqual("main #1", CrashReport.ThreadText(true, 1, null, false, false));
            Assert.AreEqual("not main #7 \"Finalizer\" background", CrashReport.ThreadText(false, 7, "Finalizer", false, true));
            Assert.AreEqual("not main #9 pool", CrashReport.ThreadText(false, 9, "", true, true));
        }

        [Test]
        public void TickStall_NeedsTwoSecondsAndFramesDrawn()
        {
            Assert.IsFalse(TickStallRules.IsStalled(false, 10d, 1000), "no server");
            Assert.IsFalse(TickStallRules.IsStalled(true, 1.9d, 1000), "under 2 s");
            Assert.IsFalse(TickStallRules.IsStalled(true, 5d, 1), "one long frame (a load, a debugger pause)");
            Assert.IsTrue(TickStallRules.IsStalled(true, 2.1d, TickStallRules.MinFrames));
            StringAssert.Contains("HOST TICK STALL", TickStallRules.Describe(2.5d, 600, 1234, 3));
            StringAssert.Contains("2.5 s", TickStallRules.Describe(2.5d, 600, 1234, 3));
        }

        [Test]
        public void HostStalled_HasItsOwnErrorScreen()
        {
            Assert.AreEqual(9, (byte)DisconnectReason.HostStalled, "sent as a byte: never renumber");
            Assert.IsNotEmpty(ConnectionMessages.Title(DisconnectReason.HostStalled));
            StringAssert.Contains("crash log", ConnectionMessages.Text(DisconnectReason.HostStalled, string.Empty));
        }

        [Test]
        public void LaunchFlags_ForTheCrashLog_ThePerfTour_AndTheStallTest()
        {
            LaunchArgs a = LaunchArgs.Parse(new[] { "x", "-host", "-perf-tour", "p.jsonl", "-crash-log", "c.log", "-stall-test", "5" });
            Assert.IsTrue(a.IsValid, a.Error);
            Assert.AreEqual("p.jsonl", a.PerfTourPath);
            Assert.AreEqual("c.log", a.CrashLogPath);
            Assert.AreEqual(5, a.StallTestSeconds);
            Assert.IsTrue(LaunchArgs.Parse(new[] { "x", "-crash-test" }).CrashTest);
            Assert.IsTrue(LaunchArgs.Parse(new[] { "x", "-connect", "127.0.0.1", "-bot", "-crash-log", "b.log" }).IsValid);
            Assert.IsFalse(LaunchArgs.Parse(new[] { "x", "-connect", "127.0.0.1", "-perf-tour", "p" }).IsValid, "the tour is the host's");
            Assert.IsFalse(LaunchArgs.Parse(new[] { "x", "-stall-test", "5" }).IsValid, "needs a server");
            Assert.IsFalse(LaunchArgs.Parse(new[] { "x", "-host", "-stall-test", "0" }).IsValid);
            Assert.IsFalse(LaunchArgs.Parse(new[] { "x", "-host", "-crash-test" }).IsValid);
            Assert.IsFalse(LaunchArgs.Parse(new[] { "x", "-crash-log" }).IsValid);
        }

        [Test]
        public void PerfStats_SummarizeAStop_AndWriteItsLine()
        {
            var frames = new List<float>();
            var gc = new List<long>();
            for (int i = 1; i <= 100; i++)
            {
                frames.Add(i);
                gc.Add(i % 2 == 0 ? 1024 : 0);
            }
            PerfStats.Summary s = PerfStats.Summarize(frames, gc);
            Assert.AreEqual(100, s.Frames);
            Assert.AreEqual(50.5f, s.AvgMs, 1e-4f);
            Assert.AreEqual(95f, s.P95Ms);
            Assert.AreEqual(99f, s.P99Ms);
            Assert.AreEqual(100f, s.MaxMs);
            Assert.AreEqual(512d, s.GcBytesPerFrame, 1e-6);
            Assert.AreEqual(1f, frames[0], "the caller's list keeps its order");
            string line = PerfStats.Line("Fry\"er", "Fryer", 3, 16, s);
            StringAssert.StartsWith("{\"station\":\"Fry'er\",\"kind\":\"Fryer\",\"index\":3,\"players\":16,\"frames\":100,\"avg_ms\":50.5,", line);
            StringAssert.Contains("\"gc_bytes_per_frame\":512}", line);
            Assert.AreEqual(0, PerfStats.Summarize(null, null).Frames);
        }

        private sealed class ListSink : ICrashSink
        {
            public readonly List<string> Entries = new List<string>();
            public void Write(string entry) => Entries.Add(entry);
        }
    }
}
