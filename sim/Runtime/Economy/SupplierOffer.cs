namespace Worktown.Sim.Economy
{
    /// <summary>
    /// Spec 034: what the supplier sells of one good: its grocer price per grocer item, how many grocer items one stock unit is
    /// (<see cref="GrocerItemsPerUnit"/>: 1, or 20 for a keg of 20 cups of a drink), the stock units in one pack (one delivery box),
    /// and the delivery delay in game minutes. Prices: <see cref="Supplier.OrderCost"/>.
    /// </summary>
    public sealed record SupplierOffer(string GoodId, long GrocerPrice, int GrocerItemsPerUnit, int PackSize, int DelayMinutes)
    {
        /// <summary>Grocer items in <paramref name="packs"/> packs (what the bulk price multiplies).</summary>
        public long GrocerItems(int packs) => (long)packs * PackSize * GrocerItemsPerUnit;
    }
}
