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

        /// <summary>
        /// Follow-up 047: the friends-list line. Steam shows <see cref="DisplayKey"/>'s token from the app's rich-presence localization
        /// (<see cref="LocalizationFile"/>, uploaded in Steamworks for the real app id; not possible with the test app 480), with
        /// %players% filled from <see cref="PlayersKey"/>. Without the upload Steam shows only the game's name.
        /// </summary>
        public const string DisplayKey = "steam_display";
        public const string PlayersKey = "players";
        public const string HostingToken = "#Status_Hosting";
        public const string PlayingToken = "#Status_Playing";

        /// <summary>The localization file, relative to the Unity project folder (game/), outside Assets (Unity does not import it).</summary>
        public const string LocalizationFile = "Steam/rich_presence_localization.vdf";

        /// <summary>The <see cref="StatusKey"/> text of a guest ("View game info" in Steam).</summary>
        public const string PlayingStatus = "Cooking in a friend's shop";

        /// <summary>The steam_display token: hosting a shop, else playing in someone's.</summary>
        public static string DisplayToken(bool hosting) => hosting ? HostingToken : PlayingToken;

        /// <summary>The %players% value (whole number, invariant culture).</summary>
        public static string PlayersValue(int players) =>
            System.Math.Max(0, players).ToString(System.Globalization.CultureInfo.InvariantCulture);

        public static string ConnectString(ulong hostSteamId) => "-steam " + hostSteamId;

        /// <summary>The host's SteamID64 from a connect string ("-steam 7656..."; anything else around it is ignored).</summary>
        public static bool TryParseConnect(string connect, out ulong hostSteamId)
        {
            hostSteamId = 0;
            if (string.IsNullOrWhiteSpace(connect)) return false;
            string[] parts = connect.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i + 1 < parts.Length; i++)
                if (parts[i] == "-steam" && TryParseSteamId(parts[i + 1], out hostSteamId)) return true;
            hostSteamId = 0;
            return false;
        }

        /// <summary>A SteamID64: digits only (no sign, no spaces), fits a ulong, not 0. Also the check of LaunchArgs' <c>-steam</c>.</summary>
        public static bool TryParseSteamId(string text, out ulong steamId)
        {
            steamId = 0;
            if (string.IsNullOrEmpty(text)) return false;
            if (!ulong.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out steamId))
                return false;
            return steamId != 0;
        }

        /// <summary>The status line friends see: "Running a shop (3/16)".</summary>
        public static string Status(int players, int maxPlayers) => "Running a shop (" + players + "/" + maxPlayers + ")";
    }
}
