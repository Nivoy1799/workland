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

        [Test]
        public void ReachIsTheSameRuleAsPickups()
        {
            Vector3 feet = Vector3.zero;
            Assert.IsTrue(PickupRules.IsInReach(feet, new Vector3(0f, 1f, 2.2f)), "a spot on a worktop in front of you");
            Assert.IsFalse(PickupRules.IsInReach(feet, new Vector3(0f, 1f, 2.4f)));
        }
    }
}
