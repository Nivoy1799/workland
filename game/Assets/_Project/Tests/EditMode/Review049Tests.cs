using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.InputSystem;
using Worktown.Game.Diagnostics;
using Worktown.Game.Net.Rules;
using Worktown.Game.Settings;
using Worktown.Game.UI;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 049 review fixes: the stall close survives a throwing stop (GuardedCall), a dedicated server quits after a stall, the crash
    /// log's session cap and its scrub of the player's folders, spec 049's runs count as scripted, the friends hint names the bound
    /// Players key, and an unknown disconnect reason still has words.
    /// </summary>
    public class Review049Tests
    {
        private static readonly DateTime Noon = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

        // ---- the stall close ----

        [Test]
        public void GuardedCall_AThrowingStop_DoesNotEndTheSteps()
        {
            var done = new List<string>();
            var warnings = new List<string>();
            bool client = GuardedCall.Run(() => throw new NullReferenceException("broken FishNet"), "stopping the client", warnings.Add);
            bool server = GuardedCall.Run(() => done.Add("server"), "stopping the server", warnings.Add);
            done.Add("fail screen and pump"); // what CloseAfterStallSoon does after CloseSoon

            Assert.IsFalse(client);
            Assert.IsTrue(server);
            CollectionAssert.AreEqual(new[] { "server", "fail screen and pump" }, done);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains("stopping the client failed: NullReferenceException: broken FishNet", warnings[0]);
        }

        [Test]
        public void GuardedCall_AThrowingWarning_OrNoStep_IsHarmless()
        {
            Assert.IsFalse(GuardedCall.Run(() => throw new InvalidOperationException(), "x", _ => throw new Exception("warn broke")));
            Assert.IsFalse(GuardedCall.Run(() => throw new InvalidOperationException(), null, null));
            Assert.IsTrue(GuardedCall.Run(null, "nothing", null));
        }

        [Test]
        public void ADedicatedServer_QuitsAfterAStall_AHostAndTheStallTestDoNot()
        {
            Assert.IsTrue(TickStallRules.QuitsAfterStall(true, false));
            Assert.IsFalse(TickStallRules.QuitsAfterStall(false, false), "a host keeps its error screen");
            Assert.IsFalse(TickStallRules.QuitsAfterStall(true, true), "StallTestRunner owns the exit code");
            Assert.AreNotEqual(0, TickStallRules.DedicatedExitCode);
        }

        // ---- the crash log cap ----

        [Test]
        public void WithinCap_StopsAtTheEntryCap_AndAtNewKeysPastTheKeyCap()
        {
            var seen = new Dictionary<string, int>();
            for (int i = 0; i < CrashReport.MaxKeys; i++) seen["k" + i] = 1;
            Assert.IsFalse(CrashReport.WithinCap(seen, "new", CrashReport.MaxKeys), "a new key past the key cap");
            Assert.IsTrue(CrashReport.WithinCap(seen, "k0", CrashReport.MaxKeys), "a known key still counts");
            Assert.IsFalse(CrashReport.WithinCap(seen, "k0", CrashReport.MaxEntries));
            Assert.IsTrue(CrashReport.WithinCap(null, "x", 0));
            Assert.IsFalse(CrashReport.WithinCap(null, "x", CrashReport.MaxEntries));
            Assert.AreEqual(CrashReport.MaxKeys, seen.Count, "asking changes nothing");
        }

        [Test]
        public void ManyDifferentExceptions_StopAtTheKeyCap_WithOneFinalLine()
        {
            var sink = new ListSink();
            var recorder = new CrashRecorder(sink, "v", "h", -1, () => Noon);
            for (int i = 0; i < CrashReport.MaxKeys + 44; i++) recorder.RecordLog("error " + i, "A.B ()", true, false);

            Assert.AreEqual(CrashReport.MaxKeys + 1, sink.Entries.Count, "256 entries and the cap line");
            Assert.AreEqual(CrashReport.MaxKeys, recorder.Written);
            Assert.IsTrue(recorder.Capped);
            Assert.AreEqual(CrashReport.CapText, sink.Entries[CrashReport.MaxKeys]);
            StringAssert.Contains("further entries suppressed", CrashReport.CapText);

            recorder.RecordLog("error 0", "A.B ()", true, false); // even a known one: nothing after the cap line
            Assert.AreEqual(CrashReport.MaxKeys + 1, sink.Entries.Count);
            recorder.CloseSession();
            StringAssert.Contains("45 entries came after the cap", sink.Entries[sink.Entries.Count - 1]);
        }

        [Test]
        public void ManyEntries_StopAtTheEntryCap_WithOneFinalLine()
        {
            var sink = new ListSink();
            var recorder = new CrashRecorder(sink, "v", "h", -1, () => Noon);
            const int keys = CrashReport.MaxEntries / (CrashReport.MaxRepeats - 1); // 250 keys, 4 times each: 1000 entries
            for (int r = 0; r < CrashReport.MaxRepeats - 1; r++)
                for (int i = 0; i < keys; i++) recorder.RecordLog("error " + i, "A.B ()", true, false);
            Assert.AreEqual(CrashReport.MaxEntries, recorder.Written);
            Assert.IsFalse(recorder.Capped);

            recorder.RecordLog("error 0", "A.B ()", true, false); // its fifth repeat would be written: the cap comes first
            recorder.RecordLog("error 1", "A.B ()", true, false);
            Assert.IsTrue(recorder.Capped);
            Assert.AreEqual(CrashReport.MaxEntries + 1, sink.Entries.Count);
            Assert.AreEqual(CrashReport.CapText, sink.Entries[CrashReport.MaxEntries]);
            Assert.AreEqual(string.Empty, CrashReport.SuppressedText(null, 0));
        }

        // ---- the scrub ----

        [Test]
        public void Scrub_ReplacesTheProfileAndTemp_InEitherSlashAndCase()
        {
            const string profile = @"C:\Users\jo";
            const string temp = @"C:\Users\jo\AppData\Local\Temp\";
            Assert.AreEqual(@"at %USERPROFILE%\git\Foo.cs:12", CrashReport.Scrub(@"at C:\Users\jo\git\Foo.cs:12", profile, temp));
            Assert.AreEqual("%USERPROFILE%/AppData/LocalLow/Co/Worktown/crash.log",
                CrashReport.Scrub("C:/Users/JO/AppData/LocalLow/Co/Worktown/crash.log", profile, temp), "Unity's forward slashes");
            Assert.AreEqual(@"in %TEMP%\Co\Worktown\Crashes.", CrashReport.Scrub(@"in C:\Users\jo\AppData\Local\Temp\Co\Worktown\Crashes.", profile, temp));
            Assert.AreEqual(@"C:\Users\joan\x and %USERPROFILE%", CrashReport.Scrub(@"C:\Users\joan\x and C:\Users\jo", profile, temp),
                "only a whole folder name");
            Assert.AreEqual("no paths", CrashReport.Scrub("no paths", null, ""));
            Assert.AreEqual(@"C:\x", CrashReport.Scrub(@"C:\x", @"C:\", null), "never a drive root");
            Assert.AreEqual(string.Empty, CrashReport.Scrub(null, profile, temp));
        }

        [Test]
        public void TheRecorder_ScrubsMessagesStacksAndThePreviousSessionNote()
        {
            var sink = new ListSink();
            var recorder = new CrashRecorder(sink, "v", "h", -1, () => Noon);
            recorder.SetPrivateFolders(@"C:\Users\jo", @"C:\Users\jo\AppData\Local\Temp");
            recorder.RecordLog(@"IOException: C:\Users\jo\save.json", @"Foo.Load () (at C:\Users\jo\git\Foo.cs:3)", true, false);
            recorder.RecordNote("previous session",
                CrashReport.PreviousSessionText(CrashReport.RunningMarker(Noon, "v"), @"C:\Users\jo\AppData\Local\Temp\Co\Worktown\Crashes"), "");

            Assert.AreEqual(2, sink.Entries.Count);
            foreach (string entry in sink.Entries) StringAssert.DoesNotContain(@"Users\jo", entry);
            StringAssert.Contains(@"message: IOException: %USERPROFILE%\save.json", sink.Entries[0]);
            StringAssert.Contains(@"(at %USERPROFILE%\git\Foo.cs:3)", sink.Entries[0]);
            StringAssert.Contains(@"%TEMP%\Co\Worktown\Crashes", sink.Entries[1]);
        }

        // ---- scripted runs, the friends hint, unknown reasons ----

        [Test]
        public void ScriptedRuns_IncludeTheStallTest_TheCrashTest_AndThePerfTour()
        {
            LaunchArgs stall = LaunchArgs.Parse(new[] { "Worktown.exe", "-host", "-stall-test", "5" });
            LaunchArgs crash = LaunchArgs.Parse(new[] { "Worktown.exe", "-crash-test" });
            LaunchArgs tour = LaunchArgs.Parse(new[] { "Worktown.exe", "-host", "-perf-tour", "p.jsonl" });
            foreach (LaunchArgs a in new[] { stall, crash, tour }) Assert.IsTrue(a.IsValid, a.Error);
            Assert.IsFalse(SettingsRules.UsesSavedSettings(stall));
            Assert.IsFalse(SettingsRules.UsesSavedSettings(crash));
            Assert.IsFalse(SettingsRules.UsesSavedSettings(tour));
            Assert.IsTrue(SettingsRules.UsesSavedSettings(LaunchArgs.Parse(new[] { "Worktown.exe", "-server" })));
        }

        [Test]
        public void TheFriendsHint_NamesTheBoundPlayersKey()
        {
            StringAssert.Contains("(Tab in their shop)", MenuRules.NoFriendsHosting(ControlBindings.Defaults()));
            StringAssert.Contains("(Tab in their shop)", MenuRules.NoFriendsHosting(null));
            ControlBindings b = ControlBindings.Defaults();
            Assert.AreEqual(RebindOutcome.Bound, b.Rebind(ControlAction.Players, Key.L).Outcome);
            string text = MenuRules.NoFriendsHosting(b);
            StringAssert.Contains("(L in their shop)", text);
            StringAssert.DoesNotContain("Tab", text);
        }

        [Test]
        public void AnUnknownReason_StillHasWords()
        {
            var future = (DisconnectReason)42;
            Assert.AreEqual("Disconnected (reason 42)", ConnectionMessages.Title(future));
            Assert.AreEqual(ConnectionMessages.UnknownText, ConnectionMessages.Text(future, "detail"));
            Assert.IsNotEmpty(ConnectionMessages.UnknownText);
            Assert.AreEqual(string.Empty, ConnectionMessages.Title(DisconnectReason.None));
            Assert.AreEqual(string.Empty, ConnectionMessages.Text(DisconnectReason.None, string.Empty));
            Assert.AreEqual(string.Empty, ConnectionMessages.Text(DisconnectReason.Left, string.Empty));
            Assert.AreEqual("The shop stopped", ConnectionMessages.Title(DisconnectReason.HostStalled), "known reasons keep their words");
        }

        private sealed class ListSink : ICrashSink
        {
            public readonly List<string> Entries = new List<string>();
            public void Write(string entry) => Entries.Add(entry);
        }
    }
}
