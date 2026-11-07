using System.Collections.Generic;
using NUnit.Framework;
using Worktown.Game.Cooking;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 016 AC3 and AC4: pan and pot cook over time through KitchenState, only while switched on
    /// (on/off stove, changed by the human on 2026-11-07).
    /// </summary>
    public class HeatSessionTests
    {
        private const int FoodId = 42;
        private const int Pan = HeatTestKit.PanIndex;
        private const int Pot = HeatTestKit.PotIndex;
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
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryApply(FoodId, StationKind.Board, 3, out sticks, out _));
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
            HeatTestKit.SwitchOn(kitchen, Pan, StationKind.Pan);

            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutOnHeat(FoodId, Pan, StationKind.Pan, 3, Start));
            Assert.IsFalse(kitchen.TryGet(FoodId, out _), "the food is in the pan, not in the world");
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewHeatTake(Pan, Start + 750, Rate, out Item fries, out byte color));

            Item expected = NewFactory().Apply("fry", new[] { sticks }, Heat(StoveSettings.PanTempC, 150f)).Item;
            Assert.AreEqual(expected.Hash, fries.Hash);
            Assert.AreEqual("crispy fried potato sticks", KitchenState.NameOf(fries));
            Assert.AreEqual(3, color, "the food keeps its color");
            Assert.IsTrue(kitchen.TryGetHeat(Pan, out _), "preview does not empty the pan");
            kitchen.ClearHeat(Pan);
            Assert.IsFalse(kitchen.TryGetHeat(Pan, out _));
        }

        [Test]
        public void NewStove_IsOff_AndFoodInItDoesNotCook()
        {
            KitchenState kitchen = KitchenWithSticks(out Item sticks);
            Assert.IsFalse(kitchen.StoveOf(Pan).IsOn);
            kitchen.TryPutOnHeat(FoodId, Pan, StationKind.Pan, 0, Start);
            Assert.AreEqual(CookOutcome.TooShort, kitchen.TryPreviewHeatTake(Pan, Start + 9000, Rate, out Item back, out _));
            Assert.AreEqual(sticks.Hash, back.Hash);
            Assert.AreEqual("potato sticks · raw", kitchen.DescribeHeat(Pan, Start + 9000, Rate));
        }

        [Test]
        public void SwitchingOffWithFoodInside_PausesCooking()
        {
            KitchenState kitchen = KitchenWithSticks(out Item sticks);
            kitchen.TryPutOnHeat(FoodId, Pan, StationKind.Pan, 0, Start);           // off: nothing yet
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryUseStove(Pan, StationKind.Pan, StationRules.StoveSwitch, Start + 100));  // on
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryUseStove(Pan, StationKind.Pan, StationRules.StoveSwitch, Start + 500));  // off: 400 ticks
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryUseStove(Pan, StationKind.Pan, StationRules.StoveSwitch, Start + 2000)); // on
            // + 350 ticks = 750 ticks on in total = 150 sim s
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewHeatTake(Pan, Start + 2350, Rate, out Item fries, out _));
            Item expected = NewFactory().Apply("fry", new[] { sticks }, Heat(StoveSettings.PanTempC, 150f)).Item;
            Assert.AreEqual(expected.Hash, fries.Hash);
        }

        [Test]
        public void TakenOutTooEarly_ComesBackUnchanged()
        {
            KitchenState kitchen = KitchenWithSticks(out Item sticks);
            HeatTestKit.SwitchOn(kitchen, Pan, StationKind.Pan);
            kitchen.TryPutOnHeat(FoodId, Pan, StationKind.Pan, 0, Start);
            // 45 ticks = 9 sim s, below the fry minimum of 10 s
            Assert.AreEqual(CookOutcome.TooShort, kitchen.TryPreviewHeatTake(Pan, Start + 45, Rate, out Item back, out _));
            Assert.AreEqual(sticks.Hash, back.Hash);
        }

        [Test]
        public void LongFry_IsClampedToTheMaximum()
        {
            KitchenState kitchen = KitchenWithSticks(out Item sticks);
            HeatTestKit.SwitchOn(kitchen, Pan, StationKind.Pan);
            kitchen.TryPutOnHeat(FoodId, Pan, StationKind.Pan, 0, Start);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewHeatTake(Pan, Start + 9000, Rate, out Item burnt, out _));
            Item expected = NewFactory().Apply("fry", new[] { sticks }, Heat(StoveSettings.PanTempC, 600f)).Item;
            Assert.AreEqual(expected.Hash, burnt.Hash);
        }

        [Test]
        public void BusyPan_RefusesASecondItem_AndModeChange_ButNotTheSwitch()
        {
            KitchenState kitchen = KitchenWithSticks(out _);
            kitchen.TryCreateIngredient("tomato", out Item tomato);
            kitchen.Register(7, tomato);
            kitchen.TryPutOnHeat(FoodId, Pan, StationKind.Pan, 0, Start);

            Assert.AreEqual(CookOutcome.StationFull, kitchen.TryPutOnHeat(7, Pan, StationKind.Pan, 0, Start));
            Assert.IsTrue(kitchen.TryGet(7, out _), "the refused tomato stays in the hands");
            Assert.AreEqual(CookOutcome.PanBusy, kitchen.TryUseStove(Pan, StationKind.Pan, StationRules.PanMode, Start));
            Assert.IsFalse(kitchen.StoveOf(Pan).Saute);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryUseStove(Pan, StationKind.Pan, StationRules.StoveSwitch, Start));
            Assert.IsTrue(kitchen.StoveOf(Pan).IsOn);
        }

        [Test]
        public void Options_AreCheckedPerStation()
        {
            var kitchen = KitchenState.CreateDemo();
            Assert.AreEqual(CookOutcome.InvalidOption, kitchen.TryUseStove(Pan, StationKind.Pan, 3, Start));
            Assert.AreEqual(CookOutcome.InvalidOption, kitchen.TryUseStove(Pan, StationKind.Pan, 0, Start));
            Assert.AreEqual(CookOutcome.InvalidOption, kitchen.TryUseStove(Pot, StationKind.Pot, StationRules.PanMode, Start));
            Assert.AreEqual(CookOutcome.InvalidOption, kitchen.TryUseStove(1, StationKind.Board, 1, Start));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryUseStove(Pot, StationKind.Pot, StationRules.StoveSwitch, Start));
            Assert.AreEqual("on", kitchen.StoveOf(Pot).Describe(StationKind.Pot));
        }

        [Test]
        public void SauteMode_GivesASauteResult()
        {
            KitchenState kitchen = KitchenWithSticks(out Item sticks);
            Assert.AreEqual("off · fry", kitchen.StoveOf(Pan).Describe(StationKind.Pan));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryUseStove(Pan, StationKind.Pan, StationRules.PanMode, Start));
            HeatTestKit.SwitchOn(kitchen, Pan, StationKind.Pan);
            Assert.AreEqual("on · saute", kitchen.StoveOf(Pan).Describe(StationKind.Pan));
            kitchen.TryPutOnHeat(FoodId, Pan, StationKind.Pan, 0, Start);
            kitchen.TryPreviewHeatTake(Pan, Start + 750, Rate, out Item sauteed, out _);
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
            Assert.AreEqual(CookOutcome.SimRefused, kitchen.TryPutOnHeat(8, Pan, StationKind.Pan, 0, Start));
            Assert.IsTrue(kitchen.TryGet(8, out _));
            Assert.IsFalse(kitchen.TryGetHeat(Pan, out _));
        }

        [Test]
        public void EmptyPan_TakeIsStationEmpty_AndLabelIsNull()
        {
            var kitchen = KitchenState.CreateDemo();
            Assert.AreEqual(CookOutcome.StationEmpty, kitchen.TryPreviewHeatTake(Pan, Start, Rate, out _, out _));
            Assert.IsNull(kitchen.DescribeHeat(Pan, Start, Rate));
        }

        [Test]
        public void Label_ShowsTheDonenessBucket()
        {
            KitchenState kitchen = KitchenWithSticks(out _);
            HeatTestKit.SwitchOn(kitchen, Pan, StationKind.Pan);
            kitchen.TryPutOnHeat(FoodId, Pan, StationKind.Pan, 0, Start);
            Assert.AreEqual("potato sticks · warming up", kitchen.DescribeHeat(Pan, Start + 10, Rate));
            Assert.AreEqual("potato sticks · medium", kitchen.DescribeHeat(Pan, Start + 750, Rate));
        }

        [Test]
        public void Clear_EmptiesPansAndSwitchesStovesOff()
        {
            KitchenState kitchen = KitchenWithSticks(out _);
            HeatTestKit.SwitchOn(kitchen, Pan, StationKind.Pan);
            kitchen.TryPutOnHeat(FoodId, Pan, StationKind.Pan, 0, Start);
            kitchen.Clear();
            Assert.IsFalse(kitchen.TryGetHeat(Pan, out _));
            Assert.IsFalse(kitchen.StoveOf(Pan).IsOn);
        }
    }
}
