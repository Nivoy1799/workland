using System;
using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;
using Xunit;
using T = Worktown.Sim.Tests.Cooking.TestItems;

namespace Worktown.Sim.Tests.Economy
{
    /// <summary>
    /// Spec 034: the shop's stock (counts, capacity, portions of an opened unit, unpacking up to capacity, the refund backstop), the
    /// supplier (bulk price, packs, delivery delay), the order book, the engine's supply purchase and refund, the wholesaler's cap at
    /// the bulk price (anti-faucet) and determinism.
    /// </summary>
    public class StockAndSupplyTests
    {
        private static readonly EconomyCatalog C = TestEconomy.Catalog;
        private static readonly EconomyEngine E = TestEconomy.Engine;

        private static ShopStock Stock(params StockLine[] lines) => ShopStock.Create(lines);
        private static StockLine Whole(string id, int count, int capacity) => new StockLine(id, count, capacity, 100, 0);

        [Fact]
        public void ATake_DecrementsTheCount_AndAnEmptyLineRefusesWithOutOfStock()
        {
            ShopStock s = Stock(Whole("lettuce", 2, 12));
            StockResult a = s.TryTake("lettuce");
            Assert.True(a.Ok);
            Assert.Equal(1, a.Amount);
            Assert.Equal(1, a.Stock.Lines[0].Count);
            Assert.Equal(2, s.Lines[0].Count); // immutable
            StockResult b = a.Stock.TryTake("lettuce");
            Assert.Equal(0, b.Stock.Lines[0].Count);
            StockResult c = b.Stock.TryTake("lettuce");
            Assert.Equal(StockError.OutOfStock, c.Error);
            Assert.Same(b.Stock, c.Stock);
            Assert.Equal(0, c.Amount);
            Assert.Equal(StockError.UnknownGood, s.TryTake("basil").Error);
        }

        [Fact]
        public void AnUnpack_FillsUpToCapacity_AndTheRestStaysInTheBox()
        {
            ShopStock s = Stock(Whole("wrap", 5, 8));
            StockResult filled = s.Fill("wrap", 6);
            Assert.True(filled.Ok);
            Assert.Equal(3, filled.Amount); // 3 fit, 3 stay in the box
            Assert.Equal(8, filled.Stock.Lines[0].Count);
            Assert.Equal(StockError.Full, filled.Stock.Fill("wrap", 3).Error);
            Assert.Equal(StockError.InvalidQuantity, s.Fill("wrap", 0).Error);
            Assert.Equal(6, Stock(Whole("wrap", 0, 8)).Fill("wrap", 6).Amount);
        }

        [Fact]
        public void APortion_OpensASealedUnit_AndUsesItUp_ThenTheLastDrops()
        {
            // A bottle of sauce is 100 %: a squeeze draws 10..30 % of the opened one.
            ShopStock s = Stock(new StockLine("chili_sauce", 1, 6, 100, 0));
            StockResult a = s.TryDraw("chili_sauce", 30);
            Assert.Equal(30, a.Amount);
            Assert.Equal((0, 70), (a.Stock.Lines[0].Count, a.Stock.Lines[0].OpenPercent));
            StockResult b = a.Stock.TryDraw("chili_sauce", 30).Stock.TryDraw("chili_sauce", 30);
            Assert.Equal(10, b.Stock.Lines[0].OpenPercent);
            StockResult last = b.Stock.TryDraw("chili_sauce", 30);
            Assert.True(last.Ok);
            Assert.Equal(10, last.Amount); // the last drops
            Assert.True(last.Stock.Lines[0].IsEmpty);
            Assert.Equal(StockError.OutOfStock, last.Stock.TryDraw("chili_sauce", 10).Error);
            Assert.Equal(StockError.InvalidQuantity, s.TryDraw("chili_sauce", 0).Error);

            // A keg is 20 cups (2000 % of a cup): a pour of 90 % opens it, and the opened one carries over into the next keg.
            ShopStock keg = Stock(new StockLine("lemonade", 2, 2, 2000, 50));
            StockResult pour = keg.TryDraw("lemonade", 90);
            Assert.Equal(90, pour.Amount);
            Assert.Equal((1, 1960), (pour.Stock.Lines[0].Count, pour.Stock.Lines[0].OpenPercent));
            Assert.Equal(1 * 2000 + 1960, pour.Stock.Lines[0].AvailablePercent);
        }

        [Fact]
        public void TheRefundBackstop_PutsATakenUnitOrPortionBack()
        {
            ShopStock s = Stock(Whole("tomato", 12, 12), new StockLine("garlic_herb_sauce", 1, 6, 100, 0));
            StockResult taken = s.TryTake("tomato");
            ShopStock back = taken.Stock.Return("tomato", 1).Stock;
            Assert.Equal(12, back.Lines[0].Count);
            // A return is never refused, even past the capacity (an unpack may have filled the line in between).
            Assert.Equal(13, back.Return("tomato", 1).Stock.Lines[0].Count);

            StockResult drawn = s.TryDraw("garlic_herb_sauce", 20);
            ShopStock sealedAgain = drawn.Stock.ReturnPercent("garlic_herb_sauce", 20).Stock;
            Assert.Equal((1, 0), (sealedAgain.Lines[1].Count, sealedAgain.Lines[1].OpenPercent));
            ShopStock part = s.TryDraw("garlic_herb_sauce", 30).Stock.TryDraw("garlic_herb_sauce", 10).Stock.ReturnPercent("garlic_herb_sauce", 10).Stock;
            Assert.Equal((0, 70), (part.Lines[1].Count, part.Lines[1].OpenPercent));
        }

        [Fact]
        public void LowStock_IsBelowAQuarterOfTheCapacity_AndAShiftStartsAtHalf()
        {
            Assert.False(ShopStock.IsLow(Whole("lettuce", 3, 12)));
            Assert.True(ShopStock.IsLow(Whole("lettuce", 2, 12)));
            Assert.True(ShopStock.IsLow(Whole("lettuce", 0, 12)));
            Assert.False(ShopStock.IsLow(Whole("wrap", 2, 8)));
            Assert.True(ShopStock.IsLow(Whole("wrap", 1, 8)));
            Assert.False(ShopStock.IsLow(new StockLine("lemonade", 0, 2, 2000, 1000)), "half a keg of two is a quarter");
            Assert.True(ShopStock.IsLow(new StockLine("lemonade", 0, 2, 2000, 999)));
            Assert.Equal(6, ShopStock.StartingCount(12));
            Assert.Equal(4, ShopStock.StartingCount(8));
            Assert.Equal(3, ShopStock.StartingCount(6));
            Assert.Equal(10, ShopStock.StartingCount(20));
            Assert.Equal(1, ShopStock.StartingCount(2));
        }

        [Fact]
        public void BadStockLines_AreRejected()
        {
            Assert.Throws<ArgumentException>(() => Stock(Whole("lettuce", 1, 12), Whole("lettuce", 1, 12)));
            Assert.Throws<ArgumentException>(() => Stock(Whole("lettuce", 13, 12)));
            Assert.Throws<ArgumentException>(() => Stock(Whole("lettuce", 1, 0)));
            Assert.Throws<ArgumentException>(() => Stock(Whole("lettuce", 1, StockLine.MaxCapacity + 1)));
            Assert.Throws<ArgumentException>(() => Stock(new StockLine("lettuce", 1, 12, 0, 0)));
            Assert.Throws<ArgumentException>(() => Stock(new StockLine("lettuce", 1, 12, 100, 101)));
            Assert.Throws<ArgumentException>(() => Stock(Whole("Not An Id", 1, 12)));
        }

        [Fact]
        public void TheBulkPrice_IsEightyPercentRoundedUp_NeverBelowTheFaucetFloor()
        {
            Assert.Equal(31, C.ByproductMaxPerComponent);
            Assert.Equal(48, Supplier.BulkUnitPrice(C, 60));
            Assert.Equal(36, Supplier.BulkUnitPrice(C, 45));
            Assert.Equal(32, Supplier.BulkUnitPrice(C, 40));
            Assert.Equal(32, Supplier.BulkUnitPrice(C, 35)); // B + 1: no bulk unit is worth less than its byproducts
            // One pack of lettuce: 6 × $45 × 0.8 = $216.
            Assert.True(Supplier.TryOffer(C, "lettuce", 1, out SupplierOffer lettuce));
            Assert.Equal((6, 1, 60), (lettuce.PackSize, lettuce.GrocerItemsPerUnit, lettuce.DelayMinutes));
            Assert.Equal(216, Supplier.OrderCost(C, lettuce, 1));
            Assert.Equal(648, Supplier.OrderCost(C, lettuce, 3));
            // A keg of lemonade: 20 cups × $60 × 0.8 = $960, one keg per box.
            Assert.True(Supplier.TryOffer(C, "lemonade", Supplier.CupsPerKeg, out SupplierOffer keg));
            Assert.Equal(1, keg.PackSize);
            Assert.Equal(960, Supplier.OrderCost(C, keg, 1));
            // Rounded up: 6 × $55 × 0.8 = $264 exactly; 1 item at $57 → $45.6 → $46.
            Assert.Equal(46, Supplier.OrderCost(C, 57, 1));
            Assert.Equal(6 * 32, Supplier.OrderCost(C, 35, 6)); // the floor for the $35 goods
            Assert.False(Supplier.TryOffer(C, "basil_juice", 1, out _));
            // Every priced good: the bulk unit is never above what one unit cost in any order.
            foreach (GrocerPrice g in C.Grocer)
                for (int items = 1; items <= 60; items++)
                    Assert.True(Supplier.BulkUnitPrice(C, g.Price) * items <= Supplier.OrderCost(C, g.Price, items), g.BaseId + " " + items);
        }

        [Fact]
        public void AnOrder_IsDueAfterTheDelay_OldestFirst()
        {
            Assert.True(Supplier.TryOffer(C, "wrap", 1, out SupplierOffer wrap));
            Assert.True(Supplier.TryOffer(C, "tomato", 1, out SupplierOffer tomato));
            SupplyBook book = SupplyBook.Empty
                .Place(wrap, 2, 336, "p1", 600, out SupplyOrder first, out StockError e1)
                .Place(tomato, 1, 336, "p2", 610, out SupplyOrder second, out StockError e2);
            Assert.Equal((StockError.None, StockError.None), (e1, e2));
            Assert.Equal((1, 2), (first.Id, second.Id));
            Assert.Equal(660, first.DueMinute);
            Assert.Equal(6, first.UnitsPerPack);
            Assert.Equal(2, book.PacksOnTheWay("wrap"));
            Assert.Equal(660, book.NextDue());
            var due = new List<SupplyOrder>();
            Assert.Same(book, book.TakeDue(659, due));
            Assert.Empty(due);
            SupplyBook rest = book.TakeDue(660, due);
            Assert.Equal(new[] { 1 }, due.Select(o => o.Id));
            Assert.Single(rest.Pending);
            SupplyBook none = book.TakeDue(10_000, due);
            Assert.Equal(new[] { 1, 2 }, due.Select(o => o.Id));
            Assert.Empty(none.Pending);
            Assert.Equal(3, none.NextId);
            Assert.Null(none.NextDue());
            Assert.Single(book.Cancel(1).Pending);
        }

        [Fact]
        public void AnOrder_RefusesBadPackCounts_AndTooManyOrders()
        {
            Assert.True(Supplier.TryOffer(C, "wrap", 1, out SupplierOffer wrap));
            SupplyBook book = SupplyBook.Empty;
            Assert.Same(book, book.Place(wrap, 0, 1, "p", 0, out SupplyOrder none, out StockError e));
            Assert.Equal(StockError.InvalidQuantity, e);
            Assert.Null(none);
            book.Place(wrap, Supplier.MaxPacksPerOrder + 1, 1, "p", 0, out _, out e);
            Assert.Equal(StockError.InvalidQuantity, e);
            for (int i = 0; i < SupplyBook.MaxPending; i++) book = book.Place(wrap, 1, 1, "p", i, out _, out _);
            Assert.Same(book, book.Place(wrap, 1, 1, "p", 99, out _, out e));
            Assert.Equal(StockError.TooManyOrders, e);
        }

        [Fact]
        public void BuyingSupply_ChargesTheBulkPriceOnce_AndARefundMovesItBack()
        {
            EconomyState s = TestEconomy.Open(E, E.Initial(), "alice", 1000);
            Assert.True(Supplier.TryOffer(C, "lettuce", 1, out SupplierOffer lettuce));
            PurchaseResult bought = E.TryBuySupply(s, "alice", lettuce, 2);
            Assert.True(bought.Ok);
            Assert.Equal(432, bought.Cost);
            Assert.Equal(568, TestEconomy.Acct(bought.State, "alice").Balance);
            Assert.Equal(432, bought.State.Totals.GrocerRevenue);
            Assert.Equal(EconomyError.InsufficientFunds, E.TryBuySupply(TestEconomy.Open(E, E.Initial(), "bob", 100), "bob", lettuce, 1).Error);
            Assert.Equal(EconomyError.InvalidQuantity, E.TryBuySupply(s, "alice", lettuce, 0).Error);
            Assert.Equal(EconomyError.UnknownAccount, E.TryBuySupply(s, "carol", lettuce, 1).Error);
            Assert.Equal(EconomyError.UnknownBaseItem, E.TryBuySupply(s, "alice", lettuce with { GrocerPrice = 1 }, 1).Error);

            LedgerResult refunded = E.Refund(bought.State, "alice", 432);
            Assert.True(refunded.Ok);
            Assert.Equal(1000, TestEconomy.Acct(refunded.State, "alice").Balance);
            Assert.Equal(0, refunded.State.Totals.GrocerRevenue);
            Assert.Equal(EconomyError.InvalidQuantity, E.Refund(refunded.State, "alice", 1).Error); // never more than the grocer took
            Assert.Equal(EconomyError.InvalidQuantity, E.Refund(bought.State, "alice", 0).Error);
            Assert.Equal(EconomyError.UnknownAccount, E.Refund(bought.State, "nobody", 10).Error);
        }

        [Fact]
        public void PriceGoodId_IsTheGoodItself_UnlessAnotherIsGiven()
        {
            var plain = new SupplierOffer("lettuce", 30, 1, Supplier.DefaultPackSize, Supplier.DelayMinutes);
            Assert.Null(plain.PriceGood);
            Assert.Equal("lettuce", plain.PriceGoodId);
            Assert.Equal("lettuce", (plain with { PriceGood = "lettuce" }).PriceGoodId);
            var cone = new SupplierOffer("seitan_cone", 110, 30, 1, Supplier.DelayMinutes, "seitan");
            Assert.Equal("seitan", cone.PriceGoodId);
            Assert.Equal("seitan_cone", cone.GoodId);
        }

        [Fact]
        public void BuyingSupply_TiesThePriceGoodToTheGood_OnlyAGoodWithoutAGrocerPriceBorrowsOne()
        {
            EconomyState s = TestEconomy.Open(E, E.Initial(), "alice", 100_000);
            Assert.True(C.TryGetGrocerPrice("seitan", out long seitan));
            Assert.False(C.TryGetGrocerPrice("seitan_cone", out _));

            // A cone has no grocer price of its own: it is priced as 30 seitan blocks, and the supplier gets exactly that.
            var cone = new SupplierOffer("seitan_cone", seitan, 30, 1, Supplier.DelayMinutes, "seitan");
            PurchaseResult bought = E.TryBuySupply(s, "alice", cone, 1);
            Assert.True(bought.Ok);
            Assert.Equal(Supplier.OrderCost(C, cone, 1), bought.Cost);
            Assert.Equal(100_000 - bought.Cost, TestEconomy.Acct(bought.State, "alice").Balance);
            Assert.Equal(bought.Cost, TestEconomy.Acct(bought.State, "alice").GrocerPaid);
            Assert.Equal(bought.Cost, bought.State.Totals.GrocerRevenue);

            // Lettuce has its own price: it cannot be bought at the seitan's (nothing changes).
            var cheap = new SupplierOffer("lettuce", seitan, 1, Supplier.DefaultPackSize, Supplier.DelayMinutes, "seitan");
            PurchaseResult refused = E.TryBuySupply(s, "alice", cheap, 1);
            Assert.Equal(EconomyError.UnknownBaseItem, refused.Error);
            Assert.Same(s, refused.State);
            Assert.Equal(0, refused.Cost);

            // The good itself as the price good, or none, is the plain offer.
            Assert.True(Supplier.TryOffer(C, "lettuce", 1, out SupplierOffer lettuce));
            Assert.True(E.TryBuySupply(s, "alice", lettuce with { PriceGood = "lettuce" }, 1).Ok);
            Assert.Equal(E.TryBuySupply(s, "alice", lettuce, 1).Cost, E.TryBuySupply(s, "alice", lettuce with { PriceGood = "lettuce" }, 1).Cost);
            // A price good the grocer does not price at all, or at another price, is refused too.
            Assert.Equal(EconomyError.UnknownBaseItem, E.TryBuySupply(s, "alice", cone with { PriceGood = "moon_cheese" }, 1).Error);
            Assert.Equal(EconomyError.UnknownBaseItem, E.TryBuySupply(s, "alice", cone with { GrocerPrice = seitan - 1 }, 1).Error);
        }

        [Fact]
        public void TheWholesalersCap_CountsTheBulkUnitPrice_SoNothingSellsForMoreThanItsStockCost()
        {
            // Plated fries: one potato, $48 in bulk (not $60): the cap is 48 - 1 - 31 = 16.
            Assert.Equal(48, PriceRules.StockCost(C, TestEconomy.PlatedFries()));
            Assert.Equal(16, PriceRules.SurplusCap(C, TestEconomy.PlatedFries()));
            var plates = new[]
            {
                TestEconomy.PlatedFries(), TestEconomy.PlatedRaw(), TestEconomy.PlatedBurnt(),
                TestEconomy.PlatedTomatoCashew(), TestEconomy.PlatedSalt(), TestEconomy.PlatedApple(),
            };
            foreach (Item item in plates)
            {
                C.TryGetMarket(item.Weight >= 2 ? "meal" : "snack", out MarketDef market);
                long stock = PriceRules.StockCost(C, item)!.Value;
                Assert.True(stock <= PriceRules.ItemCost(C, item)!.Value);
                // What the shop paid for the item's units in the smallest order (one unit each) is at least the stock cost.
                long paid = item.Components.Sum(comp => { C.TryGetGrocerPrice(comp.BaseId, out long p); return Supplier.OrderCost(C, p, 1); });
                Assert.True(stock <= paid);
                for (int q = 0; q <= 1000; q += 10)
                {
                    long wholesale = Math.Max(0, PriceRules.ItemWholesalePayout(C, market, item, q));
                    Assert.True(wholesale + item.Weight * C.ByproductMaxPerComponent < stock, item.CanonicalTree + " q=" + q);
                }
            }
        }

        [Fact]
        public void TheWholesalersCap_CountsASqueezeOrAPourAtTheLeastShareItDrew()
        {
            var items = new ItemCatalog(StreetFoodItems.All.Concat(StreetFoodItems.GrillVegetables).Concat(Drinks.Items));
            var factory = new ItemFactory(items, OperationCatalog.CreateDefault());
            Item sauce = T.Ok(factory.Apply(Assembly.SqueezeOperationId, new[] { T.Ok(factory.CreateBase("chili_sauce")) }, Assembly.SqueezeParams(2f)));
            long bottle = Supplier.BulkUnitPrice(C, 80); // $64
            Assert.Equal(bottle * 30 / 100, PriceRules.StockCost(C, sauce)); // lots: at least 30 % of a bottle drawn
            Item dash = T.Ok(factory.Apply(Assembly.SqueezeOperationId, new[] { T.Ok(factory.CreateBase("chili_sauce")) }, Assembly.SqueezeParams(0.2f)));
            Assert.Equal(bottle * 10 / 100, PriceRules.StockCost(C, dash));
            Item cup = T.Ok(factory.Apply(Drinks.FillOperationId, new[] { T.Ok(factory.CreateBase("lemonade")) }, Drinks.FillParams(360f)));
            long perCup = Supplier.BulkUnitPrice(C, 60); // $48 a cup's worth of the keg
            Assert.Equal(perCup * PriceRules.FillPercent(Drinks.RightFrom) / 100, PriceRules.StockCost(C, cup));
            Assert.True(PriceRules.StockCost(C, cup) <= perCup * PriceRules.FillPercent(360f) / 100.0);
        }

        [Fact]
        public void StockAndSupply_AreDeterministic()
        {
            Assert.True(Supplier.TryOffer(C, "seitan", 1, out SupplierOffer seitan));
            string Run()
            {
                ShopStock s = Stock(Whole("seitan", 6, 12), new StockLine("soda", 1, 2, 2000, 0));
                SupplyBook book = SupplyBook.Empty;
                var log = new List<string>();
                var due = new List<SupplyOrder>();
                for (int minute = 0; minute < 200; minute += 7)
                {
                    StockResult r = minute % 3 == 0 ? s.TryTake("seitan") : s.TryDraw("soda", 1 + minute % 100);
                    s = r.Stock;
                    if (minute % 35 == 0) book = book.Place(seitan, 1 + minute % 3, Supplier.OrderCost(C, seitan, 1 + minute % 3), "p", minute, out _, out _);
                    book = book.TakeDue(minute, due);
                    foreach (SupplyOrder o in due) s = s.Fill(o.GoodId, o.Packs * o.UnitsPerPack).Stock;
                    log.Add(r.Error + ":" + r.Amount + ":" + string.Join(",", s.Lines.Select(l => l.Count + "/" + l.OpenPercent)) + ":" + book.Pending.Count);
                }
                return string.Join("|", log);
            }
            Assert.Equal(Run(), Run());
        }
    }
}
