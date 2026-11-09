using System;

namespace Worktown.Sim.Economy
{
    /// <summary>
    /// Spec 034: the shop's stock of one good (one fridge or shelf slot, a packaging rack, the cup stack, a drink tap's kegs).
    /// <see cref="Count"/> sealed units of <see cref="Capacity"/>; a unit is <see cref="PercentPerUnit"/> percent of one grocer item
    /// (100: a potato, a bottle of sauce, a cup; 2000: a keg of 20 cups of a drink). Goods drawn in portions (a squeeze of sauce, a
    /// pour) open a unit and use it up: <see cref="OpenPercent"/> is what is left of the opened one. Immutable.
    /// </summary>
    public sealed record StockLine(string GoodId, int Count, int Capacity, int PercentPerUnit, int OpenPercent)
    {
        /// <summary>The most a line may hold (the slot's synced count is one byte).</summary>
        public const int MaxCapacity = 200;

        /// <summary>Sealed units that still fit (an unpack fills at most this many).</summary>
        public int Room => Math.Max(0, Capacity - Count);

        /// <summary>Everything left, in percent of one grocer item (sealed units and the opened one).</summary>
        public long AvailablePercent => (long)Count * PercentPerUnit + OpenPercent;

        /// <summary>Nothing left at all: a take or a draw is refused (OutOfStock).</summary>
        public bool IsEmpty => Count <= 0 && OpenPercent <= 0;
    }
}
