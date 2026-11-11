using NUnit.Framework;
using UnityEngine.InputSystem;
using Worktown.Game.Cooking;
using Worktown.Game.Settings;
using Worktown.Game.Tutorial;
using Worktown.Game.UI;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 048 review follow-ups (built with spec 049): the prompts, the F1 controls, the lobby hint and the tutorial name the bound key
    /// after a rebind; the capture gate keeps a rebind's key out of the game; the digits of the quick chat are reserved; an old settings
    /// file without a schema version loads.
    /// </summary>
    public class Review048Tests
    {
        private static ControlBindings Rebound()
        {
            ControlBindings b = ControlBindings.Defaults();
            Assert.AreEqual(RebindOutcome.Bound, b.Rebind(ControlAction.Use, Key.F).Outcome);
            Assert.AreEqual(RebindOutcome.Bound, b.Rebind(ControlAction.ToggleView, Key.B).Outcome);
            Assert.AreEqual(RebindOutcome.Bound, b.Rebind(ControlAction.Players, Key.L).Outcome);
            Assert.AreEqual(RebindOutcome.Bound, b.Rebind(ControlAction.MoveForward, Key.Z).Outcome);
            return b;
        }

        [Test]
        public void StationPrompt_FollowsARebind()
        {
            ControlBindings b = Rebound();
            string use = KeyPrompts.KeyName(b, ControlAction.Use);
            string view = KeyPrompts.KeyName(b, ControlAction.ToggleView);
            Assert.AreEqual("Potato crate: F take one", StationRules.Prompt(StationKind.Crate, "Potato crate", use, view));
            Assert.AreEqual("Fryer: fry in first person (B)", StationRules.Prompt(StationKind.Fryer, "Fryer", use, view));
            Assert.AreEqual("Sink: F put a dish in, hold F to scrub", StationRules.Prompt(StationKind.Sink, "Sink", use, view));
            foreach (StationKind kind in System.Enum.GetValues(typeof(StationKind)))
            {
                string prompt = StationRules.Prompt(kind, "X", use, view);
                StringAssert.DoesNotContain(" E ", prompt + " ", kind + " still names E");
                StringAssert.DoesNotContain("(V)", prompt, kind + " still names V");
            }
        }

        [Test]
        public void ControlsHelp_LobbyHint_AndTutorial_FollowARebind()
        {
            ControlBindings b = Rebound();
            string help = ControlsHelp.Text(b);
            StringAssert.Contains("F (third person)", help);
            StringAssert.Contains("B: first / third person", help);
            StringAssert.Contains("ZASD: walk", help);
            StringAssert.Contains("L: who is in the shop", help);
            StringAssert.DoesNotContain("{", help, "every token filled");
            Assert.AreEqual("L", KeyPrompts.KeyName(b, ControlAction.Players), "the lobby's hint: 'L closes'");
            string walk = KeyPrompts.Fill(TutorialSteps.Default[(int)TutorialStepId.MoveAndLook].Text, b);
            Assert.AreEqual("Walk with ZASD and look around with the mouse.", walk);
            Assert.AreEqual("Walk with WASD and look around with the mouse.",
                KeyPrompts.Fill(TutorialSteps.Default[(int)TutorialStepId.MoveAndLook].Text, ControlBindings.Defaults()));
        }

        [Test]
        public void KeyNames_ForNoKey_AndLongKeys()
        {
            ControlBindings b = ControlBindings.Defaults();
            Assert.AreEqual(RebindOutcome.Cleared, b.Rebind(ControlAction.Use, Key.None).Outcome);
            Assert.AreEqual(KeyPrompts.NoKey, KeyPrompts.KeyName(b, ControlAction.Use));
            Assert.AreEqual(RebindOutcome.Bound, b.Rebind(ControlAction.MoveLeft, Key.Numpad4).Outcome);
            Assert.AreEqual("W / Numpad4 / S / D", KeyPrompts.MoveKeys(b));
            Assert.AreEqual("plain", KeyPrompts.Fill("plain", b));
            Assert.AreEqual(string.Empty, KeyPrompts.Fill(null, b));
        }

        [Test]
        public void CaptureGate_IgnoresTheClickFrame_AndAHeldPadButton()
        {
            Assert.IsFalse(CaptureGate.CaptureReads(100, 100, false, false), "the Enter that clicked the button");
            Assert.IsTrue(CaptureGate.CaptureReads(100, 101, false, false), "a key on a later frame");
            Assert.IsFalse(CaptureGate.CaptureReads(100, 105, true, false), "the A that clicked is still held");
            Assert.IsTrue(CaptureGate.CaptureReads(100, 105, true, true), "every button was up once since");
            Assert.IsFalse(CaptureGate.CaptureReads(100, 100, true, true));
        }

        [Test]
        public void CaptureGate_EscThatClosesTheSettingsInTheShop_LeavesTheCursorLocked_TheTitleScreenKeepsItFree()
        {
            // Follow-up 2026-11-11: the close in the shop locks the cursor again, and the same Esc does not free it right after.
            Assert.IsTrue(CaptureGate.LocksOnClose(true, false), "in the shop, the last menu closed");
            Assert.IsFalse(CaptureGate.LocksOnClose(true, true), "another menu is still open");
            Assert.IsFalse(CaptureGate.LocksOnClose(false, false), "the title screen: the cursor stays free");
            Assert.IsFalse(CaptureGate.FreesCursor(true, 300, 300), "the Esc that closed the settings this frame");
            Assert.IsTrue(CaptureGate.FreesCursor(true, 300, 301), "a later Esc frees it as before");
            Assert.IsTrue(CaptureGate.FreesCursor(true, -1, 300), "no menu closed yet");
            Assert.IsFalse(CaptureGate.FreesCursor(false, -1, 300), "no key");
        }

        [Test]
        public void ControlsPage_TwoBindingColumns_TheFirstTakesTheExtraOne()
        {
            // Follow-up 2026-11-11: two columns so the Controls page fits at 1280x720 (walking to the view switch, then Use to Players).
            Assert.AreEqual(8, SettingsRules.BindingsPerColumn(ControlBindings.Actions.Count));
            Assert.AreEqual(ControlAction.Use, ControlBindings.Actions[SettingsRules.BindingsPerColumn(ControlBindings.Actions.Count)]);
            Assert.AreEqual(2, SettingsRules.BindingsPerColumn(4));
            Assert.AreEqual(1, SettingsRules.BindingsPerColumn(1));
            Assert.AreEqual(1, SettingsRules.BindingsPerColumn(0));
        }

        [Test]
        public void CaptureGate_GameKeys_AndTheLobbyKey_WaitWhileAMenuIsOpen()
        {
            Assert.IsTrue(CaptureGate.GameKeysLive(false, false));
            Assert.IsFalse(CaptureGate.GameKeysLive(true, false), "a menu or banner");
            Assert.IsFalse(CaptureGate.GameKeysLive(false, true), "the settings (a rebind capture)");
            Assert.IsTrue(CaptureGate.LobbyToggleLive(true, false, 0, false), "opens the lobby");
            Assert.IsTrue(CaptureGate.LobbyToggleLive(true, false, 1, true), "closes the lobby (its own count)");
            Assert.IsFalse(CaptureGate.LobbyToggleLive(true, true, 1, false), "the settings capture Tab");
            Assert.IsFalse(CaptureGate.LobbyToggleLive(true, false, 1, false), "another menu is open");
            Assert.IsFalse(CaptureGate.LobbyToggleLive(false, false, 0, false), "not in a shop");
        }

        [Test]
        public void QuickChatDigits_AreReserved()
        {
            ControlBindings b = ControlBindings.Defaults();
            for (Key k = Key.Digit1; k <= Key.Digit6; k++)
            {
                Assert.IsTrue(ControlBindings.IsReserved(k), k.ToString());
                Assert.AreEqual(RebindOutcome.Reserved, b.Rebind(ControlAction.Jump, k).Outcome, k.ToString());
            }
            Assert.IsFalse(ControlBindings.IsReserved(Key.Digit7));
            Assert.AreEqual(RebindOutcome.Bound, b.Rebind(ControlAction.Jump, Key.Digit7).Outcome);
            Assert.AreEqual(Key.Space, ControlBindings.Parse("Jump=Digit3,South").KeyOf(ControlAction.Jump), "a saved digit is skipped");
        }

        [Test]
        public void SettingsJson_HasASchemaVersion_AndAnOldFileWithoutItLoads()
        {
            StringAssert.Contains("\"SchemaVersion\":1", GameSettings.Defaults().ToJson());
            GameSettings old = GameSettings.FromJson("{\"FieldOfView\":90.0,\"Labels\":false,\"FrameCap\":60}");
            Assert.IsNotNull(old);
            Assert.AreEqual(GameSettings.CurrentSchema, old.SchemaVersion);
            Assert.AreEqual(90f, old.FieldOfView, 1e-4f);
            Assert.IsFalse(old.Labels);
            Assert.AreEqual(60, old.FrameCap);
            Assert.AreEqual(GameSettings.CurrentSchema, GameSettings.FromJson("{\"SchemaVersion\":0}").SchemaVersion);
        }
    }
}
