using System;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Cooking;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;
using Worktown.Game.Player;
using Worktown.Game.Tutorial;
using Worktown.Game.UI;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 041 AC2: the mouse-first mapping (ControlRules) for every input in every context; the right click never does anything but
    /// put down or drop. The wheel's rest turn and how it rides on the place request (PlaceTurn), the wheel's notches (WheelSteps), the
    /// prompts naming the button, the F1 controls and the tutorial texts.
    /// </summary>
    public class MouseControlsTests
    {
        private static readonly InteractionAction[] AllActions = (InteractionAction[])Enum.GetValues(typeof(InteractionAction));
        private static readonly HandInput[] AllInputs = (HandInput[])Enum.GetValues(typeof(HandInput));

        private static HandIntent First(HandInput input, InteractionAction action, bool holding = true, bool spot = false) =>
            ControlRules.IntentFor(input, true, action, holding, spot);

        private static HandIntent Third(HandInput input, bool holding = true) =>
            ControlRules.IntentFor(input, false, InteractionAction.None, holding, false);

        [Test]
        public void RightClick_NeverDoesAnythingButPutDownOrDrop()
        {
            foreach (bool firstPerson in new[] { true, false })
                foreach (InteractionAction action in AllActions)
                    foreach (bool holding in new[] { true, false })
                        foreach (bool spot in new[] { true, false })
                            foreach (HandInput input in new[] { HandInput.RightClick, HandInput.KeyQ })
                            {
                                HandIntent intent = ControlRules.IntentFor(input, firstPerson, action, holding, spot);
                                Assert.That(intent, Is.EqualTo(HandIntent.PutDown).Or.EqualTo(HandIntent.Drop).Or.EqualTo(HandIntent.None),
                                    input + " " + firstPerson + " " + action + " " + holding + " " + spot);
                                if (!holding) Assert.AreEqual(HandIntent.None, intent, "nothing in the hands: nothing to put down");
                            }
        }

        [Test]
        public void RightClick_PutsDownOnTheAimedSpot_ElseDropsAtTheFeet()
        {
            Assert.AreEqual(HandIntent.PutDown, First(HandInput.RightClick, InteractionAction.Place, spot: true));
            // Q2 of the spec: aimed at the pot with food in the hands, the right click still puts it down (next to it), never in.
            Assert.AreEqual(HandIntent.PutDown, First(HandInput.RightClick, InteractionAction.PutInto, spot: true));
            Assert.AreEqual(HandIntent.Drop, First(HandInput.RightClick, InteractionAction.PutInto, spot: false));
            Assert.AreEqual(HandIntent.Drop, First(HandInput.RightClick, InteractionAction.None));
            Assert.AreEqual(HandIntent.PutDown, First(HandInput.RightClick, InteractionAction.Cut, spot: true), "the knife over the board");
            Assert.AreEqual(HandIntent.Drop, First(HandInput.RightClick, InteractionAction.Flip), "the right click flips no more (spec 030)");
            Assert.AreEqual(HandIntent.Drop, Third(HandInput.RightClick), "third person: at the feet");
            Assert.AreEqual(HandIntent.None, Third(HandInput.RightClick, holding: false));
        }

        [Test]
        public void LeftClick_DoesTheClicksAction_TheHoldActionsHold_TheKnifeDrags()
        {
            foreach (InteractionAction action in AllActions)
            {
                HandIntent intent = First(HandInput.LeftClick, action);
                if (action == InteractionAction.None || action == InteractionAction.Cut) Assert.AreEqual(HandIntent.None, intent, action.ToString());
                else if (ControlRules.IsHoldAction(action)) Assert.AreEqual(HandIntent.Hold, intent, action.ToString());
                else Assert.AreEqual(HandIntent.Act, intent, action.ToString());
            }
            Assert.AreEqual(HandIntent.Act, First(HandInput.LeftClick, InteractionAction.Flip), "a flip is a left click with the spatula");
            Assert.AreEqual(HandIntent.Act, First(HandInput.LeftClick, InteractionAction.PickUp, holding: false));
            Assert.AreEqual(HandIntent.Act, First(HandInput.LeftClick, InteractionAction.Place), "the left click places too");
            Assert.AreEqual(HandIntent.Stroke, First(HandInput.HoldLeft, InteractionAction.Cut));
            Assert.AreEqual(HandIntent.Hold, First(HandInput.HoldLeft, InteractionAction.Squeeze));
            Assert.AreEqual(HandIntent.None, First(HandInput.HoldLeft, InteractionAction.PickUp), "a long press is no click");
            foreach (InteractionAction hold in new[] { InteractionAction.Squeeze, InteractionAction.Fill, InteractionAction.Scrub,
                         InteractionAction.Wipe, InteractionAction.Shave })
                Assert.IsTrue(ControlRules.IsHoldAction(hold), hold.ToString());
        }

        [Test]
        public void Wheel_TurnsTheFoodOnTheBoard_ElseTheHeldItemsRest()
        {
            Assert.AreEqual(HandIntent.TurnFood, First(HandInput.Wheel, InteractionAction.Cut));
            Assert.AreEqual(HandIntent.TurnRest, First(HandInput.Wheel, InteractionAction.Place, spot: true));
            Assert.AreEqual(HandIntent.TurnRest, First(HandInput.Wheel, InteractionAction.None));
            Assert.AreEqual(HandIntent.None, First(HandInput.Wheel, InteractionAction.PickUp, holding: false), "empty hands: nothing to turn");
            Assert.AreEqual(HandIntent.Zoom, Third(HandInput.Wheel), "third person: the camera");
        }

        [Test]
        public void ThirdPerson_LeftClickIsE_OnTheNearestTarget()
        {
            Assert.AreEqual(HandIntent.Act, Third(HandInput.LeftClick));
            Assert.AreEqual(HandIntent.Act, Third(HandInput.KeyE));
            Assert.AreEqual(HandIntent.Act, Third(HandInput.LeftClick, holding: false));
            Assert.AreEqual(Third(HandInput.KeyE), Third(HandInput.LeftClick), "the same meaning");
            Assert.AreEqual(HandIntent.Hold, Third(HandInput.HoldLeft), "holding E or the left button: the board's squeeze gate decides");
            foreach (InteractionAction action in AllActions)
                Assert.AreEqual(HandIntent.None, First(HandInput.KeyE, action), "spec 020 AC7: no E in first person");
        }

        [Test]
        public void EveryInput_HasExactlyOneMeaning_PerContext()
        {
            // The function is total: every input in every context gives a defined intent (no exception, a known value).
            foreach (HandInput input in AllInputs)
                foreach (bool firstPerson in new[] { true, false })
                    foreach (InteractionAction action in AllActions)
                        foreach (bool holding in new[] { true, false })
                        {
                            HandIntent intent = ControlRules.IntentFor(input, firstPerson, action, holding, holding);
                            Assert.IsTrue(Enum.IsDefined(typeof(HandIntent), intent));
                        }
        }

        // ---- PlaceTurn: the wheel's rest turn on the existing place request ----

        [Test]
        public void PlaceTurn_AddWrapsIntoEightSteps()
        {
            Assert.AreEqual(1, PlaceTurn.Add(0, 1));
            Assert.AreEqual(0, PlaceTurn.Add(7, 1));
            Assert.AreEqual(7, PlaceTurn.Add(0, -1));
            Assert.AreEqual(2, PlaceTurn.Add(1, 17));
            Assert.AreEqual(45f, PlaceTurn.StepDegrees, 1e-4f);
        }

        [Test]
        public void PlaceTurn_PackAndUnpack_ARoundTrip_AndOldSpotIntsAreUnchanged()
        {
            for (int spot = 0; spot < 40; spot++)
                for (int turn = 0; turn < PlaceTurn.Steps; turn++)
                {
                    int packed = PlaceTurn.Pack(spot, turn);
                    Assert.IsTrue(PlaceTurn.TryUnpack(packed, out int s, out int t));
                    Assert.AreEqual(spot, s);
                    Assert.AreEqual(turn, t);
                    if (turn == 0) Assert.AreEqual(spot, packed, "no turn: the spot int is the spot, as before spec 041");
                }
            Assert.IsTrue(PlaceTurn.TryUnpack(-1, out int none, out int noTurn));
            Assert.AreEqual(-1, none, "a negative spot stays negative (refused as before)");
            Assert.AreEqual(0, noTurn);
            Assert.AreEqual(-1, PlaceTurn.Pack(-1, 3));
            Assert.AreEqual(PlaceTurn.Pack(2, 1), PlaceTurn.Pack(2, 9), "the turn wraps before it is packed");
        }

        [Test]
        public void PlaceTurn_AStepOutOfRange_IsRefusedLikeAnUnknownSpot()
        {
            Assert.IsFalse(PlaceTurn.TryUnpack(3 | (8 << PlaceTurn.SpotBits), out int spot, out _));
            Assert.AreEqual(-1, spot);
            Assert.IsFalse(PlaceTurn.TryUnpack(int.MaxValue, out spot, out _));
            Assert.AreEqual(-1, spot);
            // The server's check then refuses the spot (PlaceSpots.CheckPlace): nothing moves.
            var spots = new PlaceSpots();
            Assert.AreEqual(PickupOutcome.NotFound, spots.CheckPlace(true, true, 10, 0, spot, true));
        }

        [Test]
        public void PlaceTurn_Apply_TurnsTheYawOnly_TheTiltStays()
        {
            Quaternion tilt = Quaternion.Euler(0f, 0f, 90f); // the knife on its edge
            Quaternion rest = Quaternion.Euler(0f, 180f, 0f) * tilt;
            Assert.AreEqual(rest, PlaceTurn.Apply(rest, 0));
            Quaternion turned = PlaceTurn.Apply(rest, 2);
            Assert.Less(Quaternion.Angle(PlaceSpots.TiltOf(rest), PlaceSpots.TiltOf(turned)), 0.5f, "the tilt stays");
            float yaw = Mathf.DeltaAngle(PlaceSpots.YawOf(rest).eulerAngles.y, PlaceSpots.YawOf(turned).eulerAngles.y);
            Assert.AreEqual(90f, yaw, 0.5f, "two steps: a quarter turn");
            Assert.Less(Quaternion.Angle(rest, PlaceTurn.Apply(rest, 8)), 0.01f, "eight steps: all the way round");
            // A tool put down with a turn: the handle turns away from the cook by the turn (ToolRest gives the start).
            var spot = new Vector3(0f, 0.9f, 1f);
            Quaternion tool = ToolRest.RestRotation(true, tilt, Quaternion.identity, new Vector3(0f, 0f, 0f), spot);
            float handle = Vector3.Angle(ToolRest.HandleDirection(tool), ToolRest.HandleDirection(PlaceTurn.Apply(tool, 2)));
            Assert.AreEqual(90f, handle, 0.5f);
        }

        // ---- WheelSteps ----

        [Test]
        public void WheelSteps_OneStepPerNotch_InEitherUnit()
        {
            var wheel = new WheelSteps();
            Assert.AreEqual(1, wheel.Update(120f), "a Windows notch");
            Assert.AreEqual(-1, wheel.Update(-120f));
            Assert.AreEqual(2, wheel.Update(240f));
            Assert.AreEqual(1, wheel.Update(1f), "a normalised notch");
            Assert.AreEqual(-1, wheel.Update(-1f));
            Assert.AreEqual(0, wheel.Update(0f));
            Assert.AreEqual(0, wheel.Update(float.NaN));
            Assert.AreEqual(0, wheel.Update(float.PositiveInfinity));
        }

        [Test]
        public void WheelSteps_ATouchpadsSmallValues_AddUp_AndResetForgets()
        {
            var wheel = new WheelSteps();
            int steps = 0;
            for (int i = 0; i < 10; i++) steps += wheel.Update(0.25f);
            Assert.AreEqual(2, steps, "ten quarter notches: two steps, the half kept");
            wheel.Reset();
            Assert.AreEqual(0, wheel.Update(0.5f), "the kept half is gone");
            Assert.AreEqual(1, wheel.Update(0.5f));
        }

        // ---- Prompts, help, tutorial ----

        [Test]
        public void Prompts_NameTheMouseButton()
        {
            Held potato = Held.Food("potato", false);
            Assert.AreEqual("Left click · put potato into the pot", InteractionRules.PromptFor(potato, AimTarget.Cookware(VesselKind.Pot, 0, null)));
            Assert.AreEqual("Right click · put potato here   Wheel · turn", InteractionRules.PromptFor(potato, AimTarget.Spot(2)));
            Assert.AreEqual("Left click · pick up tomato", InteractionRules.PromptFor(Held.Nothing, AimTarget.Item("tomato")));
            Assert.AreEqual("Drag down · cut   Wheel · turn", InteractionRules.PromptFor(Held.Knife, AimTarget.Cookware(VesselKind.Board, 1, "potato")));
            Assert.AreEqual("Left click · flip seitan strips",
                InteractionRules.PromptFor(Held.Spatula, AimTarget.Cookware(VesselKind.Griddle, 1, "seitan strips")));
            Assert.AreEqual("Right click · put down here", InteractionRules.PutDownPrompt(true));
            Assert.AreEqual("Right click · drop", InteractionRules.PutDownPrompt(false));
            StringAssert.StartsWith("Hold left click · ", InteractionRules.FillPrompt("lemonade"));
        }

        [Test]
        public void ControlsHelp_ShowsTheMapping_WithoutQ()
        {
            string text = ControlsHelp.Text();
            foreach (string word in new[] { "Left click", "Hold left click", "Right click: put down", "wheel", "E (third person)", "RT", "LT" })
                StringAssert.Contains(word, text);
            StringAssert.DoesNotContain("Q", text, "Q is a hidden alias");
            Assert.AreSame(text, ControlsHelp.Text(), "built once");
        }

        [Test]
        public void TutorialTexts_NameTheMouse_NeverQ_OrTheOldRightClickTurn()
        {
            foreach (TutorialStep step in TutorialSteps.Default)
            {
                Assert.IsFalse(step.Text.Contains("Q "), step.Id + " still names Q");
                Assert.IsFalse(step.Text.Contains("right click turns"), step.Id + " still turns with the right click");
                // 041 review: every click names its button ("left click", "right click"), anywhere in the text.
                Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(step.Text, @"(?<!left |right )click",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase), step.Id + " has a click without its button: " + step.Text);
            }
            StringAssert.Contains("left click the terminal", TutorialSteps.Default[(int)TutorialStepId.TakePayment].Text);
            StringAssert.Contains("right click", TutorialSteps.FryKnifeText);
            Assert.IsFalse(TutorialSteps.FryKnifeText.Contains("Q "));
            TutorialStep cut = null;
            foreach (TutorialStep step in TutorialSteps.Default)
                if (step.Id == TutorialStepId.CutSticks) cut = step;
            Assert.IsNotNull(cut);
            StringAssert.Contains("wheel", cut.Text);
        }
    }
}
