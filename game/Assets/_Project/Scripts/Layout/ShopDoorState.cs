namespace Worktown.Game.Layout
{
    /// <summary>Spec 062b: the shop's door (ShopDoor, its sign): open, closing after the service that runs, or closed (build mode).</summary>
    public enum ShopDoorState : byte
    {
        /// <summary>Today's loop: one service follows the other.</summary>
        Open = 0,
        /// <summary>The service that runs is the last one; at its summary's end the shop is closed.</summary>
        ClosingAfterService = 1,
        /// <summary>No service opens, the clock holds at 08:00, the crew may rearrange the kitchen.</summary>
        Closed = 2,
    }
}
