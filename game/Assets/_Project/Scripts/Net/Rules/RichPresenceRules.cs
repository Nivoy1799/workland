namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Spec 047, pure: the Steam rich presence of a host. Its "connect" value is what Steam hands a friend's game (the Join Game
    /// button of the friends list, an invite): the same <c>-steam id</c> flag the command line takes (LaunchArgs), so a game that
    /// Steam starts for the join goes straight in, and a running game parses it here.
    /// </summary>
    public static class RichPresenceRules
    {
        public const string ConnectKey = "connect";
        public const string StatusKey = "status";

        public static string ConnectString(ulong hostSteamId) => "-steam " + hostSteamId;

        /// <summary>The host's SteamID64 from a connect string ("-steam 7656..."; anything else around it is ignored).</summary>
        public static bool TryParseConnect(string connect, out ulong hostSteamId)
        {
            hostSteamId = 0;
            if (string.IsNullOrWhiteSpace(connect)) return false;
            string[] parts = connect.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i + 1 < parts.Length; i++)
                if (parts[i] == "-steam" && ulong.TryParse(parts[i + 1], out hostSteamId) && hostSteamId != 0) return true;
            hostSteamId = 0;
            return false;
        }

        /// <summary>The status line friends see: "Running a shop (3/16)".</summary>
        public static string Status(int players, int maxPlayers) => "Running a shop (" + players + "/" + maxPlayers + ")";
    }
}
