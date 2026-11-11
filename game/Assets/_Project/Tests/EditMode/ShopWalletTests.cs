using NUnit.Framework;
using Worktown.Game.Climb;
using Worktown.Game.Economy;
using Worktown.Sim.Climb;
using Worktown.Sim.Economy;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 053 AC5 (pillar 2, the money belongs to the group): one shop wallet. Every player's money step lands on the one shop account;
    /// it opens once per game (the starting money of a new shop, or a save's balance), and a joining or rejoining player adds nothing.
    /// EconomyService runs exactly these steps and publishes the one balance to every player's Wallet.
    /// </summary>
    public class ShopWalletTests
    {
        private static long StartingMoney() => FoodCornerRules.StartingMoney(EconomyCatalog.CreateDefault(), ClimbCatalog.CreateDefault());

        [Test]
        public void EveryPlayer_SharesTheOneShopAccount()
        {
            Assert.AreEqual(EconomyRules.ShopAccount, EconomyRules.AccountId(0));
            Assert.AreEqual(EconomyRules.AccountId(1), EconomyRules.AccountId(2));
            Assert.AreEqual(EconomyRules.AccountId(1), EconomyRules.AccountId(7));
        }

        [Test]
        public void ASaleByEitherPlayer_ChangesTheOneBalance()
        {
            TownEconomy economy = TownEconomy.CreateGame(42);
            Assert.IsTrue(economy.TryOpenAccount(EconomyRules.ShopAccount, StartingMoney()));
            long start = economy.BalanceOf(EconomyRules.ShopAccount);
            Assert.IsTrue(economy.TryPayGuest(EconomyRules.AccountId(1), 120, out long paidByOne));
            Assert.IsTrue(economy.TryPayGuest(EconomyRules.AccountId(2), 80, out long paidByTwo));
            Assert.AreEqual(start + paidByOne + paidByTwo, economy.BalanceOf(EconomyRules.AccountId(1)), "player 1 sees both sales");
            Assert.AreEqual(economy.BalanceOf(EconomyRules.AccountId(1)), economy.BalanceOf(EconomyRules.AccountId(2)), "and so does player 2");
            Assert.AreEqual(1, CountAccounts(economy), "one account in the ledger");
        }

        private static int CountAccounts(TownEconomy economy)
        {
            // Every player's id opens the same account: a second, third or rejoining player's open is refused.
            int opened = 1;
            for (int player = 1; player <= 3; player++)
                if (economy.TryOpenAccount(EconomyRules.AccountId(player), EconomyRules.StartingGrant)) opened++;
            return opened;
        }

        [Test]
        public void AJoinAndARejoin_AddNoMoney_TheStartingMoneyIsPaidOncePerNewShop()
        {
            TownEconomy economy = TownEconomy.CreateGame(7);
            long money = StartingMoney();
            Assert.IsTrue(economy.TryOpenAccount(EconomyRules.ShopAccount, money), "the new shop opens with its starting money");
            Assert.IsFalse(economy.TryOpenAccount(EconomyRules.AccountId(3), EconomyRules.StartingGrant), "a third player joins: no grant");
            Assert.IsFalse(economy.TryOpenAccount(EconomyRules.AccountId(1), EconomyRules.StartingGrant), "a rejoin: no grant");
            Assert.AreEqual(money, economy.BalanceOf(EconomyRules.ShopAccount));

            TownEconomy next = TownEconomy.CreateGame(8); // another New shop: its own economy, its own starting money once
            Assert.IsTrue(next.TryOpenAccount(EconomyRules.ShopAccount, money));
            Assert.IsFalse(next.TryOpenAccount(EconomyRules.ShopAccount, money));
            Assert.AreEqual(money, next.BalanceOf(EconomyRules.ShopAccount));
        }

        [Test]
        public void ChargesAndRefunds_StillWorkOnTheShopAccount()
        {
            TownEconomy economy = TownEconomy.CreateGame(3);
            economy.TryOpenAccount(EconomyRules.ShopAccount, 1000);
            Assert.AreEqual(EconomyError.None, economy.TryBuyBase(EconomyRules.AccountId(1), "potato", out long cost));
            Assert.AreEqual(1000 - cost, economy.BalanceOf(EconomyRules.ShopAccount), "charged once");
            Assert.AreEqual(EconomyError.None, economy.Refund(EconomyRules.AccountId(2), cost), "any player's refund backstop reaches it");
            Assert.AreEqual(1000, economy.BalanceOf(EconomyRules.ShopAccount));
            Assert.AreNotEqual(EconomyError.None, economy.Refund(EconomyRules.ShopAccount, 1), "never more than it paid");
        }

        [Test]
        public void ThePitchFee_IsClampedAtZero()
        {
            TownEconomy economy = TownEconomy.CreateGame(5);
            economy.TryOpenAccount(EconomyRules.ShopAccount, 300);
            Assert.AreEqual(200, economy.PayFee(EconomyRules.ShopAccount, 200));
            Assert.AreEqual(100, economy.PayFee(EconomyRules.ShopAccount, 575), "only what is left");
            Assert.AreEqual(0, economy.BalanceOf(EconomyRules.ShopAccount));
            Assert.AreEqual(0, economy.PayFee(EconomyRules.ShopAccount, 10));
            Assert.AreEqual(0, economy.PayFee("nobody", 10));
        }

        [Test]
        public void ALoadedDay_ShowsItsDay()
        {
            Assert.AreEqual(0, EconomyRules.DayOffsetTicks(1));
            Assert.AreEqual(72, EconomyRules.DayOffsetTicks(4));
            Assert.AreEqual(0, EconomyRules.DayOffsetTicks(0));
            TownEconomy economy = TownEconomy.CreateGame(1);
            Assert.AreEqual("Day 4, 08:00", EconomyRules.ClockText(economy.Tick + EconomyRules.DayOffsetTicks(4)));
        }
    }
}
