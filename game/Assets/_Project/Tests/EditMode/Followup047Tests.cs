using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Net.Rules;
using Worktown.Game.Player;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Follow-ups of spec 047 (2026-11-11): the "Leave your shop?" question, the held quit while hosting, the session ban list and its
    /// new reason, the Steam friends-list line (steam_display and its localization file), the labels' settle scans.
    /// </summary>
    public class Followup047Tests
    {
        // ---- 1. Leave your shop? ----

        [Test]
        public void AJoinWhileHosting_AsksFirst_AGuestLeaves_ADedicatedServerIgnores()
        {
            Assert.AreEqual(JoinChoice.JoinNow, LeaveShopRules.Decide(false, false), "not in a session");
            Assert.AreEqual(JoinChoice.LeaveThenJoin, LeaveShopRules.Decide(false, true), "a guest (or a rejoin attempt)");
            Assert.AreEqual(JoinChoice.AskFirst, LeaveShopRules.Decide(true, true), "the host with its own player");
            Assert.AreEqual(JoinChoice.Ignore, LeaveShopRules.Decide(true, false), "a dedicated server");
        }

        [Test]
        public void TheQuestion_StandsOnlyWhileHosting()
        {
            Assert.IsTrue(LeaveShopRules.StillAsking(true, true, true));
            Assert.IsFalse(LeaveShopRules.StillAsking(false, true, true), "no question");
            Assert.IsFalse(LeaveShopRules.StillAsking(true, false, false), "the shop closed (or stalled) before the answer");
            Assert.IsFalse(LeaveShopRules.StillAsking(true, true, false), "only the server is left");
        }

        [Test]
        public void TheQuestion_HasItsWords()
        {
            Assert.AreEqual("Leave your shop?", LeaveShopRules.Question);
            Assert.AreEqual("Your guests will be disconnected.", LeaveShopRules.Warning);
            Assert.AreEqual("Leave", LeaveShopRules.LeaveLabel);
            Assert.AreEqual("Stay", LeaveShopRules.StayLabel);
        }

        // ---- 2. Quitting while hosting ----

        [Test]
        public void QuittingWhileHosting_WithGuests_IsHeldForTheNotice()
        {
            Assert.IsTrue(HostQuitRules.HoldsQuit(true, 1, false, false, false), "a host with a guest");
            Assert.IsTrue(HostQuitRules.HoldsQuit(true, 15, false, false, false));
            Assert.IsFalse(HostQuitRules.HoldsQuit(true, 0, false, false, false), "nobody to tell");
            Assert.IsFalse(HostQuitRules.HoldsQuit(false, 3, false, false, false), "not the server (a guest quits at once)");
            Assert.IsFalse(HostQuitRules.HoldsQuit(true, 3, true, false, false), "the held quit itself goes through");
            Assert.IsFalse(HostQuitRules.HoldsQuit(true, 3, false, true, false), "never in the Editor (leaving Play mode)");
            Assert.IsFalse(HostQuitRules.HoldsQuit(true, 3, false, false, true), "never in a scripted run (its exit code)");
        }

        [Test]
        public void TheHeldQuit_GoesThrough_WhenClosed_OrAtTheLimit()
        {
            Assert.IsFalse(HostQuitRules.QuitNow(true, true, 0.5f), "the notice is still going out");
            Assert.IsFalse(HostQuitRules.QuitNow(false, true, 0.1f), "the server has not stopped yet");
            Assert.IsTrue(HostQuitRules.QuitNow(false, false, 1.0f), "closed: quit");
            Assert.IsTrue(HostQuitRules.QuitNow(true, true, HostQuitRules.LimitSeconds), "a broken close never keeps the game open");
            Assert.LessOrEqual(HostQuitRules.NoticeSeconds, HostQuitRules.LimitSeconds);
            Assert.LessOrEqual(HostQuitRules.LimitSeconds, 2f, "about a second, not more than two");
        }

        [Test]
        public void ScriptedRuns_AreKnown_FromTheCommandLine()
        {
            Assert.IsFalse(LaunchArgs.Parse(new[] { "x" }).IsScripted, "a player at the title screen");
            Assert.IsFalse(LaunchArgs.Parse(new[] { "x", "-host" }).IsScripted, "a player hosting");
            Assert.IsFalse(LaunchArgs.Parse(new[] { "x", "-connect", "127.0.0.1" }).IsScripted);
            Assert.IsTrue(LaunchArgs.Parse(new[] { "x", "-tour", "menu" }).IsScripted);
            Assert.IsTrue(LaunchArgs.Parse(new[] { "x", "-host", "-observe", "menu" }).IsScripted);
            Assert.IsTrue(LaunchArgs.Parse(new[] { "x", "-connect", "127.0.0.1", "-bot" }).IsScripted);
            Assert.IsTrue(LaunchArgs.Parse(new[] { "x", "-host", "-metrics", "m.jsonl" }).IsScripted);
            Assert.IsTrue(LaunchArgs.Parse(new[] { "x", "-host", "-stall-test", "5" }).IsScripted);
            Assert.IsTrue(LaunchArgs.Parse(new[] { "x", "-host", "-perf-tour", "p.jsonl" }).IsScripted);
            Assert.IsTrue(LaunchArgs.Parse(new[] { "x", "-crash-test" }).IsScripted);
        }

        // ---- 3. The session ban list ----

        [Test]
        public void TheBanKey_IsTheSteamId_OverSteam_ElseTheAddress()
        {
            Assert.AreEqual("steam:76561197960287930", SessionBans.KeyOf("76561197960287930"), "FishySteamworks' address");
            Assert.AreEqual("address:127.0.0.1", SessionBans.KeyOf("127.0.0.1"), "Tugboat's address (no port)");
            Assert.AreEqual("address:10.0.0.2", SessionBans.KeyOf(" ::FFFF:10.0.0.2 "), "an IPv4-mapped IPv6 address");
            Assert.AreEqual("address:fe80::1", SessionBans.KeyOf("FE80::1"));
            Assert.AreEqual(string.Empty, SessionBans.KeyOf(string.Empty), "unknown: nothing to ban by");
            Assert.AreEqual(string.Empty, SessionBans.KeyOf(null));
            Assert.AreEqual(string.Empty, SessionBans.KeyOf("  "));
            Assert.AreEqual("address:0", SessionBans.KeyOf("0"), "0 is no SteamID64");
        }

        [Test]
        public void AKickedPlayer_StaysOut_UntilTheShopCloses()
        {
            var bans = new SessionBans();
            string guest = SessionBans.KeyOf("76561197960287930");
            Assert.IsFalse(bans.IsBanned(guest, false));
            Assert.IsTrue(bans.Ban(guest, false));
            Assert.IsTrue(bans.IsBanned(guest, false), "the rejoin is refused");
            Assert.IsTrue(bans.Ban(guest, false), "a second kick keeps the ban");
            Assert.AreEqual(1, bans.Count);
            Assert.IsFalse(bans.IsBanned(SessionBans.KeyOf("76561197960287931"), false), "another player");
            Assert.IsFalse(bans.IsBanned(SessionBans.KeyOf("127.0.0.1"), false), "an address is not a Steam id");
            bans.Clear();
            Assert.IsFalse(bans.IsBanned(guest, false), "the shop closed: a new shop admits everyone");
            Assert.AreEqual(0, bans.Count);
        }

        [Test]
        public void TheHost_IsNeverBanned_NorAnUnknownAddress()
        {
            var bans = new SessionBans();
            string loopback = SessionBans.KeyOf("127.0.0.1");
            Assert.IsFalse(bans.Ban(loopback, true), "the host's own player");
            Assert.IsFalse(bans.IsBanned(loopback, false));
            Assert.IsTrue(bans.Ban(loopback, false), "a guest on the host's PC (a test window)");
            Assert.IsFalse(bans.IsBanned(loopback, true), "the host's own player is still never refused");
            Assert.IsFalse(bans.Ban(string.Empty, false), "no address");
            Assert.IsFalse(bans.IsBanned(string.Empty, false));
            Assert.AreEqual(1, bans.Count);
        }

        [Test]
        public void ABannedRejoin_IsRefused_First_AsKickedBefore()
        {
            Assert.AreEqual(DisconnectReason.KickedBefore, JoinRules.Decide("abc", "abc", 1, 16, true));
            Assert.AreEqual(DisconnectReason.KickedBefore, JoinRules.Decide("abd", "abc", 16, 16, true), "before the version and the places");
            Assert.AreEqual(DisconnectReason.None, JoinRules.Decide("abc", "abc", 1, 16, false));
            Assert.AreEqual(DisconnectReason.VersionMismatch, JoinRules.Decide("abd", "abc", 1, 16, false));
            Assert.AreEqual(DisconnectReason.ServerFull, JoinRules.Decide("abc", "abc", 16, 16, false));
        }

        [Test]
        public void EveryReason_KeepsItsByte()
        {
            // Sent as a byte in ConnectionNotice.Reason: never renumber. The new reason is 10.
            Assert.AreEqual(0, (byte)DisconnectReason.None);
            Assert.AreEqual(1, (byte)DisconnectReason.Left);
            Assert.AreEqual(2, (byte)DisconnectReason.VersionMismatch);
            Assert.AreEqual(3, (byte)DisconnectReason.ServerFull);
            Assert.AreEqual(4, (byte)DisconnectReason.Kicked);
            Assert.AreEqual(5, (byte)DisconnectReason.NoAnswer);
            Assert.AreEqual(6, (byte)DisconnectReason.ConnectionLost);
            Assert.AreEqual(7, (byte)DisconnectReason.Unreachable);
            Assert.AreEqual(8, (byte)DisconnectReason.HostClosed);
            Assert.AreEqual(9, (byte)DisconnectReason.HostStalled);
            Assert.AreEqual(10, (byte)DisconnectReason.KickedBefore);
            Assert.AreEqual(11, System.Enum.GetValues(typeof(DisconnectReason)).Length, "a new reason gets its own pinned line here");
        }

        [Test]
        public void TheRefusedRejoin_SaysKickedByTheHost_AndOffersNoRetry()
        {
            Assert.AreEqual("Kicked by the host", ConnectionMessages.Title(DisconnectReason.KickedBefore));
            StringAssert.Contains("cannot rejoin", ConnectionMessages.Text(DisconnectReason.KickedBefore, string.Empty));
            StringAssert.Contains("new shop", ConnectionMessages.Text(DisconnectReason.KickedBefore, string.Empty));
            Assert.AreEqual("Disconnected (reason 10)", ConnectionMessages.Unknown(DisconnectReason.KickedBefore),
                "an older build shows the byte");
            Assert.IsFalse(ConnectionMessages.CanRetry(DisconnectReason.KickedBefore, true));
            Assert.IsFalse(ConnectionMessages.CanRetry(DisconnectReason.Kicked, true));
            Assert.IsFalse(ConnectionMessages.CanRetry(DisconnectReason.VersionMismatch, true));
            Assert.IsFalse(ConnectionMessages.CanRetry(DisconnectReason.HostClosed, true));
            Assert.IsTrue(ConnectionMessages.CanRetry(DisconnectReason.ConnectionLost, true));
            Assert.IsTrue(ConnectionMessages.CanRetry(DisconnectReason.Unreachable, true));
            Assert.IsTrue(ConnectionMessages.CanRetry(DisconnectReason.ServerFull, true));
            Assert.IsTrue(ConnectionMessages.CanRetry(DisconnectReason.HostStalled, true));
            Assert.IsFalse(ConnectionMessages.CanRetry(DisconnectReason.ConnectionLost, false), "nothing to retry");
        }

        [Test]
        public void TheRefusedRejoin_EndsOnTheErrorScreen_WithoutRejoining()
        {
            var flow = new JoinFlow();
            flow.BeginJoin(JoinTarget.ByAddress("127.0.0.1", 7770));
            flow.TransportStarted();
            flow.Notice(DisconnectReason.KickedBefore, string.Empty);
            flow.TransportStopped(10d);
            Assert.AreEqual(JoinState.Message, flow.State);
            Assert.AreEqual(DisconnectReason.KickedBefore, flow.Reason);
            Assert.IsFalse(flow.Tick(12d), "no rejoin attempt after a notice");
            Assert.AreEqual(JoinState.Message, flow.State);
        }

        // ---- 4. Steam's friends-list line ----

        [Test]
        public void TheFriendsListLine_UsesTheTokens()
        {
            Assert.AreEqual("steam_display", RichPresenceRules.DisplayKey);
            Assert.AreEqual("#Status_Hosting", RichPresenceRules.DisplayToken(true));
            Assert.AreEqual("#Status_Playing", RichPresenceRules.DisplayToken(false));
            Assert.AreEqual("players", RichPresenceRules.PlayersKey);
            Assert.AreEqual("3", RichPresenceRules.PlayersValue(3));
            Assert.AreEqual("0", RichPresenceRules.PlayersValue(-1));
        }

        [Test]
        public void TheLocalizationFile_HasEveryToken_InSteamsFormat()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", RichPresenceRules.LocalizationFile));
            Assert.IsTrue(File.Exists(path), path);
            string text = File.ReadAllText(path);
            Assert.IsFalse(text.Contains("\r"), "LF line endings");
            string[] lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
            Assert.AreEqual("\"lang\"", lines[0]);
            Assert.AreEqual("\"english\"", lines[2]);
            Assert.AreEqual("\"tokens\"", lines[4]);
            Assert.AreEqual(text.Count(c => c == '{'), text.Count(c => c == '}'), "balanced braces");
            Assert.Contains("\"" + RichPresenceRules.HostingToken + "\"\t\"Running a shop (%" + RichPresenceRules.PlayersKey + "%/16)\"",
                lines);
            Assert.IsTrue(lines.Any(l => l.StartsWith("\"" + RichPresenceRules.PlayingToken + "\"\t\"", System.StringComparison.Ordinal)));
            Assert.AreEqual(16, JoinRules.MaxPlayers, "the line names 16 places (D36)");
        }

        // ---- 5. The labels at the start of a session ----

        [Test]
        public void TheLabels_ScanEveryFrame_WhileTheSessionSettles()
        {
            Assert.AreEqual(0f, WorldLabelFade.ScanDelay(0f), "on the own player's spawn");
            Assert.AreEqual(0f, WorldLabelFade.ScanDelay(WorldLabelFade.SettleSeconds - 0.01f));
            Assert.AreEqual(0.5f, WorldLabelFade.ScanDelay(WorldLabelFade.SettleSeconds), 1e-6f, "then every 0.5 s as before");
            Assert.AreEqual(0.5f, WorldLabelFade.ScanDelay(600f), 1e-6f);
        }
    }
}
