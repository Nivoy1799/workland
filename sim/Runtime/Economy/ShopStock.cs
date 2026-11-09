using System;
using System.Collections.Generic;
using Worktown.Sim.Cooking;

namespace Worktown.Sim.Economy
{
    /// <summary>
    /// Spec 034, pure and immutable: the shop's stock, one <see cref="StockLine"/> per good in a fixed order (the order of the slots'
    /// synced counts). Every step returns a new stock and changes nothing on a refusal:
    /// - <see cref="TryTake"/>: one sealed unit (an item into the hands); OutOfStock when none is left.
    /// - <see cref="TryDraw"/>: a portion in percent of one grocer item (a squeeze, a pour): opens a sealed unit when the opened one
    ///   runs short; the last drops are drawn when less than asked is left.
    /// - <see cref="Fill"/>: an unpack: up to <see cref="StockLine.Room"/> units go in, the rest stays in the box.
    /// - <see cref="Return"/> / <see cref="ReturnPercent"/>: the refund backstop (what a refused hand-over took goes back, even past
    ///   the capacity).
    /// </summary>
    public sealed class ShopStock
    {
        private readonly StockLine[] _lines;
        private readonly Dictionary<string, int> _index;

        private ShopStock(StockLine[] lines, Dictionary<string, int> index)
        {
            _lines = lines;
            _index = index;
            Lines = Array.AsReadOnly(lines);
        }

        /// <exception cref="ArgumentException">A bad id, a duplicate, a capacity outside 1..<see cref="StockLine.MaxCapacity"/>, a count
        /// outside 0..capacity, a unit size below 1 or an opened unit outside 0..unit.</exception>
        public static ShopStock Create(IEnumerable<StockLine> lines)
        {
            if (lines == null) throw new ArgumentNullException(nameof(lines));
            var list = new List<StockLine>();
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (StockLine line in lines)
            {
                if (line == null) throw new ArgumentException("Stock line must not be null.");
                if (!ItemCanonical.IsValidId(line.GoodId)) throw new ArgumentException($"Invalid stock good id '{line.GoodId}'.");
                if (index.ContainsKey(line.GoodId)) throw new ArgumentException($"Duplicate stock good '{line.GoodId}'.");
                if (line.Capacity < 1 || line.Capacity > StockLine.MaxCapacity) throw new ArgumentException($"Capacity of '{line.GoodId}' out of range.");
                if (line.Count < 0 || line.Count > line.Capacity) throw new ArgumentException($"Count of '{line.GoodId}' out of range.");
                if (line.PercentPerUnit < 1) throw new ArgumentException($"Unit size of '{line.GoodId}' must be at least 1.");
                if (line.OpenPercent < 0 || line.OpenPercent > line.PercentPerUnit) throw new ArgumentException($"Opened unit of '{line.GoodId}' out of range.");
                index.Add(line.GoodId, list.Count);
                list.Add(line);
            }
            return new ShopStock(list.ToArray(), index);
        }

        /// <summary>The lines in their fixed order.</summary>
        public IReadOnlyList<StockLine> Lines { get; }

        /// <summary>The line's position in <see cref="Lines"/>, or -1.</summary>
        public int IndexOf(string? goodId) => goodId != null && _index.TryGetValue(goodId, out int i) ? i : -1;

        public bool TryGetLine(string? goodId, out StockLine line)
        {
            int i = IndexOf(goodId);
            line = i >= 0 ? _lines[i] : null!;
            return i >= 0;
        }

        /// <summary>One sealed unit of <paramref name="goodId"/> (Amount 1). UnknownGood, OutOfStock.</summary>
        public StockResult TryTake(string goodId)
        {
            int i = IndexOf(goodId);
            if (i < 0) return Refuse(StockError.UnknownGood);
            StockLine line = _lines[i];
            if (line.Count <= 0) return Refuse(StockError.OutOfStock);
            return new StockResult(With(i, line with { Count = line.Count - 1 }), StockError.None, 1);
        }

        /// <summary>
        /// A portion of <paramref name="percent"/> % of one grocer item (Amount = the percent drawn): from the opened unit, opening
        /// sealed units as needed; when less is left than asked, what is left (at least 1 %). InvalidQuantity below 1, UnknownGood,
        /// OutOfStock when nothing is left.
        /// </summary>
        public StockResult TryDraw(string goodId, int percent) => TryDraw(goodId, percent, 1);

        /// <summary>
        /// 034 review (last drops): as <see cref="TryDraw(string, int)"/>, but OutOfStock (nothing changes) when less than
        /// <paramref name="minPercent"/> is left: the least share the draw's bucket counts (PriceRules.LeastPortionShare,
        /// LeastFillShare), so a "lots" squeeze never comes out of 20 % left and a full cup never out of the last 1 %; the wholesaler's
        /// cap (PriceRules.StockCost) then never counts more than was drawn.
        /// </summary>
        public StockResult TryDraw(string goodId, int percent, int minPercent)
        {
            if (percent < 1) return Refuse(StockError.InvalidQuantity);
            minPercent = Math.Max(1, Math.Min(minPercent, percent));
            int i = IndexOf(goodId);
            if (i < 0) return Refuse(StockError.UnknownGood);
            StockLine line = _lines[i];
            if (line.IsEmpty || line.AvailablePercent < minPercent) return Refuse(StockError.OutOfStock);
            int count = line.Count;
            long open = line.OpenPercent;
            while (open < percent && count > 0)
            {
                count--;
                open += line.PercentPerUnit;
            }
            int drawn = (int)Math.Min(percent, open);
            // 034 review: a unit is opened only while the opened part is short of the draw, so what is left is always below one unit.
            int left = (int)(open - drawn);
            return new StockResult(With(i, line with { Count = count, OpenPercent = left }), StockError.None, drawn);
        }

        /// <summary>
        /// An unpack of <paramref name="units"/> sealed units: as many as fit go in (Amount), the rest stays in the box. Full when none
        /// fits, InvalidQuantity below 1, UnknownGood.
        /// </summary>
        public StockResult Fill(string goodId, int units)
        {
            if (units < 1) return Refuse(StockError.InvalidQuantity);
            int i = IndexOf(goodId);
            if (i < 0) return Refuse(StockError.UnknownGood);
            StockLine line = _lines[i];
            int accepted = Math.Min(units, line.Room);
            if (accepted <= 0) return Refuse(StockError.Full);
            return new StockResult(With(i, line with { Count = line.Count + accepted }), StockError.None, accepted);
        }

        /// <summary>The refund backstop: <paramref name="units"/> taken units go back (never refused for a known good; may pass the capacity).</summary>
        public StockResult Return(string goodId, int units)
        {
            if (units < 1) return Refuse(StockError.InvalidQuantity);
            int i = IndexOf(goodId);
            if (i < 0) return Refuse(StockError.UnknownGood);
            StockLine line = _lines[i];
            int count = (int)Math.Min(byte.MaxValue, (long)line.Count + units);
            return new StockResult(With(i, line with { Count = count }), StockError.None, units);
        }

        /// <summary>The refund backstop for a portion: <paramref name="percent"/> drawn goes back into the opened unit (a full one seals again).</summary>
        public StockResult ReturnPercent(string goodId, int percent)
        {
            if (percent < 1) return Refuse(StockError.InvalidQuantity);
            int i = IndexOf(goodId);
            if (i < 0) return Refuse(StockError.UnknownGood);
            StockLine line = _lines[i];
            long open = (long)line.OpenPercent + percent;
            int count = line.Count;
            while (open > line.PercentPerUnit)
            {
                open -= line.PercentPerUnit;
                count++;
            }
            if (open == line.PercentPerUnit && count < line.Capacity)
            {
                open = 0;
                count++;
            }
            count = Math.Min(byte.MaxValue, count);
            return new StockResult(With(i, line with { Count = count, OpenPercent = (int)open }), StockError.None, percent);
        }

        /// <summary>
        /// Below a quarter of the capacity (spec 034: "Low stock: X" under 25 %), counting the opened unit's share: everything left is
        /// below a quarter of <c>Capacity × PercentPerUnit</c>.
        /// </summary>
        public static bool IsLow(StockLine line) =>
            line != null && line.AvailablePercent * 4 < (long)line.Capacity * line.PercentPerUnit;

        /// <summary>The starting stock of a line (a shift starts with it, so the game is playable at once): half its capacity, sealed.</summary>
        public static int StartingCount(int capacity) => Math.Max(0, capacity) / 2;

        private ShopStock With(int i, StockLine line)
        {
            var copy = (StockLine[])_lines.Clone();
            copy[i] = line;
            return new ShopStock(copy, _index);
        }

        private StockResult Refuse(StockError error) => new StockResult(this, error, 0);
    }
}
