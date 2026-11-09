namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 029b (028b review 3): the order of one stock take, so nobody pays for an item they do not get: check, create,
    /// check the hand-over, then pay, then hand over. Every refusal comes before the payment and leaves the wallet alone.
    /// The hand-over after the payment was checked (<see cref="IStockTakeSteps.CanHandOver"/>) and runs in the same server
    /// call, so it cannot be refused; if it still failed (an engine error) the answer is SimRefused and the server logs it.
    /// </summary>
    public static class StockTakeSequence
    {
        public static CookOutcome Run(IStockTakeSteps steps)
        {
            CookOutcome check = steps.Check();
            if (check != CookOutcome.Ok) return check;
            CookOutcome created = steps.Create();
            if (created != CookOutcome.Ok) return created;
            if (!steps.CanHandOver()) return CookOutcome.SimRefused;
            CookOutcome paid = steps.Pay();
            if (paid != CookOutcome.Ok) return paid;
            return steps.HandOver() ? CookOutcome.Ok : CookOutcome.SimRefused;
        }
    }
}
