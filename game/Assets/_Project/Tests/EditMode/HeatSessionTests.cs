using System.Collections.Generic;
using NUnit.Framework;
using Worktown.Game.Cooking;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 016 AC3 and AC4: pan and pot cook over time through KitchenState, only while switched on (on/off stove,
    /// changed by the human on 2026-11-07). Spec 021 AC4: keyed by the vessel's object id, cooking only while the vessel
    /// stands on a burner that is on; the hashes are the same as the station versions gave.
    /// </summary>
    public class HeatSessionTests
    {
        private const int FoodId = 42;
        private const int Pan = HeatTestKit.PanIndex;
        private const int Pot = HeatTestKit.PotIndex;
        private const int PanBurner = HeatTestKit.PanBurner;
        private const int PotBurner = HeatTestKit.PotBurner;
        private const uint Start = HeatTestKit.StartTick;
        private const double Rate = HeatTestKit.TickRate;

        private static ItemFactory NewFactory()
        {
            return new ItemFactory(new ItemCatalog(DemoIngredients.All), OperationCatalog.CreateDefault());
        }

        private static KitchenState KitchenWithSticks(out Item sticks)
        {
            var kitchen = KitchenState.CreateDemo();
            kitchen.TryCreateIngredient("potato", out Item potato);
            kitchen.Register(FoodId, potato);
            sticks = HeatTestKit.Cut(kitchen, FoodId, 3);
            return kitchen;
        }

        private static Dictionary<string, ParamValue> Heat(float? temp, float time)
        {
            var p = new Dictionary<string, ParamValue> { ["time"] = ParamValue.Number(time) };
            if (temp.HasValue) p["temp"] = ParamValue.Number(temp.Value);
            return p;
        }

        [Test]
        public void Fry750TicksSwitchedOn_MatchesTheSimDirectly()
        {
            KitchenState kitchen = KitchenWithSticks(out Item sticks);
            HeatTestKit.SwitchOn(kitchen, Pan, PanBurner);

            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutOnHeat(FoodId, Pan, VesselKind.Pan, Start));
            Assert.IsFalse(kitchen.TryGet(FoodId, out _), "the food is in the pan, not in the world");
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewHeatTake(Pan, Start + 750, Rate, out Item fries));

            Item expected = NewFactory().Apply("fry", new[] { sticks }, Heat(StoveSettings.PanTempC, 150f)).Item;
            Assert.AreEqual(expected.Hash, fries.Hash);
            Assert.AreEqual("crispy fried potato sticks", KitchenState.NameOf(fries));
            Assert.AreEqual(1, kitchen.HeatCount(Pan), "preview does not empty the pan");
            kitchen.ClearHeat(Pan);
            Assert.AreEqual(0, kitchen.HeatCount(Pan));
        }

        [Test]
        public void NewBurner_IsOff_AndFoodOnItDoesNotCook()
        {
            KitchenState kitchen = KitchenWithSticks(out Item sticks);
            Assert.IsFalse(kitchen.BurnerOf(PanBurner).IsOn);
            kitchen.SetVesselBurner(Pan, PanBurner, Start);
            kitchen.TryPutOnHeat(FoodId, Pan, VesselKind.Pan, Start);
            Assert.AreEqual(CookOutcome.TooShort, kitchen.TryPreviewHeatTake(Pan, Start + 9000, Rate, out Item back));
            Assert.AreEqual(sticks.Hash, back.Hash);
            Assert.AreEqual("potato sticks · raw", kitchen.DescribeHeat(Pan, Start + 9000, Rate));
        }

        [Test]
        public void SwitchingOffWithFoodInside_PausesCooking()
        {
            KitchenState kitchen = KitchenWithSticks(out Item sticks);
            kitchen.SetVesselBurner(Pan, PanBurner, Start);
            kitchen.TryPutOnHeat(FoodId, Pan, VesselKind.Pan, Start);                                               // off: nothing yet
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryUseBurner(PanBurner, StationRules.StoveSwitch, Start + 100));  // on
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryUseBurner(PanBurner, StationRules.StoveSwitch, Start + 500));  // off: 400 ticks
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryUseBurner(PanBurner, StationRules.StoveSwitch, Start + 2000)); // on
            // + 350 ticks = 750 ticks on in total = 150 sim s
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewHeatTake(Pan, Start + 2350, Rate, out Item fries));
            Item expected = NewFactory().Apply("fry", new[] { sticks }, Heat(StoveSettings.PanTempC, 150f)).Item;
            Assert.AreEqual(expected.Hash, fries.Hash);
        }

        [Test]
        public void TakenOutTooEarly_ComesBackUnchanged()
        {
            KitchenState kitchen = KitchenWithSticks(out Item sticks);
            HeatTestKit.SwitchOn(kitchen, Pan, PanBurner);
            kitchen.TryPutOnHeat(FoodId, Pan, VesselKind.Pan, Start);
            // 45 ticks = 9 sim s, below the fry minimum of 10 s
            Assert.AreEqual(CookOutcome.TooShort, kitchen.TryPreviewHeatTake(Pan, Start + 45, Rate, out Item back));
            Assert.AreEqual(sticks.Hash, back.Hash);
        }

        [Test]
        public void LongFry_IsClampedToTheMaximum()
        {
            KitchenState kitchen = KitchenWithSticks(out Item sticks);
            HeatTestKit.SwitchOn(kitchen, Pan, PanBurner);
            kitchen.TryPutOnHeat(FoodId, Pan, VesselKind.Pan, Start);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewHeatTake(Pan, Start + 9000, Rate, out Item burnt));
            Item expected = NewFactory().Apply("fry", new[] { sticks }, Heat(StoveSettings.PanTempC, 600f)).Item;
            Assert.AreEqual(expected.Hash, burnt.Hash);
        }

        [Test]
        public void BusyPan_RefusesASecondItem_AndItsBurnersModeChange_ButNotTheSwitch()
        {
            KitchenState kitchen = KitchenWithSticks(out _);
            kitchen.TryCreateIngredient("tomato", out Item tomato);
            kitchen.Register(7, tomato);
            kitchen.SetVesselBurner(Pan, PanBurner, Start);
            kitchen.TryPutOnHeat(FoodId, Pan, VesselKind.Pan, Start);

            Assert.AreEqual(CookOutcome.VesselFull, kitchen.TryPutOnHeat(7, Pan, VesselKind.Pan, Start), "a pan takes one food");
            Assert.IsTrue(kitchen.TryGet(7, out _), "the refused tomato stays in the hands");
            Assert.AreEqual(CookOutcome.PanBusy, kitchen.TryUseBurner(PanBurner, StationRules.PanMode, Start));
            Assert.IsFalse(kitchen.BurnerOf(PanBurner).Saute);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryUseBurner(PanBurner, StationRules.StoveSwitch, Start));
            Assert.IsTrue(kitchen.BurnerOf(PanBurner).IsOn);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryUseBurner(PotBurner, StationRules.PanMode, Start), "another burner is free");
        }

        [Test]
        public void Buttons_AreCheckedPerBurner()
        {
            var kitchen = KitchenState.CreateDemo();
            Assert.AreEqual(CookOutcome.InvalidOption, kitchen.TryUseBurner(PanBurner, 3, Start));
            Assert.AreEqual(CookOutcome.InvalidOption, kitchen.TryUseBurner(PanBurner, 0, Start));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryUseBurner(PotBurner, StationRules.StoveSwitch, Start));
            Assert.AreEqual("on · fry", kitchen.BurnerOf(PotBurner).Describe());
            Assert.AreEqual("off · fry", kitchen.BurnerOf(PanBurner).Describe());
        }

        [Test]
        public void SauteBurner_GivesASauteResult()
        {
            KitchenState kitchen = KitchenWithSticks(out Item sticks);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryUseBurner(PanBurner, StationRules.PanMode, Start));
            HeatTestKit.SwitchOn(kitchen, Pan, PanBurner);
            Assert.AreEqual("on · saute", kitchen.BurnerOf(PanBurner).Describe());
            Assert.IsTrue(kitchen.PanIsSaute(Pan), "an empty pan shows its burner's mode");
            kitchen.TryPutOnHeat(FoodId, Pan, VesselKind.Pan, Start);
            kitchen.TryPreviewHeatTake(Pan, Start + 750, Rate, out Item sauteed);
            Item expected = NewFactory().Apply("saute", new[] { sticks }, Heat(StoveSettings.PanTempC, 150f)).Item;
            Assert.AreEqual(expected.Hash, sauteed.Hash);
        }

        [Test]
        public void Pot_Boil300_MatchesTheSimDirectly()
        {
            var kitchen = KitchenState.CreateDemo();
            kitchen.TryCreateIngredient("potato", out Item potato);
            kitchen.Register(FoodId, potato);
            Item boiled = HeatTestKit.Boil(kitchen, FoodId, 300);
            Item expected = NewFactory().Apply("boil", new[] { potato }, Heat(null, 300f)).Item;
            Assert.AreEqual(expected.Hash, boiled.Hash);
        }

        [Test]
        public void PlatedFood_IsRefusedWhileStillInHand()
        {
            KitchenState kitchen = KitchenWithSticks(out _);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPlate(FoodId, out Item plated));
            kitchen.Register(8, plated);
            Assert.AreEqual(CookOutcome.AlreadyPlated, kitchen.TryPutOnHeat(8, Pan, VesselKind.Pan, Start));
            Assert.AreEqual(CookOutcome.AlreadyPlated, kitchen.TryPutOnHeat(8, Pot, VesselKind.Pot, Start));
            Assert.IsTrue(kitchen.TryGet(8, out _));
            Assert.AreEqual(0, kitchen.HeatCount(Pan));
        }

        [Test]
        public void EmptyPan_TakeIsStationEmpty_AndLabelIsNull()
        {
            var kitchen = KitchenState.CreateDemo();
            Assert.AreEqual(CookOutcome.StationEmpty, kitchen.TryPreviewHeatTake(Pan, Start, Rate, out _));
            Assert.IsNull(kitchen.DescribeHeat(Pan, Start, Rate));
            Assert.IsEmpty(kitchen.HeatNames(Pan, Start, Rate));
        }

        [Test]
        public void Label_ShowsTheDonenessBucket()
        {
            KitchenState kitchen = KitchenWithSticks(out _);
            HeatTestKit.SwitchOn(kitchen, Pan, PanBurner);
            kitchen.TryPutOnHeat(FoodId, Pan, VesselKind.Pan, Start);
            Assert.AreEqual("potato sticks · warming up", kitchen.DescribeHeat(Pan, Start + 10, Rate));
            Assert.AreEqual("potato sticks · medium", kitchen.DescribeHeat(Pan, Start + 750, Rate));
        }

        [Test]
        public void Clear_EmptiesPansAndSwitchesBurnersOff()
        {
            KitchenState kitchen = KitchenWithSticks(out _);
            HeatTestKit.SwitchOn(kitchen, Pan, PanBurner);
            kitchen.TryPutOnHeat(FoodId, Pan, VesselKind.Pan, Start);
            kitchen.Clear();
            Assert.AreEqual(0, kitchen.HeatCount(Pan));
            Assert.IsFalse(kitchen.BurnerOf(PanBurner).IsOn);
            Assert.AreEqual(KitchenState.NoBurner, kitchen.BurnerUnder(Pan));
        }

        [Test]
        public void HeatTimeline_IsBuiltOncePerItemOpAndTemperature()
        {
            // Review of spec 027c: the same food on the same heat reuses its timeline (no hundred sim runs per put-on).
            var kitchen = KitchenState.CreateDemo();
            kitchen.TryCreateIngredient("potato", out Item first);
            kitchen.TryCreateIngredient("potato", out Item second);
            Assert.AreEqual(first.Hash, second.Hash);
            kitchen.Register(FoodId, first);
            kitchen.Register(FoodId + 1, second);
            HeatTestKit.SwitchOn(kitchen, Pot, PotBurner);
            Assert.AreEqual(0, kitchen.CachedTimelines);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutOnHeat(FoodId, Pot, VesselKind.Pot, Start));
            Assert.AreEqual(1, kitchen.CachedTimelines);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutOnHeat(FoodId + 1, Pot, VesselKind.Pot, Start));
            Assert.AreEqual(1, kitchen.CachedTimelines, "the second, identical food reused it");
        }

        /// <summary>Two onions sauteed 60 s and 170 s: the same bucket ("medium"), so the same hash, but a different doneness.</summary>
        private static void SameHashOnions(out KitchenState kitchen, out Item early, out Item late)
        {
            kitchen = KitchenState.CreateDemo();
            ItemFactory factory = NewFactory();
            kitchen.TryCreateIngredient("onion", out Item onion);
            OperationResult a = factory.Apply("saute", new[] { onion }, Heat(150f, 60f));
            OperationResult b = factory.Apply("saute", new[] { onion }, Heat(150f, 170f));
            Assert.IsTrue(a.Ok && b.Ok, "the sim sautes a raw onion");
            early = a.Item;
            late = b.Item;
            Assert.AreEqual(early.Hash, late.Hash, "precondition: one bucket, one hash");
            Assert.AreNotEqual(early.Components[0].Doneness, late.Components[0].Doneness, "precondition: different doneness");
        }

        [Test]
        public void HeatTimeline_SameHashButDifferentDoneness_GetsItsOwnTimeline()
        {
            // Second review: the cache key is the exact state the heat op reads, not the hash alone.
            SameHashOnions(out KitchenState kitchen, out Item early, out Item late);
            HeatTimeline freshEarly = kitchen.FreshTimeline(early, "boil", null);
            HeatTimeline freshLate = kitchen.FreshTimeline(late, "boil", null);
            Assert.AreNotEqual(freshEarly, freshLate, "precondition: their timelines differ");
            Assert.AreEqual(freshEarly, kitchen.TimelineFor(early, "boil", null));
            Assert.AreEqual(freshLate, kitchen.TimelineFor(late, "boil", null), "not the early onion's cached timeline");
            Assert.AreEqual(2, kitchen.CachedTimelines);
            Assert.AreNotEqual(KitchenState.TimelineKeyOf(early, "boil", null), KitchenState.TimelineKeyOf(late, "boil", null));
        }

        [Test]
        public void HeatTimeline_CachedEqualsFresh_AndEachOpAndTemperatureHasItsOwn()
        {
            SameHashOnions(out KitchenState kitchen, out Item early, out _);
            HeatTimeline first = kitchen.TimelineFor(early, "saute", 150);
            Assert.AreEqual(kitchen.FreshTimeline(early, "saute", 150), first);
            Assert.AreEqual(first, kitchen.TimelineFor(early, "saute", 150), "served from the cache");
            Assert.AreEqual(1, kitchen.CachedTimelines);
            kitchen.TimelineFor(early, "saute", 200);
            kitchen.TimelineFor(early, "fry", 150);
            kitchen.TimelineFor(early, "boil", null);
            Assert.AreEqual(4, kitchen.CachedTimelines, "another temperature or op is another entry");
        }

        [Test]
        public void HeatTimeline_CacheIsClearedAtItsLimit()
        {
            SameHashOnions(out KitchenState kitchen, out Item early, out _);
            kitchen.TimelineCacheCapacity = 3;
            kitchen.TimelineFor(early, "saute", 130);
            kitchen.TimelineFor(early, "saute", 140);
            kitchen.TimelineFor(early, "saute", 150);
            Assert.AreEqual(3, kitchen.CachedTimelines);
            HeatTimeline fourth = kitchen.TimelineFor(early, "saute", 160);
            Assert.AreEqual(1, kitchen.CachedTimelines, "cleared, then the new one kept");
            Assert.AreEqual(kitchen.FreshTimeline(early, "saute", 160), fourth);
        }
    }
}
