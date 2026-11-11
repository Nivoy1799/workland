using System;
using System.Collections.Generic;
using Worktown.Game.Economy;

namespace Worktown.Game.Npc
{
    /// <summary>
    /// Spec 033, server, pure: the lines of a guest's order. An order is its dish and, optionally, a drink (a lidded cup); each line is
    /// handed over at the counter on its own, in any order. A guest whose order has a line still open stays in the queue (its patience
    /// keeps draining) and takes only that line; when the last line arrives the lines are combined into one pick for the register
    /// (<see cref="Serve"/>): one bill (the sum of the lines' bills), one tip and one review over the whole order, paid there exactly
    /// once (032). A guest that leaves with a line still open (patience, closing, changed its mind, gone) pays what it got, once
    /// (<see cref="TakeOpenBill"/>; no tip), to the cook who handed it over. Guests without a tab (no drink ordered) behave as in 032.
    /// </summary>
    public sealed class GuestTab
    {
        private sealed class Entry
        {
            public string Drink;
            public bool FoodServed;
            public bool DrinkServed;
            public long Bill;
            public int Misses;
            public int StarsSum;
            public int Lines;
            public float QualitySum;
            public OrderMatch Worst = OrderMatch.Exact;
            public byte Dish;
            public int Payee = -1;
            public string Note; // spec 036: the dish line's review note
        }

        private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();
        // 033 review: the combined bill of an order whose last line came, until the register's line took it (Close). Should the
        // hand-over fail in between, the counter's fallback pays this whole bill (TakeCompletedBill), never only the last line.
        private readonly Dictionary<int, long> _completed = new Dictionary<int, long>();

        /// <summary>Guest <paramref name="guest"/> ordered drink <paramref name="drink"/> with its dish (null: no drink line, no tab).</summary>
        public void Open(int guest, string drink)
        {
            _completed.Remove(guest);
            if (drink == null)
            {
                _entries.Remove(guest);
                return;
            }
            _entries[guest] = new Entry { Drink = drink };
        }

        /// <summary>The guest's drink line (a drink base id), or null.</summary>
        public string DrinkOf(int guest) => _entries.TryGetValue(guest, out Entry e) ? e.Drink : null;

        /// <summary>The guest still waits for its dish (true for a guest without a tab).</summary>
        public bool WantsFood(int guest) => !_entries.TryGetValue(guest, out Entry e) || !e.FoodServed;

        /// <summary>The guest still waits for its drink.</summary>
        public bool WantsDrink(int guest) => _entries.TryGetValue(guest, out Entry e) && e.Drink != null && !e.DrinkServed;

        /// <summary>Its dish came already (it waits only for its drink).</summary>
        public bool FoodServed(int guest) => _entries.TryGetValue(guest, out Entry e) && e.FoodServed;

        /// <summary>The line (dish or drink) was handed to this guest and is on its tab (it pays for it; the counter's fallback must not).</summary>
        public bool IsBilled(int guest, bool drink) =>
            _entries.TryGetValue(guest, out Entry e) && (drink ? e.DrinkServed : e.FoodServed);

        /// <summary>How many guests have a tab.</summary>
        public int Count => _entries.Count;

        /// <summary>
        /// One line was handed to <paramref name="line"/>'s guest by <paramref name="payee"/>, billed <paramref name="lineBill"/>. True when
        /// this completes the order: <paramref name="whole"/> is then the order's pick (the dish, the worst match, the steps short of every
        /// line, the mean quality and stars, the last wait, bill share 100 %) and <paramref name="wholeBill"/> the sum of the lines' bills;
        /// the tab is closed. False while a line is still open (the line is on the tab; nothing is paid yet). A guest without a tab (no
        /// drink line) is complete at once with its line as it is.
        /// </summary>
        public bool Serve(GuestPick line, int payee, long lineBill, out GuestPick whole, out long wholeBill)
        {
            whole = line;
            wholeBill = Math.Max(0L, lineBill);
            if (!_entries.TryGetValue(line.CustomerId, out Entry e)) return true;
            // 033 review: a line already served (or a drink nobody ordered) is refused: false, nothing changes.
            if (IsDuplicate(e, line.IsDrink))
            {
                whole = default;
                wholeBill = 0;
                return false;
            }
            if (line.IsDrink) e.DrinkServed = true;
            else
            {
                e.FoodServed = true;
                e.Dish = line.Dish;
                e.Note = line.Fit.Note;
            }
            e.Bill += Math.Max(0L, lineBill);
            e.Misses += line.Fit.Misses;
            e.StarsSum += line.Fit.Stars;
            e.QualitySum += line.Fit.Quality;
            e.Lines++;
            if (Rank(line.Fit.Match) > Rank(e.Worst)) e.Worst = line.Fit.Match;
            e.Payee = payee;
            if (!e.FoodServed || (e.Drink != null && !e.DrinkServed)) return false;
            _entries.Remove(line.CustomerId);
            int stars = (2 * e.StarsSum + e.Lines) / (2 * e.Lines); // the mean, rounded half up
            var fit = new OrderFit(e.Worst, e.Misses, e.QualitySum / e.Lines, stars, e.Note);
            whole = new GuestPick(line.CustomerId, e.Dish, fit, OrderRules.ExactPermille, line.WaitedTenths, line.PatienceTenths);
            wholeBill = e.Bill;
            _completed[line.CustomerId] = e.Bill;
            return true;
        }

        private static bool IsDuplicate(Entry e, bool drink) => drink ? e.Drink == null || e.DrinkServed : e.FoodServed;

        /// <summary>Whether <see cref="Serve"/> would refuse <paramref name="line"/> as a line already served (or a drink not ordered).</summary>
        public bool IsDuplicate(GuestPick line) => _entries.TryGetValue(line.CustomerId, out Entry e) && IsDuplicate(e, line.IsDrink);

        /// <summary>033 review: the completed order of <paramref name="guest"/> is in the register's line now; its bill is no longer kept here.</summary>
        public void Close(int guest) => _completed.Remove(guest);

        /// <summary>
        /// 033 review: the counter's fallback for an order whose last line came but whose guest never reached the register's line: the
        /// combined bill of every line (once: forgotten after this). False when there is none.
        /// </summary>
        public bool TakeCompletedBill(int guest, out long bill)
        {
            if (!_completed.TryGetValue(guest, out bill)) return false;
            _completed.Remove(guest);
            return true;
        }

        /// <summary>
        /// Guest <paramref name="guest"/> leaves with a line still open: the bill of the lines it got and their cook (once: the tab is
        /// closed), the stars of what it got and its steps short counting the missing line. False when it got nothing yet (or has no tab).
        /// </summary>
        public bool TakeOpenBill(int guest, out long bill, out int payee, out int stars, out int misses)
        {
            bill = 0;
            payee = -1;
            stars = 0;
            misses = 0;
            if (!_entries.TryGetValue(guest, out Entry e)) return false;
            _entries.Remove(guest);
            if (e.Lines == 0) return false;
            bill = e.Bill;
            payee = e.Payee;
            stars = (2 * e.StarsSum + e.Lines) / (2 * e.Lines);
            misses = e.Misses + 1;
            return true;
        }

        public void Remove(int guest)
        {
            _entries.Remove(guest);
            _completed.Remove(guest);
        }

        public void Clear()
        {
            _entries.Clear();
            _completed.Clear();
        }

        private static int Rank(OrderMatch m) => m == OrderMatch.Exact ? 0 : m == OrderMatch.Close ? 1 : 2;

        /// <summary>A line's bill: the sim's payout at the pick's share (as the register would bill it).</summary>
        public static long LineBill(GuestPick line, long simPayout) =>
            Worktown.Sim.Economy.CashRules.ToCoins(EconomyRules.ApplyPermille(simPayout, line.BillPermille)); // spec 037: whole coins
    }
}
