namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Follow-up 047, pure: a join that arrives while this game hosts a shop (Steam's Join Game or an invite, a join by address) asks
    /// first, "Leave your shop? Your guests will be disconnected." (Leave / Stay). A guest leaves its shop and joins without asking; a
    /// shop that is already closing joins once it is shut; a dedicated server ignores joins.
    /// </summary>
    public static class LeaveShopRules
    {
        public const string Question = "Leave your shop?";
        public const string Warning = "Your guests will be disconnected.";
        public const string LeaveLabel = "Leave";
        public const string StayLabel = "Stay";

        /// <param name="dedicated">A dedicated server (<c>-server</c>, or a server build): nobody plays here.</param>
        /// <param name="closing">The own shop is already closing (the host left, quit or stalled): nobody to ask.</param>
        /// <param name="serverStarted">This process runs the server.</param>
        /// <param name="clientStarted">This process has its own client (a host's player, or a guest).</param>
        public static JoinChoice Decide(bool dedicated, bool closing, bool serverStarted, bool clientStarted)
        {
            if (dedicated) return JoinChoice.Ignore;
            if (closing) return JoinChoice.JoinWhenClosed;
            if (serverStarted) return JoinChoice.AskFirst;
            return clientStarted ? JoinChoice.LeaveThenJoin : JoinChoice.JoinNow;
        }

        /// <summary>A waiting question still stands only while this game hosts and is not closing (the shop may close, or stall,
        /// before the answer).</summary>
        public static bool StillAsking(bool hasQuestion, bool dedicated, bool closing, bool serverStarted, bool clientStarted) =>
            hasQuestion && Decide(dedicated, closing, serverStarted, clientStarted) == JoinChoice.AskFirst;
    }
}
