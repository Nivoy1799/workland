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
                case DisconnectReason.KickedBefore: return "Kicked by the host";
                case DisconnectReason.NoAnswer: return "No answer";
                case DisconnectReason.ConnectionLost: return "Connection lost";
                case DisconnectReason.Unreachable: return "Could not connect";
                case DisconnectReason.HostClosed: return "The shop closed";
                case DisconnectReason.HostStalled: return "The shop stopped";
                case DisconnectReason.Left: return "You left the shop";
                case DisconnectReason.None: return string.Empty;
                default: return Unknown(reason);
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
                case DisconnectReason.KickedBefore:
                    return "The host removed you from this shop earlier, so you cannot rejoin it. You can join again when the host opens a new shop.";
                case DisconnectReason.NoAnswer:
                    return "The host did not get an answer from this game in time. Try again.";
                case DisconnectReason.ConnectionLost:
                    return "The connection to the host was lost and rejoining did not work within "
                        + JoinFlow.RejoinWindowSeconds.ToString("0") + " s.";
                case DisconnectReason.Unreachable:
                    return "Nobody answered" + (has ? " at " + detail : string.Empty) + ". Check the address and that the host is running.";
                case DisconnectReason.HostClosed:
                    return "The host closed the shop.";
                case DisconnectReason.HostStalled:
                    return "The host's game stopped responding on the network, so the shop was closed. A crash log was written on the host's PC.";
                case DisconnectReason.None:
                case DisconnectReason.Left:
                    return string.Empty;
                default:
                    return UnknownText;
            }
        }

        /// <summary>
        /// 049 review: what an unknown reason says, "Disconnected (reason n)". A reason byte from a newer build (the host's notice) is
        /// still shown, with its number, instead of an empty title. ProtocolSurface does not hash enum values, so adding a reason does
        /// not change the build hash: older clients can meet one.
        /// </summary>
        public static string Unknown(DisconnectReason reason) => "Disconnected (reason " + (int)reason + ")";

        /// <summary>The sentence under an unknown reason's title.</summary>
        public const string UnknownText = "The other game ended the connection for a reason this version does not know. The host may run a newer version.";

        /// <summary>
        /// The error screen offers "Try again" (when there is a target): not after a kick or a refused rejoin, a version mismatch or a
        /// closed shop (trying again cannot help there).
        /// </summary>
        public static bool CanRetry(DisconnectReason reason, bool hasTarget) =>
            hasTarget && reason != DisconnectReason.Kicked && reason != DisconnectReason.KickedBefore
            && reason != DisconnectReason.VersionMismatch && reason != DisconnectReason.HostClosed;

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
