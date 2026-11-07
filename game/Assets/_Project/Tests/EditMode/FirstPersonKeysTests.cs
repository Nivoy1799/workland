using System;
using NUnit.Framework;
using Worktown.Game.Cooking;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 019 AC7: the first-person key map has no digits (the stoves have buttons, the board a knife); third
    /// person keeps them. Also the digit-free prompts and the stove flags byte.
    /// Spec 020 AC7: first person has no E either (the mouse does it, Q drops); third person keeps E and the digits.
    /// </summary>
    public class FirstPersonKeysTests
    {
        [Test]
        public void FirstPerson_EAndDigitsSendNothing()
        {
            Assert.IsFalse(InteractionRules.KeysActInView(firstPerson: true), "E and the digits are off in first person");
            foreach (StationKind kind in Enum.GetValues(typeof(StationKind)))
                for (int digit = 1; digit <= 4; digit++)
                    Assert.AreEqual(0, StationRules.DigitOption(kind, digit, firstPerson: true), kind + " digit " + digit);
        }

        [Test]
        public void ThirdPerson_EAndDigitsWorkAsBefore()
        {
            Assert.IsTrue(InteractionRules.KeysActInView(firstPerson: false));
            Assert.AreEqual(2, StationRules.DigitOption(StationKind.Board, 2, firstPerson: false));
            Assert.AreEqual(StationRules.StoveSwitch, StationRules.DigitOption(StationKind.Pot, 1, firstPerson: false));
        }

        [Test]
        public void QDrops_InFirstPersonOnly()
        {
            Assert.IsTrue(InteractionRules.DropKeyActs(firstPerson: true));
            Assert.IsFalse(InteractionRules.DropKeyActs(firstPerson: false), "third person drops with E as before");
        }

        [Test]
        public void FirstPerson_NoDigitMakesAStationRequest()
        {
            foreach (StationKind kind in Enum.GetValues(typeof(StationKind)))
                for (int digit = 0; digit <= 9; digit++)
                    Assert.AreEqual(0, StationRules.DigitOption(kind, digit, firstPerson: true), kind + " digit " + digit);
        }

        [Test]
        public void ThirdPerson_DigitsStillWork()
        {
            for (int digit = 1; digit <= 4; digit++)
                Assert.AreEqual(digit, StationRules.DigitOption(StationKind.Board, digit, firstPerson: false));
            Assert.AreEqual(StationRules.StoveSwitch, StationRules.DigitOption(StationKind.Pan, 1, false));
            Assert.AreEqual(StationRules.PanMode, StationRules.DigitOption(StationKind.Pan, 2, false));
            Assert.AreEqual(StationRules.StoveSwitch, StationRules.DigitOption(StationKind.Pot, 1, false));
            // A digit above the pot's options is passed on, so the client can say "Pot has no option 2".
            Assert.AreEqual(2, StationRules.DigitOption(StationKind.Pot, 2, false));
        }

        [Test]
        public void ThirdPerson_StationsWithoutOptions_IgnoreDigits()
        {
            foreach (StationKind kind in new[] { StationKind.Crate, StationKind.Counter, StationKind.PlatingTable, StationKind.Bowl })
                Assert.AreEqual(0, StationRules.DigitOption(kind, 1, false), kind.ToString());
            Assert.AreEqual(0, StationRules.DigitOption(StationKind.Board, 0, false), "no key pressed");
            Assert.AreEqual(0, StationRules.DigitOption(StationKind.Board, 5, false), "only 1-4 are station keys");
        }

        [Test]
        public void KnifeOptions_AreAboveTheDigitRange()
        {
            Assert.Greater(StationRules.KnifeStroke, 4);
            Assert.Greater(StationRules.KnifeTurn, 4);
            Assert.AreNotEqual(StationRules.KnifeStroke, StationRules.KnifeTurn);
            Assert.IsFalse(StationRules.TryGetAction(StationKind.Board, StationRules.KnifeStroke, out _), "a stroke is no instant cut");
        }

        [Test]
        public void WorldLabelHints_HaveNoDigits()
        {
            foreach (StationKind kind in Enum.GetValues(typeof(StationKind)))
            {
                string hint = StationRules.FirstPersonKeysHint(kind);
                StringAssert.StartsWith("E", hint, kind.ToString());
                foreach (char c in hint) Assert.IsFalse(char.IsDigit(c), kind + ": " + hint);
            }
        }

        [Test]
        public void FirstPersonPrompts_HaveNoDigits()
        {
            foreach (StationKind kind in Enum.GetValues(typeof(StationKind)))
                foreach (bool holding in new[] { false, true })
                    foreach (bool knife in new[] { false, true })
                        foreach (string content in new[] { null, "potato" })
                        {
                            string prompt = StationRules.FirstPersonPrompt(kind, "Station", holding, knife && holding, content);
                            Assert.IsNotEmpty(prompt);
                            foreach (char c in prompt) Assert.IsFalse(char.IsDigit(c), kind + ": " + prompt);
                        }
        }

        [Test]
        public void BoardPrompts_FollowTheFlow()
        {
            Assert.AreEqual("E · put on board", StationRules.FirstPersonPrompt(StationKind.Board, "Board", true, false, null));
            Assert.AreEqual("drag down · cut   right click · turn",
                StationRules.FirstPersonPrompt(StationKind.Board, "Board", true, true, "potato"));
            Assert.AreEqual("E · take potato sticks",
                StationRules.FirstPersonPrompt(StationKind.Board, "Board", false, false, "potato sticks"));
            Assert.AreEqual("E · put in the pan", StationRules.FirstPersonPrompt(StationKind.Pan, "Pan", true, false, null));
            Assert.AreEqual("Counter: E sell", StationRules.FirstPersonPrompt(StationKind.Counter, "Counter", true, false, null));
        }

        [Test]
        public void ButtonPrompts_ShowWhatPressingDoes()
        {
            byte off = StationRules.StoveFlags(false, false);
            byte onSaute = StationRules.StoveFlags(true, true);
            Assert.AreEqual("E · switch on", StationRules.ButtonPrompt(StationRules.StoveSwitch, off));
            Assert.AreEqual("E · switch off", StationRules.ButtonPrompt(StationRules.StoveSwitch, onSaute));
            Assert.AreEqual("E · fry → sauté", StationRules.ButtonPrompt(StationRules.PanMode, off));
            Assert.AreEqual("E · sauté → fry", StationRules.ButtonPrompt(StationRules.PanMode, onSaute));
            Assert.IsNull(StationRules.ButtonPrompt(3, off));
        }

        [Test]
        public void StoveFlags_BitZeroOn_BitOneSaute()
        {
            Assert.AreEqual(0, StationRules.StoveFlags(false, false));
            Assert.AreEqual(1, StationRules.StoveFlags(true, false));
            Assert.AreEqual(2, StationRules.StoveFlags(false, true));
            Assert.AreEqual(3, StationRules.StoveFlags(true, true));
            Assert.IsTrue(StationRules.IsStoveOn(3) && StationRules.IsSaute(3));
            Assert.IsFalse(StationRules.IsStoveOn(2) || StationRules.IsSaute(1));
        }
    }
}
