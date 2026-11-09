namespace Worktown.Game.Shop
{
    /// <summary>
    /// Spec 034: what one delivery box holds: <see cref="Units"/> stock units of line <see cref="Line"/> (StockCatalog). Packed into the
    /// box's one synced ushort (<see cref="Pack"/>: the line in the high byte, the units in the low byte) for its label and prompt.
    /// </summary>
    public readonly struct BoxContents
    {
        public BoxContents(int line, int units)
        {
            Line = line;
            Units = units;
        }

        public int Line { get; }
        public int Units { get; }

        public bool IsEmpty => Units <= 0;

        public BoxContents WithUnits(int units) => new BoxContents(Line, units);

        /// <summary>The synced value; 0 for an invalid line (no box).</summary>
        public ushort Pack() =>
            StockCatalog.IsValid(Line) && Units > 0 ? (ushort)(((Line + 1) << 8) | (Units > 255 ? 255 : Units)) : (ushort)0;

        /// <summary>The contents from a synced value (false for 0 or a line outside the catalog).</summary>
        public static bool TryUnpack(ushort packed, out BoxContents box)
        {
            int line = (packed >> 8) - 1;
            int units = packed & 0xFF;
            box = new BoxContents(line, units);
            return StockCatalog.IsValid(line) && units > 0;
        }
    }
}
