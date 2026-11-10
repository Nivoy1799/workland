using NUnit.Framework;
using Worktown.Game.Cooking;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 019 AC7 / spec 020 AC7: first person has no E and no digit keys (the mouse does it; spec 041: Q is the right
    /// click's hidden alias); third person keeps E. Spec 021: the stations with digit options (board, pan, pot) became carried cookware, so no station has
    /// digit options any more; the stove buttons and their flags byte are per burner.
    /// </summary>
    public class FirstPersonKeysTests
    {
        [Test]
        public void FirstPerson_EAndDigitsSendNothing_ThirdPersonKeepsE()
        {
            Assert.IsFalse(InteractionRules.KeysActInView(firstPerson: true), "E is off in first person");
            Assert.IsTrue(InteractionRules.KeysActInView(firstPerson: false));
        }

        [Test]
        public void Q_IsTheRightClicksHiddenAlias_InBothViews()
        {
            // Spec 041 (replaces spec 020's "Q drops in first person only"): Q puts down like the right click, in both views.
            foreach (bool firstPerson in new[] { true, false })
                foreach (bool onSpot in new[] { true, false })
                    Assert.AreEqual(ControlRules.IntentFor(HandInput.RightClick, firstPerson, InteractionAction.None, true, onSpot),
                        ControlRules.IntentFor(HandInput.KeyQ, firstPerson, InteractionAction.None, true, onSpot), firstPerson + " " + onSpot);
            Assert.AreEqual(HandIntent.Drop, ControlRules.IntentFor(HandInput.KeyQ, true, InteractionAction.None, true, false));
            Assert.AreEqual(HandIntent.Drop, ControlRules.IntentFor(HandInput.KeyQ, false, InteractionAction.None, true, false));
            Assert.AreEqual(HandIntent.None, ControlRules.IntentFor(HandInput.KeyQ, true, InteractionAction.None, false, false), "empty hands");
        }

        [Test]
        public void KnifeActions_AreDistinctFromUse()
        {
            Assert.AreNotEqual(VesselRules.ActionStroke, VesselRules.ActionTurn);
            Assert.AreNotEqual(VesselRules.ActionUse, VesselRules.ActionStroke);
            Assert.AreNotEqual(VesselRules.ActionUse, VesselRules.ActionTurn);
        }

        [Test]
        public void ButtonPrompts_ShowWhatPressingDoes_PerBurner()
        {
            byte off = 0;
            byte secondOnSaute = StationRules.BurnerFlags(1, true, true);
            Assert.AreEqual("Left click · burner on", InteractionRules.ButtonPrompt(StationRules.StoveOption(0, StationRules.StoveSwitch), off));
            Assert.AreEqual("Left click · fry → sauté", InteractionRules.ButtonPrompt(StationRules.StoveOption(0, StationRules.PanMode), off));
            Assert.AreEqual("Left click · burner on", InteractionRules.ButtonPrompt(StationRules.StoveOption(0, StationRules.StoveSwitch), secondOnSaute),
                "burner 0 is still off");
            Assert.AreEqual("Left click · burner off", InteractionRules.ButtonPrompt(StationRules.StoveOption(1, StationRules.StoveSwitch), secondOnSaute));
            Assert.AreEqual("Left click · sauté → fry", InteractionRules.ButtonPrompt(StationRules.StoveOption(1, StationRules.PanMode), secondOnSaute));
            Assert.AreEqual("Left click · press", InteractionRules.ButtonPrompt(9, off));
        }

        [Test]
        public void StoveFlags_TwoBitsPerBurner()
        {
            Assert.AreEqual(0, StationRules.BurnerFlags(0, false, false));
            Assert.AreEqual(1, StationRules.BurnerFlags(0, true, false));
            Assert.AreEqual(2, StationRules.BurnerFlags(0, false, true));
            Assert.AreEqual(4, StationRules.BurnerFlags(1, true, false));
            Assert.AreEqual(8, StationRules.BurnerFlags(1, false, true));
            Assert.AreEqual(0, StationRules.BurnerFlags(2, true, true), "a stove has two burners");
            byte flags = (byte)(StationRules.BurnerFlags(0, true, false) | StationRules.BurnerFlags(1, false, true));
            Assert.IsTrue(StationRules.IsBurnerOn(flags, 0));
            Assert.IsFalse(StationRules.IsBurnerSaute(flags, 0));
            Assert.IsFalse(StationRules.IsBurnerOn(flags, 1));
            Assert.IsTrue(StationRules.IsBurnerSaute(flags, 1));
            Assert.IsFalse(StationRules.IsBurnerOn(0xFF, 2));
            Assert.IsFalse(StationRules.IsBurnerOn(0xFF, -1));
        }
    }
}
