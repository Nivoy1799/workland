namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Follow-up 047, pure: a join that arrives while this game hosts a shop (Steam's Join Game or an invite, a join by address) asks
    /// first, "Leave your shop? Your guests will be disconnected." (Leave / Stay). A guest leaves its shop and joins without asking; a
    /// dedicated server ignores joins.
    /// </summary>
    public static class LeaveShopRules
    {
        public const string Question = "Leave your shop?";
        public const string Warning = "Your guests will be disconnected.";
        public const string LeaveLabel = "Leave";
        public const string StayLabel = "Stay";

        /// <param name="serverStarted">This process runs the server (a host or a dedicated server).</param>
        /// <param name="clientStarted">This process has its own client (a host's player, or a guest).</param>
        public static JoinChoice Decide(bool serverStarted, bool clientStarted)
        {
            if (serverStarted) return clientStarted ? JoinChoice.AskFirst : JoinChoice.Ignore;
            return clientStarted ? JoinChoice.LeaveThenJoin : JoinChoice.JoinNow;
        }

        /// <summary>A waiting question still stands only while this game hosts (the shop may close, or stall, before the answer).</summary>
        public static bool StillAsking(bool hasQuestion, bool serverStarted, bool clientStarted) =>
            hasQuestion && Decide(serverStarted, clientStarted) == JoinChoice.AskFirst;
    }
}
