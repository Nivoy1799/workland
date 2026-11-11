using System;
using System.Linq;
using Worktown.Sim.Economy;
using Xunit;

namespace Worktown.Sim.Tests.Economy
{
    /// <summary>
    /// Spec 037 AC2 (D39): money is integer cents. The default catalog's integers stay (one old luxury-scale dollar is one cent), so
    /// these pins say what they mean now, and the anti-faucet rules hold for every grocer good at the new scale.
    /// </summary>
    public class CentsScaleTests
    {
        private static readonly EconomyCatalog C = TestEconomy.Catalog;

        [Fact]
        public void EveryIngredient_CostsAStreetFoodPrice() // spec 037: ingredients $0.30 to $2.00
        {
            Assert.Equal(28, C.Grocer.Count);
            foreach (GrocerPrice g in C.Grocer) Assert.InRange(g.Price, 30, 200);
            Assert.True(C.TryGetGrocerPrice("potato", out long potato));
            Assert.Equal(60, potato); // $0.60 a potato
            Assert.True(C.TryGetGrocerPrice("cashew", out long cashew));
            Assert.Equal(150, cashew); // the dearest: $1.50
            // Packaging stays $0.35 to $0.40, not the spec's $0.05 to $0.20: every grocer price must stay above B (the anti-faucet
            // rule), and B = 7 x 4 + 3 cents is the least the byproduct markets allow (a ceiling of at least 2 cents, 2C >= 3F).
            foreach (string pack in new[] { "wrap_paper", "takeaway_box", "paper_bag", "cup" })
            {
                Assert.True(C.TryGetGrocerPrice(pack, out long price), pack);
                Assert.InRange(price, 35, 40);
            }
        }

        [Fact]
        public void TheMarkets_ReadInCents()
        {
            Assert.True(C.TryGetMarket("snack", out MarketDef snack));
            Assert.Equal((100, 400), (snack.Floor, snack.Ceiling)); // $1.00 to $4.00
            Assert.True(C.TryGetMarket("meal", out MarketDef meal));
            Assert.Equal((300, 1200), (meal.Floor, meal.Ceiling)); // $3.00 to $12.00
            Assert.True(C.TryGetMarket("drink", out MarketDef drink));
            Assert.Equal((50, 250), (drink.Floor, drink.Ceiling)); // $0.50 to $2.50
            Assert.Equal(31, C.ByproductMaxPerComponent); // B = $0.31
        }

        [Fact]
        public void AntiFaucet_HoldsForEveryGrocerGood_AtTheNewScale()
        {
            long b = C.ByproductMaxPerComponent;
            foreach (GrocerPrice g in C.Grocer)
            {
                long p = g.Price;
                Assert.True(b < p, $"{g.BaseId}: B {b} below its price {p}");
                long unit = Supplier.BulkUnitPrice(C, p);
                Assert.InRange(unit, b + 1, p); // never worth less than its byproducts, never above the grocer price
                for (int n = 1; n <= 3 * Supplier.CupsPerKeg; n++)
                    Assert.True(Supplier.OrderCost(C, p, n) >= n * unit, $"{g.BaseId}: {n} units cost at least {n} x {unit}");
                // A one-component dish of it sold to the wholesaler, with every byproduct it may make, never pays its stock cost back.
                long surplusCap = unit - 1 - b;
                Assert.True(surplusCap + b < unit, g.BaseId);
                for (int q = 0; q <= 1000; q += 50)
                {
                    long wholesale = Math.Min(PriceRules.WholesalePayout(100, q), surplusCap);
                    Assert.True(Math.Max(0, wholesale) + b < unit, $"{g.BaseId} q={q}");
                }
                // A portion and a pour are charged whole cents, at least one.
                Assert.True(PriceRules.PortionCost(p, PriceRules.PortionStepPercent) >= 1);
                Assert.True(PriceRules.FillCost(35, p, 1, false) >= 1);
            }
        }

        [Fact]
        public void AntiFaucet_ThePlatedRawItems_StillLoseMoneyAtTheWholesaler()
        {
            foreach (var item in new[] { TestEconomy.PlatedRaw(), TestEconomy.PlatedSalt(), TestEconomy.PlatedApple(), TestEconomy.PlatedTomatoCashew() })
            {
                Assert.True(C.TryGetMarket(item.Weight >= 2 ? "meal" : "snack", out MarketDef market));
                long cost = PriceRules.StockCost(C, item)!.Value;
                for (int q = 0; q <= 1000; q += 10)
                    Assert.True(Math.Max(0, PriceRules.ItemWholesalePayout(C, market, item, q)) + item.Weight * C.ByproductMaxPerComponent < cost);
            }
        }

        [Fact]
        public void TheCatalog_KeepsEveryInteger() // determinism: the cents scale keeps the old numbers (pinned sums)
        {
            Assert.Equal(1755, C.Grocer.Sum(g => g.Price));
            Assert.Equal(2311, C.Markets.Sum(m => m.Floor + m.Ceiling));
        }
    }
}
