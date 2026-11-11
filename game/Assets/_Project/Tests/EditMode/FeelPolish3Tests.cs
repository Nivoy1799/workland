using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Art;
using Worktown.Game.Cooking;
using Worktown.Game.Npc;
using Worktown.Game.Player;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 044: the pure parts of feel polish 3. Button sinks, the remote put-down's landing, the exit flight of an item that leaves the
    /// world, the predicted tip and scoop, the third-person arm poses and the eat-in guest's plate. Sprint is in FeelRulesTests, the rail
    /// and the order bubbles in TicketRailSceneTests; the rest is in the visual check (.verify/visual044).
    /// </summary>
    public class FeelPolish3Tests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _objects)
                if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
            foreach (ExitFlightLook left in Object.FindObjectsByType<ExitFlightLook>(FindObjectsSortMode.None)) Object.DestroyImmediate(left.gameObject);
        }

        // ---- buttons sink (027b feels off) ----

        [Test]
        public void ButtonPress_SinksFast_HoldsAndSpringsBack()
        {
            Assert.AreEqual(0f, ButtonPressRules.Depth(0f));
            Assert.AreEqual(0f, ButtonPressRules.Depth(-1f));
            Assert.AreEqual(0f, ButtonPressRules.Depth(float.NaN));
            Assert.AreEqual(ButtonPressRules.SinkDepth * 0.5f, ButtonPressRules.Depth(ButtonPressRules.DownSeconds * 0.5f), 1e-6f);
            Assert.AreEqual(ButtonPressRules.SinkDepth, ButtonPressRules.Depth(ButtonPressRules.DownSeconds + 0.01f), 1e-6f, "held down");
            float last = ButtonPressRules.SinkDepth;
            for (float t = ButtonPressRules.DownSeconds + ButtonPressRules.HoldSeconds; t < ButtonPressRules.TotalSeconds; t += 0.01f)
            {
                float d = ButtonPressRules.Depth(t);
                Assert.LessOrEqual(d, last + 1e-6f, "springs back without overshooting at " + t);
                last = d;
            }
            Assert.AreEqual(0f, ButtonPressRules.Depth(ButtonPressRules.TotalSeconds));
            Assert.Less(ButtonPressRules.TotalSeconds, 0.3f, "a click, not a slow animation");
        }

        [Test]
        public void ButtonPress_TheFlagsChangeSaysWhichButtonWasPressed()
        {
            byte off = 0;
            byte burner1On = StationRules.BurnerFlags(1, true, false);
            Assert.IsTrue(ButtonPressRules.Pressed(StationKind.Stove, StationRules.StoveOption(1, StationRules.StoveSwitch), off, burner1On));
            Assert.IsFalse(ButtonPressRules.Pressed(StationKind.Stove, StationRules.StoveOption(0, StationRules.StoveSwitch), off, burner1On), "the other burner");
            Assert.IsFalse(ButtonPressRules.Pressed(StationKind.Stove, StationRules.StoveOption(1, StationRules.PanMode), off, burner1On), "the mode button");
            byte saute = StationRules.BurnerFlags(1, true, true);
            Assert.IsTrue(ButtonPressRules.Pressed(StationKind.Stove, StationRules.StoveOption(1, StationRules.PanMode), burner1On, saute));

            byte fryerOn = FryerRules.FryerFlags(true, false, false);
            Assert.IsTrue(ButtonPressRules.Pressed(StationKind.Fryer, FryerRules.PowerOption, 0, fryerOn));
            byte lowered = FryerRules.FryerFlags(true, false, true);
            Assert.IsTrue(ButtonPressRules.Pressed(StationKind.Fryer, FryerRules.BasketOption(1), fryerOn, lowered));
            Assert.IsTrue(ButtonPressRules.Pressed(StationKind.Fryer, FryerRules.BasketOption(1), lowered, fryerOn), "raised again");
            Assert.IsFalse(ButtonPressRules.Pressed(StationKind.Fryer, FryerRules.BasketOption(0), fryerOn, lowered));
            Assert.IsFalse(ButtonPressRules.Pressed(StationKind.Fryer, FryerRules.PowerOption, fryerOn, lowered));

            Assert.IsTrue(ButtonPressRules.Pressed(StationKind.Griddle, GriddleRules.PowerOption, 0, GriddleRules.Flags(true)));
            Assert.IsTrue(ButtonPressRules.Pressed(StationKind.Spit, SpitRules.KnobOption, SpitRules.Flags(0, false, true), SpitRules.Flags(1, false, true)));
            Assert.IsFalse(ButtonPressRules.Pressed(StationKind.Spit, SpitRules.KnobOption, SpitRules.Flags(1, false, true), SpitRules.Flags(1, true, true)), "shaving is no knob turn");
            Assert.IsFalse(ButtonPressRules.Pressed(StationKind.Stove, StationRules.StoveOption(0, StationRules.StoveSwitch), 3, 3), "no change, no press");
            Assert.IsFalse(ButtonPressRules.Pressed(StationKind.Counter, 0, 0, 1));
        }

        // ---- another player's put-down is heard where it lands (027b feels off) ----

        [Test]
        public void ItemLook_APutDownLands_OnceAndSaysWhoPutItDown()
        {
            const int other = 5;
            var look = new ItemLook();
            look.Reset(other);
            Pose hold = new Pose(new Vector3(0f, 2.5f, 0f), Quaternion.identity);
            Pose rest = new Pose(new Vector3(1f, 0.9f, 1f), Quaternion.identity);
            Pose hand = new Pose(new Vector3(0.3f, 1.2f, 0f), Quaternion.identity);
            ItemFrame f = look.Resolve(other, hold, false, default, 0f, HandAt(hand));
            Assert.AreEqual(ItemLook.NoHolder, f.LandedBy);
            f = look.Resolve(ItemLook.NoHolder, hold, true, hand, 0.02f, HandAt(hand));
            Assert.AreEqual(ItemLook.NoHolder, f.LandedBy, "not yet: the networked position is on its way");
            f = look.Resolve(ItemLook.NoHolder, rest, true, hand, 0.05f, HandAt(hand));
            Assert.AreEqual(ItemLook.NoHolder, f.LandedBy);
            f = look.Resolve(ItemLook.NoHolder, rest, true, hand, 0.11f, HandAt(hand));
            Assert.AreEqual(other, f.LandedBy, "lands on its spot: the tock plays now");
            f = look.Resolve(ItemLook.NoHolder, rest, true, rest, 0.2f, HandAt(hand));
            Assert.AreEqual(ItemLook.NoHolder, f.LandedBy, "once");
        }

        [Test]
        public void ItemLook_TakenAgainBeforeItLanded_IsNoLanding_AndALateJoinerHearsNothing()
        {
            var look = new ItemLook();
            look.Reset(5);
            Pose hold = new Pose(new Vector3(0f, 2.5f, 0f), Quaternion.identity);
            Pose hand = new Pose(new Vector3(0.3f, 1.2f, 0f), Quaternion.identity);
            look.Resolve(5, hold, false, default, 0f, HandAt(hand));
            look.Resolve(ItemLook.NoHolder, hold, true, hand, 0.02f, HandAt(hand));
            look.Resolve(6, hold, true, hand, 0.04f, HandAt(hand)); // someone took it at once
            for (float t = 0.06f; t < 1f; t += 0.05f)
                Assert.AreEqual(ItemLook.NoHolder, look.Resolve(6, hold, true, hand, t, HandAt(hand)).LandedBy);

            var late = new ItemLook();
            late.Reset(ItemLook.NoHolder); // first seen lying
            Pose rest = new Pose(Vector3.one, Quaternion.identity);
            for (float t = 0f; t < 1f; t += 0.05f)
                Assert.AreEqual(ItemLook.NoHolder, late.Resolve(ItemLook.NoHolder, rest, t > 0f, rest, t, HandAt(hand)).LandedBy);
        }

        private static HeldGoalSource HandAt(Pose hand) => (int holder, out ItemGoalKind kind, out Pose pose) =>
        {
            kind = ItemGoalKind.Hand;
            pose = hand;
            return true;
        };

        // ---- other players see food fly into the pot (027b feels off) ----

        [Test]
        public void ExitFlight_OnlyForViewersWhoDidNotDrawItGoingThere()
        {
            Assert.IsTrue(ExitFlightRules.ShowsFlight(true, true, false), "another player's put-into");
            Assert.IsFalse(ExitFlightRules.ShowsFlight(true, true, true), "our own put-into is drawn at the pot since the click");
            Assert.IsFalse(ExitFlightRules.ShowsFlight(false, true, false), "served, eaten or lost: no destination, no flight");
            Assert.IsFalse(ExitFlightRules.ShowsFlight(true, false, false), "never drawn (a headless server)");
            Assert.AreEqual(1f, ExitFlightRules.Size(0f));
            Assert.AreEqual(1f, ExitFlightRules.Size(0.5f));
            Assert.AreEqual(ExitFlightRules.EndSize, ExitFlightRules.Size(1f), 1e-6f);
            float s = ExitFlightRules.Seconds(FeelSettings.PresetA());
            Assert.That(s, Is.InRange(0.2f, 0.5f));
            Assert.Greater(ExitFlightRules.Lift(FeelSettings.PresetA()), 0f);
        }

        [Test]
        public void ExitFlight_CopiesTheModelOnly_FliesIntoThePot_AndIsGone()
        {
            var item = new GameObject("Food");
            _objects.Add(item);
            item.AddComponent<BoxCollider>();
            item.transform.position = new Vector3(0f, 1.2f, 0f);
            GameObject model = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            model.transform.SetParent(item.transform, false);
            model.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            var label = new GameObject("Label").AddComponent<TextMesh>();
            label.transform.SetParent(item.transform, false);

            Pose drawn = new Pose(new Vector3(0.2f, 1.3f, 0.1f), Quaternion.identity); // in the cook's hand
            Vector3 pot = new Vector3(1f, 0.95f, 0.5f);
            ExitFlightLook flight = ExitFlightLook.Begin(item.transform, drawn, pot, null, 10f);
            Assert.IsNotNull(flight);
            Assert.AreEqual(ExitFlightLook.ObjectName, flight.name);
            Assert.IsEmpty(flight.GetComponentsInChildren<Collider>(), "a look: nothing to aim at or bump into");
            Assert.AreEqual(1, flight.GetComponentsInChildren<MeshRenderer>().Length, "the model, not the label");
            Assert.IsNull(flight.GetComponentInChildren<TextMesh>());
            Assert.AreEqual(drawn.position, flight.transform.position, "starts where the item was drawn");

            float seconds = ExitFlightRules.Seconds(FeelSettings.Current ?? FeelSettings.PresetA());
            flight.Step(10f + seconds * 0.5f);
            Assert.Greater(flight.transform.position.y, Mathf.Lerp(drawn.position.y, pot.y, 0.5f), "on an arc");
            flight.Step(10f + seconds + 0.01f);
            Assert.IsTrue(flight == null || flight.Landed, "landed and gone");
        }

        [Test]
        public void ExitFlight_NothingToDraw_IsNoFlight()
        {
            var empty = new GameObject("Empty");
            _objects.Add(empty);
            Assert.IsNull(ExitFlightLook.Begin(empty.transform, new Pose(Vector3.zero, Quaternion.identity), Vector3.one, null, 0f));
            Assert.IsEmpty(Object.FindObjectsByType<ExitFlightLook>(FindObjectsSortMode.None));
        }

        // ---- the scoop and the tip are predicted (036 feels off, 029b) ----

        [Test]
        public void ContentsPrediction_TipAndScoop_ShowAtOnce_UntilTheAnswerOrTheTimeout()
        {
            var fries = new FoodLook(1, 2, 3, 0, FoodLook.KindSingle, 0);
            var salad = new FoodLook(4, 0, 0, 0, FoodLook.KindSingle, 0);
            List<FoodLook> tipped = ContentsPrediction.Tipped(new[] { salad }, new[] { fries });
            CollectionAssert.AreEqual(new[] { salad, fries }, tipped, "the basket's food lands on the plate");
            Assert.AreEqual(FoodLook.MaxParts, ContentsPrediction.Tipped(new[] { salad, salad, salad }, new[] { fries, fries }).Count, "never more than a plate shows");
            CollectionAssert.AreEqual(new[] { salad }, ContentsPrediction.Scooped(new[] { salad, fries }), "the last portion leaves the tray");
            Assert.IsEmpty(ContentsPrediction.Scooped(new FoodLook[0]));

            var synced = new List<FoodLook> { salad };
            var p = new ContentsPrediction();
            Assert.AreSame(synced, p.Shown(synced));
            p.Begin(tipped, 1f);
            CollectionAssert.AreEqual(tipped, p.Shown(synced));
            Assert.IsFalse(p.Expired(0.5f));
            Assert.IsTrue(p.SyncedChanged(), "the server answered");
            Assert.AreSame(synced, p.Shown(synced));
            Assert.IsFalse(p.SyncedChanged());

            p.Begin(tipped, 1f);
            Assert.IsTrue(p.Expired(1f), "no answer in time: the real contents again");
            Assert.AreSame(synced, p.Shown(synced));
        }

        // ---- third-person carry and wipe poses (034, 035 feels off) ----

        [Test]
        public void ArmPose_BothHandsOnTheBoxSides_AndTheReachPointsTheHandAtTheTarget()
        {
            ArmPoseRules.BoxHands(new Vector3(2f, 0f, 3f), 90f, out Vector3 left, out Vector3 right);
            Assert.AreEqual(HoldViewRules.BoxCarryHeight, left.y, 1e-5f);
            Assert.AreEqual(HoldViewRules.BoxCarryHeight, right.y, 1e-5f);
            Assert.AreEqual(2f * ArmPoseRules.BoxHalfWidth, Vector3.Distance(left, right), 1e-4f);
            Vector3 middle = (left + right) * 0.5f;
            Assert.AreEqual(2f + HoldViewRules.BoxCarryForward, middle.x, 1e-4f, "in front of a body facing +x");
            Assert.Greater(left.z, right.z, "facing +x, the left hand is on the +z side");

            Vector3 shoulder = new Vector3(0f, 1.4f, 0f), hand = new Vector3(0f, 0.8f, 0.05f), target = new Vector3(0.3f, 1.0f, 0.5f);
            Quaternion full = ArmPoseRules.Reach(shoulder, hand, target, 1f);
            Vector3 turned = full * (hand - shoulder);
            Assert.Less(Vector3.Angle(turned, target - shoulder), 0.1f, "the hand points at the target");
            Assert.AreEqual(Quaternion.identity, ArmPoseRules.Reach(shoulder, hand, target, 0f));
            Assert.AreEqual(Quaternion.identity, ArmPoseRules.Reach(shoulder, shoulder, target, 1f), "degenerate: as animated");
            float half = Vector3.Angle(ArmPoseRules.Reach(shoulder, hand, target, 0.5f) * (hand - shoulder), hand - shoulder);
            Assert.AreEqual(Vector3.Angle(hand - shoulder, target - shoulder) * 0.5f, half, 0.1f, "the weight fades it in");
        }

        // ---- the HUD (027b feels off, 042 known limit) ----

        [Test]
        public void Hud_ReadsTheAimAfterTheInteractor_AndARefusedTapPressNamesTheTray()
        {
            var order = (DefaultExecutionOrder)System.Attribute.GetCustomAttribute(typeof(Worktown.Game.UI.HudView), typeof(DefaultExecutionOrder));
            Assert.IsNotNull(order, "the prompt is read after the aim of the same frame");
            Assert.Greater(order.order, 0);
            Assert.IsNull(System.Attribute.GetCustomAttribute(typeof(Worktown.Game.Net.PickupInteractor), typeof(DefaultExecutionOrder)));
            var cookText = typeof(Worktown.Game.UI.HudView).GetMethod("CookText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(cookText);
            StringAssert.Contains("drip tray", (string)cookText.Invoke(null, new object[] { CookOutcome.NeedsCup }));
        }

        [Test]
        public void Outline_OnABigStation_IsAboutAsWideAsOnAPan()
        {
            float station = 0.9f; // a stove's bounding radius
            float width = station * InteractionHighlight.OutlineScaleFor(station);
            Assert.That(width, Is.InRange(InteractionHighlight.OutlineWidth * 0.9f, InteractionHighlight.OutlineWidth * 1.1f), "was 2.7 cm");
            Assert.LessOrEqual(2f * InteractionHighlight.OutlineMinScale, 0.02f, "even a 2 m counter stays under 2 cm");
        }

        // ---- eat-in guests leave their plate (035 feels off) ----

        [Test]
        public void EatInGuest_LeavesThePlate_AfterPaying_TakeAwayKeepsItsParcel()
        {
            byte fries = Menu.Fries;
            Assert.IsTrue(HandoffRules.IsPlateDish(fries));
            Assert.IsTrue(HandoffRules.LeavesPlate(CustomerRules.MoodPaying, CustomerRules.MoodHappy, true, fries));
            Assert.IsTrue(HandoffRules.LeavesPlate(CustomerRules.MoodPaying, CustomerRules.MoodUnhappy, true, fries));
            Assert.IsFalse(HandoffRules.LeavesPlate(CustomerRules.MoodPaying, CustomerRules.MoodHappy, false, fries), "take-away keeps its bag");
            Assert.IsFalse(HandoffRules.LeavesPlate(CustomerRules.MoodPaying, CustomerRules.MoodHappy, true, Menu.SeitanWrap), "a wrap is held in its paper");
            Assert.IsFalse(HandoffRules.LeavesPlate(CustomerRules.MoodWaiting, CustomerRules.MoodPaying, true, fries), "taking it is no leaving");
            Assert.IsFalse(HandoffRules.LeavesPlate(CustomerRules.MoodPaying, CustomerRules.MoodWaiting, true, fries), "a pooled guest starts over");
        }
    }
}
