using System;
using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Cooking;
using Worktown.Sim.Core;
using Worktown.Sim.Economy;
using Xunit;
using T = Worktown.Sim.Tests.Cooking.TestItems;

namespace Worktown.Sim.Tests.Economy
{
    public class FaucetTests
    {
        private static readonly EconomyCatalog C = TestEconomy.Catalog;
        private static readonly EconomyEngine E = TestEconomy.Engine;

        [Fact]
        public void SurplusPathAlwaysLosesMoneyOnTheGrid() // AC12
        {
            var plates = new[]
            {
                TestEconomy.PlatedFries(), TestEconomy.PlatedRaw(), TestEconomy.PlatedBurnt(),
                TestEconomy.PlatedTomatoCashew(), TestEconomy.PlatedSalt(), TestEconomy.PlatedApple(),
            };
            foreach (var item in plates)
            {
                // meal when weight >= 2, snack otherwise (default category rules)
                C.TryGetMarket(item.Weight >= 2 ? "meal" : "snack", out var market);
                long cost = PriceRules.ItemCost(C, item)!.Value;
                for (int q = 0; q <= 1000; q += 10)
                {
                    long wholesale = Math.Max(0, PriceRules.ItemWholesalePayout(C, market, item, q));
                    Assert.True(wholesale + item.Weight * C.ByproductMaxPerComponent < cost,
                        $"{item.CanonicalTree} q={q}: {wholesale} + {item.Weight}*B >= {cost}");
                }
            }
        }

        [Fact]
        public void CookingAndDumpingEverythingLosesMoney() // AC12, end to end
        {
            var s = TestEconomy.Open(E, E.Initial(), "alice", 1000);
            var buy = E.TryBuyBase(s, "alice", "potato", 1);           // -60
            s = buy.State;
            var potato = T.Base("potato");
            var cutRes = T.Cut(potato, "sticks");
            s = E.AddGoods(s, "alice", Byproducts.ForOperation(C, "cut", new[] { potato }, cutRes)).State;   // +1 scraps
            var cut = T.Ok(cutRes);
            var fryRes = T.Fry(cut, 190f, 150f);
            s = E.AddGoods(s, "alice", Byproducts.ForOperation(C, "fry", new[] { cut }, fryRes)).State;      // +1 oil
            var fries = T.Ok(fryRes);
            var plateRes = T.Plate(fries);
            Assert.Empty(Byproducts.ForOperation(C, "plate", new[] { fries }, plateRes));
            var plate = T.Ok(plateRes);

            var sale = E.TrySellItem(s, "alice", 1, plate);            // saturated at tick 0: +16 (spec 034: the bulk cap)
            Assert.Equal(SaleChannel.Wholesaler, sale.Channel);
            s = sale.State;
            s = TestEconomy.SetMarket(s, "food_scraps", m => m with { OpenDemand = 5, Price = 3 });
            s = TestEconomy.SetMarket(s, "used_cooking_oil", m => m with { OpenDemand = 5, Price = 4 });
            var scraps = E.TrySellGoods(s, "alice", "food_scraps", 1); // +3
            var oil = E.TrySellGoods(scraps.State, "alice", "used_cooking_oil", 1); // +4
            Assert.True(scraps.Ok && oil.Ok);
            // 1000 - 60 + 16 + 3 + 4 = 963
            Assert.Equal(963, TestEconomy.Acct(oil.State, "alice").Balance);
            Assert.True(TestEconomy.Acct(oil.State, "alice").Balance < 1000);
            Assert.Empty(TestEconomy.Acct(oil.State, "alice").Goods);
        }

        [Fact]
        public void DiscardingAPlatedRawPotatoLosesMoney() // AC12
        {
            var s = TestEconomy.Open(E, E.Initial(), "alice", 1000);
            s = E.TryBuyBase(s, "alice", "potato", 1).State;           // -60
            var plate = TestEconomy.PlatedRaw();
            Assert.Equal(EconomyError.QualityTooLow, E.TrySellItem(s, "alice", 1, plate).Error);
            s = E.Discard(s, "alice", 1, plate).State;                 // +1 scraps
            s = TestEconomy.SetMarket(s, "food_scraps", m => m with { OpenDemand = 5, Price = 3 });
            s = E.TrySellGoods(s, "alice", "food_scraps", 1).State;    // +3
            Assert.Equal(943, TestEconomy.Acct(s, "alice").Balance);   // 1000 - 60 + 3
        }

        [Fact]
        public void HandCountedMoneyBoundPerTownDay()
        {
            // zero-jitter day: 180 meals * floor(1.5*1200)=1800, 96 snacks * 600, 240 drinks * 375
            // = 324_000 + 57_600 + 90_000 = 471_600
            var e = TestEconomy.ZeroJitterEngine();
            var s = e.Initial();
            long bound = 0;
            for (int i = 0; i < 25; i++) // 24 hours of demand are closed after 25 ticks
            {
                s = TestEconomy.Advance(e, s, 1);
                bound += ClosedBound(s);
            }
            Assert.Equal(471_600, bound);
        }

        private static long ClosedBound(EconomyState s)
        {
            long total = 0;
            foreach (var m in s.Markets)
            {
                C.TryGetMarket(m.MarketId, out var def);
                if (def.Kind == MarketKind.Item) total += m.Last.Demand * PriceRules.DemandPayout(def.Ceiling, 1000);
            }
            return total;
        }

        private static void AssertTickWithinBound(EconomyState s)
        {
            foreach (var m in s.Markets)
            {
                C.TryGetMarket(m.MarketId, out var def);
                long per = def.Kind == MarketKind.Item ? PriceRules.DemandPayout(def.Ceiling, 1000) : def.Ceiling;
                Assert.True(m.Last.DemandPaid <= m.Last.Demand * per, $"{m.MarketId}: {m.Last.DemandPaid} > {m.Last.Demand} * {per}");
            }
        }

        private sealed class Bot
        {
            public Bot(string id) { Id = id; }
            public string Id { get; }
            public List<Item> Bag { get; } = new List<Item>();
        }

        [Fact]
        public void BotsCannotCreateMoneyBeyondTheNpcDemandBound() // AC14
        {
            var rng = new SeededRandom(7);
            var s = E.Initial();
            var bots = Enumerable.Range(0, 8).Select(i => new Bot("bot" + i)).ToList();
            foreach (var b in bots) s = TestEconomy.Open(E, s, b.Id, 100_000);
            string[] baseIds = { "potato", "apple", "tomato", "cashew", "salt", "onion", "basil", "oil" };
            string[] shapes = { "halves", "slices", "sticks", "dice" };
            string[] heats = { "fry", "saute", "boil" };
            long nextId = 1;
            long bound = 0;
            int soldItems = 0;

            Item? Pick(Bot b) => b.Bag.Count == 0 ? null : b.Bag[rng.NextInt(0, b.Bag.Count)];

            void Replace(Bot b, IReadOnlyList<Item> used, string op, OperationResult r)
            {
                if (!r.Ok) return;
                var goods = Byproducts.ForOperation(C, op, used, r);
                if (goods.Count > 0) s = E.AddGoods(s, b.Id, goods).State;
                foreach (var u in used) b.Bag.Remove(u);
                b.Bag.Add(r.Item!);
            }

            for (int tick = 0; tick < 72; tick++)
            {
                foreach (var b in bots)
                {
                    for (int a = 0; a < 6; a++)
                    {
                        var item = Pick(b);
                        switch (rng.NextInt(0, 10))
                        {
                            case 0:
                            case 1:
                            {
                                string id = baseIds[rng.NextInt(0, baseIds.Length)];
                                var buy = E.TryBuyBase(s, b.Id, id, rng.NextInt(1, 4));
                                if (buy.Ok) { s = buy.State; for (long k = 0; k < buy.Cost / Cost(id); k++) b.Bag.Add(T.Base(id)); }
                                break;
                            }
                            case 2:
                                if (item != null) Replace(b, new[] { item }, "cut", T.Cut(item, shapes[rng.NextInt(0, shapes.Length)]));
                                break;
                            case 3:
                                if (item != null)
                                {
                                    string op = heats[rng.NextInt(0, heats.Length)];
                                    var res = op == "boil" ? T.Boil(item, 30f + rng.NextFloat() * 1700f)
                                        : T.Heat(op, item, 120f + rng.NextFloat() * 100f, 10f + rng.NextFloat() * 590f);
                                    Replace(b, new[] { item }, op, res);
                                }
                                break;
                            case 4:
                                if (b.Bag.Count >= 2)
                                {
                                    var x = b.Bag[0];
                                    var y = b.Bag[b.Bag.Count - 1];
                                    if (!ReferenceEquals(x, y)) Replace(b, new[] { x, y }, "mix", T.Mix(x, y));
                                }
                                break;
                            case 5:
                            case 6:
                                if (item != null) Replace(b, new[] { item }, "plate", T.Plate(item));
                                break;
                            case 7:
                                if (item != null)
                                {
                                    var sale = E.TrySellItem(s, b.Id, nextId++, item);
                                    if (sale.Ok) { s = sale.State; b.Bag.Remove(item); soldItems++; }
                                }
                                break;
                            case 8: // flood: sell every item that can be sold
                                foreach (var it in b.Bag.ToList())
                                {
                                    var sale = E.TrySellItem(s, b.Id, nextId++, it);
                                    if (sale.Ok) { s = sale.State; b.Bag.Remove(it); soldItems++; }
                                }
                                break;
                            default: // discard one item, then sell every held good
                                if (item != null)
                                {
                                    var d = E.Discard(s, b.Id, nextId++, item);
                                    if (d.Ok) { s = d.State; b.Bag.Remove(item); }
                                }
                                foreach (var g in TestEconomy.Acct(s, b.Id).Goods.ToList())
                                    s = E.TrySellGoods(s, b.Id, g.GoodId, Math.Min(g.Units, EconomyLimits.MaxLot)).State;
                                break;
                        }
                    }
                }
                s = E.Tick(s, new TickInput(EconomyRng.ForTick(42, s.Tick), 120));
                AssertTickWithinBound(s);
                bound += ClosedBound(s);
            }

            s = E.Tick(s, new TickInput(EconomyRng.ForTick(42, s.Tick), 120)); // close the last bot hour
            AssertTickWithinBound(s);
            bound += ClosedBound(s);

            Assert.True(soldItems > 0, "bots never sold anything");
            Assert.True(s.Totals.GrocerRevenue > 0);
            long net = s.Totals.PaidToPlayers - s.Totals.GrocerRevenue - s.Totals.DebitedFromPlayers;
            Assert.True(net <= bound, $"net {net} exceeds bound {bound}");
            foreach (var a in s.Accounts)
            {
                Assert.True(a.Balance >= 0);
                Assert.All(a.Goods, g => Assert.True(g.Units > 0));
            }
        }

        private static long Cost(string baseId)
        {
            C.TryGetGrocerPrice(baseId, out var p);
            return p;
        }
    }
}
