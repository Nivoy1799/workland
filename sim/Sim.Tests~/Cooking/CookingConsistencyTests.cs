using System;
using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Cooking;
using Xunit;
using Xunit.Abstractions;

namespace Worktown.Sim.Tests.Cooking
{
    /// <summary>
    /// Spec 043 AC2: on every heat station (pan, pot, fryer, griddle, spit) the best rating lies in the Perfect zone, the name and
    /// menu buckets change exactly where the zones do, and an undercooked build is one step short of its order (Close, not Exact).
    /// Each case cooks one food along its station's time range in small steps with the real default op catalog.
    /// </summary>
    public class CookingConsistencyTests
    {
        private static readonly ItemCatalog Items = new ItemCatalog(StreetFoodItems.All.Concat(StreetFoodItems.GrillVegetables)
            .Concat(new[] { TestItems.Potato, TestItems.Apple, TestItems.Tomato, TestItems.Onion }));
        private static readonly ItemFactory Factory = new ItemFactory(Items, OperationCatalog.CreateDefault());

        private readonly ITestOutputHelper _out;

        public CookingConsistencyTests(ITestOutputHelper output) { _out = output; }

        private static Item Base(string id) => TestItems.Ok(Factory.CreateBase(id));
        private static Item Cut(Item input, string shape) => TestItems.Ok(Factory.Apply("cut", new[] { input },
            new Dictionary<string, ParamValue> { ["shape"] = ParamValue.Choice(shape) }));
        private static float Quality(Item item) => ItemRater.Rate(item).Quality;
        private static string Name(Item item) => ItemNaming.FallbackName(item, NameCatalog.Empty);
        private static DonenessZone Zone(Item item) => DonenessZones.Of(item.Components);

        private static IReadOnlyDictionary<string, ParamValue> Pan(float seconds) => new Dictionary<string, ParamValue>
        {
            ["temp"] = ParamValue.Number(190f), // the kitchen's fixed pan heat (StoveSettings.PanTempC)
            ["time"] = ParamValue.Number(seconds),
        };

        private static IReadOnlyDictionary<string, ParamValue> Pot(float seconds) =>
            new Dictionary<string, ParamValue> { ["time"] = ParamValue.Number(seconds) };

        private static IReadOnlyDictionary<string, ParamValue> Fryer(float seconds) => new Dictionary<string, ParamValue>
        {
            ["temp"] = ParamValue.Number(DeepFry.ReferenceTempC),
            ["time"] = ParamValue.Number(seconds),
            [DeepFry.ShakeParam] = ParamValue.Choice(DeepFry.NotShaken),
            [DeepFry.DrainParam] = ParamValue.Choice(DeepFry.Drained),
        };

        /// <summary>One food on one station: the op, its input, the op's parameters for a time, and the range to cook it over.</summary>
        public sealed record Case(string Label, string Op, Func<Item> Input, Func<float, IReadOnlyDictionary<string, ParamValue>> Params,
            float From, float To, float Step);

        private static readonly Case[] Cases =
        {
            new Case("pan: fried potato sticks", "fry", () => Cut(Base("potato"), "sticks"), Pan, 10f, 600f, 0.5f),
            new Case("pan: fried whole potato", "fry", () => Base("potato"), Pan, 10f, 600f, 0.5f),
            new Case("pan: sauteed apple slices", "saute", () => Cut(Base("apple"), "slices"), Pan, 10f, 600f, 0.5f),
            new Case("pot: boiled potato", "boil", () => Base("potato"), Pot, 30f, 1800f, 1f),
            new Case("pot: boiled onion", "boil", () => Base("onion"), Pot, 30f, 1800f, 1f),
            new Case("fryer: fries", DeepFry.OperationId, () => Cut(Base("potato"), "sticks"), Fryer, 6f, 600f, 0.5f),
            new Case("fryer: falafel", DeepFry.OperationId, () => Base("falafel_mix"), Fryer, 6f, 600f, 0.5f),
            new Case("griddle: seitan strips", Griddle.OperationId, () => Cut(Base("seitan"), "sticks"), s => Griddle.Params(s, s), 0f, 300f, 0.25f),
            new Case("griddle: paprika", Griddle.OperationId, () => Cut(Base("paprika"), "sticks"), s => Griddle.Params(s, s), 0f, 300f, 0.25f),
            new Case("griddle: eggplant", Griddle.OperationId, () => Cut(Base("eggplant"), "slices"), s => Griddle.Params(s, s), 0f, 300f, 0.25f),
            new Case("griddle: mushroom", Griddle.OperationId, () => Cut(Base("mushroom"), "halves"), s => Griddle.Params(s, s), 0f, 300f, 0.25f),
            new Case("griddle: toasted flatbread", Griddle.ToastOperationId, () => Base("flatbread"), s => Griddle.Params(s, s), 0f, 120f, 0.1f),
            new Case("griddle: toasted wrap", Griddle.ToastOperationId, () => Base("wrap"), s => Griddle.Params(s, s), 0f, 120f, 0.1f),
            new Case("spit: shaved seitan", Spit.OperationId, () => Base("seitan"), Spit.ShaveParams, 0f, 900f, 0.5f),
        };

        public static IEnumerable<object[]> CaseIndices() => Enumerable.Range(0, Cases.Length).Select(i => new object[] { i });

        private static List<(float T, Item Item)> Cook(Case c)
        {
            Item input = c.Input();
            var samples = new List<(float, Item)>();
            for (float t = c.From; t <= c.To + 1e-3f; t += c.Step)
            {
                OperationResult r = Factory.Apply(c.Op, new[] { input }, c.Params(t));
                Assert.True(r.Ok, c.Label + " at " + t + ": " + r.Error);
                samples.Add((t, r.Item!));
            }
            return samples;
        }

        [Theory]
        [MemberData(nameof(CaseIndices))]
        public void AC2_TheBestRatingLiesInPerfect_AndPerfectBeatsGood(int index)
        {
            Case c = Cases[index];
            List<(float T, Item Item)> samples = Cook(c);
            (float bestT, Item best) = samples.OrderByDescending(s => Quality(s.Item)).ThenBy(s => s.T).First();
            var perfect = samples.Where(s => Zone(s.Item) == DonenessZone.Perfect).ToList();
            var good = samples.Where(s => Zone(s.Item) == DonenessZone.Good).ToList();
            var outside = samples.Where(s => Zone(s.Item) == DonenessZone.Undercooked || Zone(s.Item) == DonenessZone.Overdone).ToList();
            _out.WriteLine(c.Label + ": best " + Quality(best).ToString("0.0000") + " at " + bestT + " sim s, Perfect from "
                + (perfect.Count > 0 ? perfect[0].T.ToString() : "-") + " to " + (perfect.Count > 0 ? perfect[perfect.Count - 1].T.ToString() : "-"));

            Assert.NotEmpty(perfect);
            Assert.Equal(DonenessZone.Perfect, Zone(best));
            // Every moment in Perfect rates at least as well as any moment in Good or outside the window: the cue is the truth.
            float worstPerfect = perfect.Min(s => Quality(s.Item));
            if (good.Count > 0) Assert.True(worstPerfect > good.Max(s => Quality(s.Item)), c.Label + ": Good beats Perfect");
            if (outside.Count > 0) Assert.True(worstPerfect > outside.Max(s => Quality(s.Item)), c.Label + ": outside the window beats Perfect");
        }

        [Theory]
        [MemberData(nameof(CaseIndices))]
        public void AC2_TheTimeBucketChangesExactlyAtTheZones(int index)
        {
            Case c = Cases[index];
            foreach ((float t, Item item) in Cook(c))
            {
                DonenessZone zone = Zone(item);
                string want = zone == DonenessZone.Undercooked ? "short" : zone == DonenessZone.Overdone ? "long" : "medium";
                Assert.True(want == item.Buckets["time"], c.Label + " at " + t + " sim s is " + zone + " but in bucket " + item.Buckets["time"]);
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(13)]
        public void AC2_TheNamesChangeExactlyAtTheZones(int index)
        {
            // The names players read: pale / golden / dark fries, lightly / plain / dark fried potato sticks, lightly / plain / soft
            // boiled potato, pale / plain / charred shaved seitan. The plain name comes exactly with Good.
            Case c = Cases[index];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach ((float t, Item item) in Cook(c))
            {
                string name = Name(item);
                DonenessZone zone = Zone(item);
                string first = name.Split(' ')[c.Op == "fry" ? 1 : 0]; // the pan's name starts with its heat ("crispy")
                string want = c.Op == DeepFry.OperationId ? (zone == DonenessZone.Undercooked ? "pale" : zone == DonenessZone.Overdone ? "dark" : "golden")
                    : c.Op == Spit.OperationId ? (zone == DonenessZone.Undercooked ? "pale" : zone == DonenessZone.Overdone ? "charred" : "shaved")
                    : c.Op == "boil" ? (zone == DonenessZone.Undercooked ? "lightly" : zone == DonenessZone.Overdone ? "soft" : "boiled")
                    : (zone == DonenessZone.Undercooked ? "lightly" : zone == DonenessZone.Overdone ? "dark" : "fried");
                Assert.True(want == first, c.Label + " at " + t + " sim s is " + zone + " but named \"" + name + "\"");
                seen.Add(first);
            }
            // Follow-up 2026-11-11: every sweep meets all three names (undercooked, done, overdone), so each boundary is checked.
            Assert.True(seen.Count == 3, c.Label + " met only " + string.Join(", ", seen));
        }

        [Fact]
        public void AC2_GriddleNames_GrilledStartsWhereBothSidesAreGood()
        {
            DonenessWindow window = StreetFoodItems.Seitan.IdealDoneness;
            FryCurve curve = Griddle.SeitanSide;
            Item strips = Cut(Base("seitan"), "sticks");
            string NameAt(float a, float b) => Name(TestItems.Ok(Factory.Apply(Griddle.OperationId, new[] { strips }, Griddle.Params(a, b))));
            float good = curve.WindowMinAt, over = curve.WindowMaxAt;

            Assert.Equal("warm seitan strips", NameAt(good - 1f, good - 1f));
            Assert.Equal("half-grilled seitan strips", NameAt(good - 1f, good));
            Assert.Equal("grilled seitan strips", NameAt(good, good));
            Assert.Equal("grilled seitan strips", NameAt(good, over));
            Assert.Equal("charred seitan strips", NameAt(good, over + 1f));
            // The zones the names follow: the side at "good" is just in the window, "over + 1" just past it.
            Assert.Equal(DonenessZone.Good, DonenessZones.Of(DeepFry.DonenessAt(curve, window, good), window));
            Assert.Equal(DonenessZone.Overdone, DonenessZones.Of(DeepFry.DonenessAt(curve, window, over + 1f), window));
        }

        private static readonly string[] TimeBuckets = { "short", "medium", "long" };

        [Fact]
        public void AC2_GriddleUnequalSides_RateByTheMeanOfBothSides()
        {
            // Follow-up 2026-11-11: pins today's rule; inbox question 16 lets the human pick a stricter "per side" rule later. Side B
            // runs ahead of side A by the Good half of the window, so B reaches Perfect when A reaches Good. While both sides are in
            // the window the item's doneness (what the stars read) is their mean, so one Good and one Perfect side can rate Perfect.
            // Each side's own zone names its bucket ("time" the shorter side, the long side its own).
            FryCurve curve = Griddle.SeitanSide;
            DonenessWindow window = StreetFoodItems.Seitan.IdealDoneness;
            float lead = curve.PerfectAt - curve.WindowMinAt;
            var unequal = new Case("griddle: seitan strips, side B ahead", Griddle.OperationId, () => Cut(Base("seitan"), "sticks"),
                s => Griddle.Params(s, s + lead), 0f, 300f, 0.25f);
            float start = unequal.Input().Components[0].Doneness;
            bool goodAndPerfectRatePerfect = false;
            List<(float T, Item Item)> samples = Cook(unequal);
            foreach ((float t, Item item) in samples)
            {
                float a = Griddle.SideDoneness(curve, window, start, t);
                float b = Griddle.SideDoneness(curve, window, start, t + lead);
                float d = item.Components[0].Doneness;
                if (Griddle.Distance(a, window) <= 0f && Griddle.Distance(b, window) <= 0f)
                    Assert.True(Math.Abs((a + b) * 0.5f - d) < 1e-5f, unequal.Label + " at " + t + ": not the mean of both sides");
                else
                    Assert.True(Math.Abs(Griddle.CombineSides(a, b, window) - d) < 1e-5f, unequal.Label + " at " + t + ": not the far side");
                Assert.Equal(TimeBuckets[DonenessZones.BucketIndex(DonenessZones.Of(a, window))], item.Buckets["time"]);
                Assert.Equal(TimeBuckets[DonenessZones.BucketIndex(DonenessZones.Of(b, window))], item.Buckets[Griddle.LongSideParam]);
                if (DonenessZones.Of(a, window) == DonenessZone.Good && DonenessZones.Of(b, window) == DonenessZone.Perfect
                    && Zone(item) == DonenessZone.Perfect)
                    goodAndPerfectRatePerfect = true;
            }
            Assert.True(goodAndPerfectRatePerfect, "one Good and one Perfect side never rated Perfect");

            (float bestT, Item best) = samples.OrderByDescending(s => Quality(s.Item)).ThenBy(s => s.T).First();
            _out.WriteLine(unequal.Label + ": best " + Quality(best).ToString("R") + " at " + bestT + " sim s (lead " + lead + ")");
            Assert.Equal(DonenessZone.Perfect, Zone(best));
            // Pinned 2026-11-11 (mean of both sides): side A at 84 sim s, side B 12 s ahead.
            Assert.Equal(84f, bestT);
            Assert.Equal(0.40729168f, Quality(best));
        }

        [Fact]
        public void AC2_TheCurveStationsStartGoodAtTheirPaceTableTimes()
        {
            // Fries Good at 25 kitchen s, falafel at 20, seitan strips at 12 a side, the spit at 40: the first "medium" bucket.
            (Case c, float goodSim)[] checks =
            {
                (Cases[5], CookingPace.Fries.WindowMinAt), (Cases[6], CookingPace.Falafel.WindowMinAt),
                (Cases[7], CookingPace.SeitanSide.WindowMinAt), (Cases[13], CookingPace.SpitOuter.WindowMinAt),
            };
            foreach ((Case c, float goodSim) in checks)
            {
                float first = Cook(c).First(s => s.Item.Buckets["time"] == "medium").T;
                Assert.True(Math.Abs(first - goodSim) <= c.Step + 1e-3f, c.Label + ": medium from " + first + ", Good from " + goodSim);
            }
        }

        // ---- an undercooked build is Close, not Exact ----

        private static Item Squeeze(string sauce) =>
            TestItems.Ok(Factory.Apply(Assembly.SqueezeOperationId, new[] { Base(sauce) }, Assembly.SqueezeParams(1f)));
        private static Item Wrapped(params Item[] layers)
        {
            Item open = TestItems.Ok(Factory.Apply(Assembly.AssembleOperationId, layers, TestItems.NoParams));
            Item folded = TestItems.Ok(Factory.Apply(Assembly.CloseOperationId, new[] { open }, Assembly.CloseParams(Assembly.Folded)));
            return TestItems.Ok(Factory.Apply(Assembly.CloseOperationId, new[] { folded }, Assembly.CloseParams(Assembly.Wrapped)));
        }
        private static Item Boxed(params Item[] layers)
        {
            Item open = TestItems.Ok(Factory.Apply(Assembly.AssembleOperationId, layers, TestItems.NoParams));
            return TestItems.Ok(Factory.Apply(Assembly.PackOperationId, new[] { open }, Assembly.PackParams(Assembly.Box)));
        }
        private static Item Shaved(float kitchenSeconds) =>
            TestItems.Ok(Factory.Apply(Spit.OperationId, new[] { Base("seitan") }, Spit.ShaveParams(kitchenSeconds * DeepFry.KitchenTimeScale)));
        private static Item Grilled(Item input, float sideKitchenSeconds) => TestItems.Ok(Factory.Apply(Griddle.OperationFor(input),
            new[] { input }, Griddle.Params(sideKitchenSeconds * DeepFry.KitchenTimeScale, sideKitchenSeconds * DeepFry.KitchenTimeScale)));
        private static Item DeepFried(Item input, float kitchenSeconds) =>
            TestItems.Ok(Factory.Apply(DeepFry.OperationId, new[] { input }, Fryer(kitchenSeconds * DeepFry.KitchenTimeScale)));

        private static int Misses(Item build, string templateId) =>
            OrderModifiers.Check(build, RecipeTemplates.ById(templateId)!, Array.Empty<OrderModifier>()).Misses;

        [Fact]
        public void AC2_AnUndercookedSeitanWrap_IsOneStepShort()
        {
            Item lettuce = Cut(Base("lettuce"), "slices");
            Item Wrap(Item seitan) => Wrapped(Base("wrap"), seitan, lettuce, Squeeze("chili_sauce"));
            Assert.Equal(0, Misses(Wrap(Shaved(55f)), RecipeTemplates.SeitanWrapId));
            Assert.Equal(1, Misses(Wrap(Shaved(30f)), RecipeTemplates.SeitanWrapId)); // pale: Good starts at 40 s
            Assert.Equal(0, Misses(Wrap(Grilled(Cut(Base("seitan"), "sticks"), 14f)), RecipeTemplates.SeitanWrapId));
            Assert.Equal(1, Misses(Wrap(Grilled(Cut(Base("seitan"), "sticks"), 10f)), RecipeTemplates.SeitanWrapId)); // warm: Good at 12 s
            // Overdone is no miss (the menu's rule: at least done); it only rates lower.
            Assert.Equal(0, Misses(Wrap(Shaved(75f)), RecipeTemplates.SeitanWrapId));
            Assert.True(Quality(Wrap(Shaved(75f))) < Quality(Wrap(Shaved(55f))));
        }

        [Fact]
        public void AC2_AnUndercookedFalafelWrapAndVeggieBox_AreOneStepShort()
        {
            Item lettuce = Cut(Base("lettuce"), "slices");
            Item Falafel(float s) => Wrapped(Base("wrap"), DeepFried(Base("falafel_mix"), s), lettuce, Squeeze("garlic_herb_sauce"));
            Assert.Equal(0, Misses(Falafel(25f), RecipeTemplates.FalafelWrapId));
            Assert.Equal(1, Misses(Falafel(15f), RecipeTemplates.FalafelWrapId)); // pale: Good at 20 s

            Item sticks = Cut(Base("potato"), "sticks");
            Item paprika = Cut(Base("paprika"), "sticks");
            Item Box(float fries, float veg) => Boxed(DeepFried(sticks, fries), Grilled(paprika, veg), Squeeze("chili_sauce"));
            Assert.Equal(0, Misses(Box(30f, 12f), RecipeTemplates.VeggieBoxId));
            Assert.Equal(1, Misses(Box(20f, 12f), RecipeTemplates.VeggieBoxId)); // pale fries: Good at 25 s
            Assert.Equal(1, Misses(Box(30f, 8f), RecipeTemplates.VeggieBoxId)); // warm paprika: Good at 10 s
        }

        // ---- the zone rules themselves ----

        [Fact]
        public void Zones_OfOneDoneness_FollowTheWindowHalves()
        {
            var w = new DonenessWindow(0.5f, 0.9f);
            Assert.Equal(0.7f, w.PerfectFrom, 6);
            Assert.Equal(DonenessZone.Undercooked, DonenessZones.Of(0.49f, w));
            Assert.Equal(DonenessZone.Good, DonenessZones.Of(0.5f, w));
            Assert.Equal(DonenessZone.Good, DonenessZones.Of(0.69f, w));
            Assert.Equal(DonenessZone.Perfect, DonenessZones.Of(0.7f, w));
            Assert.Equal(DonenessZone.Perfect, DonenessZones.Of(0.9f, w));
            Assert.Equal(DonenessZone.Overdone, DonenessZones.Of(0.91f, w));
            Assert.Throws<ArgumentNullException>(() => DonenessZones.Of(0.5f, null!));
        }

        [Fact]
        public void Zones_OfSeveralParts_TheWorstPartDecides()
        {
            var w = new DonenessWindow(0.5f, 0.9f);
            ItemComponent P(float d) => new ItemComponent("x", d, w);
            Assert.Equal(DonenessZone.Undercooked, DonenessZones.Of(Array.Empty<ItemComponent>()));
            Assert.Equal(DonenessZone.Perfect, DonenessZones.Of(new[] { P(0.8f), P(0.7f) }));
            Assert.Equal(DonenessZone.Good, DonenessZones.Of(new[] { P(0.8f), P(0.6f) }));
            Assert.Equal(DonenessZone.Undercooked, DonenessZones.Of(new[] { P(0.8f), P(0.4f) }));
            Assert.Equal(DonenessZone.Overdone, DonenessZones.Of(new[] { P(0.95f), P(0.4f) }));
        }

        [Fact]
        public void Factor_IsBestInPerfect_ALittleLessInGood_AndFallsOffOutside()
        {
            var w = new DonenessWindow(0.5f, 0.9f);
            Assert.Equal(1f, DonenessZones.Factor(0.7f, w));
            Assert.Equal(1f, DonenessZones.Factor(0.9f, w));
            Assert.Equal(DonenessZones.GoodFactor, DonenessZones.Factor(0.5f, w));
            Assert.Equal(DonenessZones.GoodFactor * 0.5f, DonenessZones.Factor(0.4f, w), 5);
            Assert.Equal(0.5f, DonenessZones.Factor(1.0f, w), 5);
            Assert.Equal(0f, DonenessZones.Factor(0.2f, w));
            // Raw food that may be raw is at its best untouched; once heated its window halves count.
            var raw = new DonenessWindow(0f, 0.3f);
            Assert.Equal(1f, DonenessZones.Factor(0f, raw));
            Assert.Equal(DonenessZones.GoodFactor, DonenessZones.Factor(0.1f, raw));
            Assert.Equal(1f, DonenessZones.Factor(0.2f, raw));
        }

        [Fact]
        public void BucketIndex_GoodAndPerfectShareTheDoneBucket()
        {
            Assert.Equal(0, DonenessZones.BucketIndex(DonenessZone.Undercooked));
            Assert.Equal(1, DonenessZones.BucketIndex(DonenessZone.Good));
            Assert.Equal(1, DonenessZones.BucketIndex(DonenessZone.Perfect));
            Assert.Equal(2, DonenessZones.BucketIndex(DonenessZone.Overdone));
        }

        [Fact]
        public void IsUndercooked_ReadsTheHeatOpsTimeBucket()
        {
            Assert.False(DonenessZones.IsUndercooked(null));
            Assert.False(DonenessZones.IsUndercooked(Base("seitan")));
            Assert.False(DonenessZones.IsUndercooked(Cut(Base("lettuce"), "slices")));
            Assert.True(DonenessZones.IsUndercooked(Shaved(20f)));
            Assert.False(DonenessZones.IsUndercooked(Shaved(50f)));
            Assert.False(DonenessZones.IsUndercooked(Shaved(80f))); // overdone is not undercooked
            Assert.True(DonenessZones.IsUndercooked(Grilled(Cut(Base("seitan"), "sticks"), 6f)));
            Assert.False(DonenessZones.IsUndercooked(Squeeze("chili_sauce")));
        }

        [Fact]
        public void IsUndercooked_OnlyForHeatOpsWithZoneBuckets()
        {
            // Follow-up 2026-11-11: the same "short" time bucket means undercooked only when the op buckets by zone. A heat op without
            // zone buckets picks its bucket from the seconds, so its first bucket says nothing about the doneness zone.
            Assert.True(OperationCatalog.CreateDefault().TryGet("boil", out OperationDef boil));
            OperationDef bySeconds = boil with { Heat = boil.Heat! with { ZoneBuckets = false } };
            var plain = new ItemFactory(Items, new OperationCatalog(new[] { bySeconds }));
            Item potato = Base("potato");
            Item zoned = TestItems.Ok(Factory.Apply("boil", new[] { potato }, Pot(30f)));
            Item seconds = TestItems.Ok(plain.Apply("boil", new[] { TestItems.Ok(plain.CreateBase("potato")) }, Pot(30f)));

            Assert.Equal(DonenessZones.UnderBucket, zoned.Buckets["time"]);
            Assert.Equal(DonenessZones.UnderBucket, seconds.Buckets["time"]);
            Assert.True(DonenessZones.IsUndercooked(zoned));
            Assert.False(DonenessZones.IsUndercooked(seconds));
            // The flag is not part of the identity: the same op id, buckets and inputs give the same hash either way.
            Assert.Equal(zoned.Hash, seconds.Hash);
        }

        [Fact]
        public void PaceTable_IsWhatTheStationsUse_AndThePanAndPotRunTwiceAsFast()
        {
            Assert.Same(CookingPace.Fries, DeepFry.Fries);
            Assert.Same(CookingPace.Falafel, DeepFry.Falafel);
            Assert.Same(CookingPace.SeitanSide, Griddle.SeitanSide);
            Assert.Same(CookingPace.FlatbreadSide, Griddle.FlatbreadSide);
            Assert.Same(CookingPace.SpitOuter, Spit.OuterCurve);
            Assert.Equal(2f, CookingPace.PanPotSpeed);
            // Perfect is the middle of the window on every curve: Fries 25 / 30 / 45 kitchen s.
            Assert.Equal(30f * DeepFry.KitchenTimeScale, CookingPace.Fries.PerfectAt, 3);
            Assert.Equal(55f * DeepFry.KitchenTimeScale, CookingPace.SpitOuter.PerfectAt, 3);
        }

        [Fact]
        public void ZoneBuckets_NeedThreeTimeBuckets()
        {
            var two = new ParamDef("time", ParamKind.Number, 10f, 100f, new[] { new BucketDef("a", 10f, ""), new BucketDef("b", 50f, "") });
            var op = new OperationDef("zap", "pan", OperationKind.Heat, false, 1, 1, new[] { two },
                new HeatProfile(100f, 0f, 0f, 0f, 0f, 0f, ZoneBuckets: true), Array.Empty<string>(), Array.Empty<string>(), "{in}");
            Assert.Throws<ArgumentException>(() => new OperationCatalog(new[] { op }));
            Assert.NotNull(new OperationCatalog(new[] { op with { Heat = op.Heat! with { ZoneBuckets = false } } }));
        }

        [Fact]
        public void ZoneBuckets_AreDeterministic_AndKeepTheExactTime()
        {
            Item sticks = Cut(Base("potato"), "sticks");
            Item a = TestItems.Ok(Factory.Apply("fry", new[] { sticks }, Pan(80f)));
            Item b = TestItems.Ok(Factory.Apply("fry", new[] { sticks }, Pan(80f)));
            Assert.Equal(a.Hash, b.Hash);
            Assert.Equal(Quality(a), Quality(b));
            // Two times in the same zone share the hash (the bucket) but not the doneness.
            Item c = TestItems.Ok(Factory.Apply("fry", new[] { sticks }, Pan(85f)));
            Assert.Equal(a.Hash, c.Hash);
            Assert.NotEqual(a.Components[0].Doneness, c.Components[0].Doneness);
        }
    }
}
