using System.Collections.Generic;
using NUnit.Framework;
using Worktown.Game.Cooking;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 016 AC3 and AC4: pan and pot cook over time through KitchenState.</summary>
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
        public void Fry190For750Ticks_MatchesTheSimDirectly()
        {
            KitchenState kitchen = KitchenWithSticks(out Item sticks);
            HeatTestKit.SetPan(kitchen, Pan, 190);

            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutOnHeat(FoodId, Pan, StationKind.Pan, 3, Start));
            Assert.IsFalse(kitchen.TryGet(FoodId, out _), "the food is in the pan, not in the world");
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewHeatTake(Pan, Start + 750, Rate, out Item fries, out byte color));

            Item expected = NewFactory().Apply("fry", new[] { sticks }, Heat(190f, 150f)).Item;
            Assert.AreEqual(expected.Hash, fries.Hash);
            Assert.AreEqual("crispy fried potato sticks", KitchenState.NameOf(fries));
            Assert.AreEqual(3, color, "the food keeps its color");
            Assert.IsTrue(kitchen.TryGetHeat(Pan, out _), "preview does not empty the pan");
            kitchen.ClearHeat(Pan);
            Assert.IsFalse(kitchen.TryGetHeat(Pan, out _));
        }

        [Test]
        public void TakenOutTooEarly_ComesBackUnchanged()
        {
            KitchenState kitchen = KitchenWithSticks(out Item sticks);
            kitchen.TryPutOnHeat(FoodId, Pan, StationKind.Pan, 0, Start);
            // 45 ticks = 9 sim s, below the fry minimum of 10 s
            Assert.AreEqual(CookOutcome.TooShort, kitchen.TryPreviewHeatTake(Pan, Start + 45, Rate, out Item back, out _));
            Assert.AreEqual(sticks.Hash, back.Hash);
        }

        [Test]
        public void LongFry_IsClampedToTheMaximum()
        {
            KitchenState kitchen = KitchenWithSticks(out Item sticks);
            kitchen.TryPutOnHeat(FoodId, Pan, StationKind.Pan, 0, Start);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewHeatTake(Pan, Start + 9000, Rate, out Item burnt, out _));
            Item expected = NewFactory().Apply("fry", new[] { sticks }, Heat(PanSettings.StartTempC, 600f)).Item;
            Assert.AreEqual(expected.Hash, burnt.Hash);
        }

        [Test]
        public void BusyPan_RefusesASecondItem_AndKnobChanges()
        {
            KitchenState kitchen = KitchenWithSticks(out _);
            kitchen.TryCreateIngredient("tomato", out Item tomato);
            kitchen.Register(7, tomato);
            kitchen.TryPutOnHeat(FoodId, Pan, StationKind.Pan, 0, Start);

            Assert.AreEqual(CookOutcome.StationFull, kitchen.TryPutOnHeat(7, Pan, StationKind.Pan, 0, Start));
            Assert.IsTrue(kitchen.TryGet(7, out _), "the refused tomato stays in the hands");
            int before = kitchen.PanOf(Pan).TempC;
            for (int option = 1; option <= 3; option++)
                Assert.AreEqual(CookOutcome.PanBusy, kitchen.TryChangePan(Pan, StationKind.Pan, option));
            Assert.AreEqual(before, kitchen.PanOf(Pan).TempC);
            Assert.IsFalse(kitchen.PanOf(Pan).Saute);
        }

        [Test]
        public void Knob_StopsAt120And220()
        {
            var kitchen = KitchenState.CreateDemo();
            PanSettings pan = kitchen.PanOf(Pan);
            Assert.AreEqual(170, pan.TempC);
            Assert.AreEqual(6, pan.Level);
            for (int i = 0; i < 20; i++) kitchen.TryChangePan(Pan, StationKind.Pan, StationRules.PanHeatUp);
            Assert.AreEqual(220, pan.TempC);
            Assert.AreEqual(PanSettings.Levels, pan.Level);
            for (int i = 0; i < 20; i++) kitchen.TryChangePan(Pan, StationKind.Pan, StationRules.PanHeatDown);
            Assert.AreEqual(120, pan.TempC);
            Assert.AreEqual(1, pan.Level);
            Assert.AreEqual(CookOutcome.InvalidOption, kitchen.TryChangePan(Pan, StationKind.Pan, 4));
            Assert.AreEqual(CookOutcome.InvalidOption, kitchen.TryChangePan(Pot, StationKind.Pot, 1));
        }

        [Test]
        public void SauteMode_GivesASauteResult()
        {
            KitchenState kitchen = KitchenWithSticks(out Item sticks);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryChangePan(Pan, StationKind.Pan, StationRules.PanToggleMode));
            Assert.AreEqual("heat 6/11 · saute", kitchen.PanOf(Pan).Describe());
            kitchen.TryPutOnHeat(FoodId, Pan, StationKind.Pan, 0, Start);
            kitchen.TryPreviewHeatTake(Pan, Start + 750, Rate, out Item sauteed, out _);
            Item expected = NewFactory().Apply("saute", new[] { sticks }, Heat(170f, 150f)).Item;
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
            kitchen.TryPutOnHeat(FoodId, Pan, StationKind.Pan, 0, Start);
            Assert.AreEqual("potato sticks · warming up", kitchen.DescribeHeat(Pan, Start + 10, Rate));
            Assert.AreEqual("potato sticks · medium", kitchen.DescribeHeat(Pan, Start + 750, Rate));
        }

        [Test]
        public void Clear_EmptiesPansAndResetsKnobs()
        {
            KitchenState kitchen = KitchenWithSticks(out _);
            kitchen.TryChangePan(Pan, StationKind.Pan, StationRules.PanHeatUp);
            kitchen.TryPutOnHeat(FoodId, Pan, StationKind.Pan, 0, Start);
            kitchen.Clear();
            Assert.IsFalse(kitchen.TryGetHeat(Pan, out _));
            Assert.AreEqual(PanSettings.StartTempC, kitchen.PanOf(Pan).TempC);
        }
    }
}
