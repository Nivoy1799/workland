namespace Worktown.Game.Net.Rules
{
    public enum LaunchMode
    {
        /// <summary>No mode flag: show the debug menu.</summary>
        Menu,
        /// <summary><c>-server</c>: dedicated server, no local player.</summary>
        Server,
        /// <summary><c>-host</c>: server plus a local client (used for the two-PC Steam test).</summary>
        Host,
        /// <summary><c>-connect ip[:port]</c>: client over Tugboat (direct UDP).</summary>
        ClientTugboat,
        /// <summary><c>-steam target</c>: client over Steam (SteamID64 of a host, or the dedicated server).</summary>
        ClientSteam
    }
}
