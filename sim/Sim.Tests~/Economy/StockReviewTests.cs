using System;
using System.Linq;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;
using Xunit;
using T = Worktown.Sim.Tests.Cooking.TestItems;

namespace Worktown.Sim.Tests.Economy
{
    /// <summary>
    /// 034 review fixes (with spec 035): no last drops (a draw is refused below its least share), a refund never above what the
    /// account paid, and the wholesaler's cap at the bulk price for a street-food build and the goods where the $32 floor matters.
    /// </summary>
    public class StockReviewTests
    {
        private static readonly EconomyCatalog C = TestEconomy.Catalog;
        private static readonly EconomyEngine E = TestEconomy.Engine;

        [Fact]
        public void ADraw_IsRefusedBelowItsLeastShare_NoLastDrops()
        {
            ShopStock s = ShopStock.Create(new[] { new StockLine("chili_sauce", 0, 6, 100, 20) }); // 20 % left of the last bottle
            StockResult lots = s.TryDraw("chili_sauce", 30, 30);
            Assert.Equal(StockError.OutOfStock, lots.Error); // no "lots" squeeze out of 20 %
            Assert.Same(s, lots.Stock);
            StockResult right = s.TryDraw("chili_sauce", 20, 20);
            Assert.True(right.Ok);
            Assert.Equal(20, right.Amount);
            Assert.True(right.Stock.Lines[0].IsEmpty);
            // The old two-argument draw still takes the last drops (no minimum).
            Assert.Equal(20, s.TryDraw("chili_sauce", 30).Amount);

            ShopStock keg = ShopStock.Create(new[] { new StockLine("lemonade", 0, 2, 2000, 1) }); // the last 1 % of a cup in the keg
            Assert.Equal(StockError.OutOfStock, keg.TryDraw("lemonade", 90, 90).Error); // no full cup out of 1 %
            Assert.True(keg.TryDraw("lemonade", 1, 1).Ok);
            // A draw that opens the next sealed unit still works with the minimum.
            ShopStock two = ShopStock.Create(new[] { new StockLine("chili_sauce", 1, 6, 100, 20) });
            StockResult opened = two.TryDraw("chili_sauce", 30, 30);
            Assert.True(opened.Ok);
            Assert.Equal(30, opened.Amount);
            Assert.Equal(0, opened.Stock.Lines[0].Count);
            Assert.Equal(90, opened.Stock.Lines[0].OpenPercent);
        }

        [Fact]
        public void ARefund_IsNeverMoreThanThisAccountPaid()
        {
            EconomyState s = TestEconomy.Open(E, E.Initial(), "alice", 1000);
            s = TestEconomy.Open(E, s, "bob", 1000);
            Assert.True(Supplier.TryOffer(C, "lettuce", 1, out SupplierOffer lettuce));
            PurchaseResult a = E.TryBuySupply(s, "alice", lettuce, 2); // $432
            PurchaseResult b = E.TryBuySupply(a.State, "bob", lettuce, 1); // $216
            Assert.True(a.Ok && b.Ok);
            Assert.Equal(216, TestEconomy.Acct(b.State, "bob").GrocerPaid);
            // The grocer took $648 in all, but bob paid only $216 of it.
            Assert.Equal(EconomyError.InvalidQuantity, E.Refund(b.State, "bob", 217).Error);
            LedgerResult back = E.Refund(b.State, "bob", 216);
            Assert.True(back.Ok);
            Assert.Equal(0, TestEconomy.Acct(back.State, "bob").GrocerPaid);
            Assert.Equal(EconomyError.InvalidQuantity, E.Refund(back.State, "bob", 1).Error);
            Assert.Equal(432, TestEconomy.Acct(back.State, "alice").GrocerPaid);
            Assert.True(E.Refund(back.State, "alice", 432).Ok);
        }

        private static Item Wrap(ItemFactory f, string container)
        {
            Item Base(string id) => T.Ok(f.CreateBase(id));
            Item falafel = T.Ok(f.Apply(DeepFry.OperationId, new[] { Base("falafel_mix") }, new System.Collections.Generic.Dictionary<string, ParamValue>
            {
                ["temp"] = ParamValue.Number(DeepFry.ReferenceTempC),
                ["time"] = ParamValue.Number(150f),
                [DeepFry.ShakeParam] = ParamValue.Choice(DeepFry.NotShaken),
                [DeepFry.DrainParam] = ParamValue.Choice(DeepFry.Drained),
            }));
            Item lettuce = T.Ok(f.Apply("cut", new[] { Base("lettuce") },
                new System.Collections.Generic.Dictionary<string, ParamValue> { ["shape"] = ParamValue.Choice("slices") }));
            Item sauce = T.Ok(f.Apply(Assembly.SqueezeOperationId, new[] { Base("garlic_herb_sauce") }, Assembly.SqueezeParams(1f)));
            Item open = T.Ok(f.Apply(Assembly.AssembleOperationId, new[] { Base("wrap"), falafel, lettuce, sauce }, T.NoParams));
            Item folded = T.Ok(f.Apply(Assembly.CloseOperationId, new[] { open }, Assembly.CloseParams(Assembly.Folded)));
            if (container == Assembly.Wrapped) return T.Ok(f.Apply(Assembly.CloseOperationId, new[] { folded }, Assembly.CloseParams(Assembly.Wrapped)));
            return T.Ok(f.Apply(Assembly.PackOperationId, new[] { folded }, Assembly.PackParams(container)));
        }

        [Fact]
        public void TheWholesalersCap_HoldsForAStreetFoodBuild_WrappedOrBagged()
        {
            var items = new ItemCatalog(StreetFoodItems.All.Concat(StreetFoodItems.GrillVegetables).Concat(Drinks.Items));
            var factory = new ItemFactory(items, OperationCatalog.CreateDefault());
            foreach (string container in new[] { Assembly.Wrapped, Assembly.Bag, Assembly.Box })
            {
                Item wrap = Wrap(factory, container);
                long stock = PriceRules.StockCost(C, wrap)!.Value;
                Assert.True(stock <= PriceRules.ItemCost(C, wrap)!.Value);
                Assert.True(C.TryGetMarket("meal", out MarketDef meal));
                for (int q = 0; q <= 1000; q += 25)
                {
                    long wholesale = Math.Max(0, PriceRules.ItemWholesalePayout(C, meal, wrap, q));
                    Assert.True(wholesale + wrap.Weight * C.ByproductMaxPerComponent < stock, container + " q=" + q);
                }
            }
        }

        [Fact]
        public void TheFaucetFloor_MattersForWrapPaperTheBagAndTheCup()
        {
            long b = C.ByproductMaxPerComponent;
            foreach (string id in new[] { "wrap_paper", "paper_bag", "cup" })
            {
                Assert.True(C.TryGetGrocerPrice(id, out long price), id);
                Assert.True(price * Supplier.BulkPermille / 1000 <= b, id + ": 80 % alone would be at most the byproducts");
                Assert.Equal(b + 1, Supplier.BulkUnitPrice(C, price)); // the floor: more than the byproducts a unit can make
                Assert.True(Supplier.BulkUnitPrice(C, price) <= Supplier.OrderCost(C, price, 1), id + ": never above one unit's order");
            }
            // The box ($40) gets the full 20 % off: 32 as well, but from the 80 %.
            Assert.True(C.TryGetGrocerPrice("takeaway_box", out long box));
            Assert.Equal(box * Supplier.BulkPermille / 1000, Supplier.BulkUnitPrice(C, box));
        }
    }
}
