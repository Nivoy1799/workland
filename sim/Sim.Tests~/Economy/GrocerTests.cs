using Worktown.Sim.Economy;
using Xunit;
using T = Worktown.Sim.Tests.Cooking.TestItems;

namespace Worktown.Sim.Tests.Economy
{
    public class GrocerTests
    {
        private static readonly EconomyEngine E = TestEconomy.Engine;

        private static EconomyState Start(long balance = 1000) => TestEconomy.Open(E, E.Initial(), "alice", balance);

        [Fact]
        public void BuyingThreePotatoesDebits180() // AC13
        {
            var r = E.TryBuyBase(Start(), "alice", "potato", 3);
            Assert.True(r.Ok);
            Assert.Equal(180, r.Cost); // 3 * 60
            Assert.Equal(820, TestEconomy.Acct(r.State, "alice").Balance);
            Assert.Equal(180, r.State.Totals.GrocerRevenue);
            Assert.Equal(0, r.State.Totals.PaidToPlayers);
        }

        [Fact]
        public void BadPurchasesFailWithoutCostOrStateChange() // AC13
        {
            var s = Start(100);
            var unknown = E.TryBuyBase(s, "alice", "unobtainium", 1);
            Assert.Equal(EconomyError.UnknownBaseItem, unknown.Error);
            var nullBase = E.TryBuyBase(s, "alice", null!, 1);
            Assert.Equal(EconomyError.UnknownBaseItem, nullBase.Error);
            foreach (int units in new[] { 0, -1, EconomyLimits.MaxLot + 1 })
            {
                var r = E.TryBuyBase(s, "alice", "potato", units);
                Assert.Equal(EconomyError.InvalidQuantity, r.Error);
                Assert.Same(s, r.State);
                Assert.Equal(0, r.Cost);
            }
            var poor = E.TryBuyBase(s, "alice", "potato", 2); // 120 > 100
            Assert.Equal(EconomyError.InsufficientFunds, poor.Error);
            Assert.Same(s, poor.State);
            Assert.Equal(0, poor.Cost);
            Assert.Equal(EconomyError.UnknownAccount, E.TryBuyBase(s, "bob", "potato", 1).Error);
            Assert.Same(s, unknown.State);
        }

        [Fact]
        public void ExactBalanceCanBeSpent()
        {
            var r = E.TryBuyBase(Start(120), "alice", "potato", 2);
            Assert.True(r.Ok);
            Assert.Equal(0, TestEconomy.Acct(r.State, "alice").Balance);
        }

        [Fact]
        public void MaxLotAtMaxPriceStaysInRange()
        {
            // 10_000 * 150 = 1_500_000
            var r = E.TryBuyBase(Start(2_000_000), "alice", "cashew", EconomyLimits.MaxLot);
            Assert.True(r.Ok);
            Assert.Equal(1_500_000, r.Cost);
        }

        [Fact]
        public void PlateWithUnpricedComponentIsRefusedWhenSaturated() // AC13
        {
            var cat = TestEconomy.Build(grocer: TestEconomy.GrocerWithout("basil"));
            var e = new EconomyEngine(cat);
            var s = TestEconomy.Open(e, e.Initial(), "alice", 0);
            var plate = T.Ok(T.Plate(T.Base("basil")));
            var r = e.TrySellItem(s, "alice", 1, plate);
            Assert.Equal(EconomyError.WholesaleRefused, r.Error);
            Assert.Same(s, r.State);
            Assert.Empty(r.Byproducts);
            Assert.Equal(0, r.Payout);
            // it can still be discarded, with no goods
            var d = e.Discard(s, "alice", 1, plate);
            Assert.True(d.Ok);
            Assert.Empty(TestEconomy.Acct(d.State, "alice").Goods);
        }
    }
}
