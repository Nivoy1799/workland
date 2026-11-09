using System;
using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Core;
using Worktown.Sim.Economy;
using Xunit;
using T = Worktown.Sim.Tests.Cooking.TestItems;

namespace Worktown.Sim.Tests.Economy
{
    public class LedgerTests
    {
        private static readonly EconomyEngine E = TestEconomy.Engine;

        [Fact]
        public void InitialStateHasCeilingPricesAndNoDemand()
        {
            var s = E.Initial();
            Assert.Equal(0, s.Tick);
            Assert.Equal(5, s.Markets.Count);
            foreach (var m in s.Markets)
            {
                TestEconomy.Catalog.TryGetMarket(m.MarketId, out var def);
                Assert.Equal(def.Ceiling, m.Price);
                Assert.Equal(0, m.OpenDemand);
                Assert.Equal(0, m.DemandCarry);
                Assert.Equal(new MarketReport(0, 0, 0, 0, 0), m.Last);
            }
            Assert.Empty(s.Accounts);
            Assert.Empty(s.RecentConsumed);
            Assert.Equal(new EconomyTotals(0, 0, 0, 0), s.Totals);
        }

        [Fact]
        public void EngineRejectsNullCatalog()
        {
            Assert.Throws<ArgumentNullException>(() => new EconomyEngine(null!));
        }

        [Fact]
        public void OpenAccountStartsAtZeroAndKeepsAccountsSorted()
        {
            var s = E.Initial();
            foreach (var id in new[] { "carol", "alice", "bob" }) s = E.OpenAccount(s, id).State;
            Assert.Equal(new[] { "alice", "bob", "carol" }, s.Accounts.Select(a => a.Id));
            Assert.All(s.Accounts, a => { Assert.Equal(0, a.Balance); Assert.Empty(a.Goods); });
        }

        [Fact]
        public void OpenAccountValidatesIds()
        {
            var s = E.Initial();
            foreach (var bad in new[] { "", "UPPER", "has space", new string('a', 33), null! })
            {
                var r = E.OpenAccount(s, bad);
                Assert.Equal(EconomyError.InvalidAccountId, r.Error);
                Assert.Same(s, r.State);
            }
            Assert.True(E.OpenAccount(s, new string('a', 32)).Ok);
            var opened = E.OpenAccount(s, "alice").State;
            Assert.Equal(EconomyError.AccountExists, E.OpenAccount(opened, "alice").Error);
        }

        [Fact]
        public void StateCollectionsAreReadOnly()
        {
            var s = E.OpenAccount(E.Initial(), "alice").State;
            s = E.AddGoods(s, "alice", new[] { new GoodAmount("food_scraps", 1) }).State;
            Assert.True(((IList<MarketState>)s.Markets).IsReadOnly);
            Assert.True(((IList<Account>)s.Accounts).IsReadOnly);
            Assert.True(((IList<ConsumedInstance>)s.RecentConsumed).IsReadOnly);
            Assert.True(((IList<GoodAmount>)s.Accounts[0].Goods).IsReadOnly);
            s = TestEconomy.Advance(E, s, 3);
            Assert.True(((IList<MarketState>)s.Markets).IsReadOnly);
        }

        [Fact]
        public void TryDebitReducesBalanceAndCountsTheSink()
        {
            var s = TestEconomy.Open(E, E.Initial(), "alice", 100);
            var r = E.TryDebit(s, "alice", 40);
            Assert.True(r.Ok);
            Assert.Equal(60, TestEconomy.Acct(r.State, "alice").Balance);
            Assert.Equal(40, r.State.Totals.DebitedFromPlayers);
            Assert.Equal(100, TestEconomy.Acct(s, "alice").Balance); // input untouched
            var all = E.TryDebit(r.State, "alice", 60);
            Assert.True(all.Ok);
            Assert.Equal(0, TestEconomy.Acct(all.State, "alice").Balance);
        }

        [Fact]
        public void TryDebitErrors() // AC9
        {
            var s = TestEconomy.Open(E, E.Initial(), "alice", 100);
            var over = E.TryDebit(s, "alice", 101);
            Assert.Equal(EconomyError.InsufficientFunds, over.Error);
            Assert.Same(s, over.State);
            Assert.Equal(EconomyError.InvalidQuantity, E.TryDebit(s, "alice", 0).Error);
            Assert.Equal(EconomyError.InvalidQuantity, E.TryDebit(s, "alice", -5).Error);
            Assert.Equal(EconomyError.UnknownAccount, E.TryDebit(s, "bob", 1).Error);
            Assert.Equal(EconomyError.UnknownAccount, E.TryDebit(s, null!, 1).Error);
        }

        [Fact]
        public void AddGoodsMergesAndSorts()
        {
            var s = E.OpenAccount(E.Initial(), "alice").State;
            s = E.AddGoods(s, "alice", new[] { new GoodAmount("used_cooking_oil", 2), new GoodAmount("food_scraps", 1), new GoodAmount("food_scraps", 4) }).State;
            s = E.AddGoods(s, "alice", new[] { new GoodAmount("used_cooking_oil", 3) }).State;
            Assert.Equal(new[] { new GoodAmount("food_scraps", 5), new GoodAmount("used_cooking_oil", 5) }, TestEconomy.Acct(s, "alice").Goods.ToArray());
        }

        [Fact]
        public void AddGoodsErrorsAreAllOrNothing()
        {
            var s = E.OpenAccount(E.Initial(), "alice").State;
            var mixedBad = E.AddGoods(s, "alice", new[] { new GoodAmount("food_scraps", 1), new GoodAmount("ghost", 1) });
            Assert.Equal(EconomyError.UnknownGood, mixedBad.Error);
            Assert.Same(s, mixedBad.State);
            Assert.Equal(EconomyError.UnknownGood, E.AddGoods(s, "alice", new[] { new GoodAmount("snack", 1) }).Error); // not a commodity
            Assert.Equal(EconomyError.InvalidQuantity, E.AddGoods(s, "alice", new[] { new GoodAmount("food_scraps", 0) }).Error);
            Assert.Equal(EconomyError.InvalidQuantity, E.AddGoods(s, "alice", new[] { new GoodAmount("food_scraps", -2) }).Error);
            Assert.Equal(EconomyError.UnknownAccount, E.AddGoods(s, "bob", new[] { new GoodAmount("food_scraps", 1) }).Error);
            var limit = E.AddGoods(s, "alice", new[] { new GoodAmount("food_scraps", EconomyLimits.MaxGoodUnits + 1) });
            Assert.Equal(EconomyError.GoodsLimit, limit.Error);
            var exact = E.AddGoods(s, "alice", new[] { new GoodAmount("food_scraps", EconomyLimits.MaxGoodUnits) });
            Assert.True(exact.Ok);
            // two entries of the same good that add up past the limit
            var split = E.AddGoods(s, "alice", new[] { new GoodAmount("food_scraps", EconomyLimits.MaxGoodUnits), new GoodAmount("food_scraps", 1) });
            Assert.Equal(EconomyError.GoodsLimit, split.Error);
            Assert.True(E.AddGoods(s, "alice", Array.Empty<GoodAmount>()).Ok);
        }

        [Fact]
        public void AddGoodsRejectsNullArguments()
        {
            var s = E.OpenAccount(E.Initial(), "alice").State;
            Assert.Throws<ArgumentNullException>(() => E.AddGoods(s, "alice", null!));
            Assert.Throws<ArgumentException>(() => E.AddGoods(s, "alice", new GoodAmount[] { null! }));
            Assert.Throws<ArgumentNullException>(() => E.TryDebit(null!, "alice", 1));
        }

        [Fact]
        public void RandomOperationsNeverLeaveNegativeBalanceOrGoods() // AC9
        {
            var rng = new SeededRandom(2024);
            var s = E.Initial();
            foreach (var id in new[] { "a", "b", "c" }) s = TestEconomy.Open(E, s, id, 500);
            var items = new[] { TestEconomy.PlatedFries(), TestEconomy.PlatedBurnt(), TestEconomy.PlatedRaw(), TestEconomy.PlatedTomatoCashew(), T.Fries() };
            string[] accounts = { "a", "b", "c", "ghost" };
            string[] goods = { "food_scraps", "used_cooking_oil", "snack", "ghost" };
            string[] bases = { "potato", "salt", "cashew", "ghost" };
            long nextId = 1;
            for (int i = 0; i < 1000; i++)
            {
                string acc = accounts[rng.NextInt(0, accounts.Length)];
                switch (rng.NextInt(0, 8))
                {
                    case 0: s = E.TryDebit(s, acc, rng.NextInt(-50, 700)).State; break;
                    case 1: s = E.AddGoods(s, acc, new[] { new GoodAmount(goods[rng.NextInt(0, goods.Length)], rng.NextInt(-3, 40)) }).State; break;
                    case 2: s = E.TrySellGoods(s, acc, goods[rng.NextInt(0, goods.Length)], rng.NextInt(-2, 30)).State; break;
                    case 3: s = E.TryBuyBase(s, acc, bases[rng.NextInt(0, bases.Length)], rng.NextInt(-1, 6)).State; break;
                    case 4: s = E.TrySellItem(s, acc, rng.NextInt(0, 3) == 0 ? nextId : nextId++, items[rng.NextInt(0, items.Length)]).State; break;
                    case 5: s = E.Discard(s, acc, nextId++, items[rng.NextInt(0, items.Length)]).State; break;
                    case 6: s = E.Tick(s, new TickInput(EconomyRng.ForTick(1, s.Tick), 120)); break;
                    default: s = E.OpenAccount(s, rng.NextInt(0, 2) == 0 ? "a" : "d" + rng.NextInt(0, 3)).State; break;
                }
                foreach (var a in s.Accounts)
                {
                    Assert.InRange(a.Balance, 0L, EconomyLimits.MaxBalance);
                    foreach (var g in a.Goods) Assert.InRange(g.Units, 1, EconomyLimits.MaxGoodUnits);
                }
            }
            Assert.True(s.Totals.PaidToPlayers > 0);
        }
    }
}
