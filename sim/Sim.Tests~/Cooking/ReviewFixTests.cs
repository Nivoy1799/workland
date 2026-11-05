using System;
using System.Collections.Generic;
using Worktown.Sim.Cooking;
using Xunit;
using static Worktown.Sim.Tests.Cooking.TestItems;

namespace Worktown.Sim.Tests.Cooking
{
    public class ReviewFixTests
    {
        private static readonly string[] Plated = { "plated" };

        private static OperationDef CutOp(ParamDef shape) =>
            new OperationDef("cut", "board", OperationKind.Cut, false, 1, 1, new[] { shape }, null,
                Array.Empty<string>(), Array.Empty<string>(), "{in} {shape}");

        private static OperationDef HeatOp(HeatProfile h, ParamDef time, ParamDef? temp = null) =>
            new OperationDef("bake", "oven", OperationKind.Heat, false, 1, 1,
                temp == null ? new[] { time } : new[] { temp, time }, h,
                Array.Empty<string>(), Array.Empty<string>(), "baked {in}");

        private static readonly ParamDef GoodTime = new ParamDef("time", ParamKind.Number, 10f, 100f,
            new[] { new BucketDef("a", 10f, "x"), new BucketDef("b", 50f, "y") });

        // 1. Immutability
        [Fact]
        public void Item_CollectionsAreNotMutableArrays()
        {
            var baseItem = Base("potato");
            var cut = Ok(TestItems.Cut(baseItem, "sticks"));
            var mix = Ok(Mix(Base("salt"), Base("oil")));
            foreach (var item in new[] { baseItem, cut, mix })
            {
                Assert.IsNotType<Item[]>(item.Inputs);
                Assert.IsNotType<ItemComponent[]>(item.Components);
                Assert.IsNotType<string[]>(item.Tags);
                Assert.False(item.Inputs is IList<Item> li && !li.IsReadOnly);
                Assert.False(item.Components is IList<ItemComponent> lc && !lc.IsReadOnly);
                Assert.False(item.Tags is IList<string> lt && !lt.IsReadOnly);
                Assert.False(item.Buckets is IDictionary<string, string> d && !d.IsReadOnly);
            }
        }

        // 2. Defensive copies
        [Fact]
        public void Catalogs_AreIsolatedFromCallerLists()
        {
            var tags = new List<string> { "veg" };
            var def = new BaseItemDef("kale", "kale", Salt.Stats, Salt.IdealDoneness, tags);
            var items = new ItemCatalog(new[] { def });
            tags.Add("plated");
            items.TryGet("kale", out var stored);
            Assert.Equal(new[] { "veg" }, stored.Tags);

            var buckets = new List<BucketDef> { new BucketDef("halves", 0.2f, "halves") };
            var shape = new ParamDef("shape", ParamKind.Choice, 0f, 1f, buckets);
            var parms = new List<ParamDef> { shape };
            var forbidden = new List<string>();
            var added = new List<string>();
            var op = new OperationDef("cut", "board", OperationKind.Cut, false, 1, 1, parms, null, forbidden, added, "{in} {shape}");
            var ops = new OperationCatalog(new[] { op });
            var factory = new ItemFactory(items, ops);

            buckets.Add(new BucketDef("dice", 0.8f, "cubes"));
            parms.Clear();
            forbidden.Add("veg");
            added.Add("plated");

            var kale = factory.CreateBase("kale").Item!;
            var r = factory.Apply("cut", new[] { kale },
                new Dictionary<string, ParamValue> { ["shape"] = ParamValue.Choice("halves") });
            Assert.True(r.Ok);
            Assert.DoesNotContain("plated", r.Item!.Tags);
            Assert.Equal(OperationError.InvalidParam, factory.Apply("cut", new[] { kale },
                new Dictionary<string, ParamValue> { ["shape"] = ParamValue.Choice("dice") }).Error);
        }

        // 4. Mix flattening by operation id
        [Fact]
        public void Mix_OnlyFlattensSameOperation()
        {
            var ops = new OperationCatalog(new[]
            {
                new OperationDef("mix", "counter", OperationKind.Mix, true, 2, 6, Array.Empty<ParamDef>(), null,
                    Array.Empty<string>(), Array.Empty<string>(), "{in} mix"),
                new OperationDef("blend", "blender", OperationKind.Mix, true, 2, 6, Array.Empty<ParamDef>(), null,
                    Array.Empty<string>(), Array.Empty<string>(), "{in} blend"),
            });
            var f = new ItemFactory(Catalog, ops);
            var blend = f.Apply("blend", new[] { Base("salt"), Base("oil") }, NoParams).Item!;
            var mixed = f.Apply("mix", new[] { blend, Base("onion") }, NoParams).Item!;
            Assert.Equal(2, mixed.Inputs.Count);
            Assert.Contains(mixed.Inputs, i => i.OperationId == "blend");
            var mix2 = f.Apply("mix", new[] { Base("salt"), Base("oil") }, NoParams).Item!;
            var mix3 = f.Apply("mix", new[] { mix2, Base("onion") }, NoParams).Item!;
            Assert.Equal(3, mix3.Inputs.Count);
        }

        // 5. Validation
        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        public void HeatProfile_NonFinite_Throws(float bad)
        {
            Assert.Throws<ArgumentException>(() => new OperationCatalog(new[]
                { HeatOp(new HeatProfile(100f, bad, 0f, 0f, 0f, 0f), GoodTime) }));
            Assert.Throws<ArgumentException>(() => new OperationCatalog(new[]
                { HeatOp(new HeatProfile(100f, 0f, bad, 0f, 0f, 0f), GoodTime) }));
            Assert.Throws<ArgumentException>(() => new OperationCatalog(new[]
                { HeatOp(new HeatProfile(100f, 0f, 0f, bad, 0f, 0f), GoodTime) }));
            Assert.Throws<ArgumentException>(() => new OperationCatalog(new[]
                { HeatOp(new HeatProfile(100f, 0f, 0f, 0f, bad, 0f), GoodTime) }));
            Assert.Throws<ArgumentException>(() => new OperationCatalog(new[]
                { HeatOp(new HeatProfile(100f, 0f, 0f, 0f, 0f, bad), GoodTime) }));
            Assert.Throws<ArgumentException>(() => new OperationCatalog(new[]
                { HeatOp(new HeatProfile(bad, 0f, 0f, 0f, 0f, 0f), GoodTime) }));
        }

        [Fact]
        public void FirstNumberBucket_MustEqualMin()
        {
            var bad = new ParamDef("time", ParamKind.Number, 10f, 100f,
                new[] { new BucketDef("a", 20f, "x"), new BucketDef("b", 50f, "y") });
            Assert.Throws<ArgumentException>(() => new OperationCatalog(new[]
                { HeatOp(new HeatProfile(100f, 0f, 0f, 0f, 0f, 0f), bad) }));
        }

        [Fact]
        public void NumberBucket_AboveMax_Throws()
        {
            var bad = new ParamDef("time", ParamKind.Number, 10f, 100f,
                new[] { new BucketDef("a", 10f, "x"), new BucketDef("b", 150f, "y") });
            Assert.Throws<ArgumentException>(() => new OperationCatalog(new[]
                { HeatOp(new HeatProfile(100f, 0f, 0f, 0f, 0f, 0f), bad) }));
        }

        [Theory]
        [InlineData(-0.1f)]
        [InlineData(1.5f)]
        public void CutShape_OutOfRange_Throws(float v)
        {
            var shape = new ParamDef("shape", ParamKind.Choice, 0f, 1f, new[] { new BucketDef("odd", v, "odd") });
            Assert.Throws<ArgumentException>(() => new OperationCatalog(new[] { CutOp(shape) }));
        }

        [Fact]
        public void ValidCustomOperations_Construct()
        {
            var ok = new OperationCatalog(new[] { HeatOp(new HeatProfile(100f, 0f, 0f, 0f, 0f, 0f), GoodTime) });
            Assert.Equal(1, ok.Count);
        }

        // 6. Single-pass naming
        [Fact]
        public void Naming_DoesNotReExpandBracesInInputNames()
        {
            var weird = new BaseItemDef("weird", "odd {shape}", Salt.Stats, Salt.IdealDoneness, Array.Empty<string>());
            var items = new ItemCatalog(new[] { weird });
            var f = new ItemFactory(items, OperationCatalog.CreateDefault());
            var cut = f.Apply("cut", new[] { f.CreateBase("weird").Item! },
                new Dictionary<string, ParamValue> { ["shape"] = ParamValue.Choice("dice") }).Item!;
            Assert.Equal("odd {shape} cubes", ItemNaming.FallbackName(cut, NameCatalog.Empty));
        }

        // Public API direct tests
        [Fact]
        public void Taste_SaltOnly_AndBalanced()
        {
            Assert.Equal(1f / 6f, ItemRater.Taste(Salt.Stats), 5);
            Assert.Equal(1f, ItemRater.Taste(Def("even", 0.4f, 0.4f, 0.4f, 0.4f, 0.4f, 0.4f).Stats), 5);
            Assert.Throws<ArgumentNullException>(() => ItemRater.Taste(null!));
        }

        [Fact]
        public void OperationCatalog_TryGet()
        {
            var ops = OperationCatalog.CreateDefault();
            Assert.True(ops.TryGet("fry", out var fry));
            Assert.Equal("fry", fry.Id);
            Assert.False(ops.TryGet("nope", out _));
            Assert.False(ops.TryGet(null, out _));
        }

        [Fact]
        public void NameCatalog_TryGetName_AndEmpty()
        {
            var c = new NameCatalog(new[] { new NamedItem("h1", "fries") });
            Assert.True(c.TryGetName("h1", out var n));
            Assert.Equal("fries", n);
            Assert.False(c.TryGetName("h2", out _));
            Assert.False(NameCatalog.Empty.TryGetName("h1", out _));
        }

        [Fact]
        public void ParamValue_Accessors()
        {
            var n = ParamValue.Number(2.5f);
            Assert.Equal(ParamKind.Number, n.Kind);
            Assert.Equal(2.5f, n.NumberValue);
            Assert.Null(n.ChoiceId);
            var c = ParamValue.Choice("dice");
            Assert.Equal(ParamKind.Choice, c.Kind);
            Assert.Equal("dice", c.ChoiceId);
            Assert.Equal(0f, c.NumberValue);
        }

        [Fact]
        public void CookingLimits_Constants()
        {
            Assert.Equal(8, CookingLimits.MaxDepth);
            Assert.Equal(16, CookingLimits.MaxComponents);
            Assert.Equal(6, CookingLimits.MaxMixInputs);
            Assert.Equal(4, CookingLimits.MaxPlateInputs);
        }
    }
}
