namespace Worktown.Sim.Economy
{
    public sealed record LedgerResult(EconomyState State, EconomyError Error)
    {
        public bool Ok => Error == EconomyError.None;
    }
}
