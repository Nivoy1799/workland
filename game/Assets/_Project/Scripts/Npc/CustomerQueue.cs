using System;
using System.Collections.Generic;

namespace Worktown.Game.Npc
{
    /// <summary>
    /// Pure, ordered queue of visible customers (spec 014). The server keeps one; customers' network
    /// objects follow it. Ids are assigned by the caller (spawned object ids). Spec 022: each customer
    /// has an ordered dish; serving picks the first exact match in queue order, else the first in line.
    /// </summary>
    public sealed class CustomerQueue
    {
        private readonly List<QueuedCustomer> _entries = new List<QueuedCustomer>();

        public IReadOnlyList<QueuedCustomer> Entries => _entries;

        /// <summary>
        /// Matches the queue to the desired wishes: customers whose wish is still wanted stay in place
        /// (oldest first), the others are returned in <paramref name="leaving"/> and removed, and the
        /// missing wishes are returned in <paramref name="joining"/> (the caller spawns and adds them).
        /// </summary>
        public void Reconcile(IReadOnlyList<string> desired, List<QueuedCustomer> leaving, List<string> joining)
        {
            leaving.Clear();
            joining.Clear();
            var wanted = new Dictionary<string, int>();
            foreach (string wish in desired) wanted[wish] = wanted.TryGetValue(wish, out int n) ? n + 1 : 1;

            var kept = new List<QueuedCustomer>(_entries.Count);
            foreach (QueuedCustomer customer in _entries)
            {
                if (wanted.TryGetValue(customer.Wish, out int n) && n > 0)
                {
                    wanted[customer.Wish] = n - 1;
                    kept.Add(customer);
                }
                else
                {
                    leaving.Add(customer);
                }
            }
            _entries.Clear();
            _entries.AddRange(kept);

            foreach (string wish in desired)
            {
                if (!wanted.TryGetValue(wish, out int n) || n <= 0) continue;
                wanted[wish] = n - 1;
                joining.Add(wish);
            }
        }

        public void Add(int id, string wish)
        {
            _entries.Add(new QueuedCustomer(id, wish));
        }

        /// <summary>Spec 022: a customer with its ordered dish joins at the end.</summary>
        public void Add(int id, string wish, byte dish)
        {
            _entries.Add(new QueuedCustomer(id, wish, dish));
        }

        /// <summary>Spec 032: a customer with its ordered dish and the order's packed modifiers joins at the end.</summary>
        public void Add(int id, string wish, byte dish, byte modifiers)
        {
            _entries.Add(new QueuedCustomer(id, wish, dish, modifiers));
        }

        /// <summary>Spec 033: with the order's drink line too (Cooking.DrinkRules.DrinkByte; 0 = none).</summary>
        public void Add(int id, string wish, byte dish, byte modifiers, byte drink)
        {
            _entries.Add(new QueuedCustomer(id, wish, dish, modifiers, drink));
        }

        /// <summary>
        /// Spec 033: as <see cref="TryPick(Func{QueuedCustomer, OrderMatch}, out QueuedCustomer, out OrderMatch)"/> among the guests
        /// <paramref name="eligible"/> says still want this kind of line (a guest whose dish came already waits only for its drink): the
        /// first of them in queue order that matches exactly, else the first of them. False when none is eligible.
        /// </summary>
        public bool TryPick(Func<QueuedCustomer, bool> eligible, Func<QueuedCustomer, OrderMatch> match, out QueuedCustomer customer,
            out OrderMatch result)
        {
            customer = default;
            result = OrderMatch.Wrong;
            if (eligible == null || match == null) return false;
            bool any = false;
            QueuedCustomer first = default;
            foreach (QueuedCustomer entry in _entries)
            {
                if (!eligible(entry)) continue;
                if (!any)
                {
                    any = true;
                    first = entry;
                }
                if (match(entry) != OrderMatch.Exact) continue;
                customer = entry;
                result = OrderMatch.Exact;
                return true;
            }
            if (!any) return false;
            customer = first;
            result = match(first);
            return true;
        }

        /// <summary>
        /// Spec 022: who gets a served dish, without changing the queue. <paramref name="match"/> rates the dish
        /// against an ordered dish id. The first customer (in queue order) whose order matches exactly, else the
        /// first in line (Close or Wrong). False if the queue is empty. Deterministic.
        /// </summary>
        public bool TryPick(Func<byte, OrderMatch> match, out QueuedCustomer customer, out OrderMatch result)
        {
            if (match == null)
            {
                customer = default;
                result = OrderMatch.Wrong;
                return false;
            }
            return TryPick(entry => match(entry.Dish), out customer, out result);
        }

        /// <summary>
        /// Spec 032: as <see cref="TryPick(Func{byte, OrderMatch}, out QueuedCustomer, out OrderMatch)"/>, with the whole entry (the
        /// dish and its modifiers) given to <paramref name="match"/>.
        /// </summary>
        public bool TryPick(Func<QueuedCustomer, OrderMatch> match, out QueuedCustomer customer, out OrderMatch result)
        {
            customer = default;
            result = OrderMatch.Wrong;
            if (match == null || _entries.Count == 0) return false;
            foreach (QueuedCustomer entry in _entries)
            {
                if (match(entry) != OrderMatch.Exact) continue;
                customer = entry;
                result = OrderMatch.Exact;
                return true;
            }
            customer = _entries[0];
            result = match(customer);
            return true;
        }

        /// <summary>Removes the customer with this id (served); false if it is not queued.</summary>
        public bool Remove(int id)
        {
            int index = IndexOf(id);
            if (index < 0) return false;
            _entries.RemoveAt(index);
            return true;
        }

        /// <summary>
        /// A dish of <paramref name="market"/> was sold to NPC demand: the first customer who wants that
        /// market takes it, otherwise the first in line. False if the queue is empty.
        /// </summary>
        public bool TryServe(string market, out QueuedCustomer served)
        {
            served = default;
            if (_entries.Count == 0) return false;
            int index = _entries.FindIndex(c => c.Wish == market);
            if (index < 0) index = 0;
            served = _entries[index];
            _entries.RemoveAt(index);
            return true;
        }

        public int IndexOf(int id)
        {
            return _entries.FindIndex(c => c.Id == id);
        }

        public void Clear()
        {
            _entries.Clear();
        }
    }
}
