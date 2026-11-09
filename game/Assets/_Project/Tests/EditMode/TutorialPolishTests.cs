using NUnit.Framework;
using Worktown.Game.Cooking;
using Worktown.Game.Net.Rules;
using Worktown.Game.Npc;
using Worktown.Game.Tutorial;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 038b: scripted runs never store the tutorial's done flag; the host's calm-guests flag follows the local runner when the
    /// server starts (a tutorial finished as a remote client, a replay before hosting); step 4's text while the knife is held.
    /// </summary>
    public class TutorialPolishTests
    {
        private static readonly string[] NoArgs = new string[0];

        [Test]
        public void AScriptedRun_NeverStoresTheDoneFlag()
        {
            Assert.IsTrue(TutorialRules.SavesDone(NoArgs), "a player's own game remembers it");
            Assert.IsTrue(TutorialRules.SavesDone(new[] { TutorialRules.Arg }), "-tutorial alone (a replay by hand) too");
            Assert.IsTrue(TutorialRules.SavesDone(null));
            Assert.IsFalse(TutorialRules.SavesDone(new[] { "-tour", "tutorial", "-tutorial" }), "the visual check's client");
            Assert.IsFalse(TutorialRules.SavesDone(new[] { "-host", "-observe", "tutorial", "-tour-orders", "-tutorial" }),
                "the visual check's host and observer");
            Assert.IsTrue(TutorialRules.SavesDone(new[] { "-tour-orders" }), "only the exact flags count");
        }

        [Test]
        public void ATutorialFinishedAsARemoteClient_IsNotStale_WhenThePlayerHostsLater()
        {
            // First launch: the launch rule says it will run, so the server half counts it from the start.
            bool launch = TutorialRules.StartsOnLaunch(NoArgs, false, false);
            Assert.IsTrue(launch);
            var host = new HostTutorial(false);
            Assert.IsTrue(host.Set(launch));
            Assert.IsTrue(host.Running);

            // The player joins someone else's server: the runner decides, runs and finishes there. Its TellHost reaches nothing (no
            // server in this process), so the host's flag is still on.
            var runner = new TutorialProgress(TutorialSteps.Default);
            runner.Start();
            runner.SkipAll();
            Assert.IsFalse(runner.Running);
            Assert.IsTrue(host.Running, "stale until the server starts");

            // Later the player hosts: the server start takes the runner's state, the guests are not calm any more.
            bool launchNow = TutorialRules.StartsOnLaunch(NoArgs, true, false); // the done flag is stored now
            Assert.IsTrue(host.OnServerStarted(true, true, runner.Running, launchNow));
            Assert.IsFalse(host.Running);
            Assert.IsFalse(TutorialRules.HostCalms(false, true, true, host.Running), "no calm guests on the new host");
        }

        [Test]
        public void AReplayPressedBeforeHosting_MakesTheHostsGuestsCalm()
        {
            // Done once: nothing at launch.
            var host = new HostTutorial(false);
            bool launch = TutorialRules.StartsOnLaunch(NoArgs, true, false);
            Assert.IsFalse(launch);
            Assert.IsFalse(host.Set(launch));

            // Replay in the menu (no server yet): the runner runs, the host has not heard of it.
            var runner = new TutorialProgress(TutorialSteps.Default);
            runner.Start();
            Assert.IsFalse(host.Running);

            // Hosting: the runner's state wins; the first guests are scripted again.
            Assert.IsTrue(host.OnServerStarted(true, true, runner.Running, launch));
            Assert.IsTrue(host.Running);
            Assert.IsNotNull(host.Orders);
            Assert.IsTrue(host.Orders.TryNext("snack", out _, out _, out _), "the tutorial's first guest");
            Assert.IsTrue(TutorialRules.HostCalms(false, true, true, host.Running));
        }

        [Test]
        public void TheServerStart_TakesTheLaunchRule_UntilTheRunnerDecided_AndNothingWithoutARunner()
        {
            Assert.IsTrue(TutorialRules.RunsForHost(false, false, true), "not decided yet: the launch rule it will decide by");
            Assert.IsFalse(TutorialRules.RunsForHost(false, false, false));
            Assert.IsTrue(TutorialRules.RunsForHost(true, true, false), "decided: its own state");
            Assert.IsFalse(TutorialRules.RunsForHost(true, false, true));

            var host = new HostTutorial(false);
            host.Set(true);
            Assert.IsFalse(host.OnServerStarted(false, true, false, false), "no runner (batch mode): unchanged");
            Assert.IsTrue(host.Running);
            // The tutorial tour's host: its runner is off (-observe) and never decides; -tutorial keeps the guests calm.
            Assert.IsFalse(host.OnServerStarted(true, false, false,
                TutorialRules.StartsOnLaunch(new[] { "-host", "-observe", "tutorial", TutorialRules.Arg }, true, true)));
            Assert.IsTrue(host.Running);

            var server = new HostTutorial(true);
            Assert.IsFalse(server.Set(true), "a server build ignores it");
            Assert.IsFalse(server.OnServerStarted(true, true, true, true));
            Assert.IsFalse(server.Running);
            Assert.IsNull(server.Orders);
        }

        [Test]
        public void ARestartOrASecondStart_ScriptsTheFirstGuestsAgain_OnlyWhileItRuns()
        {
            var host = new HostTutorial(false);
            host.Set(true);
            TutorialOrders first = host.Orders;
            Assert.IsFalse(host.Set(true), "running already: unchanged");
            Assert.AreSame(first, host.Orders);
            host.NewGame();
            Assert.AreNotSame(first, host.Orders, "a new game scripts them again");
            host.Set(false);
            TutorialOrders ended = host.Orders;
            host.NewGame();
            Assert.AreSame(ended, host.Orders, "not while it is off");
            host.Set(true);
            Assert.AreNotSame(ended, host.Orders, "a replay scripts them again");
        }

        [Test]
        public void StepFour_SaysPutTheKnifeDownFirst_WhileTheKnifeIsHeld()
        {
            TutorialStep fry = TutorialSteps.Default[(int)TutorialStepId.FrySticks];
            var knife = new TutorialSnapshot { Held = Held.Knife, BoardTitle = "potato sticks" };
            Assert.AreEqual(TutorialTarget.CuttingBoard, fry.TargetFor(in knife), "the board with the sticks");
            Assert.AreEqual(TutorialSteps.FryKnifeText, fry.TextFor(in knife));
            StringAssert.Contains("knife down", fry.TextFor(in knife));
            Assert.LessOrEqual(TutorialSteps.FryKnifeText.Length, 140, "a short instruction");

            var sticks = new TutorialSnapshot { Held = Held.Food("potato sticks", false) };
            Assert.AreEqual(fry.Text, fry.TextFor(in sticks), "the sticks in the hands: the step's own text");
            Assert.AreEqual(fry.Text, fry.TextFor(new TutorialSnapshot()), "empty hands too");
            var frying = new TutorialSnapshot { Held = Held.Knife, BasketTitle = "potato sticks" };
            Assert.AreEqual(fry.Text, fry.TextFor(in frying), "the sticks are in the basket already: the knife does not matter");
            Assert.AreSame(fry.TextFor(in knife), fry.TextFor(in knife), "a constant: nothing allocated per snapshot");

            foreach (TutorialStep s in TutorialSteps.Default)
                if (s.Id != TutorialStepId.FrySticks) Assert.AreEqual(s.Text, s.TextFor(in knife), s.Id + " has one text");
        }
    }
}
