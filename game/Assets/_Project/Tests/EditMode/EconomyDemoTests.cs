using NUnit.Framework;
using Worktown.Game.Cooking;
using Worktown.Game.Economy;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 009 AC1-AC4 with the real spec 008 engine and default catalog.</summary>
    public class EconomyDemoTests
    {
        private const string Player = "p0";

        private static TownEconomy NewEconomyWithPlayer(long grant = EconomyRules.StartingGrant)
        {
            TownEconomy economy = TownEconomy.CreateDemo();
            Assert.IsTrue(economy.TryOpenAccount(Player, grant));
            return economy;
        }

        /// <summary>potato -> board option -> pan option -> plated, via KitchenState like the game.</summary>
        private static Item PlatedFries(int panOption)
        {
            var kitchen = KitchenState.CreateDemo();
            kitchen.TryCreateIngredient("potato", out Item potato);
            kitchen.Register(1, potato);
            kitchen.TryApply(1, StationKind.Board, 3, out _, out _);
            kitchen.TryApply(1, StationKind.Pan, panOption, out _, out _);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPlate(1, out Item plated));
            return plated;
        }

        // AC1
        [Test]
        public void OpenAccount_GrantsStartingMoney_OnlyOnce()
        {
            TownEconomy economy = NewEconomyWithPlayer();
            Assert.AreEqual(500, economy.BalanceOf(Player));
            Assert.IsFalse(economy.TryOpenAccount(Player, 500), "a reconnect must not grant twice");
            Assert.AreEqual(500, economy.BalanceOf(Player));
            Assert.AreEqual(-1, economy.BalanceOf("p9"));
        }

        [Test]
        public void BuyingPotato_CostsGrocerPrice()
        {
            TownEconomy economy = NewEconomyWithPlayer();
            Assert.IsTrue(economy.TryGetGrocerPrice("potato", out long price));
            Assert.AreEqual(EconomyError.None, economy.TryBuyBase(Player, "potato", out long cost));
            Assert.AreEqual(price, cost);
            Assert.AreEqual(500 - price, economy.BalanceOf(Player));
        }

        [Test]
        public void BuyingWithoutEnoughMoney_IsRefusedAndChangesNothing()
        {
            TownEconomy economy = NewEconomyWithPlayer(grant: 50);
            Assert.AreEqual(EconomyError.InsufficientFunds, economy.TryBuyBase(Player, "potato", out long cost));
            Assert.AreEqual(0, cost);
            Assert.AreEqual(50, economy.BalanceOf(Player));
        }

        // AC2
        [Test]
        public void GoldenFries_PayMoreThanDarkFries()
        {
            TownEconomy forGolden = NewEconomyWithPlayer();
            TownEconomy forDark = NewEconomyWithPlayer();

            SaleResult golden = forGolden.TrySell(Player, PlatedFries(2));
            SaleResult dark = forDark.TrySell(Player, PlatedFries(3));

            Assert.IsTrue(golden.Ok, golden.Error.ToString());
            Assert.IsTrue(dark.Ok, dark.Error.ToString());
            Assert.AreEqual("snack", golden.MarketId);
            Assert.Greater(golden.Payout, dark.Payout);
            Assert.AreEqual(500 + golden.Payout, forGolden.BalanceOf(Player));
        }

        [Test]
        public void RawPotato_CannotBeSold()
        {
            TownEconomy economy = NewEconomyWithPlayer();
            var kitchen = KitchenState.CreateDemo();
            kitchen.TryCreateIngredient("potato", out Item potato);
            kitchen.Register(1, potato);
            kitchen.TryPlate(1, out Item plated);

            SaleResult sale = economy.TrySell(Player, plated);
            Assert.AreEqual(EconomyError.QualityTooLow, sale.Error);
            Assert.AreEqual(500, economy.BalanceOf(Player));
        }

        // AC3
        [Test]
        public void SellingRepeatedly_NeverRaisesPayout_AndDemandRefillsNextHour()
        {
            TownEconomy economy = NewEconomyWithPlayer();
            Item fries = PlatedFries(2);
            long previous = long.MaxValue;
            for (int i = 0; i < 20; i++)
            {
                SaleResult sale = economy.TrySell(Player, fries);
                if (!sale.Ok) break;
                Assert.LessOrEqual(sale.Payout, previous, "sale " + i);
                previous = sale.Payout;
            }
            Assert.IsTrue(economy.TryGetMarket("snack", out MarketState drained));
            Assert.AreEqual(0, drained.OpenDemand, "one hour of snack demand is used up");

            for (int i = 0; i < 4; i++) economy.TickHour();
            economy.TryGetMarket("snack", out MarketState refilled);
            Assert.Greater(refilled.OpenDemand, 0);
        }

        [Test]
        public void SameSeed_SameEconomy()
        {
            TownEconomy a = TownEconomy.CreateDemo();
            TownEconomy b = TownEconomy.CreateDemo();
            a.TryGetMarket("meal", out MarketState ma);
            b.TryGetMarket("meal", out MarketState mb);
            Assert.AreEqual(EconomyRules.StartHour, a.Tick);
            Assert.AreEqual(ma.Price, mb.Price);
            Assert.AreEqual(ma.OpenDemand, mb.OpenDemand);
            Assert.Greater(ma.OpenDemand, 0, "the 08:00 start has breakfast demand");
        }

        // AC4
        [TestCase(0, "Day 1, 00:00")]
        [TestCase(8, "Day 1, 08:00")]
        [TestCase(38, "Day 2, 14:00")]
        [TestCase(-5, "Day 1, 00:00")]
        public void ClockText(long tick, string expected)
        {
            Assert.AreEqual(expected, EconomyRules.ClockText(tick));
        }

        [Test]
        public void AccountIdAndMoney()
        {
            Assert.AreEqual("p3", EconomyRules.AccountId(3));
            Assert.AreEqual("$1,234", EconomyRules.Money(1234));
            Assert.AreEqual("-$60", EconomyRules.Money(-60));
        }
    }
}
