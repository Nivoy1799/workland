namespace Worktown.Game.Net.Rules
{
    /// <summary>Spec 047: what the screen shows for the connection (JoinFlow).</summary>
    public enum JoinState
    {
        /// <summary>The title screen: not connected.</summary>
        Menu,
        /// <summary>The loading screen: hosting or joining, not in the shop yet.</summary>
        Loading,
        /// <summary>In the shop (the own player spawned).</summary>
        InGame,
        /// <summary>The connection dropped: trying to get back in for JoinFlow.RejoinWindowSeconds.</summary>
        Rejoining,
        /// <summary>An error screen (JoinFlow.Reason) with a way back to the menu.</summary>
        Message
    }
}
