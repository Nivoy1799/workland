using NUnit.Framework;
using Worktown.Game.Cooking;
using Worktown.Game.Economy;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;

namespace Worktown.Game.Tests
{
    /// <summary>Tester additions: edge cases of KitchenState, TownEconomy and EconomyRules not covered elsewhere.</summary>
    public class TesterGapTests
    {
        [Test]
        public void TryPlate_UnknownObject_IsNotFood()
        {
            Assert.AreEqual(CookOutcome.NotFood, KitchenState.CreateDemo().TryPlate(99, out Item plated));
            Assert.IsNull(plated);
        }

        [Test]
        public void TryPlate_DoesNotChangeStoredItem()
        {
            var kitchen = KitchenState.CreateDemo();
            kitchen.TryCreateIngredient("potato", out Item potato);
            kitchen.Register(1, potato);
            kitchen.TryPlate(1, out _);
            kitchen.TryGet(1, out Item stored);
            Assert.AreEqual(potato.Hash, stored.Hash);
        }

        [Test]
        public void Clear_ForgetsAllFood()
        {
            var kitchen = KitchenState.CreateDemo();
            kitchen.TryCreateIngredient("potato", out Item potato);
            kitchen.Register(1, potato);
            kitchen.Clear();
            Assert.IsFalse(kitchen.TryGet(1, out _));
        }

        [Test]
        public void BuyingUnknownBase_IsRefusedAndChangesNothing()
        {
            TownEconomy economy = TownEconomy.CreateDemo();
            economy.TryOpenAccount("p0", 500);
            Assert.AreNotEqual(EconomyError.None, economy.TryBuyBase("p0", "dragonfruit", out _));
            Assert.AreEqual(500, economy.BalanceOf("p0"));
        }

        [Test]
        public void UnknownAccount_BalanceIsMinusOne_AndBuyRefused()
        {
            TownEconomy economy = TownEconomy.CreateDemo();
            Assert.AreEqual(-1, economy.BalanceOf("nobody"));
            Assert.AreNotEqual(EconomyError.None, economy.TryBuyBase("nobody", "potato", out _));
        }

        [Test]
        public void Reconnect_KeepsBalance_NoSecondGrant()
        {
            TownEconomy economy = TownEconomy.CreateDemo();
            economy.TryOpenAccount("p0", 500);
            economy.TryBuyBase("p0", "potato", out _);
            long after = economy.BalanceOf("p0");
            Assert.IsFalse(economy.TryOpenAccount("p0", 500));
            Assert.AreEqual(after, economy.BalanceOf("p0"));
        }

        [Test]
        public void TickHour_AdvancesTickByOne()
        {
            TownEconomy economy = TownEconomy.CreateDemo();
            long before = economy.Tick;
            economy.TickHour();
            Assert.AreEqual(before + 1, economy.Tick);
        }

        [TestCase(-5L, "Day 1, 00:00")]
        [TestCase(23L, "Day 1, 23:00")]
        [TestCase(24L, "Day 2, 00:00")]
        [TestCase(49L, "Day 3, 01:00")]
        public void ClockText_Boundaries(long tick, string expected)
        {
            Assert.AreEqual(expected, EconomyRules.ClockText(tick));
        }

        [Test]
        public void Money_Zero()
        {
            Assert.AreEqual("$0.00", EconomyRules.Money(0)); // spec 037 re-pin: money is cents (D39)
        }
    }
}
