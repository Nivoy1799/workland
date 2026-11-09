using System;
using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Cooking;
using Xunit;

namespace Worktown.Sim.Tests.Cooking
{
    /// <summary>Spec 030 AC2: the griddle and toast ops (per-side doneness, flip, combined rating, toasting, determinism).</summary>
    public class GriddleTests
    {
        private static readonly ItemCatalog Items = new ItemCatalog(
            StreetFoodItems.All.Concat(StreetFoodItems.GrillVegetables).Concat(new[] { TestItems.Potato }));
        private static readonly ItemFactory Factory = new ItemFactory(Items, OperationCatalog.CreateDefault());

        private static Item Base(string id) => TestItems.Ok(Factory.CreateBase(id));

        private static Item Cut(Item input, string shape) => TestItems.Ok(Factory.Apply("cut", new[] { input },
            new Dictionary<string, ParamValue> { ["shape"] = ParamValue.Choice(shape) }));

        private static Item Strips() => Cut(Base("seitan"), "sticks");

        private static OperationResult Grill(Item input, float sideA, float sideB, string op = Griddle.OperationId) =>
            Factory.Apply(op, new[] { input }, Griddle.Params(sideA, sideB));

        private static float Sec(float kitchenSeconds) => kitchenSeconds * DeepFry.KitchenTimeScale;
        private static float Done(Item item) => item.Components[0].Doneness;
        private static float Quality(Item item) => ItemRater.Rate(item).Quality;
        private static string Name(Item item) => ItemNaming.FallbackName(item, NameCatalog.Empty);

        [Fact]
        public void TheDefaultCatalog_HasTheGriddleAndToastOps_AtTheGriddle()
        {
            OperationCatalog ops = OperationCatalog.CreateDefault();
            foreach (string id in new[] { Griddle.OperationId, Griddle.ToastOperationId })
            {
                Assert.True(ops.TryGet(id, out OperationDef op), id);
                Assert.Equal(Griddle.Station, op.Station);
                Assert.Equal(OperationKind.Heat, op.Kind);
                Assert.NotNull(op.Heat!.Grill);
                Assert.Equal(Griddle.PlateTempC, op.Heat.FixedTemperatureC);
            }
            Assert.True(ops.TryGet(Griddle.ToastOperationId, out OperationDef toast));
            Assert.True(toast.Heat!.Grill!.Toasts);
        }

        [Fact]
        public void Timings_AreTheKitchenSecondsOfTheSpec_PerSide()
        {
            // Seitan strips good 12 s, perfect 14 s, burnt 22 s; vegetables 10 s; bread good 4 s, burnt 9 s.
            Assert.Equal(Sec(12f), Griddle.SeitanSide.WindowMinAt);
            Assert.Equal(Sec(16f), Griddle.SeitanSide.WindowMaxAt); // perfect 14 s is the middle of the window
            Assert.Equal(Sec(22f), Griddle.SeitanSide.BurntAt);
            foreach (FryCurve veg in new[] { Griddle.PaprikaSide, Griddle.EggplantSide, Griddle.MushroomSide })
                Assert.Equal(Sec(10f), veg.WindowMinAt);
            Assert.Equal(Sec(4f), Griddle.FlatbreadSide.WindowMinAt);
            Assert.Equal(Sec(9f), Griddle.FlatbreadSide.BurntAt);
            Assert.Equal(Sec(9f), Griddle.WrapSide.BurntAt);
        }

        [Fact]
        public void EachSide_CooksOnItsOwn_AlongItsCurve()
        {
            DonenessWindow w = StreetFoodItems.Seitan.IdealDoneness;
            FryCurve c = Griddle.SeitanSide;
            Assert.Equal(0f, Griddle.SideDoneness(c, w, 0f, 0f));
            Assert.Equal(w.Min, Griddle.SideDoneness(c, w, 0f, c.WindowMinAt), 4);
            Assert.Equal(w.Max, Griddle.SideDoneness(c, w, 0f, c.WindowMaxAt), 4);
            Assert.Equal(w.Max + DeepFry.BurnMargin, Griddle.SideDoneness(c, w, 0f, c.BurntAt), 4);
            // Both sides equal: the item's doneness is that side's.
            Assert.Equal(Griddle.SideDoneness(c, w, 0f, Sec(14f)), Done(TestItems.Ok(Grill(Strips(), Sec(14f), Sec(14f)))), 4);
            float prev = -1f;
            for (float t = 0f; t <= 300f; t += 6f)
            {
                float d = Done(TestItems.Ok(Grill(Strips(), t, t)));
                Assert.True(d >= prev, "doneness never decreases (t " + t + ")");
                prev = d;
            }
        }

        [Fact]
        public void Sides_StateAndFlip_OnlyTheDownSideCooks()
        {
            GriddleSides s = GriddleSides.Fresh.Cook(30f);
            Assert.Equal(30f, s.SideA);
            Assert.Equal(0f, s.SideB);
            Assert.Equal(0, s.DownSide);
            s = s.Flip().Cook(20f);
            Assert.Equal(1, s.DownSide);
            Assert.Equal(30f, s.SideA);
            Assert.Equal(20f, s.SideB);
            Assert.Equal(20f, s.Down);
            Assert.Equal(30f, s.Up);
            s = s.Flip().Flip().Cook(-5f).Cook(float.NaN); // flipping a raw or any side is allowed; bad times count as 0
            Assert.Equal(new GriddleSides(30f, 20f, true), s);
        }

        [Fact]
        public void Flip_TheSameTwoSides_GiveTheSameItem_WhicheverWentFirst()
        {
            Item ab = TestItems.Ok(Grill(Strips(), Sec(14f), Sec(13f)));
            Item ba = TestItems.Ok(Grill(Strips(), Sec(13f), Sec(14f)));
            Assert.Equal(ab.Hash, ba.Hash);
            Assert.Equal(Done(ab), Done(ba));
            // "time" is the shorter side: the other order is refused when given by hand.
            OperationResult wrong = Factory.Apply(Griddle.OperationId, new[] { Strips() }, new Dictionary<string, ParamValue>
            {
                ["time"] = ParamValue.Number(90f),
                [Griddle.LongSideParam] = ParamValue.Number(30f),
            });
            Assert.Equal(OperationError.InvalidParam, wrong.Error);
        }

        [Fact]
        public void CombinedRating_NeedsBothSidesInTheWindow()
        {
            float both = Quality(TestItems.Ok(Grill(Strips(), Sec(14f), Sec(14f))));
            float unflipped = Quality(TestItems.Ok(Grill(Strips(), Sec(14f), 0f)));
            float oneBurnt = Quality(TestItems.Ok(Grill(Strips(), Sec(14f), Sec(30f))));
            float oneUnder = Quality(TestItems.Ok(Grill(Strips(), Sec(14f), Sec(8f))));
            Assert.True(both > unflipped + 0.3f, "without a flip the top stays raw and it scores low: " + both + " vs " + unflipped);
            Assert.True(both > oneBurnt, "one burnt side is worse");
            Assert.True(both > oneUnder, "one underdone side is worse");
            Assert.Equal(1f, ItemRater.Rate(TestItems.Ok(Grill(Strips(), Sec(13f), Sec(15f)))).DonenessFactor, 4);
            // The combination: inside the window the mean, else the worse side.
            DonenessWindow w = new DonenessWindow(0.5f, 0.8f);
            Assert.Equal(0.6f, Griddle.CombineSides(0.5f, 0.7f, w), 5);
            Assert.Equal(0.1f, Griddle.CombineSides(0.6f, 0.1f, w), 5);
            Assert.Equal(0.95f, Griddle.CombineSides(0.95f, 0.6f, w), 5);
            Assert.Equal(0.625f, Griddle.CombineSides(0.25f, 1f, new DonenessWindow(0.5f, 0.75f)), 5); // equally far out on both ends: the mean
        }

        [Fact]
        public void Names_FollowTheTwoSidesBuckets_SeitanSticksAreStrips()
        {
            Assert.Equal("grilled seitan strips", Name(TestItems.Ok(Grill(Strips(), Sec(14f), Sec(14f)))));
            Assert.Equal("half-grilled seitan strips", Name(TestItems.Ok(Grill(Strips(), Sec(14f), 0f))));
            Assert.Equal("warm seitan strips", Name(TestItems.Ok(Grill(Strips(), Sec(2f), 0f))));
            Assert.Equal("charred seitan strips", Name(TestItems.Ok(Grill(Strips(), Sec(14f), Sec(30f)))));
            Assert.Equal("grilled paprika sticks", Name(TestItems.Ok(Grill(Cut(Base("paprika"), "sticks"), Sec(11f), Sec(11f)))));
            Assert.Equal("grilled eggplant slices", Name(TestItems.Ok(Grill(Cut(Base("eggplant"), "slices"), Sec(11f), Sec(11f)))));
            Assert.Equal("grilled seitan", Name(TestItems.Ok(Grill(Base("seitan"), Sec(14f), Sec(14f)))));
        }

        [Fact]
        public void Toasting_IsShort_AndBurnsAtNineSeconds()
        {
            Item bread = Base("flatbread");
            Assert.Equal(Griddle.ToastOperationId, Griddle.OperationFor(bread));
            Assert.Equal(Griddle.ToastOperationId, Griddle.OperationFor(Base("wrap")));
            Assert.Equal(Griddle.OperationId, Griddle.OperationFor(Strips()));
            DonenessWindow w = StreetFoodItems.Flatbread.IdealDoneness;
            Item toasted = TestItems.Ok(Grill(bread, Sec(5f), Sec(5f), Griddle.ToastOperationId));
            Assert.InRange(Done(toasted), w.Min, w.Max);
            Assert.Equal("toasted flatbread", Name(toasted));
            Assert.Equal("half-toasted flatbread", Name(TestItems.Ok(Grill(bread, Sec(5f), 0f, Griddle.ToastOperationId))));
            Item burnt = TestItems.Ok(Grill(bread, Sec(5f), Sec(9f), Griddle.ToastOperationId));
            Assert.Equal(w.Max + DeepFry.BurnMargin, burnt.Components[0].Doneness, 4);
            Assert.Equal("charred flatbread", Name(burnt));
            Assert.True(Quality(toasted) > Quality(burnt));
            // Bread on the grill op is far slower: the toast op is the short window.
            Assert.True(Done(TestItems.Ok(Grill(bread, Sec(5f), Sec(5f)))) < Done(toasted));
        }

        [Fact]
        public void TheSameInputs_GiveTheSameItem_AndPlatingKeepsTheRating()
        {
            Item a = TestItems.Ok(Grill(Strips(), 77.3f, 81.9f));
            Item b = TestItems.Ok(Grill(Strips(), 81.9f, 77.3f));
            Assert.Equal(a.Hash, b.Hash);
            Assert.Equal(a.Stats, b.Stats);
            Assert.Equal(Quality(a), Quality(b));
            Assert.Equal(1f, a.QualityFactor);
            Item plate = TestItems.Ok(Factory.Apply("plate", new[] { a }, TestItems.NoParams));
            Assert.Equal(Quality(a), Quality(plate), 5);
            Assert.NotEqual(TestItems.Ok(Grill(Strips(), 0f, 80f)).Hash, TestItems.Ok(Grill(Strips(), 80f, 80f)).Hash);
        }

        [Fact]
        public void TheGrillVegetables_AreValidBaseItems_AndCutAsListed()
        {
            Assert.Equal(new[] { "paprika", "eggplant", "mushroom" }, StreetFoodItems.GrillVegetables.Select(d => d.Id));
            Assert.Equal(10, StreetFoodItems.All.Count);
            Assert.Equal("sticks", StreetFoodItems.CutShape("paprika"));
            Assert.Equal("slices", StreetFoodItems.CutShape("eggplant"));
            Assert.Equal("halves", StreetFoodItems.CutShape("mushroom"));
            foreach (BaseItemDef def in StreetFoodItems.GrillVegetables)
            {
                Assert.True(Factory.CreateBase(def.Id).Ok, def.Id);
                Assert.Null(ContentRules.FindForbidden(def.DisplayName));
            }
        }

        [Fact]
        public void ABadGriddleDefinition_Throws()
        {
            var time = new ParamDef("time", ParamKind.Number, 0f, 600f, new[] { new BucketDef("short", 0f, "") });
            var heat = new HeatProfile(200f, 0f, 0f, 0f, 0f, 0f, null, Griddle.GrillProfile);
            Assert.Throws<ArgumentException>(() => new OperationCatalog(new[]
            {
                new OperationDef("g", "griddle", OperationKind.Heat, false, 1, 1, new[] { time }, heat,
                    Array.Empty<string>(), Array.Empty<string>(), "{in}"),
            }));
        }
    }
}
