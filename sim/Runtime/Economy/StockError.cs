namespace Worktown.Sim.Economy
{
    /// <summary>Spec 034: why a stock or supply step was refused (nothing changed then).</summary>
    public enum StockError
    {
        None,
        UnknownGood,
        OutOfStock,
        InvalidQuantity,
        /// <summary>The line is at capacity: an unpack puts nothing in.</summary>
        Full,
        /// <summary>Too many orders on the way (<see cref="SupplyBook.MaxPending"/>).</summary>
        TooManyOrders
    }
}
