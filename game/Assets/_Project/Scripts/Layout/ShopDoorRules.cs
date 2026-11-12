namespace Worktown.Game.Layout
{
    /// <summary>
    /// Spec 062b, pure: the shop's door. A press on the door sign: Open becomes ClosingAfterService during a service and Closed between
    /// services; ClosingAfterService goes back to Open; Closed becomes Open (the next service starts at once). At a service summary's end
    /// ClosingAfterService becomes Closed (<see cref="AfterSummary"/>). While Closed no service opens and no hour passes
    /// (<see cref="HoldsClock"/>: no demand, no upkeep, no rent: time runs only while playing). A loaded game starts Open.
    /// </summary>
    public static class ShopDoorRules
    {
        public const string OpenSign = "OPEN";
        public const string ClosingSign = "Closing after this service";
        public const string ClosedSign = "CLOSED";
        public const string ClosedLine = "The shop is closed: rearrange the kitchen (B)";
        public const string ClosingLine = "The shop closes after this service";
        public const string OpenedLine = "The shop is open";

        /// <summary>The state after a press (<paramref name="press"/>) with a service open now or not.</summary>
        public static ShopDoorState Next(ShopDoorState state, bool serviceOpen, bool press)
        {
            if (!press) return state;
            switch (state)
            {
                case ShopDoorState.Open: return serviceOpen ? ShopDoorState.ClosingAfterService : ShopDoorState.Closed;
                case ShopDoorState.ClosingAfterService: return ShopDoorState.Open;
                default: return ShopDoorState.Open;
            }
        }

        /// <summary>The state once a service's summary is over: closing becomes closed; the others stay.</summary>
        public static ShopDoorState AfterSummary(ShopDoorState state) => state == ShopDoorState.ClosingAfterService ? ShopDoorState.Closed : state;

        /// <summary>Whether a new service may open: not while the shop is closed.</summary>
        public static bool MayOpenService(ShopDoorState state) => state != ShopDoorState.Closed;

        /// <summary>Whether the clock stands still: closed and no service open (a service that runs ends first).</summary>
        public static bool HoldsClock(ShopDoorState state, bool serviceOpen) => state == ShopDoorState.Closed && !serviceOpen;

        /// <summary>Whether build mode may be entered: only while the shop is closed.</summary>
        public static bool AllowsBuild(ShopDoorState state) => state == ShopDoorState.Closed;

        /// <summary>What the sign reads.</summary>
        public static string SignText(ShopDoorState state) =>
            state == ShopDoorState.Closed ? ClosedSign : state == ShopDoorState.ClosingAfterService ? ClosingSign : OpenSign;

        /// <summary>What a press does, for the prompt ("Click · close after this service").</summary>
        public static string PressWords(ShopDoorState state, bool serviceOpen)
        {
            switch (Next(state, serviceOpen, true))
            {
                case ShopDoorState.ClosingAfterService: return "close after this service";
                case ShopDoorState.Closed: return "close the shop";
                default: return state == ShopDoorState.ClosingAfterService ? "keep the shop open" : "open the shop";
            }
        }

        /// <summary>The serve feed's line when the door changed to <paramref name="state"/>.</summary>
        public static string FeedLine(ShopDoorState state) =>
            state == ShopDoorState.Closed ? ClosedLine : state == ShopDoorState.ClosingAfterService ? ClosingLine : OpenedLine;
    }
}
