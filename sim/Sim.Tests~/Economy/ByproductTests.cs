using System.Linq;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;
using Xunit;
using T = Worktown.Sim.Tests.Cooking.TestItems;

namespace Worktown.Sim.Tests.Economy
{
    public class ByproductTests
    {
        private static readonly EconomyCatalog C = TestEconomy.Catalog;

        private static (OperationResult Result, Item[] Inputs) Cut(Item input, string shape)
            => (T.Cut(input, shape), new[] { input });

        private static void Is(System.Collections.Generic.IReadOnlyList<GoodAmount> got, params (string, int)[] want)
            => Assert.Equal(want.Select(w => new GoodAmount(w.Item1, w.Item2)).ToList(), got.ToList());

        [Fact]
        public void CutOnPotatoGivesOneFoodScraps() // AC6
        {
            var (r, inputs) = Cut(T.Base("potato"), "sticks");
            Is(Byproducts.ForOperation(C, "cut", inputs, r), ("food_scraps", 1));
        }

        [Fact]
        public void FryOnCutPotatoGivesOneUsedCookingOil() // AC6
        {
            var cut = T.Ok(T.Cut(T.Base("potato"), "sticks"));
            var inputs = new[] { cut };
            var r = T.Fry(cut, 190f, 150f);
            Is(Byproducts.ForOperation(C, "fry", inputs, r), ("used_cooking_oil", 1));
        }

        [Fact]
        public void CutNoOpGivesNothing()
        {
            var sticks = T.Ok(T.Cut(T.Base("potato"), "sticks"));
            var again = T.Cut(sticks, "halves"); // coarser than sticks: collapses to the same item
            Assert.Equal(sticks.Hash, again.Item!.Hash);
            Assert.Empty(Byproducts.ForOperation(C, "cut", new[] { sticks }, again));
        }

        [Fact]
        public void FailedResultGivesNothing()
        {
            var plated = TestEconomy.PlatedFries();
            var r = T.Fry(plated, 190f, 150f); // plated is a forbidden input
            Assert.False(r.Ok);
            Assert.Empty(Byproducts.ForOperation(C, "fry", new[] { plated }, r));
        }

        [Fact]
        public void UnpricedInputGivesNothing()
        {
            var noBasil = TestEconomy.Build(grocer: TestEconomy.GrocerWithout("basil"));
            var (r, inputs) = Cut(T.Base("basil"), "sticks");
            Assert.True(r.Ok);
            Assert.Empty(Byproducts.ForOperation(noBasil, "cut", inputs, r));
            Assert.Empty(Byproducts.ForDiscard(noBasil, T.Ok(T.Plate(T.Base("basil")))));
        }

        [Fact]
        public void OperationWithoutRulesGivesNothing()
        {
            var a = T.Base("tomato");
            var b = T.Base("cashew");
            Assert.Empty(Byproducts.ForOperation(C, "mix", new[] { a, b }, T.Mix(a, b)));
            Assert.Empty(Byproducts.ForOperation(C, "plate", new[] { a }, T.Plate(a)));
        }

        [Fact]
        public void CutScrapsScaleWithInputWeight()
        {
            var mix = T.Ok(T.Mix(T.Base("tomato"), T.Base("cashew"), T.Base("onion")));
            var (r, inputs) = Cut(mix, "dice");
            Is(Byproducts.ForOperation(C, "cut", inputs, r), ("food_scraps", 3));
        }

        [Fact]
        public void DiscardGivesOneFoodScrapsPerComponent()
        {
            Is(Byproducts.ForDiscard(C, TestEconomy.PlatedRaw()), ("food_scraps", 1));
            Is(Byproducts.ForDiscard(C, TestEconomy.PlatedTomatoCashew()), ("food_scraps", 2));
        }

        [Fact]
        public void RulesMergePerGoodAndSortByGoodId()
        {
            var rules = new[]
            {
                new ByproductRule(ByproductTrigger.Operation, "cut", "used_cooking_oil", 2, 0),
                new ByproductRule(ByproductTrigger.Operation, "cut", "food_scraps", 1, 1),
                new ByproductRule(ByproductTrigger.Operation, "cut", "food_scraps", 0, 2),
            };
            // B = 7 * ((1+1)*3 + (0+2)*3 + 2*4 = 20) + 3 = 143, so lift the grocer above it
            var cat = TestEconomy.Build(byproducts: rules, grocer: C.Grocer.Select(g => g with { Price = g.Price + 200 }));
            var tom = T.Base("tomato");
            var r = T.Cut(tom, "dice");
            // weight 1: food_scraps = (1 + 1*1) + (0 + 2*1) = 4, oil = 2; sorted: food_scraps, used_cooking_oil
            Is(Byproducts.ForOperation(cat, "cut", new[] { tom }, r), ("food_scraps", 4), ("used_cooking_oil", 2));
        }

        [Fact]
        public void SaleRulesApplyOnSaleOnly()
        {
            var rules = new[] { new ByproductRule(ByproductTrigger.Sale, "snack", "food_scraps", 2, 1) };
            var cat = TestEconomy.Build(byproducts: rules);
            var e = new EconomyEngine(cat);
            var s = TestEconomy.Open(e, e.Initial(), "alice", 0);
            var res = e.TrySellItem(s, "alice", 1, TestEconomy.PlatedFries());
            Assert.True(res.Ok, res.Error.ToString());
            Is(res.Byproducts, ("food_scraps", 3)); // 2 + 1*weight 1
            Assert.Equal(3, TestEconomy.Units(res.State, "alice", "food_scraps"));
        }

        [Fact]
        public void ResultListsAreReadOnly()
        {
            var (r, inputs) = Cut(T.Base("potato"), "sticks");
            Assert.True(((System.Collections.Generic.IList<GoodAmount>)Byproducts.ForOperation(C, "cut", inputs, r)).IsReadOnly);
            Assert.True(((System.Collections.Generic.IList<GoodAmount>)Byproducts.ForDiscard(C, TestEconomy.PlatedRaw())).IsReadOnly);
        }

        [Fact]
        public void ScrapsAndOilSellAtOrAboveFloor() // AC6, second half
        {
            var e = TestEconomy.Engine;
            var s = TestEconomy.Open(e, e.Initial(), "alice", 0);
            var cutPotato = T.Ok(T.Cut(T.Base("potato"), "sticks"));
            var goods = Byproducts.ForOperation(C, "cut", new[] { T.Base("potato") }, T.Cut(T.Base("potato"), "sticks"))
                .Concat(Byproducts.ForOperation(C, "fry", new[] { cutPotato }, T.Fry(cutPotato, 190f, 150f))).ToList();
            var add = e.AddGoods(s, "alice", goods);
            Assert.True(add.Ok);
            foreach (var good in new[] { "food_scraps", "used_cooking_oil" })
            {
                var sale = e.TrySellGoods(add.State, "alice", good, 1);
                Assert.True(sale.Ok);
                Assert.True(sale.Payout >= 2); // both commodity floors are 2
            }
        }
    }
}
