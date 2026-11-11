using NUnit.Framework;
using UnityEngine;
using Worktown.Game.Cooking;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;
using Worktown.Game.Player;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// The 042 review follow-ups (folded into the spec 043 round): the rest-prediction flag, the one lid-target rule, the busy-cup
    /// option, an empty-handed look at a cup spot, and the server's pick-up veto.
    /// </summary>
    public class CupTrayReviewTests
    {
        private static readonly Pose Somewhere = new Pose(new Vector3(1f, 0.9f, 2f), Quaternion.identity);

        [Test]
        public void HasRestPrediction_OnlyWhilePredictingARest()
        {
            var look = new ItemLook();
            Assert.IsFalse(look.HasRestPrediction, "nothing predicted");
            look.PredictRest(Somewhere);
            Assert.IsTrue(look.HasRestPrediction);
            look.PredictHeld(3);
            Assert.IsFalse(look.HasRestPrediction, "a take is no rest");
            look.PredictInto(Somewhere);
            Assert.IsFalse(look.HasRestPrediction, "into a vessel is no rest");
            look.PredictRest(Somewhere);
            look.EndPrediction(false, 0f);
            Assert.IsFalse(look.HasRestPrediction, "a refused rest ends");
            look.PredictRest(Somewhere);
            look.EndPrediction(true, 0f);
            Assert.IsFalse(look.HasRestPrediction, "a confirmed rest ends too");
        }

        private static byte Open(int tap, byte level) => DrinkRules.SpotByte(true, false, tap, level);

        [Test]
        public void LiddableTap_TheFirstFilledOpenCupNobodyPoursInto()
        {
            byte empty = DrinkRules.SpotByte(true, false, -1, DrinkRules.LevelEmpty);
            byte lidded = DrinkRules.SpotByte(true, true, 1, DrinkRules.LevelRight);
            byte[] spots = { DrinkRules.NoSpotCup, Open(1, DrinkRules.LevelRight), Open(2, DrinkRules.LevelLow) };
            Assert.AreEqual(1, DrinkRules.LiddableTap(t => spots[t], t => false));
            Assert.AreEqual(2, DrinkRules.LiddableTap(t => spots[t], t => t == 1), "tap 1 pours: the next one");
            Assert.AreEqual(-1, DrinkRules.LiddableTap(t => spots[t], t => t != 0), "all filled cups are poured into");
            byte[] none = { empty, lidded, DrinkRules.NoSpotCup };
            Assert.AreEqual(-1, DrinkRules.LiddableTap(t => none[t], t => false), "an empty cup and a lidded one take no lid");
            Assert.AreEqual(-1, DrinkRules.LiddableTap(null, null));
            Assert.AreEqual(0, DrinkRules.LiddableTap(t => Open(t, DrinkRules.LevelFull), null), "no pour info: nobody pours");
        }

        [Test]
        public void TakeableTap_ThirdPersonETakesTheCupOnASpot_LiddedFirst_ThenTheNearest()
        {
            // Follow-up 2026-11-11: E with empty hands takes an empty cup back off the tray (it took a new cup from the stack before).
            byte empty = DrinkRules.SpotByte(true, false, -1, DrinkRules.LevelEmpty);
            byte lidded = DrinkRules.SpotByte(true, true, 1, DrinkRules.LevelRight);
            byte[] oneEmpty = { DrinkRules.NoSpotCup, empty, DrinkRules.NoSpotCup };
            Assert.AreEqual(1, DrinkRules.TakeableTap(t => oneEmpty[t], t => false), "the empty cup put back");
            Assert.AreEqual(-1, DrinkRules.TakeableTap(t => oneEmpty[t], t => t == 1), "another player pours into it: busy, as in first person");
            Assert.AreEqual(-1, DrinkRules.TakeableTap(t => DrinkRules.NoSpotCup, null), "no cup on any spot: the stack");
            Assert.AreEqual(-1, DrinkRules.TakeableTap(null, null));

            byte[] mixed = { empty, empty, lidded };
            float[] far = { 9f, 1f, 4f };
            Assert.AreEqual(2, DrinkRules.TakeableTap(t => mixed[t], null, t => far[t]), "a lidded cup first, even when further away");
            Assert.AreEqual(1, DrinkRules.TakeableTap(t => mixed[t], t => t == 2, t => far[t]), "then the nearest other cup");
            Assert.AreEqual(0, DrinkRules.TakeableTap(t => mixed[t], t => t == 2), "no distances: the tap order");
        }

        [Test]
        public void BusyCup_CarriesTheNamedOption()
        {
            AimTarget busy = AimTarget.BusyCup("cup of lemonade");
            Assert.IsTrue(busy.IsBusy);
            Assert.AreEqual(AimTarget.BusyOption, busy.Option);
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Nothing, busy));
            Assert.AreEqual(InteractionRules.BusyCupText, InteractionRules.Reason(Held.Nothing, busy));
        }

        [Test]
        public void CupSpot_WithEmptyHands_IsNoActionAndNoReason()
        {
            AimTarget spot = AimTarget.CupSpot(0, 1);
            Assert.AreEqual(InteractionAction.None, InteractionRules.ActionFor(Held.Nothing, spot));
            Assert.IsNull(InteractionRules.Reason(Held.Nothing, spot), "empty hands: nothing to explain (it was HandsFull before)");
        }

        [Test]
        public void PlanPickup_TheVetoSaysTaken_AndClaimsNothing()
        {
            const int cup = 50, client = 3;
            var registry = new PickupRegistry();
            Assert.AreEqual(PickupOutcome.Taken, PickupService.PlanPickup(registry, cup, client, true, false));
            Assert.AreEqual(PickupRegistry.NoHolder, registry.HolderOf(cup));
            Assert.IsFalse(registry.TryGetHeldItem(client, out _));

            Assert.AreEqual(PickupOutcome.TooFar, PickupService.PlanPickup(registry, cup, client, false, true));
            Assert.AreEqual(PickupRegistry.NoHolder, registry.HolderOf(cup));
            Assert.AreEqual(PickupOutcome.NotFound, PickupService.PlanPickup(null, cup, client, true, true));

            Assert.AreEqual(PickupOutcome.Ok, PickupService.PlanPickup(registry, cup, client, true, true));
            Assert.AreEqual(client, registry.HolderOf(cup));
            Assert.AreEqual(PickupOutcome.AlreadyHolding, PickupService.PlanPickup(registry, 51, client, true, true));
            Assert.AreEqual(PickupOutcome.Taken, PickupService.PlanPickup(registry, cup, 4, true, true), "another player holds it");
            Assert.AreEqual(client, registry.HolderOf(cup));
        }
    }
}
