using System;
using System.Collections.Generic;

namespace Worktown.Sim.Economy
{
    /// <summary>
    /// Spec 034, pure and immutable: the orders on their way, in the order they were placed. <see cref="Place"/> books a paid order
    /// (its delivery is due <see cref="SupplierOffer.DelayMinutes"/> later); <see cref="TakeDue"/> hands over every order due by a game
    /// minute, oldest first, and keeps the rest. Ids count up from 1 and are never reused.
    /// </summary>
    public sealed class SupplyBook
    {
        /// <summary>The most orders on their way at once (a spam guard; the terminal says so).</summary>
        public const int MaxPending = 12;

        private static readonly SupplyOrder[] None = Array.Empty<SupplyOrder>();
        private readonly SupplyOrder[] _pending;

        private SupplyBook(int nextId, SupplyOrder[] pending)
        {
            NextId = nextId;
            _pending = pending;
            Pending = Array.AsReadOnly(pending);
        }

        public static SupplyBook Empty { get; } = new SupplyBook(1, None);

        public int NextId { get; }

        /// <summary>The orders on their way, oldest first.</summary>
        public IReadOnlyList<SupplyOrder> Pending { get; }

        /// <summary>
        /// Books <paramref name="packs"/> packs of <paramref name="offer"/> paid <paramref name="cost"/> by <paramref name="payerId"/> at
        /// game minute <paramref name="nowMinute"/>. InvalidQuantity outside 1..<see cref="Supplier.MaxPacksPerOrder"/> or a negative
        /// cost, TooManyOrders at <see cref="MaxPending"/>. The order is null on a refusal (and the book unchanged).
        /// </summary>
        public SupplyBook Place(SupplierOffer offer, int packs, long cost, string payerId, long nowMinute, out SupplyOrder order,
            out StockError error)
        {
            if (offer == null) throw new ArgumentNullException(nameof(offer));
            order = null!;
            if (packs < 1 || packs > Supplier.MaxPacksPerOrder || cost < 0)
            {
                error = StockError.InvalidQuantity;
                return this;
            }
            if (_pending.Length >= MaxPending)
            {
                error = StockError.TooManyOrders;
                return this;
            }
            order = new SupplyOrder(NextId, offer.GoodId, packs, offer.PackSize, cost, payerId ?? string.Empty, nowMinute,
                nowMinute + Math.Max(0, offer.DelayMinutes));
            var next = new SupplyOrder[_pending.Length + 1];
            Array.Copy(_pending, next, _pending.Length);
            next[_pending.Length] = order;
            error = StockError.None;
            return new SupplyBook(NextId + 1, next);
        }

        /// <summary>Every order due by <paramref name="nowMinute"/> (oldest first) into <paramref name="due"/> (cleared first); the book of the rest.</summary>
        public SupplyBook TakeDue(long nowMinute, List<SupplyOrder> due)
        {
            if (due == null) throw new ArgumentNullException(nameof(due));
            due.Clear();
            foreach (SupplyOrder o in _pending)
                if (o.DueMinute <= nowMinute) due.Add(o);
            if (due.Count == 0) return this;
            var rest = new List<SupplyOrder>(_pending.Length - due.Count);
            foreach (SupplyOrder o in _pending)
                if (o.DueMinute > nowMinute) rest.Add(o);
            return new SupplyBook(NextId, rest.ToArray());
        }

        /// <summary>The refund backstop: the order <paramref name="orderId"/> is taken off the book (it was paid but could not stand).</summary>
        public SupplyBook Cancel(int orderId)
        {
            int at = Array.FindIndex(_pending, o => o.Id == orderId);
            if (at < 0) return this;
            var rest = new List<SupplyOrder>(_pending);
            rest.RemoveAt(at);
            return new SupplyBook(NextId, rest.ToArray());
        }

        /// <summary>Packs of <paramref name="goodId"/> on their way.</summary>
        public int PacksOnTheWay(string goodId)
        {
            int n = 0;
            foreach (SupplyOrder o in _pending)
                if (string.Equals(o.GoodId, goodId, StringComparison.Ordinal)) n += o.Packs;
            return n;
        }

        /// <summary>The earliest due minute of the orders on their way, or null.</summary>
        public long? NextDue()
        {
            long? next = null;
            foreach (SupplyOrder o in _pending)
                if (next == null || o.DueMinute < next.Value) next = o.DueMinute;
            return next;
        }
    }
}
