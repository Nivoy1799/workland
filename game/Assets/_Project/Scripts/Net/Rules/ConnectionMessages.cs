namespace Worktown.Game.Net.Rules
{
    /// <summary>Spec 047, pure: the words of the loading and error screens.</summary>
    public static class ConnectionMessages
    {
        public static string Title(DisconnectReason reason)
        {
            switch (reason)
            {
                case DisconnectReason.VersionMismatch: return "Version mismatch";
                case DisconnectReason.ServerFull: return "The shop is full";
                case DisconnectReason.Kicked: return "Removed from the shop";
                case DisconnectReason.NoAnswer: return "No answer";
                case DisconnectReason.ConnectionLost: return "Connection lost";
                case DisconnectReason.Unreachable: return "Could not connect";
                case DisconnectReason.HostClosed: return "The shop closed";
                case DisconnectReason.Left: return "You left the shop";
                default: return string.Empty;
            }
        }

        /// <summary>The sentence under the title. <paramref name="detail"/>: the builds (version), the places (full), the target
        /// (unreachable); may be empty.</summary>
        public static string Text(DisconnectReason reason, string detail)
        {
            bool has = !string.IsNullOrEmpty(detail);
            switch (reason)
            {
                case DisconnectReason.VersionMismatch:
                    return "You and the host run different versions of the game" + (has ? " (" + detail + ")" : string.Empty)
                        + ". Both need the same version: update and try again.";
                case DisconnectReason.ServerFull:
                    return "All " + (has ? detail : JoinRules.MaxPlayers.ToString()) + " places are taken. Try again later.";
                case DisconnectReason.Kicked:
                    return "The host removed you from the shop.";
                case DisconnectReason.NoAnswer:
                    return "The host did not get an answer from this game in time. Try again.";
                case DisconnectReason.ConnectionLost:
                    return "The connection to the host was lost and rejoining did not work within "
                        + JoinFlow.RejoinWindowSeconds.ToString("0") + " s.";
                case DisconnectReason.Unreachable:
                    return "Nobody answered" + (has ? " at " + detail : string.Empty) + ". Check the address and that the host is running.";
                case DisconnectReason.HostClosed:
                    return "The host closed the shop.";
                default:
                    return string.Empty;
            }
        }

        /// <summary>The rejoin line, with whole seconds left.</summary>
        public static string Rejoining(float secondsLeft) =>
            "Connection lost. Rejoining... " + (int)System.Math.Ceiling(System.Math.Max(0f, secondsLeft)) + " s";

        /// <summary>The loading line for a stage.</summary>
        public static string Stage(JoinStage stage, string target)
        {
            switch (stage)
            {
                case JoinStage.Connecting: return "Connecting to " + target + "...";
                case JoinStage.CheckingVersion: return "Checking the version...";
                case JoinStage.LoadingShop: return "Loading the shop...";
                default: return "Ready";
            }
        }
    }
}
