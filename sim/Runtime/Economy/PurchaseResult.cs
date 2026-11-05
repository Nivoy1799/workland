namespace Worktown.Sim.Economy
{
    public sealed record PurchaseResult(EconomyState State, EconomyError Error, long Cost)
    {
        public bool Ok => Error == EconomyError.None;
    }
}
