namespace Worktown.Game.Net.Rules
{
    /// <summary>Follow-up 047: what a join (a Steam Join Game or invite, a typed address) does while this game is in a session.</summary>
    public enum JoinChoice
    {
        /// <summary>Not in a session: join at once.</summary>
        JoinNow = 0,
        /// <summary>A guest in someone's shop: leave it, then join (nobody else is affected).</summary>
        LeaveThenJoin = 1,
        /// <summary>The host of a shop: ask "Leave your shop?" first (the guests would be disconnected).</summary>
        AskFirst = 2,
        /// <summary>A dedicated server (no player here): never joins anything.</summary>
        Ignore = 3,
        /// <summary>The own shop is already closing: join once it is shut (no question).</summary>
        JoinWhenClosed = 4
    }
}
