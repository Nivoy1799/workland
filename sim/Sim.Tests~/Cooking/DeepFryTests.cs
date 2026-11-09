using System;
using System.Collections.Generic;
using Worktown.Sim.Cooking;
using Xunit;

namespace Worktown.Sim.Tests.Cooking
{
    /// <summary>Spec 029 AC2: the deep_fry op (doneness by time and temperature, shaping, shake bonus, greasy, determinism).</summary>
    public class DeepFryTests
    {
        private static readonly ItemCatalog Items = new ItemCatalog(new[] { TestItems.Potato, StreetFoodItems.FalafelMix, TestItems.Tomato });
        private static readonly ItemFactory Factory = new ItemFactory(Items, OperationCatalog.CreateDefault());

        private static Item Base(string id) => TestItems.Ok(Factory.CreateBase(id));

        private static Item Sticks() => TestItems.Ok(Factory.Apply("cut", new[] { Base("potato") },
            new Dictionary<string, ParamValue> { ["shape"] = ParamValue.Choice("sticks") }));

        private static OperationResult Fry(Item input, float seconds, float temp = DeepFry.ReferenceTempC, bool shaken = false, bool greasy = false) =>
            Factory.Apply(DeepFry.OperationId, new[] { input }, new Dictionary<string, ParamValue>
            {
                ["temp"] = ParamValue.Number(temp),
                ["time"] = ParamValue.Number(seconds),
                [DeepFry.ShakeParam] = ParamValue.Choice(shaken ? DeepFry.Shaken : DeepFry.NotShaken),
                [DeepFry.DrainParam] = ParamValue.Choice(greasy ? DeepFry.Greasy : DeepFry.Drained),
            });

        private static float Done(Item item) => item.Components[0].Doneness;

        [Fact]
        public void TheDefaultCatalog_HasTheDeepFryOp_AtTheFryer()
        {
            Assert.True(OperationCatalog.CreateDefault().TryGet(DeepFry.OperationId, out OperationDef op));
            Assert.Equal("fryer", op.Station);
            Assert.Equal(OperationKind.Heat, op.Kind);
            Assert.NotNull(op.Heat!.Fry);
        }

        [Fact]
        public void Timings_AreTheKitchenSeconds_OfTheSpec()
        {
            // Good 25 s, perfect 30 s (the window's middle), burnt 45 s; falafel 20 / 25 / 38 s; 6 sim s per kitchen s.
            Assert.Equal(150f, DeepFry.Fries.WindowMinAt);
            Assert.Equal(210f, DeepFry.Fries.WindowMaxAt);
            Assert.Equal(270f, DeepFry.Fries.BurntAt);
            Assert.Equal(120f, DeepFry.Falafel.WindowMinAt);
            Assert.Equal(180f, DeepFry.Falafel.WindowMaxAt);
            Assert.Equal(228f, DeepFry.Falafel.BurntAt);
            Assert.Throws<ArgumentException>(() => FryCurve.FromKitchenSeconds("potato", 30f, 25f, 45f));
        }

        [Fact]
        public void Fries_ReachTheWindow_AtTheirTimes_AndBurnAfterIt()
        {
            DonenessWindow w = TestItems.Potato.IdealDoneness;
            Assert.Equal(w.Min, Done(TestItems.Ok(Fry(Sticks(), DeepFry.Fries.WindowMinAt))), 4);
            Assert.Equal(w.Max, Done(TestItems.Ok(Fry(Sticks(), DeepFry.Fries.WindowMaxAt))), 4);
            Assert.Equal(w.Max + DeepFry.BurnMargin, Done(TestItems.Ok(Fry(Sticks(), DeepFry.Fries.BurntAt))), 4);
            Assert.True(Done(TestItems.Ok(Fry(Sticks(), 60f))) < w.Min);
            float prev = -1f;
            for (float t = 6f; t <= 600f; t += 6f)
            {
                float d = Done(TestItems.Ok(Fry(Sticks(), t)));
                Assert.True(d >= prev, "doneness never decreases (t " + t + ")");
                prev = d;
            }
        }

        [Fact]
        public void Falafel_HasItsOwnCurve_AndTheBestRatingInsideItsWindow()
        {
            Item mix = Base("falafel_mix");
            DonenessWindow w = StreetFoodItems.FalafelMix.IdealDoneness;
            Assert.Equal(w.Min, Done(TestItems.Ok(Fry(mix, DeepFry.Falafel.WindowMinAt))), 4);
            Assert.Equal(1f, Done(TestItems.Ok(Fry(mix, DeepFry.Falafel.BurntAt))), 4);
            float perfect = ItemRater.Rate(TestItems.Ok(Fry(mix, 150f))).Quality;
            Assert.True(perfect > ItemRater.Rate(TestItems.Ok(Fry(mix, 60f))).Quality, "underdone is worse");
            Assert.True(perfect > ItemRater.Rate(TestItems.Ok(Fry(mix, 300f))).Quality, "burnt is worse");
        }

        [Fact]
        public void HotterOil_FriesFaster_CoolerOilSlower()
        {
            float at180 = Done(TestItems.Ok(Fry(Sticks(), 100f, 180f)));
            Assert.True(Done(TestItems.Ok(Fry(Sticks(), 100f, 190f))) > at180);
            Assert.True(Done(TestItems.Ok(Fry(Sticks(), 100f, 150f))) < at180);
            Assert.Equal(60f, DeepFry.EffectiveSeconds(60f, DeepFry.ReferenceTempC), 4);
        }

        [Fact]
        public void TheBasket_ShapesPotatoSticksIntoFries_AndOneFalafelPortionIntoFourBalls()
        {
            Item fries = TestItems.Ok(Fry(Sticks(), 180f));
            Assert.Equal("golden fries", ItemNaming.FallbackName(fries, NameCatalog.Empty));
            Assert.True(DeepFry.TryGetShape(Sticks(), out DeepFryShape s1));
            Assert.Equal(1, s1.Pieces);

            Item falafel = TestItems.Ok(Fry(Base("falafel_mix"), 150f));
            Assert.Equal("golden falafel balls", ItemNaming.FallbackName(falafel, NameCatalog.Empty));
            Assert.True(DeepFry.TryGetShape(Base("falafel_mix"), out DeepFryShape s2));
            Assert.Equal(4, s2.Pieces);

            // A whole potato is no fries; it keeps its name.
            Assert.False(DeepFry.TryGetShape(Base("potato"), out _));
            Assert.Equal("golden deep-fried potato", ItemNaming.FallbackName(TestItems.Ok(Fry(Base("potato"), 180f)), NameCatalog.Empty));
        }

        [Fact]
        public void Shaking_GivesTheBonusOnce_GreasyThePenalty_AndTheNameSaysGreasy()
        {
            float plain = ItemRater.Rate(TestItems.Ok(Fry(Sticks(), 180f))).Quality;
            Item shaken = TestItems.Ok(Fry(Sticks(), 180f, shaken: true));
            Item greasy = TestItems.Ok(Fry(Sticks(), 180f, greasy: true));
            Assert.Equal(Math.Min(1f, plain * (1f + DeepFry.ShakeBonus)), ItemRater.Rate(shaken).Quality, 4);
            Assert.Equal(plain * (1f - DeepFry.GreasyPenalty), ItemRater.Rate(greasy).Quality, 4);
            Assert.Equal("greasy golden fries", ItemNaming.FallbackName(greasy, NameCatalog.Empty));
            Assert.NotEqual(TestItems.Ok(Fry(Sticks(), 180f)).Hash, shaken.Hash);
            // The bonus is one choice, so it can only count once per fry.
            Assert.Equal(1f + DeepFry.ShakeBonus, DeepFry.FactorOf(true, false), 5);
        }

        [Fact]
        public void TheBonus_SurvivesPlating_AndOtherFoodIsUnchanged()
        {
            Item shaken = TestItems.Ok(Fry(Sticks(), 180f, shaken: true));
            Item plate = TestItems.Ok(Factory.Apply("plate", new[] { shaken }, TestItems.NoParams));
            Assert.Equal(ItemRater.Rate(shaken).Quality, ItemRater.Rate(plate).Quality, 5);
            Assert.Equal(1f, TestItems.Fries().QualityFactor);
            Assert.Equal(1f, TestItems.Ok(TestItems.Mix(TestItems.Base("salt"), TestItems.Base("tomato"))).QualityFactor);
        }

        [Fact]
        public void TheSameInputs_GiveTheSameItem()
        {
            Item a = TestItems.Ok(Fry(Sticks(), 173f, 178f, shaken: true));
            Item b = TestItems.Ok(Fry(Sticks(), 173f, 178f, shaken: true));
            Assert.Equal(a.Hash, b.Hash);
            Assert.Equal(a.Components[0].Doneness, b.Components[0].Doneness);
            Assert.Equal(ItemRater.Rate(a).Quality, ItemRater.Rate(b).Quality);
            Assert.Equal(a.Stats, b.Stats);
        }

        [Fact]
        public void MissingOrWrongChoices_AreRefused_AndBadCurvesThrow()
        {
            var noShake = Factory.Apply(DeepFry.OperationId, new[] { Sticks() }, new Dictionary<string, ParamValue>
            {
                ["temp"] = ParamValue.Number(180f),
                ["time"] = ParamValue.Number(60f),
            });
            Assert.Equal(OperationError.InvalidParam, noShake.Error);
            Assert.True(OperationCatalog.CreateDefault().TryGet(DeepFry.OperationId, out OperationDef op));
            var bad = op with { Heat = op.Heat! with { Fry = new DeepFryProfile(new[] { new FryCurve("potato", 10f, 5f, 20f) }, DeepFry.Fries) } };
            Assert.Throws<ArgumentException>(() => new OperationCatalog(new[] { bad }));
            var noChoices = op with { Params = new[] { op.Params[0], op.Params[1] } };
            Assert.Throws<ArgumentException>(() => new OperationCatalog(new[] { noChoices }));
        }

        [Fact]
        public void PreCookedFood_ContinuesAlongTheCurve()
        {
            Item once = TestItems.Ok(Fry(Sticks(), 90f));
            Item twice = TestItems.Ok(Fry(once, 90f));
            Assert.Equal(Done(TestItems.Ok(Fry(Sticks(), 180f))), Done(twice), 3);
        }
    }
}
