namespace Worktown.Game.Cooking
{
    /// <summary>Spec 029: where a fryer basket is. In the low bits of the basket's synced mode byte (<see cref="FryerRules"/>).</summary>
    public enum BasketState : byte
    {
        /// <summary>Hanging above the oil: food goes in and comes out here.</summary>
        Raised = 0,
        /// <summary>In the oil: the food fries while the fryer is on; a click shakes it.</summary>
        Lowered = 1,
        /// <summary>Just raised, dripping for <see cref="FryerRules.DrainSeconds"/>; food taken out now is greasy.</summary>
        Draining = 2
    }
}
