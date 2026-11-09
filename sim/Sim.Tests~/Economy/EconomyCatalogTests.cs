using System;
using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Economy;
using Xunit;

namespace Worktown.Sim.Tests.Economy
{
    public class EconomyCatalogTests
    {
        private static readonly EconomyCatalog D = TestEconomy.Catalog;

        private static MarketDef Mk(string id, long f, long c, MarketKind kind = MarketKind.Item) =>
            new MarketDef(id, kind, f, c, 350, 150, null, 25);

        private static IEnumerable<MarketDef> Replace(string id, Func<MarketDef, MarketDef> f) => TestEconomy.WithMarket(id, f);

        [Fact]
        public void DefaultCatalogIsValidWithBEqual31()
        {
            // B = (8-1) * V_op + max(V_discard, V_sale); V_op = max(cut: 1*3, fry: 1*4) = 4; V_discard = 1*3 = 3
            // = 7*4 + 3 = 31
            Assert.Equal(31, D.ByproductMaxPerComponent);
            Assert.Equal(5, D.Markets.Count);
            Assert.Equal(3, D.Rules.Count);
            Assert.Equal(3, D.Byproducts.Count);
            Assert.Equal(18, D.Grocer.Count); // spec 028: eight old entries, ten street-food items
        }

        [Fact]
        public void ListsAreSortedAsSpecified()
        {
            Assert.Equal(new[] { "drink", "food_scraps", "meal", "snack", "used_cooking_oil" }, D.Markets.Select(m => m.Id));
            Assert.Equal(new[]
            {
                "apple", "basil", "cashew", "chili_sauce", "cucumber", "falafel_mix", "flatbread", "garlic_herb_sauce", "lemon",
                "lettuce", "oil", "onion", "potato", "red_cabbage", "salt", "seitan", "tomato", "wrap"
            }, D.Grocer.Select(g => g.BaseId));
            Assert.Equal(new[] { "drink", "meal", "snack" }, D.Rules.Select(r => r.MarketId)); // given order
        }

        [Fact]
        public void LookupsFindKnownAndRejectUnknownOrNull()
        {
            Assert.True(D.TryGetMarket("snack", out var m));
            Assert.Equal(400, m.Ceiling);
            Assert.False(D.TryGetMarket("nope", out _));
            Assert.False(D.TryGetMarket(null, out _));
            Assert.True(D.TryGetGrocerPrice("potato", out var p));
            Assert.Equal(60, p);
            Assert.False(D.TryGetGrocerPrice("nope", out var none));
            Assert.Equal(0, none);
            Assert.False(D.TryGetGrocerPrice(null, out _));
        }

        [Fact]
        public void ExposedCollectionsAreReadOnlyAndInputsAreCopied()
        {
            Assert.True(((IList<MarketDef>)D.Markets).IsReadOnly);
            Assert.True(((IList<CategoryRule>)D.Rules).IsReadOnly);
            Assert.True(((IList<ByproductRule>)D.Byproducts).IsReadOnly);
            Assert.True(((IList<GrocerPrice>)D.Grocer).IsReadOnly);
            Assert.True(((IList<int>)D.Markets.Single(m => m.Id == "meal").Demand!.HourWeights).IsReadOnly);
            Assert.True(((IList<string>)D.Rules[0].RequiredTags).IsReadOnly);

            var weights = Enumerable.Repeat(1, 24).ToList();
            var markets = Replace("snack", m => m with { Demand = new DemandProfile(80, weights, 10) }).ToList();
            var c = TestEconomy.Build(markets: markets);
            weights[0] = 999;
            markets.Clear();
            Assert.Equal(1, c.Markets.Single(m => m.Id == "snack").Demand!.HourWeights[0]);
            Assert.Equal(5, c.Markets.Count);
        }

        [Fact]
        public void MarginViolationThrows()
        {
            // 2*149 = 298 < 3*100 = 300
            Assert.Throws<ArgumentException>(() => TestEconomy.Build(markets: Replace("snack", m => m with { Floor = 100, Ceiling = 149 })));
            // 2*150 = 300 >= 300 is fine
            TestEconomy.Build(markets: Replace("snack", m => m with { Floor = 100, Ceiling = 150 }));
        }

        [Fact]
        public void ByproductToNonCommodityThrows()
        {
            var rules = new[] { new ByproductRule(ByproductTrigger.Discard, "", "meal", 1, 0) };
            Assert.Throws<ArgumentException>(() => TestEconomy.Build(byproducts: rules));
            var missing = new[] { new ByproductRule(ByproductTrigger.Discard, "", "ghost", 1, 0) };
            Assert.Throws<ArgumentException>(() => TestEconomy.Build(byproducts: missing));
        }

        [Fact]
        public void HourWeightsMustHave24EntriesInRangeWithPositiveSum()
        {
            DemandProfile P(IReadOnlyList<int> w) => new DemandProfile(80, w, 10);
            Assert.Throws<ArgumentException>(() => TestEconomy.Build(markets: Replace("snack", m => m with { Demand = P(Enumerable.Repeat(1, 23).ToList()) })));
            Assert.Throws<ArgumentException>(() => TestEconomy.Build(markets: Replace("snack", m => m with { Demand = P(Enumerable.Repeat(0, 24).ToList()) })));
            Assert.Throws<ArgumentException>(() => TestEconomy.Build(markets: Replace("snack", m => m with { Demand = P(Enumerable.Repeat(1001, 24).ToList()) })));
            Assert.Throws<ArgumentException>(() => TestEconomy.Build(markets: Replace("snack", m => m with { Demand = P(Enumerable.Repeat(-1, 24).ToList()) })));
        }

        [Fact]
        public void MarketFieldRangesAreChecked()
        {
            void Bad(Func<MarketDef, MarketDef> f) =>
                Assert.Throws<ArgumentException>(() => TestEconomy.Build(markets: Replace("snack", f)));
            Bad(m => m with { NpcQualityPermille = 500 });
            Bad(m => m with { NpcQualityPermille = -1 });
            Bad(m => m with { MinQualityPermille = 1001 });
            Bad(m => m with { SmoothingPercent = 0 });
            Bad(m => m with { SmoothingPercent = 101 });
            Bad(m => m with { Floor = 0, Ceiling = 5 });
            Bad(m => m with { Ceiling = EconomyLimits.MaxPrice + 1 });
            Bad(m => m with { Id = "Bad Id" });
            Bad(m => m with { Demand = m.Demand! with { JitterPercent = 51 } });
            Bad(m => m with { Demand = m.Demand! with { UnitsPerNpcPerDayCenti = 100_001 } });
            // boundaries are accepted
            TestEconomy.Build(markets: Replace("snack", m => m with { NpcQualityPermille = 499, MinQualityPermille = 1000, SmoothingPercent = 100 }));
        }

        [Fact]
        public void DuplicateMarketThrows()
        {
            var markets = D.Markets.Concat(new[] { Mk("snack", 10, 20) });
            Assert.Throws<ArgumentException>(() => TestEconomy.Build(markets: markets));
        }

        [Fact]
        public void CategoryRuleProblemsThrow()
        {
            var none = Array.Empty<string>();
            Assert.Throws<ArgumentException>(() => TestEconomy.Build(rules: D.Rules.Concat(new[] { new CategoryRule("ghost", none, none, 1) })));
            Assert.Throws<ArgumentException>(() => TestEconomy.Build(rules: D.Rules.Concat(new[] { new CategoryRule("food_scraps", none, none, 1) })));
            // an Item market no rule reaches
            Assert.Throws<ArgumentException>(() => TestEconomy.Build(rules: D.Rules.Where(r => r.MarketId != "drink")));
            Assert.Throws<ArgumentException>(() => TestEconomy.Build(rules: D.Rules.Concat(new[] { new CategoryRule("snack", new[] { "BAD" }, none, 1) })));
        }

        [Fact]
        public void ByproductRuleProblemsThrow()
        {
            ByproductRule R(ByproductTrigger t, string src, int fix, int per) => new ByproductRule(t, src, "food_scraps", fix, per);
            void Bad(params ByproductRule[] rules) => Assert.Throws<ArgumentException>(() => TestEconomy.Build(byproducts: rules));
            Bad(R(ByproductTrigger.Operation, "mix", 1, 0));           // mix flattens: not allowed
            Bad(R(ByproductTrigger.Operation, "ghost", 1, 0));         // unknown operation
            Bad(R(ByproductTrigger.Operation, "cut", 0, 0));           // both zero
            Bad(R(ByproductTrigger.Operation, "cut", 1001, 0));
            Bad(R(ByproductTrigger.Operation, "cut", -1, 1));
            Bad(R(ByproductTrigger.Sale, "food_scraps", 1, 0));        // Sale rule not on an Item market
            Bad(R(ByproductTrigger.Sale, "ghost", 1, 0));
            Bad(R(ByproductTrigger.Discard, "snack", 1, 0));           // Discard needs ""
        }

        [Fact]
        public void GrocerProblemsThrow()
        {
            var p = D.Grocer.ToList();
            Assert.Throws<ArgumentException>(() => TestEconomy.Build(grocer: p.Concat(new[] { new GrocerPrice("potato", 99) })));  // duplicate
            Assert.Throws<ArgumentException>(() => TestEconomy.Build(grocer: p.Concat(new[] { new GrocerPrice("BAD", 99) })));
            Assert.Throws<ArgumentException>(() => TestEconomy.Build(grocer: p.Concat(new[] { new GrocerPrice("pear", 0) })));
            Assert.Throws<ArgumentException>(() => TestEconomy.Build(grocer: p.Concat(new[] { new GrocerPrice("pear", EconomyLimits.MaxPrice + 1) })));
            // byproduct rules but no grocer
            Assert.Throws<ArgumentException>(() => TestEconomy.Build(grocer: Array.Empty<GrocerPrice>()));
            // no byproducts and no grocer is fine (B = 0)
            Assert.Equal(0, TestEconomy.Build(byproducts: Array.Empty<ByproductRule>(), grocer: Array.Empty<GrocerPrice>()).ByproductMaxPerComponent);
        }

        [Fact]
        public void AntiFaucetRuleRejectsGrocerPriceAtOrBelowB()
        {
            // B = 31 with default byproducts: price 31 throws, 32 passes
            Assert.Throws<ArgumentException>(() => TestEconomy.Build(grocer: D.Grocer.Concat(new[] { new GrocerPrice("pear", 31) })));
            TestEconomy.Build(grocer: D.Grocer.Concat(new[] { new GrocerPrice("pear", 32) }));
        }

        [Fact]
        public void BCombinesOperationDiscardAndSaleGroups()
        {
            // cut: (0+1)*3 = 3 and a second cut rule (2+0)*4 (oil) = 8 -> cut group = 11; fry = 1*4 = 4 -> V_op = 11
            // discard = 3; sale snack = (1+1)*3 = 6 -> max(3, 6) = 6;  B = 7*11 + 6 = 83
            var rules = new[]
            {
                new ByproductRule(ByproductTrigger.Operation, "cut", "food_scraps", 0, 1),
                new ByproductRule(ByproductTrigger.Operation, "cut", "used_cooking_oil", 2, 0),
                new ByproductRule(ByproductTrigger.Operation, "fry", "used_cooking_oil", 1, 0),
                new ByproductRule(ByproductTrigger.Discard, "", "food_scraps", 0, 1),
                new ByproductRule(ByproductTrigger.Sale, "snack", "food_scraps", 1, 1),
            };
            var expensive = D.Grocer.Select(g => g with { Price = g.Price + 100 });
            Assert.Equal(83, TestEconomy.Build(byproducts: rules, grocer: expensive).ByproductMaxPerComponent);
        }
    }
}
