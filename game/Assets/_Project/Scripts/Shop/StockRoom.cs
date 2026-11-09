using System.Collections.Generic;
using Worktown.Game.Cooking;
using Worktown.Sim.Economy;

namespace Worktown.Game.Shop
{
    /// <summary>
    /// Spec 034, server, pure: the shop's stock (Sim.Economy.ShopStock, lines in StockCatalog order), the orders on their way
    /// (SupplyBook) and the delivery boxes (waiting at the supplier for a free spot at the back door, or lying in the shop). The stations
    /// take and draw from it, the terminal orders into it, an unpack fills it. Every step checks first and changes nothing on a refusal;
    /// the money and stock steps have a refund backstop. <see cref="Version"/> counts every change (the server republishes the counts).
    /// </summary>
    public sealed class StockRoom
    {
        private readonly Dictionary<int, BoxContents> _boxes = new Dictionary<int, BoxContents>();
        private readonly List<BoxContents> _waiting = new List<BoxContents>();
        private readonly List<SupplyOrder> _due = new List<SupplyOrder>();

        public StockRoom() : this(StockCatalog.Starting()) { }

        public StockRoom(ShopStock start)
        {
            Stock = start ?? throw new System.ArgumentNullException(nameof(start));
            Book = SupplyBook.Empty;
        }

        public ShopStock Stock { get; private set; }
        public SupplyBook Book { get; private set; }
        public int Version { get; private set; }

        /// <summary>Boxes due but not yet at the back door (no free spot).</summary>
        public int WaitingBoxes => _waiting.Count;

        /// <summary>Boxes lying in the shop (spawned, by object id).</summary>
        public int BoxesInShop => _boxes.Count;

        /// <summary>A new game: the starting stock, no orders, no boxes.</summary>
        public void Reset()
        {
            Stock = StockCatalog.Starting();
            Book = SupplyBook.Empty;
            _boxes.Clear();
            _waiting.Clear();
            Version++;
        }

        public StockLine LineOf(int line) => StockCatalog.IsValid(line) && line < Stock.Lines.Count ? Stock.Lines[line] : null;

        private static CookOutcome OutcomeOf(StockError error)
        {
            switch (error)
            {
                case StockError.None: return CookOutcome.Ok;
                case StockError.OutOfStock: return CookOutcome.OutOfStock;
                case StockError.Full: return CookOutcome.StockFull;
                case StockError.TooManyOrders: return CookOutcome.TooManyOrders;
                default: return CookOutcome.InvalidOption;
            }
        }

        private CookOutcome Apply(StockResult r)
        {
            if (!r.Ok) return OutcomeOf(r.Error);
            Stock = r.Stock;
            Version++;
            return CookOutcome.Ok;
        }

        /// <summary>One sealed unit of line <paramref name="line"/> (a take from a slot, the rack or the cup stack). OutOfStock when empty.</summary>
        public CookOutcome Take(int line)
        {
            StockLine l = LineOf(line);
            return l == null ? CookOutcome.InvalidOption : Apply(Stock.TryTake(l.GoodId));
        }

        /// <summary>Whether line <paramref name="line"/> has a sealed unit to take (no change).</summary>
        public bool CanTake(int line) => LineOf(line) is StockLine l && l.Count > 0;

        /// <summary>Whether line <paramref name="line"/> has anything left to draw a portion from (no change).</summary>
        public bool CanDraw(int line) => LineOf(line) is StockLine l && !l.IsEmpty;

        /// <summary>A portion of <paramref name="percent"/> % of one grocer item from line <paramref name="line"/> (a squeeze, a pour). OutOfStock when empty.</summary>
        public CookOutcome Draw(int line, int percent, out int drawn)
        {
            drawn = 0;
            StockLine l = LineOf(line);
            if (l == null) return CookOutcome.InvalidOption;
            StockResult r = Stock.TryDraw(l.GoodId, percent);
            if (r.Ok) drawn = r.Amount;
            return Apply(r);
        }

        /// <summary>The refund backstop: <paramref name="units"/> taken units go back.</summary>
        public void Return(int line, int units)
        {
            StockLine l = LineOf(line);
            if (l != null && units > 0) Apply(Stock.Return(l.GoodId, units));
        }

        /// <summary>The refund backstop for a portion: <paramref name="percent"/> drawn goes back.</summary>
        public void ReturnPercent(int line, int percent)
        {
            StockLine l = LineOf(line);
            if (l != null && percent > 0) Apply(Stock.ReturnPercent(l.GoodId, percent));
        }

        // ---- ordering (the terminal) ----

        /// <summary>
        /// An order of <paramref name="packs"/> packs of line <paramref name="line"/> by <paramref name="payerId"/> at game minute
        /// <paramref name="nowMinute"/>: checked (the good, the pack count, the orders on the way, the money), paid once through
        /// <paramref name="wallet"/>, then booked. A refusal charges nothing; should the booking still fail after the payment, the
        /// payment is refunded (the backstop). Without a wallet the order is free.
        /// </summary>
        public CookOutcome Order(int line, int packs, string payerId, long nowMinute, EconomyCatalog catalog, ISupplyWallet wallet,
            out SupplyOrder order)
        {
            order = null;
            if (catalog == null || !StockCatalog.TryOffer(catalog, line, out SupplierOffer offer)) return CookOutcome.InvalidOption;
            if (packs < 1 || packs > Supplier.MaxPacksPerOrder) return CookOutcome.InvalidOption;
            if (Book.Pending.Count >= SupplyBook.MaxPending) return CookOutcome.TooManyOrders;
            long price = Supplier.OrderCost(catalog, offer, packs);
            if (wallet != null && !wallet.CanAfford(price)) return CookOutcome.NotEnoughMoney;
            long cost = 0;
            if (wallet != null)
            {
                CookOutcome paid = wallet.Charge(offer, packs, out cost);
                if (paid != CookOutcome.Ok) return paid;
            }
            SupplyBook booked = Book.Place(offer, packs, cost, payerId, nowMinute, out order, out StockError error);
            if (error != StockError.None)
            {
                if (wallet != null && cost > 0) wallet.Refund(cost); // spec 034: never charged without the order
                order = null;
                return OutcomeOf(error);
            }
            Book = booked;
            Version++;
            return CookOutcome.Ok;
        }

        // ---- deliveries ----

        /// <summary>
        /// Every order due by <paramref name="nowMinute"/> leaves the book and its boxes (one per pack) wait for the back door; the due
        /// orders end up in <paramref name="arrived"/> (cleared first) for the feed.
        /// </summary>
        public void Deliver(long nowMinute, List<SupplyOrder> arrived)
        {
            arrived?.Clear();
            Book = Book.TakeDue(nowMinute, _due);
            foreach (SupplyOrder o in _due)
            {
                int line = StockCatalog.IndexOf(o.GoodId);
                for (int p = 0; p < o.Packs; p++) _waiting.Add(new BoxContents(line, o.UnitsPerPack));
                arrived?.Add(o);
            }
            if (_due.Count > 0) Version++;
        }

        /// <summary>The next box waiting for a free spot at the back door (false when none).</summary>
        public bool TryPeekWaiting(out BoxContents box)
        {
            box = _waiting.Count > 0 ? _waiting[0] : default;
            return _waiting.Count > 0;
        }

        /// <summary>The waiting box from <see cref="TryPeekWaiting"/> lies in the shop now as object <paramref name="boxId"/>.</summary>
        public void ConfirmSpawned(int boxId)
        {
            if (_waiting.Count == 0) return;
            _boxes[boxId] = _waiting[0];
            _waiting.RemoveAt(0);
        }

        public bool TryGetBox(int boxId, out BoxContents box) => _boxes.TryGetValue(boxId, out box);

        /// <summary>A box object is gone (despawned): its contents go with it.</summary>
        public void ForgetBox(int boxId) => _boxes.Remove(boxId);

        /// <summary>
        /// Box <paramref name="boxId"/> is unpacked into line <paramref name="line"/>: as many units as fit go in (<paramref name="accepted"/>),
        /// the rest stays in the box (<paramref name="left"/>; an empty box is forgotten, the caller despawns it). NotFood for no such box,
        /// WrongBox for another good, StockFull when nothing fits; nothing changes then.
        /// </summary>
        public CookOutcome Unpack(int boxId, int line, out int accepted, out int left)
        {
            accepted = 0;
            left = 0;
            if (!_boxes.TryGetValue(boxId, out BoxContents box)) return CookOutcome.NotFood;
            left = box.Units;
            if (box.Line != line) return CookOutcome.WrongBox;
            StockLine l = LineOf(line);
            if (l == null) return CookOutcome.InvalidOption;
            StockResult r = Stock.Fill(l.GoodId, box.Units);
            CookOutcome filled = Apply(r);
            if (filled != CookOutcome.Ok) return filled;
            accepted = r.Amount;
            left = box.Units - accepted;
            if (left <= 0) _boxes.Remove(boxId);
            else _boxes[boxId] = box.WithUnits(left);
            return CookOutcome.Ok;
        }

        /// <summary>The synced count of every line (StockCatalog.Presented) into <paramref name="counts"/> (at least StockCatalog.Count long).</summary>
        public void WriteCounts(byte[] counts)
        {
            if (counts == null) return;
            for (int i = 0; i < Stock.Lines.Count && i < counts.Length; i++) counts[i] = StockCatalog.Presented(Stock.Lines[i]);
        }
    }
}
