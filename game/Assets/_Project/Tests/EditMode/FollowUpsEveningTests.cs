using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Worktown.Game.Art;
using Worktown.Game.Climb;
using Worktown.Game.Cooking;
using Worktown.Game.Economy;
using Worktown.Game.Player;
using Worktown.Game.Settings;
using Worktown.Game.UI;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Follow-ups (2026-11-11 evening) of specs 053 (closed-station covers, the money lines), 044 (the two-bone arm bend) and 048 (the
    /// gamepad capture over a pad state).
    /// </summary>
    public class FollowUpsEveningTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/Graybox.unity";

        // ---- 053: the closed-station covers ----

        [Test]
        public void CoverRules_TheClothCoversTheStationAndWhatRestsOnIt_TheSignIsSmall()
        {
            var station = new Bounds(new Vector3(0f, 0.45f, 0f), new Vector3(0.6f, 0.9f, 0.6f));
            var pan = new Bounds(new Vector3(0.2f, 0.95f, 0.05f), new Vector3(0.5f, 0.1f, 0.3f)); // its handle sticks out at x 0.45
            var floorBox = new Bounds(new Vector3(0.1f, 0.1f, 0.1f), new Vector3(0.2f, 0.2f, 0.2f));
            var hood = new Bounds(new Vector3(0f, 2.2f, 0f), new Vector3(0.6f, 0.4f, 0.6f));
            var beside = new Bounds(new Vector3(0.8f, 0.95f, 0f), new Vector3(0.2f, 0.1f, 0.2f));
            Assert.IsTrue(ClosedCoverRules.RestsOn(station, pan));
            Assert.IsFalse(ClosedCoverRules.RestsOn(station, floorBox), "on the floor, not on the station");
            Assert.IsFalse(ClosedCoverRules.RestsOn(station, hood), "far above");
            Assert.IsFalse(ClosedCoverRules.RestsOn(station, beside), "beside it");

            Bounds cloth = ClosedCoverRules.Cloth(station, pan, true);
            Assert.GreaterOrEqual(cloth.max.y, pan.max.y + ClosedCoverRules.Clearance - 1e-5f, "nothing pokes through the top");
            Assert.GreaterOrEqual(cloth.max.x, pan.max.x + ClosedCoverRules.Margin - 1e-5f, "the handle is under the cloth");
            Assert.LessOrEqual(cloth.min.x, station.min.x - ClosedCoverRules.Margin + 1e-5f);
            Assert.AreEqual(station.max.y - ClosedCoverRules.Skirt, cloth.min.y, 1e-5f, "a short skirt below the top");
            Bounds bare = ClosedCoverRules.Cloth(station, default, false);
            Assert.AreEqual(station.max.y + ClosedCoverRules.Clearance, bare.max.y, 1e-5f);

            Assert.AreEqual(1f, ClosedCoverRules.SignScale(1.2f));
            Assert.AreEqual((0.3f - 2f * ClosedCoverRules.Margin) / ClosedCoverRules.SignSize.x, ClosedCoverRules.SignScale(0.3f), 1e-5f);
            Assert.AreEqual(0f, ClosedCoverRules.SignScale(0.01f));
            Assert.LessOrEqual(ClosedCoverRules.SignSize.x, 0.45f, "a small sign");
            Vector3 sign = ClosedCoverRules.SignCentre(cloth, Vector3.back, 1f);
            Assert.AreEqual(cloth.min.z - ClosedCoverRules.SignProud, sign.z, 1e-5f, "on the front face");
            Assert.Less(sign.y + ClosedCoverRules.SignSize.y * 0.5f, cloth.max.y);
            Assert.Greater(sign.y - ClosedCoverRules.SignSize.y * 0.5f, cloth.min.y);
        }

        [Test]
        public void BuiltScene_ClosedCovers_NothingPokesThrough_SageClothAndASmallSign()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var renderers = new List<Renderer>();
                var covers = new List<ClosedCover>();
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    renderers.AddRange(root.GetComponentsInChildren<Renderer>());
                    covers.AddRange(root.GetComponentsInChildren<ClosedCover>(true));
                }
                Assert.IsNotEmpty(covers);
                int withContents = 0;
                foreach (ClosedCover cover in covers)
                {
                    Station station = cover.GetComponentInParent<Station>();
                    Assert.IsNotNull(station, cover.name);
                    Assert.IsTrue(ClosedCover.TryStationBounds(station, out Bounds b), station.name);
                    Transform clothT = cover.transform.Find("Cloth and sign/Cloth");
                    Assert.IsNotNull(clothT, station.name + " has no cloth");
                    Renderer clothR = clothT.GetComponent<Renderer>();
                    Bounds cloth = clothR.bounds;
                    Assert.Less(Vector4.Distance(Palette.Sage, clothR.sharedMaterial.color), 0.01f, station.name + ": a Sage cloth");
                    Assert.GreaterOrEqual(cloth.max.y, b.max.y, station.name + ": the station's top is under the cloth");
                    Assert.Less(cloth.min.y, b.max.y, station.name);
                    if (ClosedCover.TryRestingBounds(station, b, renderers, out Bounds on))
                    {
                        withContents++;
                        const float e = 1e-3f;
                        Assert.GreaterOrEqual(cloth.max.y + e, on.max.y, station.name + ": something pokes through the top");
                        Assert.GreaterOrEqual(cloth.max.x + e, on.max.x, station.name + ": something pokes out (+x)");
                        Assert.LessOrEqual(cloth.min.x - e, on.min.x, station.name + ": something pokes out (-x)");
                        Assert.GreaterOrEqual(cloth.max.z + e, on.max.z, station.name + ": something pokes out (+z)");
                        Assert.LessOrEqual(cloth.min.z - e, on.min.z, station.name + ": something pokes out (-z)");
                    }
                    Transform card = cover.transform.Find("Cloth and sign/Sign card");
                    if (card == null) continue; // too narrow for a sign
                    Assert.LessOrEqual(Mathf.Max(card.lossyScale.x, card.lossyScale.z), ClosedCoverRules.SignSize.x + 1e-3f, station.name + ": the sign is small");
                    TextMesh text = cover.transform.Find("Cloth and sign/Sign").GetComponent<TextMesh>();
                    Assert.LessOrEqual(text.characterSize, ClosedCoverRules.SignCharacterSize + 1e-6f, station.name);
                    Assert.AreEqual(ShopStageRules.NotOpenText, text.text);
                    Assert.Less(Vector4.Distance(Palette.Cream, card.GetComponent<Renderer>().sharedMaterial.color), 0.01f, station.name + ": a Cream card");
                }
                Assert.Greater(withContents, 0, "a stove's pan rests under a cover");
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        // ---- 053: the money lines ----

        [Test]
        public void Money_TheShopBalanceNeverGoesBelowZero_AndANegativeAmountWouldShowAsNegative()
        {
            TownEconomy economy = TownEconomy.CreateDemo();
            Assert.IsTrue(economy.TryOpenAccount(EconomyRules.ShopAccount, 100));
            Assert.AreEqual(100, economy.PayFee(EconomyRules.ShopAccount, 575), "the fee takes at most the balance");
            Assert.AreEqual(0, economy.BalanceOf(EconomyRules.ShopAccount), "never below 0, so the display needs no clamp");
            Assert.AreEqual(-1, economy.BalanceOf("nobody"), "-1 is only 'no account' (never published)");

            Assert.AreEqual("-$120", EconomyRules.Money(-120));
            Assert.AreEqual("Save for the rent: -$120 / $400", FoodCornerRules.GoalLine(-120, 400), "no clamp to $0");
            Assert.AreEqual("Day 2 · -$120", MenuRules.ContinueLine(2, -120), "the saved balance as it is");
            Assert.AreEqual("Day 2 · $3,243", MenuRules.ContinueLine(2, 3243));
        }

        [Test]
        public void TheDelivery_ShowsOnTheHudAsMoneyLeaving_AndPassesTheD27WordCheck()
        {
            Assert.AreEqual("Potatoes delivered -$96 (2 on the shelf)", FoodCornerRules.DeliveryText(2, 96));
            string none = FoodCornerRules.DeliveryText(0, 0);
            StringAssert.StartsWith("No potatoes delivered", none);
            foreach (string text in new[] { FoodCornerRules.DeliveryText(2, 96), none })
                Assert.IsNull(ContentRules.FindForbidden(text), text);
        }

        // ---- 044: the two-bone arm bend ----

        private static readonly Vector3 Shoulder = new Vector3(0f, 1.4f, 0f);
        private const float UpperLength = 0.3f, LowerLength = 0.28f;

        [Test]
        public void Elbow_MakesATriangleWithTheTarget_BentTowardsTheHint_StraightOutOfReach()
        {
            Vector3 target = new Vector3(0.2f, 1.15f, 0.3f);
            Vector3 hint = Vector3.down;
            Vector3 elbow = ArmPoseRules.Elbow(Shoulder, target, UpperLength, LowerLength, hint);
            Assert.AreEqual(UpperLength, Vector3.Distance(Shoulder, elbow), 1e-4f, "the upper arm keeps its length");
            Assert.AreEqual(LowerLength, Vector3.Distance(elbow, target), 1e-4f, "the forearm keeps its length and the hand is on the target");
            Vector3 middle = Vector3.Lerp(Shoulder, target, 0.5f);
            Assert.Greater(Vector3.Dot(elbow - middle, hint), 0.05f, "the elbow bends towards the hint");

            Vector3 far = Shoulder + new Vector3(0f, 0f, 2f);
            Vector3 straight = ArmPoseRules.Elbow(Shoulder, far, UpperLength, LowerLength, hint);
            Assert.AreEqual(0f, Vector3.Distance(straight, Shoulder + Vector3.forward * UpperLength), 1e-4f, "out of reach: straight at it");

            Vector3 degenerate = ArmPoseRules.Elbow(Shoulder, Shoulder, UpperLength, LowerLength, hint);
            Assert.AreEqual(0f, Vector3.Distance(degenerate, Shoulder + Vector3.down * UpperLength), 1e-4f);
            Vector3 left = ArmPoseRules.BendHint(0f, true), right = ArmPoseRules.BendHint(0f, false);
            Assert.Less(left.x, 0f, "the left elbow leans out to the left");
            Assert.Greater(right.x, 0f);
            Assert.Less(left.y, 0f);
            Assert.Less(left.z, 0f, "and a little back");
        }

        [Test]
        public void TwoBone_TheHandReachesTheTarget_WithABentElbow_WeightZeroIsTheAnimation()
        {
            Vector3 elbow = Shoulder + Vector3.down * UpperLength, hand = elbow + Vector3.down * LowerLength; // a straight hanging arm
            Vector3 target = new Vector3(0.17f, 1.1f, 0.35f);
            Assert.IsTrue(ArmPoseRules.TwoBone(Shoulder, elbow, hand, target, ArmPoseRules.BendHint(0f, false), 1f, out Quaternion up, out Quaternion low));
            Vector3 elbowAfter = Shoulder + up * (elbow - Shoulder);
            Vector3 handAfter = elbowAfter + low * (up * (hand - elbow));
            Assert.AreEqual(0f, Vector3.Distance(handAfter, target), 1e-3f, "the hand is on the target");
            Assert.Greater(Vector3.Angle(elbowAfter - Shoulder, handAfter - elbowAfter), 20f, "the elbow bends");

            Assert.IsFalse(ArmPoseRules.TwoBone(Shoulder, elbow, hand, target, Vector3.down, 0f, out up, out low));
            Assert.AreEqual(Quaternion.identity, up);
            Assert.AreEqual(Quaternion.identity, low);
            Assert.IsFalse(ArmPoseRules.TwoBone(Shoulder, elbow, hand, target, Vector3.down, float.NaN, out _, out _));
            Assert.IsFalse(ArmPoseRules.TwoBone(Shoulder, Shoulder, hand, target, Vector3.down, 1f, out _, out _), "degenerate: as animated");
        }

        [Test]
        public void BendArm_OnRealBones_ReachesTheTarget_WithNoAllocations()
        {
            var shoulder = new GameObject("Shoulder");
            try
            {
                shoulder.transform.position = Shoulder;
                var elbow = new GameObject("Elbow").transform;
                elbow.SetParent(shoulder.transform, false);
                elbow.localPosition = Vector3.down * UpperLength;
                var hand = new GameObject("Hand").transform;
                hand.SetParent(elbow, false);
                hand.localPosition = Vector3.down * LowerLength;
                Vector3 target = new Vector3(-0.15f, 1.05f, 0.32f);
                Vector3 hint = ArmPoseRules.BendHint(0f, true);

                CharacterVisual.BendArm(shoulder.transform, elbow, hand, target, hint, 1f);
                Assert.AreEqual(0f, Vector3.Distance(hand.position, target), 1e-3f, "the hand is on the target");
                Assert.AreEqual(LowerLength, Vector3.Distance(elbow.position, hand.position), 1e-4f, "the bones keep their lengths");

                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 500; i++)
                {
                    shoulder.transform.rotation = Quaternion.identity; // the animation's pose again, as every frame
                    elbow.localRotation = Quaternion.identity;
                    CharacterVisual.BendArm(shoulder.transform, elbow, hand, target, hint, 0.5f + (i % 2) * 0.5f);
                }
                Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before, "no per-frame allocations");
            }
            finally { UnityEngine.Object.DestroyImmediate(shoulder); }
        }

        // ---- 048: the gamepad capture over a pad state ----

        [Test]
        public void PadCapture_WaitsForTheClickingAToBeReleased_ThenTakesTheFirstPressedButton()
        {
            uint a = CaptureGate.PadBit(PadButton.South), y = CaptureGate.PadBit(PadButton.North);
            Assert.AreEqual(0u, CaptureGate.PadBit(PadButton.None));
            Assert.AreNotEqual(a, y);
            bool released = false;
            Assert.AreEqual(PadButton.None, CaptureGate.PadCapture(10, 10, true, a, a, ref released), "not on the frame it began");
            Assert.AreEqual(PadButton.None, CaptureGate.PadCapture(10, 11, true, a, a, ref released), "the clicking A is still held");
            Assert.IsFalse(released);
            Assert.AreEqual(PadButton.None, CaptureGate.PadCapture(10, 12, true, 0u, 0u, ref released));
            Assert.IsTrue(released, "every button was up once");
            Assert.AreEqual(PadButton.North, CaptureGate.PadCapture(10, 13, true, y, y, ref released));
            Assert.AreEqual(PadButton.South, CaptureGate.PadCapture(10, 14, true, a | y, a | y, ref released), "the first in PadButton order");
            bool none = true;
            Assert.AreEqual(PadButton.None, CaptureGate.PadCapture(10, 15, false, y, y, ref none), "no pad: keep waiting");

            // What the menu tour's capture then does: Y binds Jump and Turn (Y's action) swaps onto A.
            ControlBindings bindings = ControlBindings.Defaults();
            RebindResult result = bindings.Rebind(ControlAction.Jump, PadButton.North);
            Assert.IsTrue(result.Changed);
            Assert.AreEqual(PadButton.North, bindings.PadOf(ControlAction.Jump));
            Assert.AreEqual(PadButton.South, bindings.PadOf(ControlAction.Turn));
        }
    }
}
