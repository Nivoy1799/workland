using System.Collections.Generic;

namespace Worktown.Game.Npc
{
    /// <summary>
    /// Pure, ordered queue of visible customers (spec 014). The server keeps one; customers' network
    /// objects follow it. Ids are assigned by the caller (spawned object ids).
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
