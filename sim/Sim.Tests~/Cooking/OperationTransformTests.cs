using System;
using System.Collections.Generic;
using Worktown.Sim.Cooking;
using Xunit;
using static Worktown.Sim.Tests.Cooking.TestItems;

namespace Worktown.Sim.Tests.Cooking
{
    public class OperationTransformTests
    {
        private const double Tol = 1e-5;

        private static void AssertStats(ItemStats e, ItemStats a)
        {
            Assert.Equal(e.Sweet, a.Sweet, Tol);
            Assert.Equal(e.Salty, a.Salty, Tol);
            Assert.Equal(e.Sour, a.Sour, Tol);
            Assert.Equal(e.Bitter, a.Bitter, Tol);
            Assert.Equal(e.Umami, a.Umami, Tol);
            Assert.Equal(e.Fat, a.Fat, Tol);
            Assert.Equal(e.Crunch, a.Crunch, Tol);
            Assert.Equal(e.HeatTolerance, a.HeatTolerance, Tol);
            Assert.Equal(e.Doneness, a.Doneness, Tol);
            Assert.Equal(e.Moisture, a.Moisture, Tol);
        }

        // Input for AC11: raw potato, Surface 0: sweet .1, salty 0, sour 0, bitter .1, umami .1, fat 0,
        // crunch .5, heatTolerance .6, doneness 0, moisture .8.

        [Fact]
        public void AC11_Fry_ExactStats()
        {
            // T=180, t=90: dose = ((180-60)/120) * (90/180) * (0.5+0) = 1 * 0.5 * 0.5 = 0.25; g = 0.25
            // D = 0 + 0.25*(1 - 0.5*0.6) = 0.175 = Doneness
            // Fat = 0 + 0.35*0.25*(1-0) = 0.0875
            // Umami = 0.1 + 0.15*0.25*(1-0.1) = 0.1 + 0.03375 = 0.13375 (dilution 0)
            // Moisture = 0.8*(1 + (-0.6)*0.25) = 0.8*0.85 = 0.68
            // Crunch = 0.5 + 0.5*0.25*(1-0.68)*(1-0.5) = 0.5 + 0.02 = 0.52
            // Bitter = 0.1 + 2*max(0, 0.175-0.9) = 0.1; Sweet .1, Salty 0, Sour 0, HT .6 unchanged
            var r = Ok(Fry(Base("potato"), 180f, 90f));
            AssertStats(new ItemStats(0.1f, 0f, 0f, 0.1f, 0.13375f, 0.0875f, 0.52f, 0.6f, 0.175f, 0.68f), r.Stats);
            Assert.Equal(0.175f, r.Components[0].Doneness, Tol);
        }

        [Fact]
        public void AC11_Saute_ExactStats()
        {
            // T=180, t=90: dose = 0.25 as for fry; g = 0.25; D = 0.175
            // Fat = 0 + 0.15*0.25*(1-0) = 0.0375
            // Umami = 0.1 + 0.2*0.25*(1-0.1) = 0.1 + 0.045 = 0.145
            // Moisture = 0.8*(1 + (-0.4)*0.25) = 0.8*0.9 = 0.72
            // Crunch = 0.5 + 0.25*0.25*(1-0.72)*(1-0.5) = 0.5 + 0.00875 = 0.50875
            // Bitter = 0.1 (doneness 0.175 < 0.9)
            var r = Ok(Heat("saute", Base("potato"), 180f, 90f));
            AssertStats(new ItemStats(0.1f, 0f, 0f, 0.1f, 0.145f, 0.0375f, 0.50875f, 0.6f, 0.175f, 0.72f), r.Stats);
        }

        [Fact]
        public void AC11_Boil_ExactStats()
        {
            // T=100 fixed, t=600: dose = ((100-60)/120) * (600/180) * 0.5 = (1/3)*(10/3)*0.5 = 5/9 = 0.555556; g = 5/9
            // D = (5/9)*(1 - 0.3) = 0.388889 = Doneness
            // Fat = 0 (FatGain 0); Umami before dilution = 0.1 (UmamiGain 0)
            // Moisture = 0.8 + 0.5*(5/9)*(1-0.8) = 0.8 + 0.055556 = 0.855556
            // Crunch = 0.5*(1 + (-0.8)*(5/9)) = 0.5*(1 - 0.444444) = 0.277778
            // Dilution 0.1: factor = 1 - 0.1*(5/9) = 0.944444; Sweet = 0.0944444, Salty = 0, Umami = 0.0944444
            // Bitter = 0.1 (0.389 < 0.9)
            var r = Ok(Boil(Base("potato"), 600f));
            AssertStats(new ItemStats(0.0944444f, 0f, 0f, 0.1f, 0.0944444f, 0f, 0.277778f, 0.6f, 0.388889f, 0.855556f), r.Stats);
        }

        [Fact]
        public void Heat_ChargedItemGetsBitterAndStaysInRange()
        {
            var r = Ok(Fry(Base("potato"), 220f, 600f));
            Assert.Equal(1f, r.Stats.Doneness, Tol);
            // Bitter = 0.1 + 2*(1-0.9) = 0.3
            Assert.Equal(0.3f, r.Stats.Bitter, Tol);
        }

        [Fact]
        public void Cut_SetsSurfaceAndKeepsStats()
        {
            var potato = Base("potato");
            var cut = Ok(Cut(potato, "dice"));
            Assert.Equal(0.8f, cut.Surface, Tol);
            Assert.Equal(potato.Stats, cut.Stats);
        }

        [Fact]
        public void Cut_FinerSurfaceHeatsFaster()
        {
            var whole = Ok(Fry(Base("potato"), 180f, 90f));
            var diced = Ok(Fry(Ok(Cut(Base("potato"), "dice")), 180f, 90f));
            Assert.True(diced.Stats.Doneness > whole.Stats.Doneness);
        }

        [Fact]
        public void AC8_DepthLimit()
        {
            var item = Base("potato");
            for (int i = 0; i < 7; i++) item = Ok(Fry(item, 120f, 10f));
            Assert.Equal(8, item.Depth);
            var r = Fry(item, 120f, 10f);
            Assert.Equal(OperationError.DepthLimit, r.Error);
            Assert.Null(r.Item);
        }

        [Fact]
        public void AC8_ComponentLimit()
        {
            var six = new[] { "salt", "tomato", "cashew", "basil", "oil", "onion" };
            var five = new[] { "salt", "tomato", "cashew", "basil", "oil" };
            Item CutMix(string[] ids)
            {
                var items = new Item[ids.Length];
                for (int i = 0; i < ids.Length; i++) items[i] = Base(ids[i]);
                return Ok(Cut(Ok(Mix(items)), "halves"));
            }
            var a = CutMix(six);
            var b = CutMix(new[] { "potato", "apple", "salt", "tomato", "cashew", "basil" });
            var c = CutMix(five);
            Assert.Equal(17, a.Weight + b.Weight + c.Weight);
            Assert.Equal(OperationError.ComponentLimit, Mix(a, b, c).Error);
        }

        [Fact]
        public void AC8_MixArityAfterFlattening()
        {
            var m1 = Ok(Mix(Base("salt"), Base("tomato"), Base("cashew"), Base("basil")));
            var m2 = Ok(Mix(Base("oil"), Base("onion"), Base("potato")));
            Assert.Equal(OperationError.ArityViolation, Mix(m1, m2).Error); // 4 + 3 = 7 flattened
            Assert.Equal(OperationError.ArityViolation, Mix(Base("salt")).Error);
            Assert.Equal(OperationError.ArityViolation, Mix().Error);
            Assert.Equal(OperationError.ArityViolation,
                Factory.Apply("cut", new[] { Base("salt"), Base("oil") }, new Dictionary<string, ParamValue> { ["shape"] = ParamValue.Choice("halves") }).Error);
        }

        [Fact]
        public void AC8_PlatedInputIsForbidden()
        {
            var plate = Ok(Plate(Fries()));
            Assert.Contains("plated", plate.Tags);
            Assert.Equal(OperationError.ForbiddenInput, Mix(plate, Base("salt")).Error);
            Assert.Equal(OperationError.ForbiddenInput, Plate(plate).Error);
            Assert.Equal(OperationError.ForbiddenInput, Fry(plate, 150f, 60f).Error);
        }

        [Fact]
        public void AC8_InvalidParams()
        {
            var potato = Base("potato");
            Assert.Equal(OperationError.InvalidParam, Fry(potato, 250f, 60f).Error);
            Assert.Equal(OperationError.InvalidParam, Fry(potato, float.NaN, 60f).Error);
            Assert.Equal(OperationError.InvalidParam, Fry(potato, float.PositiveInfinity, 60f).Error);
            Assert.Equal(OperationError.InvalidParam, Fry(potato, 150f, 5f).Error);
            Assert.Equal(OperationError.InvalidParam, Cut(potato, "mush").Error);
            // missing, unknown and wrong-kind params
            Assert.Equal(OperationError.InvalidParam, Factory.Apply("fry", new[] { potato }, NoParams).Error);
            Assert.Equal(OperationError.InvalidParam, Factory.Apply("cut", new[] { potato },
                new Dictionary<string, ParamValue> { ["shape"] = ParamValue.Number(0.5f) }).Error);
            Assert.Equal(OperationError.InvalidParam, Factory.Apply("mix", new[] { potato, potato },
                new Dictionary<string, ParamValue> { ["extra"] = ParamValue.Number(1f) }).Error);
        }

        [Fact]
        public void UnknownBaseAndOperation()
        {
            Assert.Equal(OperationError.UnknownBaseItem, Factory.CreateBase("unobtainium").Error);
            Assert.Equal(OperationError.UnknownOperation, Factory.Apply("grill", new[] { Base("salt") }, NoParams).Error);
        }

        [Fact]
        public void ProgrammerErrors_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => Factory.Apply("mix", null!, NoParams));
            Assert.Throws<ArgumentNullException>(() => Factory.Apply("mix", new[] { Base("salt"), Base("oil") }, null!));
            Assert.Throws<ArgumentException>(() => Factory.Apply("mix", new Item[] { Base("salt"), null! }, NoParams));
        }

        [Fact]
        public void Plate_AddsTagAndKeepsWeight()
        {
            var plate = Ok(Plate(Fries(), Base("apple")));
            Assert.Equal(2, plate.Weight);
            Assert.Equal(OperationError.ArityViolation,
                Plate(Base("salt"), Base("oil"), Base("onion"), Base("basil"), Base("tomato")).Error);
        }
    }
}
