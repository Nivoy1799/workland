using System;
using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;
using Xunit;
using T = Worktown.Sim.Tests.Cooking.TestItems;

namespace Worktown.Sim.Tests.Economy
{
    /// <summary>Shared fixtures for the economy tests. Spec 005 items come from TestItems.</summary>
    internal static class TestEconomy
    {
        public static readonly EconomyCatalog Catalog = EconomyCatalog.CreateDefault();
        public static readonly EconomyEngine Engine = new EconomyEngine(Catalog);

        /// <summary>Default catalog with every demand profile's jitter set to 0.</summary>
        public static EconomyCatalog ZeroJitterCatalog() =>
            Build(markets: Catalog.Markets.Select(m => m.Demand == null ? m : m with { Demand = m.Demand with { JitterPercent = 0 } }));

        public static EconomyEngine ZeroJitterEngine() => new EconomyEngine(ZeroJitterCatalog());

        public static EconomyCatalog Build(IEnumerable<MarketDef>? markets = null, IEnumerable<CategoryRule>? rules = null,
            IEnumerable<ByproductRule>? byproducts = null, IEnumerable<GrocerPrice>? grocer = null) =>
            new EconomyCatalog(markets ?? Catalog.Markets, rules ?? Catalog.Rules, byproducts ?? Catalog.Byproducts,
                grocer ?? Catalog.Grocer, OperationCatalog.CreateDefault());

        public static IEnumerable<MarketDef> WithMarket(string id, Func<MarketDef, MarketDef> change) =>
            Catalog.Markets.Select(m => m.Id == id ? change(m) : m).ToList();

        public static IEnumerable<GrocerPrice> GrocerWithout(string baseId) =>
            Catalog.Grocer.Where(g => g.BaseId != baseId).ToList();

        public static EconomyState Advance(EconomyEngine e, EconomyState s, int ticks, int pop = 120, ulong seed = 42)
        {
            for (int i = 0; i < ticks; i++) s = e.Tick(s, new TickInput(EconomyRng.ForTick(seed, s.Tick), pop));
            return s;
        }

        /// <summary>Opens an account and sets its balance directly (a grant is a /game decision, not an engine call).</summary>
        public static EconomyState Open(EconomyEngine e, EconomyState s, string id, long balance)
        {
            var r = e.OpenAccount(s, id);
            Assert.True(r.Ok, "OpenAccount failed: " + r.Error);
            return SetAccount(r.State, id, a => a with { Balance = balance });
        }

        public static Account Acct(EconomyState s, string id) => s.Accounts.Single(a => a.Id == id);
        public static MarketState Mkt(EconomyState s, string id) => s.Markets.Single(m => m.MarketId == id);

        public static int Units(EconomyState s, string account, string good)
        {
            foreach (var g in Acct(s, account).Goods) if (g.GoodId == good) return g.Units;
            return 0;
        }

        public static EconomyState SetMarket(EconomyState s, string id, Func<MarketState, MarketState> f) =>
            s with { Markets = s.Markets.Select(m => m.MarketId == id ? f(m) : m).ToList().AsReadOnly() };

        public static EconomyState SetAccount(EconomyState s, string id, Func<Account, Account> f) =>
            s with { Accounts = s.Accounts.Select(a => a.Id == id ? f(a) : a).ToList().AsReadOnly() };

        // fixtures (qualities measured: fries 0.697, burnt 0.296, raw 0.125, tomato+cheese 0.746, salt 0.167, apple 0.397)
        public static Item PlatedFries() => T.Ok(T.Plate(T.Fries()));
        public static Item PlatedBurnt() =>
            T.Ok(T.Plate(T.Ok(T.Fry(T.Ok(T.Cut(T.Base("potato"), "sticks")), 220f, 600f))));
        public static Item PlatedRaw() => T.Ok(T.Plate(T.Base("potato")));
        public static Item PlatedTomatoCheese() => T.Ok(T.Plate(T.Ok(T.Mix(T.Base("tomato"), T.Base("cheese")))));
        public static Item PlatedSalt() => T.Ok(T.Plate(T.Base("salt")));
        public static Item PlatedApple() => T.Ok(T.Plate(T.Base("apple")));
    }
}
