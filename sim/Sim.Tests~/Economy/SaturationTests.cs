using Worktown.Sim.Economy;
using Xunit;

namespace Worktown.Sim.Tests.Economy
{
    public class SaturationTests
    {
        private static readonly EconomyEngine E = TestEconomy.Engine;

        private static EconomyState Maxed(string market)
        {
            var s = TestEconomy.Advance(E, E.Initial(), 12);
            s = TestEconomy.Open(E, s, "alice", 0);
            return TestEconomy.SetMarket(s, market, m => m with
            {
                OpenDemand = 0,
                PlayerFilledThisTick = int.MaxValue,
                WholesaleThisTick = int.MaxValue,
            });
        }

        [Fact]
        public void ItemWholesaleCounterSaturates()
        {
            var r = E.TrySellItem(Maxed("snack"), "alice", 1, TestEconomy.PlatedFries());
            Assert.True(r.Ok, r.Error.ToString());
            Assert.Equal(SaleChannel.Wholesaler, r.Channel);
            Assert.Equal(int.MaxValue, TestEconomy.Mkt(r.State, "snack").WholesaleThisTick);
        }

        [Fact]
        public void GoodsSaleCountersSaturate()
        {
            var s = Maxed("food_scraps");
            s = TestEconomy.SetMarket(s, "food_scraps", m => m with { OpenDemand = 1 });
            s = TestEconomy.SetAccount(s, "alice", a => a with { Goods = new[] { new GoodAmount("food_scraps", 5) } });
            var r = E.TrySellGoods(s, "alice", "food_scraps", 5);
            Assert.True(r.Ok, r.Error.ToString());
            var m2 = TestEconomy.Mkt(r.State, "food_scraps");
            Assert.Equal(int.MaxValue, m2.PlayerFilledThisTick);
            Assert.Equal(int.MaxValue, m2.WholesaleThisTick);
        }

        [Fact]
        public void TickWithMaxedCountersKeepsSupplyNonNegative()
        {
            var s = Maxed("snack");
            var next = E.Tick(s, new TickInput(EconomyRng.ForTick(1, s.Tick), 120));
            var last = TestEconomy.Mkt(next, "snack").Last;
            Assert.Equal(int.MaxValue, last.PlayerFilled);
            Assert.Equal(int.MaxValue, last.Wholesale);
            Assert.True(TestEconomy.Mkt(next, "snack").Price > 0);
        }
    }
}
