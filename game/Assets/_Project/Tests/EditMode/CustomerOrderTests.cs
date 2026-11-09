using System.Collections.Generic;
using NUnit.Framework;
using Worktown.Game.Economy;
using Worktown.Game.Npc;
using Worktown.Sim.Cooking;
using Worktown.Sim.Core;
using Worktown.Sim.Economy;

namespace Worktown.Game.Tests
{
    /// <summary>Spec 022 AC3 (serving and pay), AC4 (seeded orders) and AC5 (the shift and its summary).</summary>
    public class CustomerOrderTests
    {
        private static System.Func<byte, OrderMatch> Rates(Item served) => dish => OrderRules.Match(served, Menu.ById(dish));

        // ---- AC3: who gets the plate ----

        [Test]
        public void Serve_PicksTheFirstExactMatch_InQueueOrder()
        {
            var queue = new CustomerQueue();
            queue.Add(1, "snack", Menu.PotatoSoup);
            queue.Add(2, "snack", Menu.Fries);
            queue.Add(3, "snack", Menu.Fries);

            Assert.IsTrue(queue.TryPick(Rates(MenuKitchen.ExactFries()), out QueuedCustomer picked, out OrderMatch match));
            Assert.AreEqual(2, picked.Id, "the first fries order, not the first in line");
            Assert.AreEqual(OrderMatch.Exact, match);
            Assert.AreEqual(3, queue.Entries.Count, "picking changes nothing");
            Assert.IsTrue(queue.Remove(2));
            Assert.IsFalse(queue.Remove(2));
            CollectionAssert.AreEqual(new[] { 1, 3 }, IdsOf(queue));
        }

        [Test]
        public void Serve_WithoutExactMatch_GoesToTheFirstInLine()
        {
            var queue = new CustomerQueue();
            queue.Add(7, "snack", Menu.PotatoSoup);
            queue.Add(8, "meal", Menu.TomatoSalad);

            Assert.IsTrue(queue.TryPick(Rates(MenuKitchen.ExactOnionSoup()), out QueuedCustomer picked, out OrderMatch match));
            Assert.AreEqual(7, picked.Id);
            Assert.AreEqual(OrderMatch.Close, match, "potato soup ordered, onion soup served: one ingredient too many");

            Assert.IsTrue(queue.TryPick(Rates(MenuKitchen.ExactFriedApple()), out picked, out match));
            Assert.AreEqual(7, picked.Id);
            Assert.AreEqual(OrderMatch.Wrong, match);
        }

        [Test]
        public void Serve_EmptyQueue_PicksNobody()
        {
            Assert.IsFalse(new CustomerQueue().TryPick(Rates(MenuKitchen.ExactFries()), out _, out _));
        }

        [Test]
        public void Serve_IsDeterministic()
        {
            Item served = MenuKitchen.ExactTomatoSalad();
            for (int run = 0; run < 3; run++)
            {
                var queue = new CustomerQueue();
                queue.Add(1, "snack", Menu.Fries);
                queue.Add(2, "meal", Menu.TomatoSalad);
                queue.Add(3, "meal", Menu.TomatoSalad);
                Assert.IsTrue(queue.TryPick(Rates(served), out QueuedCustomer picked, out _));
                Assert.AreEqual(2, picked.Id);
            }
        }

        [Test]
        public void Sale_PaysTheOrderShare_OfTheSimPrice()
        {
            Item fries = MenuKitchen.ExactFries();
            TownEconomy full = NewEconomy();
            TownEconomy close = NewEconomy();
            TownEconomy tipped = NewEconomy();

            SaleResult a = full.TrySell("p0", fries);
            SaleResult b = close.TrySell("p0", fries, OrderRules.PayPermille(OrderMatch.Close, 4));
            SaleResult c = tipped.TrySell("p0", fries, OrderRules.PayPermille(OrderMatch.Exact, 4));

            Assert.IsTrue(a.Ok && b.Ok && c.Ok);
            Assert.AreEqual(SaleChannel.NpcDemand, a.Channel);
            Assert.AreEqual(EconomyRules.ApplyPermille(a.Payout, 600), b.Payout);
            Assert.AreEqual(EconomyRules.ApplyPermille(a.Payout, 1200), c.Payout, "exact with 4 stars: +20 % tip");
            Assert.AreEqual(500 + a.Payout, full.BalanceOf("p0"));
            Assert.AreEqual(500 + b.Payout, close.BalanceOf("p0"));
            Assert.AreEqual(500 + c.Payout, tipped.BalanceOf("p0"));
        }

        [Test]
        public void RefusedSale_ChangesNothing_WithAFactor()
        {
            TownEconomy economy = NewEconomy();
            SaleResult refused = economy.TrySell("p0", MenuKitchen.RawWholePotato(), 250);
            Assert.IsFalse(refused.Ok);
            Assert.AreEqual(500, economy.BalanceOf("p0"));
        }

        private static TownEconomy NewEconomy()
        {
            TownEconomy economy = TownEconomy.CreateDemo();
            Assert.IsTrue(economy.TryOpenAccount("p0", 500));
            return economy;
        }

        // ---- AC4: orders ----

        private static List<byte> DrawMany(ulong seed, int count)
        {
            var random = new SeededRandom(seed);
            var drawn = new List<byte>();
            for (int i = 0; i < count; i++)
            {
                MenuDish dish = Menu.Draw(random, i % 2 == 0 ? "meal" : "snack", Menu.CrateIngredients);
                Assert.IsNotNull(dish);
                Assert.AreEqual(i % 2 == 0 ? "meal" : "snack", dish.Market);
                drawn.Add(dish.Id);
            }
            return drawn;
        }

        [Test]
        public void Orders_AreDeterministicForASeed()
        {
            List<byte> first = DrawMany(CustomerRules.OrderSeed, 40);
            CollectionAssert.AreEqual(first, DrawMany(CustomerRules.OrderSeed, 40));
            CollectionAssert.AreNotEqual(first, DrawMany(CustomerRules.OrderSeed + 1, 40), "another seed, other orders");
            var kinds = new HashSet<byte>(first);
            Assert.AreEqual(Menu.All.Count, kinds.Count, "40 orders use the whole menu");
        }

        [Test]
        public void Orders_OnlyDishesWhoseIngredientsAreForSale()
        {
            var potatoAndOnion = new[] { "potato", "onion" };
            CollectionAssert.AreEqual(new[] { Menu.Fries, Menu.PotatoSoup }, IdsOf(Menu.Available("snack", potatoAndOnion)));
            CollectionAssert.AreEqual(new[] { Menu.OnionSoup }, IdsOf(Menu.Available("meal", potatoAndOnion)),
                "no tomato salad without tomatoes, no veggie mix with two ingredients");
            var random = new SeededRandom(5);
            for (int i = 0; i < 30; i++)
                Assert.AreEqual(Menu.OnionSoup, Menu.Draw(random, "meal", potatoAndOnion).Id);
            Assert.IsEmpty(Menu.Available("meal", new[] { "apple" }));
            Assert.IsFalse(Menu.HasDishes("meal", new string[0]));
        }

        [Test]
        public void Drinks_HaveNoDish_NoDrawIsUsed_AndNoDrinkCustomers()
        {
            var random = new SeededRandom(9);
            Assert.IsNull(Menu.Draw(random, "drink", Menu.CrateIngredients));
            Assert.AreEqual(new SeededRandom(9).NextUInt64(), random.NextUInt64(), "no draw for a market without dishes");

            var demand = new Dictionary<string, int> { ["meal"] = 1, ["snack"] = 2, ["drink"] = 5 };
            IReadOnlyList<string> wishes = CustomerRules.DesiredWishes(m => demand[m], m => Menu.HasDishes(m, Menu.CrateIngredients), true);
            CollectionAssert.AreEqual(new[] { "meal", "snack", "snack" }, wishes);
        }

        [Test]
        public void TheDemoTown_HasOrdersAtTheStart_SoCustomersComeRightAway()
        {
            TownEconomy economy = TownEconomy.CreateDemo();
            Assert.AreEqual(Shift.OpenHour, Shift.HourOf(economy.Tick), "the server starts at 08:00");
            Assert.IsTrue(economy.TryGetMarket("meal", out MarketState meal));
            Assert.IsTrue(economy.TryGetMarket("snack", out MarketState snack));
            Assert.Greater(meal.OpenDemand + snack.OpenDemand, 0);
        }

        [Test]
        public void Tickets_PackDishAndMinutes()
        {
            int ticket = CustomerRules.PackTicket(Menu.TomatoSalad, 12);
            Assert.AreEqual(Menu.TomatoSalad, CustomerRules.TicketDish(ticket));
            Assert.AreEqual(12, CustomerRules.TicketMinutes(ticket));
            Assert.AreEqual("Tomato salad\n12 min", CustomerRules.TicketText(ticket));
            Assert.AreEqual(0, CustomerRules.TicketMinutes(CustomerRules.PackTicket(Menu.Fries, -3)));
            Assert.AreEqual(CustomerRules.MaxTicketMinutes, CustomerRules.TicketMinutes(CustomerRules.PackTicket(Menu.Fries, 1 << 20)));
            Assert.AreEqual(12, CustomerRules.WaitMinutes(12.7f), "one real second is one in-game minute");
            Assert.AreEqual(0, CustomerRules.WaitMinutes(-1f));
            Assert.AreEqual("Fries", CustomerRules.OrderText(Menu.Fries));
            StringAssert.StartsWith("Thanks! +$", CustomerRules.LeaveText(OrderMatch.Exact, 120));
            StringAssert.StartsWith("That's not what I ordered", CustomerRules.LeaveText(OrderMatch.Wrong, 30));
            Assert.AreEqual(CustomerRules.MoodUnhappy, CustomerRules.MoodOf(OrderMatch.Wrong));
        }

        // ---- AC5: the shift ----

        [Test]
        public void ShiftHours_AreEightToFourteen_NoCustomersWhenClosed()
        {
            Assert.IsFalse(Shift.IsShiftHour(7));
            Assert.IsTrue(Shift.IsShiftHour(8));
            Assert.IsTrue(Shift.IsShiftHour(13));
            Assert.IsFalse(Shift.IsShiftHour(14));
            Assert.IsTrue(Shift.IsShiftHour(24 + 9), "day 2, 09:00");
            Assert.AreEqual(18, Shift.HoursToNextOpen(14));
            Assert.AreEqual(3, Shift.HoursToNextOpen(5));
            Assert.AreEqual(Shift.OpenHour, Shift.HourOf(14 + Shift.HoursToNextOpen(14)));
            Assert.AreEqual(2, Shift.DayOf(14 + Shift.HoursToNextOpen(14)));

            var demand = new Dictionary<string, int> { ["meal"] = 9, ["snack"] = 9, ["drink"] = 9 };
            Assert.IsEmpty(CustomerRules.DesiredWishes(m => demand[m], _ => true, false), "no new customers after 14:00");
            Assert.AreEqual(CustomerRules.MaxVisible, CustomerRules.DesiredWishes(m => demand[m], _ => true, true).Count);
        }

        [Test]
        public void ScriptedShift_SummaryNumbersAreCorrect()
        {
            var shift = new Shift();
            Assert.IsTrue(shift.OnClock(8, 0f, 0), "08:00 opens the shift");
            Assert.IsTrue(shift.IsOpen);
            Assert.AreEqual(1, shift.Day);
            Assert.IsFalse(shift.HasLast);

            shift.RecordServed(4, 120);
            shift.RecordServed(5, 200);
            shift.RecordMissed(1);      // walked away (too slow)
            Assert.IsFalse(shift.OnClock(9, 60f, 5), "09:00: still open");
            shift.RecordServed(2, 30);
            Assert.AreEqual(3, shift.Live.Served);

            Assert.IsTrue(shift.OnClock(14, 360f, 2), "14:00 closes; two still waiting");
            Assert.IsFalse(shift.IsOpen);
            Assert.IsTrue(shift.SummaryShowing);
            Assert.AreEqual(1, shift.LastDay);
            Assert.AreEqual(3, shift.Last.Served);
            Assert.AreEqual(350, shift.Last.Earned);
            Assert.AreEqual(37, shift.Last.AverageStarsTenths, "(4 + 5 + 2) / 3 = 3.67");
            Assert.AreEqual("3.7", ShiftSummary.StarsText(shift.Last.AverageStarsTenths));
            Assert.AreEqual(3, shift.Last.Missed, "one walked away, two were sent home at 14:00");

            shift.RecordServed(5, 999); // a sale after closing is not part of the shift
            Assert.AreEqual(3, shift.Last.Served);
            Assert.IsFalse(shift.OnClock(14, 361f, 0), "no new shift during the summary");
            Assert.IsFalse(shift.UpdateSummary(374.9f));
            Assert.IsTrue(shift.UpdateSummary(375f), "the summary shows for 15 s");
            Assert.IsFalse(shift.SummaryShowing);
            Assert.IsFalse(shift.UpdateSummary(400f));

            long nextMorning = 14 + Shift.HoursToNextOpen(14);
            Assert.IsTrue(shift.OnClock(nextMorning, 375f, 0), "the next day, 08:00");
            Assert.IsTrue(shift.IsOpen);
            Assert.AreEqual(2, shift.Day);
            Assert.AreEqual(0, shift.Live.Served);
            Assert.AreEqual(350, shift.Last.Earned, "the last summary stays for F3");
        }

        [Test]
        public void EmptyShift_HasZeroAverage()
        {
            var shift = new Shift();
            shift.OnClock(8, 0f, 0);
            shift.OnClock(14, 1f, 0);
            Assert.AreEqual(0, shift.Last.Served);
            Assert.AreEqual(0, shift.Last.AverageStarsTenths);
            shift.Reset();
            Assert.IsFalse(shift.IsOpen || shift.SummaryShowing || shift.HasLast);
        }

        private static int[] IdsOf(CustomerQueue queue)
        {
            var ids = new int[queue.Entries.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = queue.Entries[i].Id;
            return ids;
        }

        private static byte[] IdsOf(IReadOnlyList<MenuDish> dishes)
        {
            var ids = new byte[dishes.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = dishes[i].Id;
            return ids;
        }
    }
}
