using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 020 AC5: the server's place check (pure part, PlaceSpots.CheckPlace and the spot table PickupService
    /// uses): a valid place marks the spot; a taken spot, a bad index, an out-of-reach spot and empty hands are
    /// refused; picking the item up frees the spot. The networked part (moving the item) is checked in play.
    /// </summary>
    public class PlaceServiceTests
    {
        private const int Surface = 2;
        private const int SpotCount = 8;
        private const int Knife = 40;
        private const int Potato = 41;

        private static PickupOutcome Check(PlaceSpots spots, bool holding = true, bool surfaceExists = true, int spot = 3, bool inReach = true)
        {
            return spots.CheckPlace(holding, surfaceExists, SpotCount, Surface, spot, inReach);
        }

        /// <summary>What PickupService.TryPlace does with an accepted check.</summary>
        private static PickupOutcome Place(PlaceSpots spots, int objectId, int spot)
        {
            PickupOutcome outcome = Check(spots, spot: spot);
            if (outcome == PickupOutcome.Ok) Assert.IsTrue(spots.TryTake(Surface, spot, objectId));
            return outcome;
        }

        [Test]
        public void ValidPlace_MarksTheSpot()
        {
            var spots = new PlaceSpots();
            Assert.AreEqual(PickupOutcome.Ok, Place(spots, Knife, 3));
            Assert.IsTrue(spots.IsTaken(Surface, 3));
            Assert.IsTrue(spots.TryGetOccupant(Surface, 3, out int occupant));
            Assert.AreEqual(Knife, occupant);
        }

        [Test]
        public void TakenSpot_IsRefused()
        {
            var spots = new PlaceSpots();
            Place(spots, Knife, 3);
            Assert.AreEqual(PickupOutcome.Taken, Place(spots, Potato, 3));
            Assert.AreEqual(PickupOutcome.Ok, Place(spots, Potato, 4), "the spot next to it is free");
        }

        [Test]
        public void BadSpotIndex_IsRefused()
        {
            var spots = new PlaceSpots();
            Assert.AreEqual(PickupOutcome.NotFound, Check(spots, spot: -1));
            Assert.AreEqual(PickupOutcome.NotFound, Check(spots, spot: SpotCount));
            Assert.AreEqual(PickupOutcome.NotFound, Check(spots, spot: int.MaxValue));
        }

        [Test]
        public void UnknownSurface_IsRefused()
        {
            Assert.AreEqual(PickupOutcome.NotFound, Check(new PlaceSpots(), surfaceExists: false));
        }

        [Test]
        public void OutOfReach_IsRefused()
        {
            Assert.AreEqual(PickupOutcome.TooFar, Check(new PlaceSpots(), inReach: false));
        }

        [Test]
        public void EmptyHands_AreRefusedFirst()
        {
            var spots = new PlaceSpots();
            Assert.AreEqual(PickupOutcome.NothingHeld, Check(spots, holding: false));
            Assert.AreEqual(PickupOutcome.NothingHeld, spots.CheckPlace(false, false, 0, -1, -1, false));
        }

        [Test]
        public void Pickup_FreesTheSpot()
        {
            var spots = new PlaceSpots();
            Place(spots, Knife, 3);
            Assert.IsTrue(spots.Free(Knife)); // PickupService.TryPickup frees the picked-up object's spot
            Assert.IsFalse(spots.IsTaken(Surface, 3));
            Assert.AreEqual(PickupOutcome.Ok, Place(spots, Potato, 3));
        }

        /// <summary>
        /// 041 review: the server's whole place step (PickupService.PlanPlace, which TryPlace runs before it moves anything): the packed
        /// wheel turn reaches the rest pose, and a step out of range is refused before any check or move.
        /// </summary>
        [Test]
        public void TryPlace_TurnTwo_TurnsTheRestNinetyDegrees_StepEight_IsRefusedWithNothingMoved()
        {
            var go = new GameObject("Worktop under test");
            try
            {
                go.transform.SetPositionAndRotation(new Vector3(2f, 0f, 3f), Quaternion.Euler(0f, 30f, 0f));
                var surface = go.AddComponent<PlaceSurface>();
                var so = new UnityEditor.SerializedObject(surface);
                so.FindProperty("_index").intValue = Surface;
                so.FindProperty("_size").vector2Value = new Vector2(1f, 0.6f);
                so.FindProperty("_topY").floatValue = 0.9f;
                so.ApplyModifiedPropertiesWithoutUndo();
                Assert.Greater(surface.SpotCount, 1);
                var spots = new PlaceSpots();
                Vector3 player = surface.SpotWorldPosition(1) + go.transform.rotation * new Vector3(0f, -0.9f, -0.8f);
                Quaternion tilt = Quaternion.Euler(90f, 0f, 0f); // a knife lies flat
                int probes = 0;
                bool Occupied(Vector3 at)
                {
                    probes++;
                    return false;
                }

                Assert.AreEqual(PickupOutcome.Ok, PickupService.PlanPlace(spots, true, Surface, surface, PlaceTurn.Pack(1, 0), player, false, tilt,
                    0.02f, Occupied, out int plain, out Pose rest));
                Assert.AreEqual(1, plain);
                Assert.AreEqual(PickupOutcome.Ok, PickupService.PlanPlace(spots, true, Surface, surface, PlaceTurn.Pack(1, 2), player, false, tilt,
                    0.02f, Occupied, out int turnedSpot, out Pose turned));
                Assert.AreEqual(1, turnedSpot, "the turn rides above the spot, the spot is the same");
                Assert.AreEqual(Vector3.Distance(rest.position, turned.position), 0f, 1e-5f, "it lands on the same spot");
                Assert.AreEqual(0f, Vector3.Distance(surface.SpotWorldPosition(1) + go.transform.up * 0.02f, rest.position), 1e-5f, "its bottom on the top");
                Vector3 a = rest.rotation * Vector3.up, b = turned.rotation * Vector3.up; // the knife's long side, lying flat
                Assert.AreEqual(90f, Vector3.SignedAngle(Vector3.ProjectOnPlane(a, Vector3.up), Vector3.ProjectOnPlane(b, Vector3.up), Vector3.up), 0.01f,
                    "two wheel steps turn the rest 90 degrees about the vertical");
                Assert.AreEqual(Vector3.Angle(a, Vector3.up), Vector3.Angle(b, Vector3.up), 0.01f, "the tilt stays");

                int outOfRange = 1 | (PlaceTurn.Steps << PlaceTurn.SpotBits);
                probes = 0;
                Assert.AreEqual(PickupOutcome.NotFound, PickupService.PlanPlace(spots, true, Surface, surface, outOfRange, player, false, tilt,
                    0.02f, Occupied, out int refused, out Pose none));
                Assert.AreEqual(-1, refused);
                Assert.AreEqual(default(Pose), none, "no rest pose: nothing to move it to");
                Assert.AreEqual(0, probes, "refused before the spot is even looked at");
                Assert.IsFalse(spots.IsTaken(Surface, 1), "no spot taken");
                Assert.AreEqual(PickupOutcome.NothingHeld, PickupService.PlanPlace(spots, false, Surface, surface, PlaceTurn.Pack(1, 2), player,
                    false, tilt, 0.02f, Occupied, out _, out _), "the other checks still run in their order");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ReachIsTheSameRuleAsPickups()
        {
            Vector3 feet = Vector3.zero;
            Assert.IsTrue(PickupRules.IsInReach(feet, new Vector3(0f, 1f, 2.2f)), "a spot on a worktop in front of you");
            Assert.IsFalse(PickupRules.IsInReach(feet, new Vector3(0f, 1f, 2.4f)));
        }
    }
}
