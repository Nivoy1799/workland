using NUnit.Framework;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 047 AC2: the connection as the player sees it (Rules.JoinFlow): loading, errors, kick, the 30 s rejoin.</summary>
    public class JoinFlowTests
    {
        private static JoinFlow Joined(double now = 0d)
        {
            var flow = new JoinFlow();
            flow.BeginJoin(JoinTarget.ByAddress("10.0.0.2", 7770));
            flow.TransportStarted();
            flow.Authenticated();
            flow.PlayerSpawned();
            Assert.AreEqual(JoinState.InGame, flow.State);
            return flow;
        }

        [Test]
        public void ANewFlow_ShowsTheTitleScreen()
        {
            var flow = new JoinFlow();
            Assert.AreEqual(JoinState.Menu, flow.State);
            Assert.IsTrue(flow.ShowsMenu);
            Assert.IsFalse(flow.Tick(100d));
        }

        [Test]
        public void Joining_WalksTheLoadingStages_WithARisingBar()
        {
            var flow = new JoinFlow();
            flow.BeginJoin(JoinTarget.ByAddress("10.0.0.2", 7770));
            Assert.AreEqual(JoinState.Loading, flow.State);
            Assert.AreEqual(JoinStage.Connecting, flow.Stage);
            float a = flow.Progress;
            flow.TransportStarted();
            Assert.AreEqual(JoinStage.CheckingVersion, flow.Stage);
            float b = flow.Progress;
            flow.Authenticated();
            Assert.AreEqual(JoinStage.LoadingShop, flow.Stage);
            float c = flow.Progress;
            flow.PlayerSpawned();
            Assert.AreEqual(JoinState.InGame, flow.State);
            Assert.IsFalse(flow.ShowsMenu);
            Assert.Less(a, b);
            Assert.Less(b, c);
            Assert.Less(c, flow.Progress);
            Assert.AreEqual(1f, flow.Progress);
        }

        [Test]
        public void Hosting_GoesThroughLoading_IntoTheShop()
        {
            var flow = new JoinFlow();
            flow.BeginHost();
            Assert.AreEqual(JoinTargetKind.Host, flow.Target.Kind);
            Assert.AreEqual(JoinState.Loading, flow.State);
            flow.TransportStarted();
            flow.Authenticated();
            flow.PlayerSpawned();
            Assert.AreEqual(JoinState.InGame, flow.State);
        }

        [Test]
        public void NobodyAnswering_ShowsCouldNotConnect_WithTheAddress()
        {
            var flow = new JoinFlow();
            flow.BeginJoin(JoinTarget.ByAddress("10.0.0.2", 7771));
            flow.TransportStopped(5d);
            Assert.AreEqual(JoinState.Message, flow.State);
            Assert.AreEqual(DisconnectReason.Unreachable, flow.Reason);
            Assert.AreEqual("10.0.0.2:7771", flow.Detail);
        }

        [TestCase(DisconnectReason.VersionMismatch)]
        [TestCase(DisconnectReason.ServerFull)]
        [TestCase(DisconnectReason.NoAnswer)]
        public void ARefusal_ShowsItsReason_AndNeverRejoins(DisconnectReason reason)
        {
            var flow = new JoinFlow();
            flow.BeginJoin(JoinTarget.ByAddress("10.0.0.2", 7770));
            flow.TransportStarted();
            flow.Notice(reason, "detail");
            flow.TransportStopped(1d);
            Assert.AreEqual(JoinState.Message, flow.State);
            Assert.AreEqual(reason, flow.Reason);
            Assert.AreEqual("detail", flow.Detail);
            Assert.IsFalse(flow.Tick(2d));
            Assert.AreEqual(JoinState.Message, flow.State);
        }

        [Test]
        public void AKick_ShowsRemoved_AndNeverRejoins()
        {
            JoinFlow flow = Joined();
            flow.Notice(DisconnectReason.Kicked, string.Empty);
            Assert.AreEqual(JoinState.InGame, flow.State, "the notice alone changes nothing");
            flow.TransportStopped(10d);
            Assert.AreEqual(JoinState.Message, flow.State);
            Assert.AreEqual(DisconnectReason.Kicked, flow.Reason);
            for (double t = 10d; t < 50d; t += 0.5d) Assert.IsFalse(flow.Tick(t));
        }

        [Test]
        public void TheHostClosingTheShop_ShowsItClosed_AndNeverRejoins()
        {
            JoinFlow flow = Joined();
            flow.Notice(DisconnectReason.HostClosed, string.Empty);
            flow.TransportStopped(10d);
            Assert.AreEqual(DisconnectReason.HostClosed, flow.Reason);
            Assert.IsFalse(flow.Tick(12d));
        }

        [Test]
        public void ADrop_InTheShop_Rejoins_AndGetsBackIn()
        {
            JoinFlow flow = Joined();
            flow.TransportStopped(100d);
            Assert.AreEqual(JoinState.Rejoining, flow.State);
            Assert.IsTrue(flow.ShowsMenu);
            Assert.AreEqual(JoinFlow.RejoinWindowSeconds, flow.RejoinSecondsLeft(100d), 1e-4f);
            Assert.IsFalse(flow.Tick(100.5d), "the first attempt comes after a second");
            Assert.IsTrue(flow.Tick(101d), "attempt 1");
            Assert.IsTrue(flow.Attempting);
            Assert.IsFalse(flow.Tick(101.5d), "one attempt at a time");
            flow.TransportStopped(106d); // attempt 1 failed
            Assert.AreEqual(JoinState.Rejoining, flow.State);
            Assert.IsFalse(flow.Tick(108d));
            Assert.IsTrue(flow.Tick(109d), "attempt 2, 3 s after the last failed");
            flow.TransportStarted();
            flow.Authenticated();
            Assert.AreEqual(JoinState.Rejoining, flow.State, "still on the rejoin screen until the player spawns");
            flow.PlayerSpawned();
            Assert.AreEqual(JoinState.InGame, flow.State);
            Assert.AreEqual(DisconnectReason.None, flow.Reason);
        }

        [Test]
        public void TheRejoin_GivesUpAfterThirtySeconds_WithConnectionLost()
        {
            JoinFlow flow = Joined();
            double drop = 100d, t = drop;
            int attempts = 0;
            while (flow.State == JoinState.Rejoining || t == drop)
            {
                if (t == drop) flow.TransportStopped(drop);
                if (flow.Tick(t))
                {
                    attempts++;
                    flow.TransportStopped(t + 0.5d); // every attempt fails at once
                }
                t += 0.25d;
                Assert.Less(t, drop + 60d, "gave up in time");
            }
            Assert.AreEqual(JoinState.Message, flow.State);
            Assert.AreEqual(DisconnectReason.ConnectionLost, flow.Reason);
            Assert.GreaterOrEqual(t - drop, JoinFlow.RejoinWindowSeconds);
            Assert.Less(t - drop, JoinFlow.RejoinWindowSeconds + 1d);
            Assert.GreaterOrEqual(attempts, 8, "about one attempt every 3.5 s");
        }

        [Test]
        public void AHangingAttempt_IsGivenUp_AfterTheWindowAndItsGrace()
        {
            JoinFlow flow = Joined();
            flow.TransportStopped(0d);
            Assert.IsTrue(flow.Tick(1d));
            Assert.IsFalse(flow.Tick(JoinFlow.RejoinWindowSeconds + 1d));
            Assert.AreEqual(JoinState.Rejoining, flow.State, "the attempt may still get in");
            flow.Tick(JoinFlow.RejoinWindowSeconds + JoinFlow.AttemptGraceSeconds);
            Assert.AreEqual(JoinState.Message, flow.State);
            Assert.AreEqual(DisconnectReason.ConnectionLost, flow.Reason);
        }

        [Test]
        public void TheHostsOwnClientDropping_ShowsConnectionLost_WithoutARejoin()
        {
            var flow = new JoinFlow();
            flow.BeginHost();
            flow.PlayerSpawned();
            flow.TransportStopped(5d);
            Assert.AreEqual(JoinState.Message, flow.State);
            Assert.AreEqual(DisconnectReason.ConnectionLost, flow.Reason);
        }

        [Test]
        public void Leaving_GoesBackToTheMenu_WhenTheConnectionStops()
        {
            JoinFlow flow = Joined();
            flow.Leave(anythingRunning: true);
            Assert.AreEqual(JoinState.InGame, flow.State, "until it stopped");
            flow.TransportStopped(3d);
            Assert.AreEqual(JoinState.Menu, flow.State);
            Assert.IsFalse(flow.Tick(10d), "no rejoin after leaving");
        }

        [Test]
        public void CancellingARejoin_BetweenAttempts_GoesBackAtOnce()
        {
            JoinFlow flow = Joined();
            flow.TransportStopped(0d);
            flow.Leave(anythingRunning: false);
            Assert.AreEqual(JoinState.Menu, flow.State);
            Assert.IsFalse(flow.Tick(5d));
        }

        [Test]
        public void Dismiss_AndFail()
        {
            var flow = new JoinFlow();
            flow.BeginJoin(JoinTarget.BySteam("76561197960287930"));
            flow.Fail(DisconnectReason.Unreachable, "Steam is not running");
            Assert.AreEqual(JoinState.Message, flow.State);
            Assert.AreEqual("Steam is not running", flow.Detail);
            flow.Dismiss();
            Assert.AreEqual(JoinState.Menu, flow.State);
            Assert.AreEqual(DisconnectReason.None, flow.Reason);
        }

        [Test]
        public void ANewJoin_ForgetsAnOldNotice()
        {
            var flow = new JoinFlow();
            flow.BeginJoin(JoinTarget.ByAddress("a", 1));
            flow.Notice(DisconnectReason.ServerFull, "16");
            flow.BeginJoin(JoinTarget.ByAddress("a", 1));
            flow.TransportStarted();
            flow.Authenticated();
            flow.PlayerSpawned();
            flow.TransportStopped(1d);
            Assert.AreEqual(JoinState.Rejoining, flow.State, "a plain drop, not the old refusal");
        }
    }
}
