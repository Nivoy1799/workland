namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 029b: the steps of one stock take, in the order <see cref="StockTakeSequence.Run"/> calls them. The server's
    /// KitchenService implements them with the real kitchen, food prefab, hands and grocer; tests count the calls.
    /// </summary>
    public interface IStockTakeSteps
    {
        /// <summary>The slot, reach, hands and ingredient checks (StockRules.CheckTake).</summary>
        CookOutcome Check();
        /// <summary>The sim item of the ingredient.</summary>
        CookOutcome Create();
        /// <summary>
        /// Everything the hand-over needs, checked without changing anything: a food prefab that can be held, and hands the
        /// registry would let it into. After a true answer the hand-over cannot be refused.
        /// </summary>
        bool CanHandOver();
        /// <summary>The grocer is paid: called at most once, only after <see cref="CanHandOver"/>.</summary>
        CookOutcome Pay();
        /// <summary>The food object is spawned into the hands.</summary>
        bool HandOver();
    }
}
