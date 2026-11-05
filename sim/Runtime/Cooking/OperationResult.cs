namespace Worktown.Sim.Cooking
{
    public sealed record OperationResult(Item? Item, OperationError Error)
    {
        public bool Ok => Error == OperationError.None;
    }
}
