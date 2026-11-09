using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;
using Xunit;
using T = Worktown.Sim.Tests.Cooking.TestItems;

namespace Worktown.Sim.Tests.Economy
{
    /// <summary>
    /// Spec 031b item 3: a squeeze of sauce costs a portion of the bottle's grocer price (10 % per started 0.5 s, at most 30 %), and
    /// the wholesaler's cap counts a squeezed portion at the least it was charged, so a build never pays out more than was spent.
    /// </summary>
    public class PortionPriceTests
    {
        private static readonly EconomyEngine E = TestEconomy.Engine;
        private static readonly ItemCatalog Items = new ItemCatalog(StreetFoodItems.All.Concat(StreetFoodItems.GrillVegetables));
        private static readonly ItemFactory Factory = new ItemFactory(Items, OperationCatalog.CreateDefault());

        private static Item Base(string id) => T.Ok(Factory.CreateBase(id));

        private static Item Sauce(float seconds) =>
            T.Ok(Factory.Apply(Assembly.SqueezeOperationId, new[] { Base("garlic_herb_sauce") }, Assembly.SqueezeParams(seconds)));

        private static Item Build(params Item[] layers) => T.Ok(Factory.Apply(Assembly.AssembleOperationId, layers, T.NoParams));

        [Fact]
        public void APortion_IsTenPercentPerStartedHalfSecond_AtMostThirty()
        {
            Assert.Equal(10, PriceRules.PortionPercent(0f));
            Assert.Equal(10, PriceRules.PortionPercent(0.05f));
            Assert.Equal(10, PriceRules.PortionPercent(0.5f));
            Assert.Equal(20, PriceRules.PortionPercent(0.51f));
            Assert.Equal(20, PriceRules.PortionPercent(1.0f)); // exactly 1 s is two started halves
            Assert.Equal(30, PriceRules.PortionPercent(1.01f));
            Assert.Equal(30, PriceRules.PortionPercent(3f));
            Assert.Equal(30, PriceRules.PortionPercent(6f));
            Assert.Equal(10, PriceRules.PortionPercent(-1f));
            Assert.Equal(10, PriceRules.PortionPercent(float.NaN));
        }

        [Fact]
        public void PortionCost_RoundsHalfUp_AndIsNeverFreeForAPricedItem()
        {
            Assert.Equal(8, PriceRules.PortionCost(80, 10));
            Assert.Equal(24, PriceRules.PortionCost(80, 30));
            Assert.Equal(4, PriceRules.PortionCost(35, 10)); // 3.5
            Assert.Equal(1, PriceRules.PortionCost(3, 10)); // 0.3, at least 1
            Assert.Equal(0, PriceRules.PortionCost(0, 30));
            Assert.Equal(80, PriceRules.PortionCost(80, 100));
            Assert.Equal(80, PriceRules.PortionCost(80, 250)); // clamped to the full price
        }

        [Fact]
        public void TheLeastChargedShare_FollowsTheAmountBucket()
        {
            Assert.Equal(10, PriceRules.PortionPercentOf(Assembly.Little));
            Assert.Equal(10, PriceRules.PortionPercentOf(Assembly.Right));
            Assert.Equal(30, PriceRules.PortionPercentOf(Assembly.Lots));
            Assert.Equal(10, PriceRules.PortionPercentOf(null));
            // Every squeeze in a bucket was charged at least that much: the bucket's lowest seconds.
            Assert.Equal(PriceRules.PortionPercentOf(Assembly.Right), PriceRules.PortionPercent(Assembly.RightFrom));
            Assert.Equal(PriceRules.PortionPercentOf(Assembly.Lots), PriceRules.PortionPercent(Assembly.LotsFrom));
        }

        [Fact]
        public void BuyingAPortion_DebitsItsShare_AndRefusalsChangeNothing()
        {
            var s = TestEconomy.Open(E, E.Initial(), "alice", 100);
            var r = E.TryBuyPortion(s, "alice", "garlic_herb_sauce", 20);
            Assert.True(r.Ok);
            Assert.Equal(16, r.Cost);
            Assert.Equal(84, TestEconomy.Acct(r.State, "alice").Balance);
            Assert.Equal(16, r.State.Totals.GrocerRevenue);
            foreach (int percent in new[] { 0, -5, 101 })
            {
                var bad = E.TryBuyPortion(s, "alice", "garlic_herb_sauce", percent);
                Assert.Equal(EconomyError.InvalidQuantity, bad.Error);
                Assert.Same(s, bad.State);
                Assert.Equal(0, bad.Cost);
            }
            Assert.Equal(EconomyError.UnknownBaseItem, E.TryBuyPortion(s, "alice", "unobtainium", 10).Error);
            Assert.Equal(EconomyError.UnknownAccount, E.TryBuyPortion(s, "bob", "garlic_herb_sauce", 10).Error);
            var poor = TestEconomy.Open(E, E.Initial(), "carl", 7);
            var refused = E.TryBuyPortion(poor, "carl", "garlic_herb_sauce", 10);
            Assert.Equal(EconomyError.InsufficientFunds, refused.Error);
            Assert.Same(poor, refused.State);
        }

        [Fact]
        public void ItemCost_CountsASqueezeAtItsLeastCharge_AndOtherItemsAsBefore()
        {
            var c = TestEconomy.Catalog;
            Assert.True(c.TryGetGrocerPrice("wrap", out long wrap));
            Assert.True(c.TryGetGrocerPrice("garlic_herb_sauce", out long bottle));
            Assert.Equal(bottle, PriceRules.ItemCost(c, Base("garlic_herb_sauce")));
            Assert.Equal(PriceRules.PortionCost(bottle, 10), PriceRules.ItemCost(c, Sauce(0.2f)));
            Assert.Equal(PriceRules.PortionCost(bottle, 10), PriceRules.ItemCost(c, Sauce(1.0f)));
            Assert.Equal(PriceRules.PortionCost(bottle, 30), PriceRules.ItemCost(c, Sauce(2f)));
            Item build = Build(Base("wrap"), Sauce(1f));
            Assert.Equal(wrap + PriceRules.PortionCost(bottle, 10), PriceRules.ItemCost(c, build));
            Item plated = T.Ok(Factory.Apply("plate", new[] { build }, T.NoParams));
            Assert.Equal(PriceRules.ItemCost(c, build), PriceRules.ItemCost(c, plated)); // through a plate too
            // A whole bottle on a build is the whole price.
            Assert.Equal(wrap + bottle, PriceRules.ItemCost(c, Build(Base("wrap"), Base("garlic_herb_sauce"))));
        }

        /// <summary>
        /// 031b review: three 10 % taps of 0.5 s each merge into one "lots" layer (1.5 s). At a bottle price of 14 each tap costs
        /// PortionCost(14, 10) = 1, so $3 were paid, while ItemCost counts the lots bucket at PortionCost(14, 30) = 4: the ceiling
        /// is $1 above what was paid (rounding). SurplusCap's -1 absorbs it, so the wholesaler never pays more than was spent.
        /// </summary>
        [Fact]
        public void ThreeMergedTaps_CountAsLots_AndTheCapAbsorbsTheRounding()
        {
            var grocer = TestEconomy.Catalog.Grocer.Select(g => g.BaseId == "garlic_herb_sauce" ? new GrocerPrice(g.BaseId, 14) : g).ToList();
            EconomyCatalog c = TestEconomy.Build(byproducts: new ByproductRule[0], grocer: grocer); // no byproducts: B = 0 is below 14
            Assert.True(c.TryGetGrocerPrice("wrap", out long wrap));
            long perTap = PriceRules.PortionCost(14, PriceRules.PortionPercent(0.5f));
            Assert.Equal(1, perTap);
            long paid = wrap + 3 * perTap;
            Item merged = Sauce(1.5f); // what KitchenState makes of three merged 0.5 s taps: one squeeze of their sum
            Assert.Equal(Assembly.Lots, Assembly.SauceAmountOf(merged));
            Assert.Equal(PriceRules.PortionCost(14, 30), PriceRules.ItemCost(c, merged));
            Assert.Equal(4, PriceRules.ItemCost(c, merged));
            Item build = Build(Base("wrap"), merged);
            Assert.Equal(paid + 1, PriceRules.ItemCost(c, build)); // the ceiling is one above the paid amount
            Assert.Equal(paid, PriceRules.SurplusCap(c, build)); // the -1 takes the rounding back: never more than was spent
        }

        [Fact]
        public void TheWholesaleCap_StaysBelowWhatASauceBuildCost()
        {
            var c = TestEconomy.Catalog;
            Assert.True(c.TryGetGrocerPrice("wrap", out long wrap));
            Assert.True(c.TryGetGrocerPrice("garlic_herb_sauce", out long bottle));
            foreach (float seconds in new[] { 0.1f, 0.5f, 0.9f, 1.4f, 1.6f, 3f })
            {
                Item build = Build(Base("wrap"), Sauce(seconds));
                long paid = wrap + PriceRules.PortionCost(bottle, PriceRules.PortionPercent(seconds));
                Assert.True(PriceRules.SurplusCap(c, build) < paid, seconds + " s");
            }
        }
    }
}
