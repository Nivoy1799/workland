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

        /// <summary>
        /// Whether line <paramref name="line"/> has at least <paramref name="minPercent"/> % of a grocer item left to draw a portion from
        /// (no change). 035 review: a squeeze press needs its smallest step (10 %), a pour its smallest share (1 %), so a press is never
        /// taken on last drops the release cannot draw.
        /// </summary>
        public bool CanDraw(int line, int minPercent) => LineOf(line) is StockLine l && !l.IsEmpty && l.AvailablePercent >= System.Math.Max(1, minPercent);

        /// <summary>
        /// A portion of <paramref name="percent"/> % of one grocer item from line <paramref name="line"/> (a squeeze, a pour), refused
        /// (OutOfStock, nothing changes) when less than <paramref name="minPercent"/> is left (034 review, last drops: ShopStock.TryDraw).
        /// </summary>
        public CookOutcome Draw(int line, int percent, int minPercent, out int drawn)
        {
            drawn = 0;
            StockLine l = LineOf(line);
            if (l == null) return CookOutcome.InvalidOption;
            StockResult r = Stock.TryDraw(l.GoodId, percent, minPercent);
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

        /// <summary>The ids of the boxes lying in the shop, into <paramref name="ids"/> (cleared first).</summary>
        public void BoxIds(List<int> ids)
        {
            if (ids == null) return;
            ids.Clear();
            foreach (int id in _boxes.Keys) ids.Add(id);
            ids.Sort(); // a stable order (deterministic logs and tests)
        }

        /// <summary>
        /// 034 review: box <paramref name="boxId"/> was lost (below the world, or despawned without its unpack): its object is forgotten
        /// and its contents wait first in line for the back door again (kept, never lost, never doubled). False for no such box.
        /// </summary>
        public bool ReturnLostBox(int boxId)
        {
            if (!_boxes.TryGetValue(boxId, out BoxContents box)) return false;
            _boxes.Remove(boxId);
            _waiting.Insert(0, box);
            Version++;
            return true;
        }

        /// <summary>
        /// 034 review: the server stops but the game (the economy) goes on. Every order still on its way is refunded to its payer through
        /// <paramref name="refund"/> (account id, amount), once, and leaves the book; the boxes lying in the shop (FishNet despawns
        /// them) wait for the back door again with their contents. The stock itself is kept: it belongs to the game, not to the server
        /// run (<see cref="Reset"/> is a new game). Returns the number of orders refunded. 035 review: an order whose refund fails
        /// (<paramref name="refund"/> returns false) is never lost: it stays in the book and still arrives later.
        /// </summary>
        public int EndSession(System.Func<string, long, bool> refund)
        {
            int refunded = 0;
            SupplyBook book = Book;
            foreach (SupplyOrder o in Book.Pending)
            {
                if (o.Cost > 0 && (refund == null || !refund(o.PayerId, o.Cost))) continue; // kept: it still arrives
                book = book.Cancel(o.Id);
                refunded++;
            }
            Book = book;
            var ids = new List<int>(_boxes.Keys);
            ids.Sort();
            for (int i = ids.Count - 1; i >= 0; i--) _waiting.Insert(0, _boxes[ids[i]]);
            _boxes.Clear();
            Version++;
            return refunded;
        }

        /// <summary>
        /// 034 review (pure): the delivery spot for the next box in one spawn pass: the first spot neither <paramref name="occupied"/>
        /// (the physics probe) nor <paramref name="usedThisPass"/> (a box was just put there; the probe does not see it before the
        /// transforms sync), or -1. The caller marks the chosen spot used.
        /// </summary>
        public static int FreeSpot(IReadOnlyList<bool> occupied, IReadOnlyList<bool> usedThisPass)
        {
            if (occupied == null) return -1;
            for (int i = 0; i < occupied.Count; i++)
                if (!occupied[i] && (usedThisPass == null || i >= usedThisPass.Count || !usedThisPass[i])) return i;
            return -1;
        }

        /// <summary>
        /// 035 review: one spawn pass of the waiting boxes (SupplyService runs exactly this): each next box gets the next
        /// <see cref="FreeSpot"/> (the spots used in this pass count as taken; <paramref name="used"/> is cleared first), is spawned by
        /// <paramref name="spawn"/> (box, spot) which returns its object id (or -1: stop, the box keeps waiting) and is confirmed. Returns
        /// how many boxes were spawned.
        /// </summary>
        public int SpawnWaiting(IReadOnlyList<bool> occupied, bool[] used, System.Func<BoxContents, int, int> spawn)
        {
            if (occupied == null || used == null || spawn == null) return 0;
            for (int i = 0; i < used.Length; i++) used[i] = false;
            int spawned = 0;
            while (TryPeekWaiting(out BoxContents box))
            {
                int s = FreeSpot(occupied, used);
                if (s < 0) break; // the rest waits for a free spot
                used[s] = true;
                int id = spawn(box, s);
                if (id < 0) break;
                ConfirmSpawned(id);
                spawned++;
            }
            return spawned;
        }

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
