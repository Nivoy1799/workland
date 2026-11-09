using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Cooking;
using Worktown.Sim.Core;
using Xunit;

namespace Worktown.Sim.Tests.Cooking
{
    /// <summary>
    /// Spec 032 AC2: order modifiers. Only from the recipe's allowed list ("no X" for required salad and sauce layers, "extra X" for
    /// required sauce and filling layers), at most two, drawn from the seed; packed in one byte; the order's template; the check of
    /// a build against an order (per layer, with the modifiers), and that a plain order scores exactly as in 031.
    /// </summary>
    public class OrderModifierTests
    {
        private static readonly ItemCatalog Items = new ItemCatalog(StreetFoodItems.All.Concat(StreetFoodItems.GrillVegetables)
            .Concat(new[] { TestItems.Potato, TestItems.Tomato, TestItems.Onion }));
        private static readonly ItemFactory Factory = new ItemFactory(Items, OperationCatalog.CreateDefault());

        private static Item Base(string id) => TestItems.Ok(Factory.CreateBase(id));
        private static Item Cut(Item input, string shape) => TestItems.Ok(Factory.Apply("cut", new[] { input },
            new Dictionary<string, ParamValue> { ["shape"] = ParamValue.Choice(shape) }));
        private static Item Falafel() => TestItems.Ok(Factory.Apply(DeepFry.OperationId, new[] { Base("falafel_mix") },
            new Dictionary<string, ParamValue>
            {
                ["temp"] = ParamValue.Number(DeepFry.ReferenceTempC),
                ["time"] = ParamValue.Number(150f),
                [DeepFry.ShakeParam] = ParamValue.Choice(DeepFry.NotShaken),
                [DeepFry.DrainParam] = ParamValue.Choice(DeepFry.Drained),
            }));
        private static Item Lettuce() => Cut(Base("lettuce"), "slices");
        private static Item Garlic(float seconds = 1f) =>
            TestItems.Ok(Factory.Apply(Assembly.SqueezeOperationId, new[] { Base("garlic_herb_sauce") }, Assembly.SqueezeParams(seconds)));
        private static Item Wrapped(params Item[] layers)
        {
            Item open = TestItems.Ok(Factory.Apply(Assembly.AssembleOperationId, layers, TestItems.NoParams));
            Item folded = TestItems.Ok(Factory.Apply(Assembly.CloseOperationId, new[] { open }, Assembly.CloseParams(Assembly.Folded)));
            return TestItems.Ok(Factory.Apply(Assembly.CloseOperationId, new[] { folded }, Assembly.CloseParams(Assembly.Wrapped)));
        }

        private static RecipeTemplate FalafelWrap => RecipeTemplates.ById(RecipeTemplates.FalafelWrapId)!;
        private static OrderModifier Mod(RecipeTemplate t, string text) => OrderModifiers.Allowed(t).Single(m => m.Text == text);

        [Fact]
        public void TheAllowedList_ComesOnlyFromTheRecipe()
        {
            Assert.Equal(new[] { "extra falafel", "no lettuce", "no garlic", "extra garlic" },
                OrderModifiers.Allowed(FalafelWrap).Select(m => m.Text));
            Assert.Equal(new[] { "extra seitan", "no lettuce", "no chili", "extra chili" },
                OrderModifiers.Allowed(RecipeTemplates.ById(RecipeTemplates.SeitanWrapId)).Select(m => m.Text));
            Assert.Equal(new[] { "extra seitan", "no salad", "no sauce", "extra sauce" },
                OrderModifiers.Allowed(RecipeTemplates.ById(RecipeTemplates.SeitanFlatbreadId)).Select(m => m.Text));
            Assert.Equal(new[] { "extra fries", "extra veggies", "no sauce", "extra sauce" },
                OrderModifiers.Allowed(RecipeTemplates.ById(RecipeTemplates.VeggieBoxId)).Select(m => m.Text));
            Assert.Empty(OrderModifiers.Allowed(null));
            foreach (RecipeTemplate t in RecipeTemplates.Default)
                foreach (OrderModifier m in OrderModifiers.Allowed(t))
                {
                    TemplateLayer l = t.Layers[m.LayerIndex];
                    Assert.True(l.Required, t.Id + " " + m.Text);
                    Assert.NotEqual(LayerRole.Base, l.Role);
                    if (m.Kind == ModifierKind.Remove) Assert.True(l.Role == LayerRole.Salad || l.Role == LayerRole.Sauce);
                    else Assert.True(l.Role == LayerRole.Sauce || l.Role == LayerRole.Filling);
                }
        }

        [Fact]
        public void TheDraw_IsSeeded_AtMostTwo_AndNeverClashes()
        {
            var a = new SeededRandom(32);
            var b = new SeededRandom(32);
            var counts = new int[3];
            for (int i = 0; i < 400; i++)
            {
                RecipeTemplate t = RecipeTemplates.Default[i % RecipeTemplates.Default.Count];
                IReadOnlyList<OrderModifier> x = OrderModifiers.Draw(a, t);
                IReadOnlyList<OrderModifier> y = OrderModifiers.Draw(b, t);
                Assert.Equal(x, y);
                Assert.InRange(x.Count, 0, OrderModifiers.MaxPerOrder);
                counts[x.Count]++;
                Assert.Equal(x.Count, x.Select(m => m.LayerIndex).Distinct().Count());
                Assert.All(x, m => Assert.Contains(m, OrderModifiers.Allowed(t)));
            }
            Assert.InRange(counts[0], 160, 240); // about half
            Assert.InRange(counts[1], 80, 160);
            Assert.InRange(counts[2], 50, 120);
            var none = new SeededRandom(1);
            Assert.Empty(OrderModifiers.Draw(none, null));
            Assert.Equal(new SeededRandom(1).NextInt(0, 100), none.NextInt(0, 100)); // no draw for a dish without a template
        }

        [Fact]
        public void Modifiers_PackIntoOneByte_AndBack()
        {
            var mods = new[] { Mod(FalafelWrap, "no lettuce"), Mod(FalafelWrap, "extra garlic") };
            byte packed = OrderModifiers.Pack(FalafelWrap, mods);
            Assert.NotEqual(0, packed);
            Assert.Equal(mods, OrderModifiers.Unpack(FalafelWrap, packed));
            Assert.Equal(0, OrderModifiers.Pack(FalafelWrap, new OrderModifier[0]));
            Assert.Empty(OrderModifiers.Unpack(FalafelWrap, 0));
            Assert.Empty(OrderModifiers.Unpack(FalafelWrap, 0xFF)); // indices out of the list are dropped
            Assert.Equal("no lettuce, extra garlic", OrderModifiers.Describe(mods));
        }

        [Fact]
        public void APlainOrder_ScoresExactlyAsIn031()
        {
            Item wrap = Wrapped(Base("wrap"), Falafel(), Lettuce(), Garlic());
            Assert.Same(FalafelWrap, OrderModifiers.ForOrder(FalafelWrap, new OrderModifier[0]));
            ModifierCheck check = OrderModifiers.Check(wrap, FalafelWrap, null);
            Assert.Equal(RecipeTemplates.Score(wrap, FalafelWrap), check.Score);
            Assert.Equal(0, check.Mistakes);
            Assert.Equal(0, check.Misses);
        }

        [Fact]
        public void NoLettuce_IsKeptWithoutIt_AndNotMissing_ButAMistakeWithIt()
        {
            var noLettuce = new[] { Mod(FalafelWrap, "no lettuce") };
            Item without = Wrapped(Base("wrap"), Falafel(), Garlic());
            Item with = Wrapped(Base("wrap"), Falafel(), Lettuce(), Garlic());
            ModifierCheck ok = OrderModifiers.Check(without, FalafelWrap, noLettuce);
            Assert.Equal(0, ok.Misses);
            Assert.Equal(0, ok.Score.Missing);
            Assert.Equal(1f, ok.Score.Factor, 3);
            Assert.Equal(1, RecipeTemplates.Score(without, FalafelWrap).Missing); // the plain template would miss it
            ModifierCheck bad = OrderModifiers.Check(with, FalafelWrap, noLettuce);
            Assert.Equal(1, bad.Mistakes);
            Assert.Equal(1, bad.Misses);
            Assert.True(ok.Score.Quality > RecipeTemplates.Score(without, FalafelWrap).Quality);
        }

        [Fact]
        public void ExtraGarlic_IsKeptWithLotsOrTwoSqueezes_AndLotsCostsNoScore()
        {
            var extra = new[] { Mod(FalafelWrap, "extra garlic") };
            Item lots = Wrapped(Base("wrap"), Falafel(), Lettuce(), Garlic(2f));
            Item twice = Wrapped(Base("wrap"), Falafel(), Garlic(), Lettuce(), Garlic());
            Item once = Wrapped(Base("wrap"), Falafel(), Lettuce(), Garlic());
            Assert.Equal(0, OrderModifiers.Check(lots, FalafelWrap, extra).Misses);
            Assert.Equal(0, OrderModifiers.Check(lots, FalafelWrap, extra).Score.SauceOff);
            Assert.Equal(1, RecipeTemplates.Score(lots, FalafelWrap).SauceOff); // plain: lots is off
            Assert.Equal(0, OrderModifiers.Check(twice, FalafelWrap, extra).Mistakes);
            Assert.Equal(1, OrderModifiers.Check(once, FalafelWrap, extra).Mistakes);
        }

        [Fact]
        public void ExtraFalafel_NeedsTwoPreparedLayers()
        {
            var extra = new[] { Mod(FalafelWrap, "extra falafel") };
            Item two = Wrapped(Base("wrap"), Falafel(), Falafel(), Lettuce(), Garlic());
            Item rawSecond = Wrapped(Base("wrap"), Falafel(), Base("falafel_mix"), Lettuce(), Garlic());
            Assert.Equal(0, OrderModifiers.Check(two, FalafelWrap, extra).Mistakes);
            Assert.Equal(1, OrderModifiers.Check(rawSecond, FalafelWrap, extra).Mistakes);
            Assert.Equal(5, OrderModifiers.LayerCount(FalafelWrap, extra));
            Assert.Equal(3, OrderModifiers.LayerCount(FalafelWrap, new[] { Mod(FalafelWrap, "no lettuce") }));
            Assert.Equal(4, OrderModifiers.LayerCount(FalafelWrap, null));
        }

        [Fact]
        public void TheCheck_IsDeterministic()
        {
            var mods = new[] { Mod(FalafelWrap, "no lettuce"), Mod(FalafelWrap, "extra garlic") };
            Item a = Wrapped(Base("wrap"), Falafel(), Garlic(2f));
            Item b = Wrapped(Base("wrap"), Falafel(), Garlic(2f));
            Assert.Equal(a.Hash, b.Hash);
            ModifierCheck x = OrderModifiers.Check(a, FalafelWrap, mods), y = OrderModifiers.Check(b, FalafelWrap, mods);
            Assert.Equal(x.Score.Quality, y.Score.Quality);
            Assert.Equal(x.Misses, y.Misses);
            Assert.Equal(x.Mistakes, y.Mistakes);
            Assert.Equal(x.Score.Factor, y.Score.Factor);
        }
    }
}
