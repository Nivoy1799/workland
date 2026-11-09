using System;
using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;
using Xunit;
using T = Worktown.Sim.Tests.Cooking.TestItems;

namespace Worktown.Sim.Tests.Economy
{
    public class SaleTests
    {
        private static readonly EconomyEngine E = TestEconomy.Engine;

        /// <summary>Tick 12 (hour 12): snack and meal demand are open, prices at Ceiling. alice has 0.</summary>
        private static EconomyState Noon()
        {
            var s = TestEconomy.Advance(E, E.Initial(), 12);
            return TestEconomy.Open(E, s, "alice", 0);
        }

        [Fact]
        public void FriesSellIntoOpenSnackDemand()
        {
            var s = Noon();
            int open = TestEconomy.Mkt(s, "snack").OpenDemand;
            Assert.True(open > 0);
            var r = E.TrySellItem(s, "alice", 1, TestEconomy.PlatedFries());
            Assert.True(r.Ok, r.Error.ToString());
            // fries quality 0.69699 -> 696 permille; payout = 400 * (500 + 696) / 1000 = 478.4 -> 478
            Assert.Equal(478, r.Payout);
            Assert.Equal("snack", r.MarketId);
            Assert.Equal(SaleChannel.NpcDemand, r.Channel);
            Assert.Empty(r.Byproducts);
            var m = TestEconomy.Mkt(r.State, "snack");
            Assert.Equal(open - 1, m.OpenDemand);
            Assert.Equal(1, m.PlayerFilledThisTick);
            Assert.Equal(478, m.DemandPaidThisTick);
            Assert.Equal(478, TestEconomy.Acct(r.State, "alice").Balance);
            Assert.Equal(478, r.State.Totals.PaidToPlayers);
            Assert.Contains(r.State.RecentConsumed, c => c.InstanceId == 1 && c.Tick == 12);
        }

        [Fact]
        public void InputStateIsNeverMutated()
        {
            var s = Noon();
            var before = EconomySnapshot.Canonical(s);
            E.TrySellItem(s, "alice", 1, TestEconomy.PlatedFries());
            Assert.Equal(before, EconomySnapshot.Canonical(s));
        }

        [Fact]
        public void BetterQualityEarnsMoreInTheSameMarket() // AC4
        {
            var s = Noon();
            var fries = E.TrySellItem(s, "alice", 1, TestEconomy.PlatedFries());
            var burnt = E.TrySellItem(s, "alice", 2, TestEconomy.PlatedBurnt());
            Assert.True(fries.Ok && burnt.Ok);
            Assert.Equal("snack", fries.MarketId);
            Assert.Equal("snack", burnt.MarketId);
            Assert.Equal(SaleChannel.NpcDemand, fries.Channel);
            Assert.Equal(SaleChannel.NpcDemand, burnt.Channel);
            // burnt q = 0.29606 -> 296; payout = 400 * 796 / 1000 = 318.4 -> 318
            Assert.Equal(318, burnt.Payout);
            Assert.True(fries.Payout > burnt.Payout);
        }

        [Fact]
        public void TwoIngredientDishGoesToMealMarket()
        {
            var s = Noon();
            Assert.True(TestEconomy.Mkt(s, "meal").OpenDemand > 0);
            var r = E.TrySellItem(s, "alice", 1, TestEconomy.PlatedTomatoCashew());
            Assert.True(r.Ok);
            Assert.Equal("meal", r.MarketId);
            // q = 0.74609 -> 746; payout = 1200 * 1246 / 1000 = 1495.2 -> 1495
            Assert.Equal(1495, r.Payout);
        }

        [Fact]
        public void SaturatedMarketPaysTheCappedWholesalePayout()
        {
            var s = TestEconomy.Open(E, E.Initial(), "alice", 0); // tick 0: no demand yet
            var r = E.TrySellItem(s, "alice", 1, TestEconomy.PlatedFries());
            Assert.True(r.Ok);
            Assert.Equal(SaleChannel.Wholesaler, r.Channel);
            Assert.Equal(16, r.Payout); // min(84, 48 - 1 - 31): spec 034, the bulk unit price
            var m = TestEconomy.Mkt(r.State, "snack");
            Assert.Equal(1, m.WholesaleThisTick);
            Assert.Equal(0, m.PlayerFilledThisTick);
            Assert.Equal(0, m.DemandPaidThisTick);
        }

        [Fact]
        public void SellingBeyondOpenDemandSwitchesToWholesaler()
        {
            var s = TestEconomy.SetMarket(Noon(), "snack", m => m with { OpenDemand = 1 });
            var first = E.TrySellItem(s, "alice", 1, TestEconomy.PlatedFries());
            var second = E.TrySellItem(first.State, "alice", 2, TestEconomy.PlatedFries());
            Assert.Equal(SaleChannel.NpcDemand, first.Channel);
            Assert.Equal(SaleChannel.Wholesaler, second.Channel);
            Assert.Equal(478 + 16, TestEconomy.Acct(second.State, "alice").Balance); // spec 034: the cap at the bulk price
        }

        [Fact]
        public void CheapPlateGetsOnlyTheSmallCap()
        {
            var s = TestEconomy.Open(E, E.Initial(), "alice", 0);
            // Spec 034: the caps count the bulk unit price: salt 32 - 1 - 31 = 0, so the wholesaler refuses it.
            var salt = E.TrySellItem(s, "alice", 1, TestEconomy.PlatedSalt());
            Assert.Equal(EconomyError.WholesaleRefused, salt.Error);
            Assert.Equal(0, salt.Payout);
            var cashew = E.TrySellItem(s, "alice", 2, TestEconomy.PlatedTomatoCashew());
            Assert.Equal(113, cashew.Payout); // (56 + 120) - 1 - 62
        }

        [Fact]
        public void RepeatedInstanceIsRejectedAndExpiresAfterTheWindow()
        {
            var s = Noon(); // tick 12
            var r = E.TrySellItem(s, "alice", 7, TestEconomy.PlatedFries());
            Assert.True(r.Ok);
            Assert.Equal(EconomyError.AlreadyConsumed, E.TrySellItem(r.State, "alice", 7, TestEconomy.PlatedFries()).Error);
            // kept while Tick - 24 <= 12, i.e. through tick 36, dropped at tick 37 (12 < 37 - 24)
            var t36 = TestEconomy.Advance(E, r.State, 24);
            Assert.Equal(36, t36.Tick);
            Assert.Equal(EconomyError.AlreadyConsumed, E.TrySellItem(t36, "alice", 7, TestEconomy.PlatedFries()).Error);
            var t37 = TestEconomy.Advance(E, t36, 1);
            Assert.Empty(t37.RecentConsumed);
            Assert.True(E.TrySellItem(t37, "alice", 7, TestEconomy.PlatedFries()).Ok);
        }

        [Fact]
        public void BalanceLimitBoundary()
        {
            var s = TestEconomy.SetAccount(Noon(), "alice", a => a with { Balance = EconomyLimits.MaxBalance - 478 });
            var ok = E.TrySellItem(s, "alice", 1, TestEconomy.PlatedFries());
            Assert.True(ok.Ok);
            Assert.Equal(EconomyLimits.MaxBalance, TestEconomy.Acct(ok.State, "alice").Balance);
            var s2 = TestEconomy.SetAccount(Noon(), "alice", a => a with { Balance = EconomyLimits.MaxBalance - 477 });
            var bad = E.TrySellItem(s2, "alice", 1, TestEconomy.PlatedFries());
            Assert.Equal(EconomyError.BalanceLimit, bad.Error);
            Assert.Same(s2, bad.State);
        }

        [Fact]
        public void SaleByproductsOverTheGoodsLimitFailWithoutPartialEffects()
        {
            var rules = new[] { new ByproductRule(ByproductTrigger.Sale, "snack", "food_scraps", 1, 0) };
            var e = new EconomyEngine(TestEconomy.Build(byproducts: rules));
            var s = TestEconomy.Open(e, e.Initial(), "alice", 0);
            s = TestEconomy.SetAccount(s, "alice", a => a with { Goods = new[] { new GoodAmount("food_scraps", EconomyLimits.MaxGoodUnits) } });
            var r = e.TrySellItem(s, "alice", 1, TestEconomy.PlatedFries());
            Assert.Equal(EconomyError.GoodsLimit, r.Error);
            Assert.Same(s, r.State);
        }

        [Fact]
        public void SellGoodsAtDemandPriceAndFloorAndMixed()
        {
            var s = TestEconomy.Open(E, E.Initial(), "alice", 0);
            s = TestEconomy.SetAccount(s, "alice", a => a with { Goods = new[] { new GoodAmount("food_scraps", 20) } });
            s = TestEconomy.SetMarket(s, "food_scraps", m => m with { OpenDemand = 3, Price = 3 });

            // 2 units, all at demand: 2 * 3 = 6
            var demand = E.TrySellGoods(s, "alice", "food_scraps", 2);
            Assert.Equal(6, demand.Payout);
            Assert.Equal(SaleChannel.NpcDemand, demand.Channel);
            Assert.Equal(1, TestEconomy.Mkt(demand.State, "food_scraps").OpenDemand);
            Assert.Equal(6, TestEconomy.Mkt(demand.State, "food_scraps").DemandPaidThisTick);
            Assert.Equal(18, TestEconomy.Units(demand.State, "alice", "food_scraps"));

            // 5 units with 3 open: 3 * 3 + 2 * 2 (floor) = 13
            var mixed = E.TrySellGoods(s, "alice", "food_scraps", 5);
            Assert.Equal(13, mixed.Payout);
            Assert.Equal(SaleChannel.Mixed, mixed.Channel);
            var m = TestEconomy.Mkt(mixed.State, "food_scraps");
            Assert.Equal(0, m.OpenDemand);
            Assert.Equal(3, m.PlayerFilledThisTick);
            Assert.Equal(2, m.WholesaleThisTick);
            Assert.Equal(9, m.DemandPaidThisTick);
            Assert.Equal(13, mixed.State.Totals.PaidToPlayers);

            // no open demand: floor only
            var floor = E.TrySellGoods(TestEconomy.SetMarket(s, "food_scraps", x => x with { OpenDemand = 0 }), "alice", "food_scraps", 4);
            Assert.Equal(8, floor.Payout);
            Assert.Equal(SaleChannel.Wholesaler, floor.Channel);
        }

        [Fact]
        public void SellingAllHeldGoodsRemovesTheEntry()
        {
            var s = TestEconomy.Open(E, E.Initial(), "alice", 0);
            s = TestEconomy.SetAccount(s, "alice", a => a with { Goods = new[] { new GoodAmount("food_scraps", 4), new GoodAmount("used_cooking_oil", 1) } });
            var r = E.TrySellGoods(s, "alice", "food_scraps", 4);
            Assert.Equal(new[] { "used_cooking_oil" }, TestEconomy.Acct(r.State, "alice").Goods.Select(g => g.GoodId));
        }

        [Fact]
        public void SellGoodsErrorsKeepState()
        {
            var s = TestEconomy.Open(E, E.Initial(), "alice", 0);
            s = TestEconomy.SetAccount(s, "alice", a => a with { Goods = new[] { new GoodAmount("food_scraps", 3) } });
            void Expect(EconomyError err, SaleResult r)
            {
                Assert.Equal(err, r.Error);
                Assert.Same(s, r.State);
                Assert.Equal(0, r.Payout);
                Assert.Equal(SaleChannel.None, r.Channel);
            }
            Expect(EconomyError.UnknownAccount, E.TrySellGoods(s, "bob", "food_scraps", 1));
            Expect(EconomyError.UnknownGood, E.TrySellGoods(s, "alice", "ghost", 1));
            Expect(EconomyError.UnknownGood, E.TrySellGoods(s, "alice", "snack", 1)); // Item market, not a commodity
            Expect(EconomyError.UnknownGood, E.TrySellGoods(s, "alice", null!, 1));
            Expect(EconomyError.InvalidQuantity, E.TrySellGoods(s, "alice", "food_scraps", 0));
            Expect(EconomyError.InvalidQuantity, E.TrySellGoods(s, "alice", "food_scraps", EconomyLimits.MaxLot + 1));
            Expect(EconomyError.InsufficientGoods, E.TrySellGoods(s, "alice", "food_scraps", 4));
            Expect(EconomyError.InsufficientGoods, E.TrySellGoods(s, "alice", "used_cooking_oil", 1));
        }

        [Fact]
        public void SellGoodsBalanceLimit()
        {
            var s = TestEconomy.Open(E, E.Initial(), "alice", EconomyLimits.MaxBalance - 1);
            s = TestEconomy.SetAccount(s, "alice", a => a with { Goods = new[] { new GoodAmount("food_scraps", 3) } });
            var r = E.TrySellGoods(s, "alice", "food_scraps", 1); // floor 2 > 1 headroom
            Assert.Equal(EconomyError.BalanceLimit, r.Error);
            Assert.Same(s, r.State);
        }

        [Fact]
        public void DiscardGivesScrapsAndConsumesTheInstance()
        {
            var s = TestEconomy.Open(E, E.Initial(), "alice", 50);
            var r = E.Discard(s, "alice", 9, TestEconomy.PlatedTomatoCashew());
            Assert.True(r.Ok);
            Assert.Equal(2, TestEconomy.Units(r.State, "alice", "food_scraps"));
            Assert.Equal(50, TestEconomy.Acct(r.State, "alice").Balance);
            Assert.Equal(EconomyError.AlreadyConsumed, E.TrySellItem(r.State, "alice", 9, TestEconomy.PlatedFries()).Error);
            Assert.Equal(EconomyError.AlreadyConsumed, E.Discard(r.State, "alice", 9, TestEconomy.PlatedRaw()).Error);
        }

        [Fact]
        public void DiscardErrorsKeepState()
        {
            var s = TestEconomy.Open(E, E.Initial(), "alice", 0);
            Assert.Equal(EconomyError.UnknownAccount, E.Discard(s, "bob", 1, T.Fries()).Error);
            var bad = E.Discard(s, "alice", 0, T.Fries());
            Assert.Equal(EconomyError.InvalidInstanceId, bad.Error);
            Assert.Same(s, bad.State);
            var full = TestEconomy.SetAccount(s, "alice", a => a with { Goods = new[] { new GoodAmount("food_scraps", EconomyLimits.MaxGoodUnits) } });
            var lim = E.Discard(full, "alice", 1, TestEconomy.PlatedRaw());
            Assert.Equal(EconomyError.GoodsLimit, lim.Error);
            Assert.Same(full, lim.State);
            // unplated items can be discarded too (no dead-end items)
            Assert.True(E.Discard(s, "alice", 2, T.Fries()).Ok);
        }

        [Fact]
        public void NullArgumentsThrowButStringsDoNot()
        {
            var s = TestEconomy.Open(E, E.Initial(), "alice", 0);
            Assert.Throws<ArgumentNullException>(() => E.TrySellItem(null!, "alice", 1, T.Fries()));
            Assert.Throws<ArgumentNullException>(() => E.TrySellItem(s, "alice", 1, null!));
            Assert.Equal(EconomyError.UnknownAccount, E.TrySellItem(s, null!, 1, T.Fries()).Error);
        }

        [Fact]
        public void StateFromAnotherCatalogThrowsInvalidOperation()
        {
            var s = TestEconomy.Open(E, E.Initial(), "alice", 0);
            var broken = s with { Markets = s.Markets.Where(m => m.MarketId != "snack").ToList().AsReadOnly() };
            Assert.Throws<InvalidOperationException>(() => E.TrySellItem(broken, "alice", 1, TestEconomy.PlatedFries()));
        }

        [Fact]
        public void EveryErrorIsProducedWithStateUnchanged() // AC8
        {
            var s = TestEconomy.SetAccount(Noon(), "alice", a => a with { Balance = 100 });
            var basilEngine = new EconomyEngine(TestEconomy.Build(grocer: TestEconomy.GrocerWithout("basil")));
            var basilState = TestEconomy.Open(basilEngine, basilEngine.Initial(), "alice", 0);
            var sold = E.TrySellItem(s, "alice", 5, TestEconomy.PlatedFries()).State;
            var rich = TestEconomy.SetAccount(s, "alice", a => a with { Balance = EconomyLimits.MaxBalance - 1 });
            var goodsFull = new[] { new GoodAmount("food_scraps", EconomyLimits.MaxGoodUnits) };

            var cases = new List<(EconomyError Expected, EconomyState Before, Func<(EconomyState State, EconomyError Error, long Payout)> Run)>
            {
                (EconomyError.InvalidAccountId, s, () => L(E.OpenAccount(s, "Not Valid"))),
                (EconomyError.AccountExists, s, () => L(E.OpenAccount(s, "alice"))),
                (EconomyError.UnknownAccount, s, () => S(E.TrySellItem(s, "ghost", 1, TestEconomy.PlatedFries()))),
                (EconomyError.InvalidInstanceId, s, () => S(E.TrySellItem(s, "alice", 0, TestEconomy.PlatedFries()))),
                (EconomyError.AlreadyConsumed, sold, () => S(E.TrySellItem(sold, "alice", 5, TestEconomy.PlatedFries()))),
                (EconomyError.NoMarket, s, () => S(E.TrySellItem(s, "alice", 1, T.Fries()))),               // unplated
                (EconomyError.QualityTooLow, s, () => S(E.TrySellItem(s, "alice", 1, TestEconomy.PlatedRaw()))),
                (EconomyError.UnknownGood, s, () => L(E.AddGoods(s, "alice", new[] { new GoodAmount("ghost", 1) }))),
                (EconomyError.InvalidQuantity, s, () => L(E.TryDebit(s, "alice", 0))),
                (EconomyError.InsufficientGoods, s, () => S(E.TrySellGoods(s, "alice", "food_scraps", 1))),
                (EconomyError.InsufficientFunds, s, () => L(E.TryDebit(s, "alice", 101))),
                (EconomyError.BalanceLimit, rich, () => S(E.TrySellItem(rich, "alice", 1, TestEconomy.PlatedFries()))),
                (EconomyError.GoodsLimit, s, () => L(E.AddGoods(s, "alice", goodsFull.Concat(new[] { new GoodAmount("food_scraps", 1) }).ToList()))),
                (EconomyError.UnknownBaseItem, s, () => P(E.TryBuyBase(s, "alice", "ghost", 1))),
                (EconomyError.WholesaleRefused, basilState, () => S(basilEngine.TrySellItem(basilState, "alice", 1, T.Ok(T.Plate(T.Base("basil")))))),
            };

            var covered = new HashSet<EconomyError>();
            foreach (var c in cases)
            {
                var (state, error, payout) = c.Run();
                Assert.Equal(c.Expected, error);
                Assert.Same(c.Before, state);
                Assert.Equal(0, payout);
                covered.Add(error);
            }
            var all = Enum.GetValues(typeof(EconomyError)).Cast<EconomyError>().Where(x => x != EconomyError.None);
            Assert.True(all.All(x => covered.Contains(x)), "not every EconomyError is covered");

            (EconomyState, EconomyError, long) L(LedgerResult r) => (r.State, r.Error, 0);
            (EconomyState, EconomyError, long) S(SaleResult r) => (r.State, r.Error, r.Payout);
            (EconomyState, EconomyError, long) P(PurchaseResult r) => (r.State, r.Error, r.Cost);
        }

        [Fact]
        public void DedupWindowBoundary()
        {
            var s = Noon(); // tick 12
            long t0 = s.Tick;
            var sold = E.TrySellItem(s, "alice", 77, TestEconomy.PlatedFries());
            Assert.True(sold.Ok, sold.Error.ToString());
            var st = sold.State;
            for (int k = 0; k <= EconomyLimits.DedupWindowTicks; k++)
            {
                Assert.Equal(t0 + k, st.Tick);
                var again = E.TrySellItem(st, "alice", 77, TestEconomy.PlatedFries());
                Assert.Equal(EconomyError.AlreadyConsumed, again.Error);
                st = TestEconomy.Advance(E, st, 1);
            }
            Assert.Equal(t0 + EconomyLimits.DedupWindowTicks + 1, st.Tick);
            Assert.True(E.TrySellItem(st, "alice", 77, TestEconomy.PlatedFries()).Ok);
        }
    }
}
