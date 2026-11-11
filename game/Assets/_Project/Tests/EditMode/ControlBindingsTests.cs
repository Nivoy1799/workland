using System;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Worktown.Game.Settings;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 048 AC2: key and button rebinding. The defaults are the spec 041 mapping; a key or button that another action has is detected
    /// and the two actions swap; fixed keys are refused; the table survives its saved text and never loads with a conflict.
    /// </summary>
    public class ControlBindingsTests
    {
        [Test]
        public void Defaults_AreThe041Mapping_WithoutAConflict()
        {
            ControlBindings b = ControlBindings.Defaults();
            Assert.AreEqual(Key.W, b.KeyOf(ControlAction.MoveForward));
            Assert.AreEqual(Key.S, b.KeyOf(ControlAction.MoveBack));
            Assert.AreEqual(Key.A, b.KeyOf(ControlAction.MoveLeft));
            Assert.AreEqual(Key.D, b.KeyOf(ControlAction.MoveRight));
            Assert.AreEqual(Key.LeftShift, b.KeyOf(ControlAction.Sprint));
            Assert.AreEqual(Key.C, b.KeyOf(ControlAction.Crouch));
            Assert.AreEqual(Key.Space, b.KeyOf(ControlAction.Jump));
            Assert.AreEqual(Key.V, b.KeyOf(ControlAction.ToggleView));
            Assert.AreEqual(Key.E, b.KeyOf(ControlAction.Use));
            Assert.AreEqual(Key.Q, b.KeyOf(ControlAction.PutDown));
            Assert.AreEqual(Key.T, b.KeyOf(ControlAction.QuickChat));
            Assert.AreEqual(Key.G, b.KeyOf(ControlAction.EmoteWheel));
            Assert.AreEqual(Key.Tab, b.KeyOf(ControlAction.Players));
            Assert.AreEqual(Key.None, b.KeyOf(ControlAction.Turn), "the wheel turns");
            Assert.AreEqual(PadButton.South, b.PadOf(ControlAction.Jump));
            Assert.AreEqual(PadButton.East, b.PadOf(ControlAction.Crouch));
            Assert.AreEqual(PadButton.West, b.PadOf(ControlAction.PutDown), "X, the hidden alias of LT");
            Assert.AreEqual(PadButton.North, b.PadOf(ControlAction.Turn), "Y turns one step");
            Assert.AreEqual(PadButton.LeftShoulder, b.PadOf(ControlAction.EmoteWheel));
            Assert.AreEqual(PadButton.RightShoulder, b.PadOf(ControlAction.Ping));
            Assert.AreEqual(PadButton.LeftStick, b.PadOf(ControlAction.Sprint));
            Assert.AreEqual(PadButton.RightStick, b.PadOf(ControlAction.ToggleView));
            Assert.IsEmpty(b.Conflicts());
            foreach (ControlAction action in ControlBindings.Actions)
                Assert.IsFalse(ControlBindings.IsReserved(b.KeyOf(action)), action + " starts on a fixed key");
        }

        [Test]
        public void Rebind_ToAFreeKey_Binds()
        {
            ControlBindings b = ControlBindings.Defaults();
            RebindResult r = b.Rebind(ControlAction.Jump, Key.J);
            Assert.AreEqual(RebindOutcome.Bound, r.Outcome);
            Assert.IsTrue(r.Changed);
            Assert.IsNull(r.Other);
            Assert.AreEqual(Key.J, b.KeyOf(ControlAction.Jump));
            Assert.IsEmpty(b.Conflicts());
        }

        [Test]
        public void Rebind_ToAnotherActionsKey_IsDetected_AndSwaps()
        {
            ControlBindings b = ControlBindings.Defaults();
            Assert.AreEqual(ControlAction.Use, b.ConflictFor(ControlAction.Jump, Key.E), "E is Use's");
            Assert.IsNull(b.ConflictFor(ControlAction.Use, Key.E), "its own key is no conflict");
            Assert.IsNull(b.ConflictFor(ControlAction.Jump, Key.None));

            RebindResult r = b.Rebind(ControlAction.Jump, Key.E);
            Assert.AreEqual(RebindOutcome.Swapped, r.Outcome);
            Assert.AreEqual(ControlAction.Use, r.Other);
            Assert.AreEqual(Key.E, b.KeyOf(ControlAction.Jump));
            Assert.AreEqual(Key.Space, b.KeyOf(ControlAction.Use), "Use took Jump's old key");
            Assert.IsEmpty(b.Conflicts(), "a swap never leaves two actions on one key");
        }

        [Test]
        public void Rebind_AButtonOfAnotherAction_Swaps()
        {
            ControlBindings b = ControlBindings.Defaults();
            Assert.AreEqual(ControlAction.Crouch, b.ConflictFor(ControlAction.Jump, PadButton.East));
            RebindResult r = b.Rebind(ControlAction.Jump, PadButton.East);
            Assert.AreEqual(RebindOutcome.Swapped, r.Outcome);
            Assert.AreEqual(ControlAction.Crouch, r.Other);
            Assert.AreEqual(PadButton.East, b.PadOf(ControlAction.Jump));
            Assert.AreEqual(PadButton.South, b.PadOf(ControlAction.Crouch));
            Assert.IsEmpty(b.Conflicts());

            // A button onto an action without one: the other action is left with none.
            RebindResult toNone = b.Rebind(ControlAction.Use, PadButton.East);
            Assert.AreEqual(RebindOutcome.Swapped, toNone.Outcome);
            Assert.AreEqual(PadButton.None, b.PadOf(ControlAction.Jump));
            Assert.IsEmpty(b.Conflicts());
        }

        [TestCase(Key.Escape)]
        [TestCase(Key.Enter)]
        [TestCase(Key.NumpadEnter)]
        [TestCase(Key.F1)]
        [TestCase(Key.F12)]
        [TestCase(Key.LeftAlt)]
        [TestCase(Key.UpArrow)]
        [TestCase(Key.LeftArrow)]
        [TestCase(Key.Backspace)]
        [TestCase(Key.LeftMeta)]
        [TestCase(Key.F13)]
        [TestCase((Key)9999)]
        public void Rebind_ToAFixedKey_IsRefused(Key key)
        {
            ControlBindings b = ControlBindings.Defaults();
            Assert.IsTrue(ControlBindings.IsReserved(key));
            RebindResult r = b.Rebind(ControlAction.Jump, key);
            Assert.AreEqual(RebindOutcome.Reserved, r.Outcome);
            Assert.IsFalse(r.Changed);
            Assert.AreEqual(Key.Space, b.KeyOf(ControlAction.Jump));
        }

        [Test]
        public void Rebind_TheSameKey_IsUnchanged_AndNoneClears()
        {
            ControlBindings b = ControlBindings.Defaults();
            Assert.AreEqual(RebindOutcome.Unchanged, b.Rebind(ControlAction.Jump, Key.Space).Outcome);
            Assert.AreEqual(RebindOutcome.Cleared, b.Rebind(ControlAction.Jump, Key.None).Outcome);
            Assert.AreEqual(Key.None, b.KeyOf(ControlAction.Jump));
            Assert.AreEqual(RebindOutcome.Bound, b.Rebind(ControlAction.Turn, Key.Space).Outcome, "the freed key is free");
            Assert.AreEqual(RebindOutcome.Cleared, b.Rebind(ControlAction.Ping, PadButton.None).Outcome);
            Assert.IsEmpty(b.Conflicts(), "actions without a key never conflict");
        }

        [Test]
        public void Rebind_LeavesOtherTables_Untouched()
        {
            ControlBindings a = ControlBindings.Defaults();
            ControlBindings copy = a.Clone();
            copy.Rebind(ControlAction.Jump, Key.E);
            Assert.AreEqual(Key.Space, a.KeyOf(ControlAction.Jump));
            Assert.AreEqual(Key.Space, ControlBindings.Defaults().KeyOf(ControlAction.Jump));
        }

        [Test]
        public void Serialize_KeepsOnlyChanges_AndParseRestoresThem()
        {
            Assert.AreEqual(string.Empty, ControlBindings.Defaults().Serialize());
            ControlBindings b = ControlBindings.Defaults();
            b.Rebind(ControlAction.Jump, Key.E);
            b.Rebind(ControlAction.Turn, Key.R);
            b.Rebind(ControlAction.Ping, PadButton.Select);
            string text = b.Serialize();
            StringAssert.Contains("Jump=E,South", text);
            ControlBindings back = ControlBindings.Parse(text);
            foreach (ControlAction action in ControlBindings.Actions)
            {
                Assert.AreEqual(b.KeyOf(action), back.KeyOf(action), action.ToString());
                Assert.AreEqual(b.PadOf(action), back.PadOf(action), action.ToString());
            }
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("garbage")]
        [TestCase("Jump=")]
        [TestCase("Nope=W,South")]
        [TestCase("Jump=12,3")]
        [TestCase("Jump=Escape,South")]
        public void Parse_BrokenText_KeepsTheDefaults(string text)
        {
            ControlBindings b = ControlBindings.Parse(text);
            Assert.AreEqual(Key.Space, b.KeyOf(ControlAction.Jump));
            Assert.AreEqual(PadButton.South, b.PadOf(ControlAction.Jump));
            Assert.IsEmpty(b.Conflicts());
        }

        [Test]
        public void Parse_AConflictingSave_FallsBackToTheDefaults()
        {
            // A hand-edited save with Jump on W (still MoveForward's): never loaded with two actions on one key.
            ControlBindings b = ControlBindings.Parse("Jump=W,South;Turn=R,North");
            Assert.AreEqual(Key.Space, b.KeyOf(ControlAction.Jump));
            Assert.AreEqual(Key.None, b.KeyOf(ControlAction.Turn), "the whole table, not just the conflict");
            Assert.IsEmpty(b.Conflicts());
            Assert.AreEqual(Key.R, ControlBindings.Parse("Turn=R,North").KeyOf(ControlAction.Turn));
        }

        [Test]
        public void PadButtons_MapToTheInputSystemsButtons()
        {
            Assert.IsNull(GameInput.ToGamepadButton(PadButton.None));
            Assert.AreEqual(GamepadButton.South, GameInput.ToGamepadButton(PadButton.South));
            Assert.AreEqual(GamepadButton.West, GameInput.ToGamepadButton(PadButton.West));
            Assert.AreEqual(GamepadButton.LeftShoulder, GameInput.ToGamepadButton(PadButton.LeftShoulder));
            Assert.AreEqual(GamepadButton.RightStick, GameInput.ToGamepadButton(PadButton.RightStick));
            Assert.AreEqual(GamepadButton.Select, GameInput.ToGamepadButton(PadButton.Select));
            foreach (PadButton button in (PadButton[])Enum.GetValues(typeof(PadButton)))
                Assert.AreEqual(button != PadButton.None, GameInput.ToGamepadButton(button).HasValue, button.ToString());
        }

        [Test]
        public void Words_NameKeysButtonsAndSwaps()
        {
            Assert.AreEqual("W", SettingsRules.KeyText(Key.W));
            Assert.AreEqual("Left Shift", SettingsRules.KeyText(Key.LeftShift));
            Assert.AreEqual("1", SettingsRules.KeyText(Key.Digit1));
            Assert.AreEqual("-", SettingsRules.KeyText(Key.None));
            Assert.AreEqual("A", SettingsRules.PadText(PadButton.South));
            Assert.AreEqual("LB", SettingsRules.PadText(PadButton.LeftShoulder));
            var swap = new RebindResult(RebindOutcome.Swapped, ControlAction.Use);
            Assert.AreEqual("E was Use (third person): swapped, Use (third person) now uses Space",
                SettingsRules.RebindText(ControlAction.Jump, swap, "E", "Space"));
            StringAssert.Contains("kept", SettingsRules.RebindText(ControlAction.Jump, new RebindResult(RebindOutcome.Reserved), "Escape", "Space"));
            foreach (ControlAction action in ControlBindings.Actions)
                Assert.IsNotEmpty(SettingsRules.ActionText(action));
        }
    }
}
