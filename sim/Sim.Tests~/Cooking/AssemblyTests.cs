using System;
using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Cooking;
using Xunit;

namespace Worktown.Sim.Tests.Cooking
{
    /// <summary>
    /// Spec 031 AC2: the assembly board in the sim: ordered layers (assemble), the sauce squeeze, close and pack in order, the
    /// recipe-template score (order, missing, extra, sauce amount, every layer's quality), names, determinism, and that no
    /// repeated step stacks a bonus.
    /// </summary>
    public class AssemblyTests
    {
        private static readonly ItemCatalog Items = new ItemCatalog(StreetFoodItems.All.Concat(StreetFoodItems.GrillVegetables)
            .Concat(new[] { TestItems.Potato, TestItems.Tomato, TestItems.Onion }));
        private static readonly ItemFactory Factory = new ItemFactory(Items, OperationCatalog.CreateDefault());

        private static Item Base(string id) => TestItems.Ok(Factory.CreateBase(id));

        private static Item Cut(Item input, string shape) => TestItems.Ok(Factory.Apply("cut", new[] { input },
            new Dictionary<string, ParamValue> { ["shape"] = ParamValue.Choice(shape) }));

        private static Item DeepFried(Item input, float seconds = 150f, bool shaken = false) =>
            TestItems.Ok(Factory.Apply(DeepFry.OperationId, new[] { input }, new Dictionary<string, ParamValue>
            {
                ["temp"] = ParamValue.Number(DeepFry.ReferenceTempC),
                ["time"] = ParamValue.Number(seconds),
                [DeepFry.ShakeParam] = ParamValue.Choice(shaken ? DeepFry.Shaken : DeepFry.NotShaken),
                [DeepFry.DrainParam] = ParamValue.Choice(DeepFry.Drained),
            }));

        private static Item Falafel(bool shaken = false) => DeepFried(Base("falafel_mix"), 150f, shaken);
        private static Item Lettuce() => Cut(Base("lettuce"), "slices");
        private static Item Cucumber() => Cut(Base("cucumber"), "slices");
        private static Item Sauce(string id = "garlic_herb_sauce", float seconds = 1f) =>
            TestItems.Ok(Factory.Apply(Assembly.SqueezeOperationId, new[] { Base(id) }, Assembly.SqueezeParams(seconds)));

        private static OperationResult Assemble(params Item[] layers) =>
            Factory.Apply(Assembly.AssembleOperationId, layers, TestItems.NoParams);

        private static OperationResult Close(Item build, string style) =>
            Factory.Apply(Assembly.CloseOperationId, new[] { build }, Assembly.CloseParams(style));

        private static OperationResult Pack(Item build, string container) =>
            Factory.Apply(Assembly.PackOperationId, new[] { build }, Assembly.PackParams(container));

        private static Item FalafelWrapOpen() => TestItems.Ok(Assemble(Base("wrap"), Falafel(), Lettuce(), Cucumber(), Sauce()));

        private static Item FalafelWrap() =>
            TestItems.Ok(Close(TestItems.Ok(Close(FalafelWrapOpen(), Assembly.Folded)), Assembly.Wrapped));

        private static RecipeTemplate T(string id) => RecipeTemplates.ById(id)!;
        private static float Quality(Item item) => ItemRater.Rate(item).Quality;
        private static string Name(Item item) => ItemNaming.FallbackName(item, NameCatalog.Empty);

        // ---- assemble: ordered layers ----

        [Fact]
        public void Assemble_KeepsTheLayersInTheOrderTheyWentOn_AndTheOrderIsPartOfTheIdentity()
        {
            Item wrap = Base("wrap"), falafel = Falafel(), lettuce = Lettuce();
            Item a = TestItems.Ok(Assemble(wrap, falafel, lettuce));
            Item b = TestItems.Ok(Assemble(wrap, lettuce, falafel));
            Assert.Equal(new[] { wrap.Hash, falafel.Hash, lettuce.Hash }, Assembly.LayersOf(a).Select(l => l.Hash));
            Assert.NotEqual(a.Hash, b.Hash);
            Assert.True(Assembly.IsBuild(a));
            Assert.Contains(Assembly.BuildTag, a.Tags);
            Assert.False(Assembly.IsBuild(falafel));
            Assert.Same(a, Assembly.BuildNode(a));
            Assert.Equal(3, a.Weight);
        }

        [Fact]
        public void Assemble_RefusesPlatedFood_ABuildInsideABuild_AndTooManyLayers()
        {
            Item plated = TestItems.Ok(Factory.Apply("plate", new[] { Lettuce() }, TestItems.NoParams));
            Assert.Equal(OperationError.ForbiddenInput, Assemble(Base("wrap"), plated).Error);
            Assert.Equal(OperationError.ForbiddenInput, Assemble(FalafelWrapOpen(), Lettuce()).Error);
            Item[] eleven = Enumerable.Range(0, CookingLimits.MaxBuildLayers + 1).Select(_ => Lettuce()).ToArray();
            Assert.Equal(OperationError.ArityViolation, Assemble(eleven).Error);
            Assert.True(Assemble(eleven.Take(CookingLimits.MaxBuildLayers).ToArray()).Ok);
        }

        // ---- the sauce squeeze ----

        [Fact]
        public void Squeeze_TheAmountBucketsFollowTheHeldSeconds()
        {
            Assert.Equal(Assembly.Little, Assembly.SauceAmountOf(Sauce(seconds: 0.3f)));
            Assert.Equal(Assembly.Right, Assembly.SauceAmountOf(Sauce(seconds: 0.5f)));
            Assert.Equal(Assembly.Right, Assembly.SauceAmountOf(Sauce(seconds: 1.4f)));
            Assert.Equal(Assembly.Lots, Assembly.SauceAmountOf(Sauce(seconds: 1.5f)));
            Assert.Equal(Assembly.Lots, Assembly.SauceAmountOf(Base("chili_sauce")));
            Assert.Equal("garlic-herb sauce", Name(Sauce(seconds: 1f)));
            Assert.Equal("a dash of garlic-herb sauce", Name(Sauce(seconds: 0.2f)));
            Assert.Equal("lots of chili sauce", Name(Sauce("chili_sauce", 2f)));
            Assert.Equal(Assembly.MaxSqueezeSeconds, Assembly.SqueezeParams(99f)[Assembly.AmountParam].NumberValue);
            Assert.Equal(0f, Assembly.SqueezeParams(float.NaN)[Assembly.AmountParam].NumberValue);
            Assert.True(Assembly.IsSauce(Sauce()));
            Assert.False(Assembly.IsSauce(Lettuce()));
        }

        // ---- close and pack, in order ----

        [Fact]
        public void Close_FoldsABreadBuild_ThenPaperWrapsIt_InOrderOnly()
        {
            Item open = FalafelWrapOpen();
            Assert.Equal(OperationError.StepOrder, Close(open, Assembly.Wrapped).Error);
            Item folded = TestItems.Ok(Close(open, Assembly.Folded));
            Assert.Equal(Assembly.Folded, Assembly.CloseOf(folded));
            Assert.Equal(OperationError.StepOrder, Close(folded, Assembly.Folded).Error);
            Item wrapped = TestItems.Ok(Close(folded, Assembly.Wrapped));
            Assert.Equal(Assembly.Wrapped, Assembly.CloseOf(wrapped));
            Assert.Contains(Assembly.PlatedTag, wrapped.Tags);
            Assert.DoesNotContain(Assembly.PlatedTag, folded.Tags);
            Assert.Equal(OperationError.ForbiddenInput, Close(wrapped, Assembly.Wrapped).Error);
            Assert.Equal(OperationError.StepOrder, Close(Lettuce(), Assembly.Folded).Error);
            Item box = TestItems.Ok(Assemble(DeepFried(Cut(Base("potato"), "sticks")), Sauce()));
            Assert.Equal(OperationError.StepOrder, Close(box, Assembly.Folded).Error);
            Assert.Equal(Assembly.LayersOf(open).Select(l => l.Hash), Assembly.LayersOf(wrapped).Select(l => l.Hash));
        }

        [Fact]
        public void Pack_ABoxNeedsNoClose_ABreadBuildIsFoldedFirst_AndPackedOnce()
        {
            Item open = FalafelWrapOpen();
            Assert.Equal(OperationError.StepOrder, Pack(open, Assembly.Bag).Error);
            Item bagged = TestItems.Ok(Pack(TestItems.Ok(Close(open, Assembly.Folded)), Assembly.Bag));
            Assert.Equal(Assembly.Bag, Assembly.PackOf(bagged));
            Assert.Contains(Assembly.PlatedTag, bagged.Tags);
            Assert.Equal(OperationError.ForbiddenInput, Pack(bagged, Assembly.Box).Error);
            Item veggies = TestItems.Ok(Assemble(DeepFried(Cut(Base("potato"), "sticks")), Grilled("paprika"), Sauce()));
            Item boxed = TestItems.Ok(Pack(veggies, Assembly.Box));
            Assert.Equal(Assembly.Box, Assembly.PackOf(boxed));
            Assert.Null(Assembly.CloseOf(boxed));
            Assert.Equal(OperationError.StepOrder, Pack(Lettuce(), Assembly.Box).Error);
            Assert.Equal(OperationError.ForbiddenInput, Pack(FalafelWrap(), Assembly.Bag).Error);
        }

        private static Item Grilled(string id) =>
            TestItems.Ok(Factory.Apply(Griddle.OperationId, new[] { Base(id) }, Griddle.Params(66f, 66f)));

        // ---- recipe templates: the score ----

        [Fact]
        public void AFullFalafelWrap_InOrder_WithTheRightSauce_HasFactorOne_AndIsNamedAfterTheDish()
        {
            Item wrap = FalafelWrap();
            TemplateScore s = RecipeTemplates.Score(wrap, T(RecipeTemplates.FalafelWrapId));
            Assert.Equal(0, s.Missing);
            Assert.Equal(0, s.WrongPrep);
            Assert.Equal(0, s.OutOfOrder);
            Assert.Equal(0, s.SauceOff);
            Assert.Equal(0, s.CloseShort);
            Assert.Equal(0, s.PackShort);
            Assert.Equal(1f, s.Factor);
            Assert.Equal(5, s.Claimed);
            Assert.Equal(RecipeTemplates.FalafelWrapId, RecipeTemplates.Best(wrap)!.Template.Id);
            Assert.Equal(s.Quality, Quality(wrap), 5);
            Assert.True(s.Quality > 0.3f, "a good wrap rates well: " + s.Quality);
            Assert.Equal("falafel wrap", Name(FalafelWrapOpen()));
            Assert.Equal("folded falafel wrap", Name(TestItems.Ok(Close(FalafelWrapOpen(), Assembly.Folded))));
            Assert.Equal("falafel wrap in paper", Name(wrap));
            Assert.Equal(1f, s.DonenessFactor, 4);
        }

        [Fact]
        public void WrongOrder_CostsALittle_MissingLayersCostMore_ExtrasAreNeutral()
        {
            Item good = FalafelWrapOpen();
            Item swapped = TestItems.Ok(Assemble(Base("wrap"), Lettuce(), Cucumber(), Falafel(), Sauce()));
            Item noSauce = TestItems.Ok(Assemble(Base("wrap"), Falafel(), Lettuce(), Cucumber()));
            Item extra = TestItems.Ok(Assemble(Base("wrap"), Falafel(), Lettuce(), Cucumber(), Sauce(), Cut(Base("red_cabbage"), "slices")));
            RecipeTemplate t = T(RecipeTemplates.FalafelWrapId);
            TemplateScore g = RecipeTemplates.Score(good, t), o = RecipeTemplates.Score(swapped, t), m = RecipeTemplates.Score(noSauce, t),
                e = RecipeTemplates.Score(extra, t);
            Assert.Equal(2, o.OutOfOrder);
            Assert.True(o.Quality < g.Quality, "out of order costs");
            Assert.Equal(1, m.Missing);
            Assert.True(1f - m.Factor > 1f - o.Factor, "a missing layer costs more than the wrong order");
            Assert.Equal(g.Factor, e.Factor);
            Assert.Equal(g.Quality, e.Quality, 5);
            Assert.Equal(g.Claimed, e.Claimed);
            Assert.Equal("wrap with golden falafel balls, lettuce slices and cucumber slices", Name(noSauce));
        }

        [Fact]
        public void SauceAmount_TooLittleOrTooMuchCostsALittle()
        {
            RecipeTemplate t = T(RecipeTemplates.FalafelWrapId);
            float Factor(float seconds) =>
                RecipeTemplates.Score(TestItems.Ok(Assemble(Base("wrap"), Falafel(), Lettuce(), Sauce(seconds: seconds))), t).Factor;
            float right = Factor(1f); // open (no close yet): the close penalty is in all three
            Assert.Equal(1f - RecipeTemplates.CloseMissingPenalty, right, 5);
            Assert.Equal(right - RecipeTemplates.SaucePenalty, Factor(0.2f), 5);
            Assert.Equal(right - RecipeTemplates.SaucePenalty, Factor(3f), 5);
        }

        [Fact]
        public void RawFalafel_IsThereButPreparedWrong_TheCloseAndPackStepsCount()
        {
            RecipeTemplate t = T(RecipeTemplates.FalafelWrapId);
            TemplateScore raw = RecipeTemplates.Score(TestItems.Ok(Assemble(Base("wrap"), Base("falafel_mix"), Lettuce(), Sauce())), t);
            Assert.Equal(0, raw.Missing);
            Assert.Equal(1, raw.WrongPrep);
            TemplateScore open = RecipeTemplates.Score(FalafelWrapOpen(), t);
            TemplateScore folded = RecipeTemplates.Score(TestItems.Ok(Close(FalafelWrapOpen(), Assembly.Folded)), t);
            Assert.Equal(1f - RecipeTemplates.CloseMissingPenalty, open.Factor, 5);
            Assert.Equal(1f - RecipeTemplates.ClosePartialPenalty, folded.Factor, 5);
            Item veggies = TestItems.Ok(Assemble(DeepFried(Cut(Base("potato"), "sticks")), Grilled("paprika"), Sauce()));
            RecipeTemplate box = T(RecipeTemplates.VeggieBoxId);
            Assert.Equal(1f - RecipeTemplates.PackMissingPenalty, RecipeTemplates.Score(veggies, box).Factor, 5);
            Item boxed = TestItems.Ok(Pack(veggies, Assembly.Box));
            Assert.Equal(1f, RecipeTemplates.Score(boxed, box).Factor);
            Assert.Equal("veggie box", Name(boxed));
            Assert.Equal(RecipeTemplates.VeggieBoxId, RecipeTemplates.Best(boxed)!.Template.Id);
        }

        [Fact]
        public void EveryLayersQuality_Counts_ABurntFalafelRatesLower()
        {
            Item good = FalafelWrapOpen();
            Item burnt = TestItems.Ok(Assemble(Base("wrap"), DeepFried(Base("falafel_mix"), 400f), Lettuce(), Cucumber(), Sauce()));
            Assert.True(Quality(burnt) < Quality(good), Quality(burnt) + " vs " + Quality(good));
            Assert.True(ItemRater.Rate(burnt).DonenessFactor < 1f);
        }

        [Fact]
        public void TheOtherDishes_SeitanWrapFlatbreadAndVeggieBox_ScoreFullWhenBuiltRight()
        {
            Item strips = TestItems.Ok(Factory.Apply(Griddle.OperationId, new[] { Cut(Base("seitan"), "sticks") }, Griddle.Params(78f, 78f)));
            Item seitanWrap = TestItems.Ok(Close(TestItems.Ok(Close(TestItems.Ok(
                Assemble(Base("wrap"), strips, Lettuce(), Sauce("chili_sauce"))), Assembly.Folded)), Assembly.Wrapped));
            Assert.Equal(1f, RecipeTemplates.Score(seitanWrap, T(RecipeTemplates.SeitanWrapId)).Factor);
            Assert.Equal("seitan wrap in paper", Name(seitanWrap));
            Item flatbread = TestItems.Ok(Pack(TestItems.Ok(Close(TestItems.Ok(
                Assemble(Base("flatbread"), strips, Cut(Base("red_cabbage"), "slices"), Sauce())), Assembly.Folded)), Assembly.Bag));
            Assert.Equal(1f, RecipeTemplates.Score(flatbread, T(RecipeTemplates.SeitanFlatbreadId)).Factor);
            Assert.Equal("seitan flatbread in a bag", Name(flatbread));
            Assert.Equal(4, RecipeTemplates.Default.Count);
            Assert.Null(RecipeTemplates.ById("nope"));
            Assert.Null(RecipeTemplates.Best(Lettuce()));
        }

        // ---- no exploit: repeated steps never stack ----

        [Fact]
        public void RepeatedSteps_CannotRaiseTheScore()
        {
            Item good = FalafelWrapOpen();
            float q = Quality(good);
            // More of the same good layers (lettuce, sauce, shaken falafel) on top: extras, neutral.
            Item piled = TestItems.Ok(Assemble(Base("wrap"), Falafel(), Lettuce(), Cucumber(), Sauce(),
                Lettuce(), Lettuce(), Sauce(), Sauce(), Falafel(true)));
            Assert.True(Quality(piled) <= q + 1e-6f, Quality(piled) + " vs " + q);
            Assert.True(RecipeTemplates.Score(piled, T(RecipeTemplates.FalafelWrapId)).Factor <= 1f);
            // Every factor and every rating stays within 0..1, however a build is closed.
            foreach (RecipeTemplate t in RecipeTemplates.Default)
            {
                TemplateScore s = RecipeTemplates.Score(FalafelWrap(), t);
                Assert.InRange(s.Factor, RecipeTemplates.MinFactor, 1f);
                Assert.InRange(s.Quality, 0f, 1f);
            }
            // A shaken falafel is the fryer's bonus, kept once (capped in 029b), never added per layer.
            Item shaken = TestItems.Ok(Assemble(Base("wrap"), Falafel(true), Lettuce(), Cucumber(), Sauce()));
            Item twoShaken = TestItems.Ok(Assemble(Base("wrap"), Falafel(true), Lettuce(), Cucumber(), Sauce(), Falafel(true)));
            Assert.Equal(Quality(shaken), Quality(twoShaken), 5);
        }

        [Fact]
        public void Determinism_TheSameStepsGiveTheSameHashNameAndRating()
        {
            Item a = FalafelWrap(), b = FalafelWrap();
            Assert.Equal(a.Hash, b.Hash);
            Assert.Equal(a.CanonicalTree, b.CanonicalTree);
            Assert.Equal(Name(a), Name(b));
            Assert.Equal(Quality(a), Quality(b));
            Assert.StartsWith("close(style=wrapped;close(style=folded;assemble(;wrap,", a.CanonicalTree);
        }

        [Fact]
        public void OldItems_KeepTheirHashes_TheNewOpsAreAppended()
        {
            Item fries = TestItems.Fries();
            Assert.False(Assembly.IsBuild(fries));
            OperationCatalog ops = OperationCatalog.CreateDefault();
            foreach (string id in new[] { Assembly.SqueezeOperationId, Assembly.AssembleOperationId, Assembly.CloseOperationId, Assembly.PackOperationId })
                Assert.True(ops.TryGet(id, out _), id);
            Assert.True(ops.TryGet(Assembly.AssembleOperationId, out OperationDef assemble));
            Assert.False(assemble.Commutative, "the order of the layers counts");
            Assert.Equal(Assembly.Station, assemble.Station);
        }

        [Fact]
        public void Helpers_ReadTheLayers()
        {
            Item open = FalafelWrapOpen();
            Assert.True(Assembly.HasBreadBase(open));
            Assert.Equal("wrap", Assembly.BaseOf(Assembly.LayersOf(open)[0]));
            Assert.Equal(DeepFry.OperationId, Assembly.HeatOpOf(Assembly.LayersOf(open)[1]));
            Assert.Null(Assembly.HeatOpOf(Lettuce()));
            Assert.True(Assembly.IsCut(Lettuce()));
            Assert.False(Assembly.IsCut(Base("wrap")));
            Assert.Empty(Assembly.LayersOf(Lettuce()));
            Item plated = TestItems.Ok(Factory.Apply("plate", new[] { TestItems.Ok(Close(open, Assembly.Folded)) }, TestItems.NoParams));
            Assert.True(Assembly.IsBuild(plated), "a folded wrap on a plate is still the build");
            Assert.Equal(Assembly.Folded, Assembly.CloseOf(plated));
            Assert.Null(Assembly.PackOf(plated));
            Assert.Equal(OperationError.None, Assembly.CheckStep(Assembly.CloseOperationId, Assembly.Folded, open));
            Assert.Equal(OperationError.StepOrder, Assembly.CheckStep(Assembly.CloseOperationId, "spin", open));
            Assert.NotNull(RecipeTemplates.Named(open));
            Assert.Null(RecipeTemplates.Named(TestItems.Ok(Assemble(Lettuce()))));
            Assert.Throws<ArgumentNullException>(() => RecipeTemplates.Score(null!, RecipeTemplates.Default[0]));
        }

        // ---- Spec 031b (review of 031): old items keep their hashes and ratings ----

        /// <summary>
        /// Pinned at commit 1710193 (spec 030, before 031): crispy fried potato sticks, grilled seitan strips (13 kitchen s a side) and a
        /// sauteed potato. Spec 031 and 031b changed the op catalog, the factory and the rater; these must not move.
        /// <para>
        /// Spec 043 re-pinned them on purpose (the reasons, written 2026-11-11):
        /// <list type="bullet">
        /// <item>The saute: 50 s at the 2x pan is the old dose, so its quality stays 0.121. But the dish is undercooked now, so its
        /// time bucket is "short" (zone buckets), its name "lightly sauteed potato" and its hash new.</item>
        /// <item>The fries are now pinned at 80 s at the 2x pan (<see cref="TestItems.Fries"/>): that is Perfect, with the same
        /// hash and name.</item>
        /// <item>The seitan at 13 kitchen s a side is Good, so its rating carries the 0.9 Good factor
        /// (<see cref="DonenessZones.GoodFactor"/>).</item>
        /// </list>
        /// </para>
        /// </summary>
        [Fact]
        public void Pre031Items_KeepTheirHashesAndRatings()
        {
            Item fries = TestItems.Fries();
            Item strips = Cut(Base("seitan"), "sticks");
            float side = 13f * DeepFry.KitchenTimeScale;
            Item grilled = TestItems.Ok(Factory.Apply(Griddle.OperationId, new[] { strips }, Griddle.Params(side, side)));
            Item pan = TestItems.Ok(TestItems.Heat("saute", TestItems.Base("potato"), 150f, 50f));

            Assert.Equal("2ab1f86227c96055d1d69a98ce1485820e94a0805096d923ef51a6b4286857d3", fries.Hash);
            Assert.Equal(0.69777298f, Quality(fries));
            Assert.Equal("crispy fried potato sticks", Name(fries));
            Assert.Equal("f81768d9941d99ff3e8ded4f87ddbd6dcc22577c3b562bca9b112bcefc3f5799", grilled.Hash);
            Assert.Equal(0.37411365f, Quality(grilled));
            Assert.Equal("grilled seitan strips", Name(grilled));
            Assert.Equal("ee6d91d9e4a6bff4c11e4ebc9e532091d41250f952b77a134fd7d7232bce2ce9", pan.Hash);
            Assert.Equal(0.121469915f, Quality(pan));
            Assert.Equal("lightly sauteed potato", Name(pan));
        }

        // ---- Spec 031b (review of 031): builds stay builds ----

        [Fact]
        public void ABuild_IsNeverCutHeatedOrMixed_AndAPlateTakesItOnlyAlone()
        {
            Item open = FalafelWrapOpen();
            Item folded = TestItems.Ok(Close(open, Assembly.Folded));
            foreach (Item build in new[] { open, folded })
            {
                Assert.Equal(OperationError.ForbiddenInput, Factory.Apply("cut", new[] { build },
                    new Dictionary<string, ParamValue> { ["shape"] = ParamValue.Choice("slices") }).Error);
                foreach (string op in new[] { "fry", "saute" })
                    Assert.Equal(OperationError.ForbiddenInput, Factory.Apply(op, new[] { build }, new Dictionary<string, ParamValue>
                    {
                        ["temp"] = ParamValue.Number(150f),
                        ["time"] = ParamValue.Number(60f),
                    }).Error);
                Assert.Equal(OperationError.ForbiddenInput, Factory.Apply("boil", new[] { build },
                    new Dictionary<string, ParamValue> { ["time"] = ParamValue.Number(60f) }).Error);
                Assert.Equal(OperationError.ForbiddenInput, Factory.Apply(Griddle.OperationId, new[] { build }, Griddle.Params(10f, 10f)).Error);
                Assert.Equal(OperationError.ForbiddenInput, Factory.Apply(Griddle.ToastOperationId, new[] { build }, Griddle.Params(10f, 10f)).Error);
                Assert.Equal(OperationError.ForbiddenInput, Factory.Apply(DeepFry.OperationId, new[] { build }, new Dictionary<string, ParamValue>
                {
                    ["temp"] = ParamValue.Number(DeepFry.ReferenceTempC),
                    ["time"] = ParamValue.Number(60f),
                    [DeepFry.ShakeParam] = ParamValue.Choice(DeepFry.NotShaken),
                    [DeepFry.DrainParam] = ParamValue.Choice(DeepFry.Drained),
                }).Error);
                Assert.Equal(OperationError.ForbiddenInput, Factory.Apply("mix", new[] { build, Lettuce() }, TestItems.NoParams).Error);
                // Never on a plate with other food.
                Assert.Equal(OperationError.ForbiddenInput, Factory.Apply("plate", new[] { build, Lettuce() }, TestItems.NoParams).Error);
                Assert.True(Factory.Apply("plate", new[] { build }, TestItems.NoParams).Ok, "alone: the counter plates it as it is");
            }
            Assert.True(Factory.Apply("plate", new[] { Lettuce(), Cucumber() }, TestItems.NoParams).Ok, "other food plates as before");
        }

        [Fact]
        public void OnlyASingleIngredientChain_FillsATemplateLayer()
        {
            Assert.True(Assembly.IsSingleChain(Base("wrap")));
            Assert.True(Assembly.IsSingleChain(Lettuce()));
            Assert.True(Assembly.IsSingleChain(Falafel()));
            Assert.True(Assembly.IsSingleChain(Sauce()));
            Item mix = TestItems.Ok(Factory.Apply("mix", new[] { Lettuce(), Cucumber() }, TestItems.NoParams));
            Item plate = TestItems.Ok(Factory.Apply("plate", new[] { Falafel() }, TestItems.NoParams));
            Assert.False(Assembly.IsSingleChain(mix));
            Assert.False(Assembly.IsSingleChain(plate));
            Assert.False(Assembly.IsSingleChain(FalafelWrapOpen()));

            // A plate never goes on a build at all; falafel mixed with lettuce is neither a falafel nor a lettuce layer: the
            // template counts both missing.
            Assert.Equal(OperationError.ForbiddenInput, Assemble(Base("wrap"), plate).Error);
            Item mixed = TestItems.Ok(Factory.Apply("mix", new[] { Falafel(), Lettuce() }, TestItems.NoParams));
            Item real = TestItems.Ok(Assemble(Base("wrap"), Falafel(), Lettuce(), Sauce()));
            Item fake = TestItems.Ok(Assemble(Base("wrap"), mixed, Sauce()));
            TemplateScore good = RecipeTemplates.Score(real, T("falafel_wrap"));
            TemplateScore bad = RecipeTemplates.Score(fake, T("falafel_wrap"));
            Assert.Equal(0, good.Missing);
            Assert.True(bad.Missing > good.Missing);
            Assert.True(bad.Quality < good.Quality);
        }
    }
}
