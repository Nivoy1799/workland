using System.Collections.Generic;
using NUnit.Framework;
using Worktown.Game.Art;
using Worktown.Game.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// 044 follow-ups (2026-11-11): a refused tip or scoop goes back on the refusal of its own request (spec 045 request ids), not
    /// after the confirm timeout; the held cloth eases from the hand to the spill in CleaningArtRules.ClothBlendSeconds; an emptied box
    /// gets the 044 exit flight for everyone but the player who unpacked it.
    /// </summary>
    public class FeelPolish3FollowUpTests
    {
        [Test]
        public void ARefusalOfItsOwnRequest_EndsTheTipOrScoopLookAtOnce()
        {
            var fries = new FoodLook(1, 2, 3, 0, FoodLook.KindSingle, 0);
            var salad = new FoodLook(4, 0, 0, 0, FoodLook.KindSingle, 0);
            var synced = new List<FoodLook> { salad };
            var p = new ContentsPrediction();
            p.Begin(ContentsPrediction.Tipped(synced, new[] { fries }), 10f);
            Assert.IsTrue(p.IsActive);

            Assert.IsFalse(ContentsPrediction.RefusalEnds(0, 7, false), "not an answer to one of ours");
            Assert.IsFalse(ContentsPrediction.RefusalEnds(6, 7, false), "a refusal of an earlier request leaves this look");
            Assert.IsFalse(ContentsPrediction.RefusalEnds(7, 7, true), "a yes: the synced contents end it when they change");
            Assert.IsTrue(ContentsPrediction.RefusalEnds(7, 7, false), "the refusal of this very request");

            Assert.IsTrue(p.Refused(), "it ends now, long before the timeout");
            Assert.IsFalse(p.IsActive);
            Assert.AreSame(synced, p.Shown(synced), "the real contents again");
            Assert.IsFalse(p.Refused(), "only once");
            Assert.IsFalse(p.Expired(20f), "nothing left to run out");
        }

        [Test]
        public void TheHeldCloth_EasesToTheSpill_AndBack_InsteadOfJumping()
        {
            Assert.AreEqual(0.15f, CleaningArtRules.ClothBlendSeconds, 1e-6f);
            Assert.AreEqual(0f, CleaningArtRules.ClothShare(true, 0f, 0f), 1e-6f, "the wipe starts: still in the hand this frame");
            float early = CleaningArtRules.ClothShare(true, 0f, 0.03f);
            Assert.Greater(early, 0f);
            Assert.Less(early, 0.6f, "on its way, not there yet");
            Assert.Greater(CleaningArtRules.ClothShare(true, 0f, 0.1f), early, "it keeps moving");
            Assert.AreEqual(1f, CleaningArtRules.ClothShare(true, 0f, CleaningArtRules.ClothBlendSeconds), 1e-6f, "on the spill after 0.15 s");
            Assert.AreEqual(1f, CleaningArtRules.ClothShare(true, 0f, float.PositiveInfinity), 1e-6f, "long ago");
            Assert.AreEqual(0f, CleaningArtRules.ClothShare(false, 0f, float.PositiveInfinity), 1e-6f, "never wiped: in the hand");

            float mid = CleaningArtRules.ClothShare(true, 0f, 0.05f);
            Assert.AreEqual(mid, CleaningArtRules.ClothShare(false, mid, 0f), 1e-6f, "the wipe ends: back from where it was, no jump");
            Assert.Less(CleaningArtRules.ClothShare(false, mid, 0.05f), mid);
            Assert.AreEqual(0f, CleaningArtRules.ClothShare(false, mid, CleaningArtRules.ClothBlendSeconds), 1e-6f, "back in the hand");
        }

        [Test]
        public void AnEmptiedBox_FliesIntoTheSlotForOnlookers_NotForTheUnpacker()
        {
            // Pickup.OnStopClient: the server named the slot (SupplyService.Unpack -> TakeAway(.., exitTo, Unpack)); the unpacker hid its
            // box and drew its own tip into the slot (SupplyBox.HiddenForUnpack), which counts as its prediction.
            Assert.IsTrue(ExitFlightRules.ShowsFlight(hasExit: true, hasDrawn: true, predicted: false), "an onlooker sees it fly");
            Assert.IsFalse(ExitFlightRules.ShowsFlight(hasExit: true, hasDrawn: true, predicted: true), "the unpacker drew it already");
            Assert.IsFalse(ExitFlightRules.ShowsFlight(hasExit: false, hasDrawn: true, predicted: false), "no exit named: it just goes");
        }
    }
}
