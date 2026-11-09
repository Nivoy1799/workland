namespace Worktown.Sim.Economy
{
    /// <summary>
    /// Spec 034: the answer of one <see cref="ShopStock"/> step: the new stock (the same instance on a refusal), the error, and the
    /// amount moved (units taken or filled, percent drawn; 0 on a refusal).
    /// </summary>
    public sealed record StockResult(ShopStock Stock, StockError Error, int Amount)
    {
        public bool Ok => Error == StockError.None;
    }
}
