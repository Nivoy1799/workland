namespace Worktown.Game.Net.Rules
{
    /// <summary>Spec 047: how a session was started.</summary>
    public enum JoinTargetKind
    {
        /// <summary>This PC hosts (server plus the own player); nothing to rejoin.</summary>
        Host,
        /// <summary>Joined by address over Tugboat (testing, LAN).</summary>
        Address,
        /// <summary>Joined a friend over Steam (by SteamID64).</summary>
        Steam
    }
}
