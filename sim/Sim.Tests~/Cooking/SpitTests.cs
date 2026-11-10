using System;
using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Cooking;
using Xunit;
using Xunit.Abstractions;

namespace Worktown.Sim.Tests.Cooking
{
    /// <summary>
    /// Spec 036 AC2: the seitan spit in the sim: the outer layer's doneness by heat time (high and low), the shave (portions from the
    /// held time, the doneness of the layer), the layer reset after a shave, the cone running out, the two seitans in the seitan wrap and
    /// flatbread, old hashes and ratings unchanged, no stacking exploit, determinism.
    /// </summary>
    public class SpitTests
    {
        private static readonly ItemCatalog Items = new ItemCatalog(StreetFoodItems.All.Concat(StreetFoodItems.GrillVegetables)
            .Concat(new[] { TestItems.Potato, TestItems.Tomato, TestItems.Onion }));
        private static readonly ItemFactory Factory = new ItemFactory(Items, OperationCatalog.CreateDefault());
        private readonly ITestOutputHelper _out;

        public SpitTests(ITestOutputHelper output) { _out = output; }

        private static Item Base(string id) => TestItems.Ok(Factory.CreateBase(id));
        private static Item Cut(Item input, string shape) => TestItems.Ok(Factory.Apply("cut", new[] { input },
            new Dictionary<string, ParamValue> { ["shape"] = ParamValue.Choice(shape) }));
        private static float Sec(float kitchenSeconds) => kitchenSeconds * DeepFry.KitchenTimeScale;
        private static OperationResult ShaveOp(Item input, float simSeconds) => Factory.Apply(Spit.OperationId, new[] { input }, Spit.ShaveParams(simSeconds));
        private static Item Shaved(float kitchenSeconds) => TestItems.Ok(ShaveOp(Base("seitan"), Sec(kitchenSeconds)));
        private static float Done(Item item) => item.Components[0].Doneness;
        private static float DoneFactor(Item item) => ItemRater.Rate(item).DonenessFactor;
        private static float Quality(Item item) => ItemRater.Rate(item).Quality;
        private static string Name(Item item) => ItemNaming.FallbackName(item, NameCatalog.Empty);
        private static Item Strips() => TestItems.Ok(Factory.Apply(Griddle.OperationId, new[] { Cut(Base("seitan"), "sticks") },
            Griddle.Params(Sec(13f), Sec(13f))));
        private static Item Chili() => TestItems.Ok(Factory.Apply(Assembly.SqueezeOperationId, new[] { Base("chili_sauce") }, Assembly.SqueezeParams(1f)));
        private static Item Build(params Item[] layers) => TestItems.Ok(Factory.Apply(Assembly.AssembleOperationId, layers, TestItems.NoParams));
        private static Item Close(Item build, string style) => TestItems.Ok(Factory.Apply(Assembly.CloseOperationId, new[] { build }, Assembly.CloseParams(style)));
        private static Item Pack(Item build, string container) => TestItems.Ok(Factory.Apply(Assembly.PackOperationId, new[] { build }, Assembly.PackParams(container)));
        private static Item SeitanWrap(Item seitan) =>
            Close(Close(Build(Base("wrap"), seitan, Cut(Base("lettuce"), "slices"), Chili()), Assembly.Folded), Assembly.Wrapped);
        private static Item SeitanFlatbread(Item seitan) =>
            Pack(Close(Build(Base("flatbread"), seitan, Cut(Base("red_cabbage"), "slices"), Chili()), Assembly.Folded), Assembly.Bag);

        [Fact]
        public void TheCatalog_HasTheShaveOp_AtTheSpit()
        {
            Assert.True(OperationCatalog.CreateDefault().TryGet(Spit.OperationId, out OperationDef op));
            Assert.Equal(Spit.Station, op.Station);
            Assert.Equal(OperationKind.Heat, op.Kind);
            Assert.Same(Spit.OuterCurve, op.Heat!.Spit);
            Assert.Null(op.Heat.Fry);
            Assert.Null(op.Heat.Grill);
        }

        [Fact]
        public void TheOuterLayer_IsGoodAt40s_PerfectAt55s_BurntAt90s_OnHighHeat()
        {
            DonenessWindow w = StreetFoodItems.Seitan.IdealDoneness;
            Assert.True(Done(Shaved(39f)) < w.Min, "pale before 40 s");
            Assert.Equal(w.Min, Done(Shaved(40f)), 3);
            Assert.Equal((w.Min + w.Max) * 0.5f, Done(Shaved(55f)), 3);
            Assert.Equal(w.Max, Done(Shaved(70f)), 3);
            Assert.Equal(w.Max + DeepFry.BurnMargin, Done(Shaved(90f)), 3);
            Assert.Equal(1f, DoneFactor(Shaved(55f)), 3);
            Assert.True(DoneFactor(Shaved(20f)) < DoneFactor(Shaved(40f)));
            Assert.True(DoneFactor(Shaved(90f)) < DoneFactor(Shaved(70f)));
            Assert.Equal("pale shaved seitan", Name(Shaved(20f)));
            Assert.Equal("shaved seitan", Name(Shaved(55f)));
            Assert.Equal("charred shaved seitan", Name(Shaved(80f)));
            // Doneness never falls with more heat.
            float last = -1f;
            for (int s = 0; s <= 300; s += 5)
            {
                float d = Done(Shaved(s));
                Assert.True(d >= last - 1e-6f, "at " + s + " s");
                last = d;
            }
        }

        [Fact]
        public void TheCone_CooksItsOuterLayerOnlyWhileTheHeatIsOn_LowAtHalfSpeed()
        {
            SpitCone cone = SpitCone.Fresh();
            Assert.Equal(Spit.PortionsPerCone, cone.Portions);
            Assert.Equal(0f, cone.OuterSeconds);
            Assert.Same(cone, cone.Heat(Sec(10f), Spit.Off));
            Assert.Equal(Sec(10f), cone.Heat(Sec(10f), Spit.High).OuterSeconds, 3);
            Assert.Equal(Sec(5f), cone.Heat(Sec(10f), Spit.Low).OuterSeconds, 3);
            Assert.Equal(Spit.MaxLayerSeconds, cone.Heat(1e6f, Spit.High).OuterSeconds);
            Assert.Equal(Spit.Low, Spit.NextLevel(Spit.Off));
            Assert.Equal(Spit.High, Spit.NextLevel(Spit.Low));
            Assert.Equal(Spit.Off, Spit.NextLevel(Spit.High));
        }

        [Fact]
        public void AShave_PortionsFromTheHeldTime_InSteps()
        {
            Assert.Equal(0, Spit.PortionsFor(0.2));
            Assert.Equal(0, Spit.PortionsFor(0.49));
            Assert.Equal(1, Spit.PortionsFor(0.5));
            Assert.Equal(1, Spit.PortionsFor(1.0));
            Assert.Equal(1, Spit.PortionsFor(1.49));
            Assert.Equal(2, Spit.PortionsFor(1.5));
            Assert.Equal(2, Spit.PortionsFor(2.0));
            Assert.Equal(3, Spit.PortionsFor(2.5));
            Assert.Equal(3, Spit.PortionsFor(Spit.MaxShaveSeconds));
            Assert.Equal(3, Spit.PortionsFor(60.0));
            Assert.Equal(0, Spit.PortionsFor(double.NaN));
            SpitCone cone = SpitCone.Fresh().Heat(Sec(55f), Spit.High);
            SpitCone after = cone.Shave(2.0, 3, out int two);
            Assert.Equal(2, two);
            Assert.Equal(Spit.PortionsPerCone - 2, after.Portions);
            Assert.Equal(1, after.Shaves);
            cone.Shave(3.0, 1, out int room);
            Assert.Equal(1, room); // the tray's room caps it
            Assert.Same(cone, cone.Shave(0.3, 3, out int none));
            Assert.Equal(0, none);
            Assert.Same(cone, cone.Shave(1.0, 0, out _));
        }

        [Fact]
        public void AfterAShave_TheNewOuterLayerStartsAtHalfTheGoodLevel_NeverMoreCookedThanBefore()
        {
            Assert.Equal(0.5f * Spit.OuterCurve.WindowMinAt, Spit.ResetSeconds, 3);
            Assert.Equal(Sec(20f), Spit.ResetSeconds, 3);
            SpitCone perfect = SpitCone.Fresh().Heat(Sec(55f), Spit.High).Shave(1.0, 3, out _);
            Assert.Equal(Spit.ResetSeconds, perfect.OuterSeconds, 3);
            // The next layer is good again 20 s later (half the way was done already).
            Assert.Equal(Sec(40f), perfect.Heat(Sec(20f), Spit.High).OuterSeconds, 3);
            SpitCone pale = SpitCone.Fresh().Heat(Sec(5f), Spit.High).Shave(1.0, 3, out _);
            Assert.Equal(Sec(5f), pale.OuterSeconds, 3);
        }

        [Fact]
        public void TheCone_RunsOutAfterItsPortions()
        {
            SpitCone cone = SpitCone.Fresh();
            int shaved = 0;
            for (int i = 0; i < 100 && !cone.IsEmpty; i++)
            {
                cone = cone.Heat(Sec(30f), Spit.High).Shave(3.0, 3, out int n);
                shaved += n;
            }
            Assert.True(cone.IsEmpty);
            Assert.Equal(Spit.PortionsPerCone, shaved);
            Assert.Equal(0f, cone.Heat(Sec(10f), Spit.High).OuterSeconds - cone.OuterSeconds, 3);
            Assert.Same(cone, cone.Shave(2.0, 3, out int more));
            Assert.Equal(0, more);
        }

        [Fact]
        public void OnlyTheConesMeat_IsShaved_NeverShavedSeitanAgain()
        {
            Assert.Equal(OperationError.ForbiddenInput, ShaveOp(Shaved(55f), 0f).Error);
            Assert.Equal(OperationError.ForbiddenInput, ShaveOp(Cut(Base("seitan"), "sticks"), Sec(50f)).Error);
            Assert.Equal(OperationError.ForbiddenInput, ShaveOp(Base("wrap"), Sec(50f)).Error);
            Assert.Equal(OperationError.InvalidParam, Factory.Apply(Spit.OperationId, new[] { Base("seitan") },
                new Dictionary<string, ParamValue> { ["time"] = ParamValue.Number(Spit.MaxLayerSeconds + 1f) }).Error);
            Assert.Equal(Spit.MaxLayerSeconds, Spit.ShaveParams(1e9f)["time"].NumberValue);
        }

        [Fact]
        public void TheSeitanWrapAndFlatbread_TakeShavedOrGrilled_BothExact_AndTheReviewSaysWhich()
        {
            foreach (Func<Item, Item> dish in new Func<Item, Item>[] { SeitanWrap, SeitanFlatbread })
            {
                Item spit = dish(Shaved(55f)), grill = dish(Strips());
                foreach (Item served in new[] { spit, grill })
                {
                    TemplateScore best = RecipeTemplates.Best(served)!;
                    Assert.Equal(0, best.Missing);
                    Assert.Equal(0, best.WrongPrep);
                    Assert.Equal(1f, best.Factor, 3);
                }
                Assert.Equal(Name(grill), Name(spit));
                _out.WriteLine(Name(spit) + ": spit q " + Quality(spit).ToString("0.000") + ", griddle q " + Quality(grill).ToString("0.000"));
                Assert.Equal("The seitan was shaved fresh off the spit.", Spit.ReviewNote(spit));
                Assert.Equal("The seitan came as grilled strips.", Spit.ReviewNote(grill));
            }
            Assert.Null(Spit.ReviewNote(Build(Base("wrap"), Cut(Base("lettuce"), "slices"))));
            Assert.Null(Spit.ReviewNote(Base("seitan")));
            // Pale seitan off the spit is a seitan wrap prepared right, but it rates lower.
            Assert.True(Quality(SeitanWrap(Shaved(20f))) < Quality(SeitanWrap(Shaved(55f))));
        }

        /// <summary>
        /// Measured before spec 036 (at commit 93ce453): a seitan wrap and a seitan flatbread with grilled strips (13 kitchen s a side).
        /// Spec 036 adds shaved seitan to their templates; these keep their hash, name and rating.
        /// Spec 043 re-pinned the ratings (hash and name stay): 13 s a side is Good for seitan, which now rates 0.9 instead of 1.
        /// </summary>
        [Fact]
        public void Pre036Dishes_KeepTheirHashesNamesAndRatings()
        {
            Item wrap = SeitanWrap(Strips());
            Assert.Equal("58f0c74c52ca1b918150770a2f4e07f1a6da3bb7f0b78d6279f0bef0cec4cb4a", wrap.Hash);
            Assert.Equal("seitan wrap in paper", Name(wrap));
            Assert.Equal(0.687459f, Quality(wrap), 6);
            Item flatbread = SeitanFlatbread(Strips());
            Assert.Equal("e111d79d407e753f86b344895c5ec1d26fc99e39b29f6e0010b54f505a3bdbbf", flatbread.Hash);
            Assert.Equal("seitan flatbread in a bag", Name(flatbread));
            Assert.Equal(0.710634f, Quality(flatbread), 6);
        }

        [Fact]
        public void ShavingAgain_NeverRaisesQualityBeyondHonestPlay()
        {
            // Honest play: one shave at the best time (the best single shave of a fresh layer, searched over every second). Any schedule
            // of heat and holds (shaving early, late, again and again, on low or high) gives no portion better than that, and piling
            // portions into one wrap adds nothing (extras are neutral).
            float honest = 0f, best = 0f;
            for (int t = 0; t <= 300; t++)
                if (Quality(Shaved(t)) > honest)
                {
                    honest = Quality(Shaved(t));
                    best = t;
                }
            _out.WriteLine("the best single shave: " + best + " s, q " + honest.ToString("0.000") + "; at 55 s q " + Quality(Shaved(55f)).ToString("0.000"));
            Assert.InRange(best, 40f, 70f); // inside the good window
            float honestWrap = Quality(SeitanWrap(Shaved(best)));
            var random = new Worktown.Sim.Core.SeededRandom(36);
            for (int run = 0; run < 40; run++)
            {
                SpitCone cone = SpitCone.Fresh();
                for (int step = 0; step < 12 && !cone.IsEmpty; step++)
                {
                    cone = cone.Heat(Sec(random.NextFloat() * 80f), random.NextInt(0, Spit.Levels));
                    float layer = cone.OuterSeconds;
                    cone = cone.Shave(random.NextFloat() * 4.0, 3, out int n);
                    for (int i = 0; i < n; i++)
                    {
                        Item portion = TestItems.Ok(ShaveOp(Base("seitan"), layer));
                        Assert.True(Quality(portion) <= honest + 1e-6f);
                        Assert.Equal(1f, portion.QualityFactor);
                    }
                }
            }
            Item piled = Close(Close(Build(Base("wrap"), Shaved(best), Shaved(best), Shaved(best), Cut(Base("lettuce"), "slices"), Chili()),
                Assembly.Folded), Assembly.Wrapped);
            Assert.True(Quality(piled) <= honestWrap + 1e-6f, "three portions in one wrap rate as one");
            // Right after a shave the next layer is pale: shaving again at once only gives worse seitan.
            SpitCone after = SpitCone.Fresh().Heat(Sec(55f), Spit.High).Shave(1.0, 3, out _);
            Assert.True(Quality(TestItems.Ok(ShaveOp(Base("seitan"), after.OuterSeconds))) < honest);
        }

        [Fact]
        public void TheSameSteps_GiveTheSameCone_AndTheSamePortion()
        {
            SpitCone Run()
            {
                SpitCone c = SpitCone.Fresh();
                for (int i = 0; i < 8; i++) c = c.Heat(Sec(7f + i), i % Spit.Levels).Shave(0.4 + i * 0.3, 3 - i % 3, out _);
                return c;
            }
            Assert.Equal(Run(), Run());
            Item a = Shaved(47.5f), b = Shaved(47.5f);
            Assert.Equal(a.Hash, b.Hash);
            Assert.Equal(Quality(a), Quality(b));
            Assert.Equal(Name(a), Name(b));
        }
    }
}
