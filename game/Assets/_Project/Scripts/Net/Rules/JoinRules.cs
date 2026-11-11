using System.Text;

namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Spec 047, server side, pure: who may join. The client sends its build hash and a display name (JoinRequest); the server
    /// compares the hash with its own and counts the players already in (the host's own player counts). Nothing else from the
    /// client is used; the name is only shown, cleaned and cut.
    /// </summary>
    public static class JoinRules
    {
        /// <summary>D36: 16 players per game, the host included.</summary>
        public const int MaxPlayers = 16;

        /// <summary>A connection that has not sent its join request after this long is refused (NoAnswer).</summary>
        public const float AnswerSeconds = 10f;

        public const int MaxNameLength = 24;

        /// <summary>Server: None to admit the client, else the reason it is refused (the version first, then the places).</summary>
        public static DisconnectReason Decide(string clientBuild, string serverBuild, int playersIn, int maxPlayers)
        {
            if (string.IsNullOrEmpty(clientBuild) || clientBuild != serverBuild) return DisconnectReason.VersionMismatch;
            if (playersIn >= (maxPlayers > 0 ? maxPlayers : MaxPlayers)) return DisconnectReason.ServerFull;
            return DisconnectReason.None;
        }

        /// <summary>
        /// Follow-up 047, server: as <see cref="Decide(string,string,int,int)"/>, but a player the host kicked earlier in this session
        /// (<paramref name="kickedBefore"/>: SessionBans, the transport's Steam id or address) is refused first, as KickedBefore.
        /// </summary>
        public static DisconnectReason Decide(string clientBuild, string serverBuild, int playersIn, int maxPlayers, bool kickedBefore) =>
            kickedBefore ? DisconnectReason.KickedBefore : Decide(clientBuild, serverBuild, playersIn, maxPlayers);

        /// <summary>
        /// The name other players see: control and format characters (bidi controls, zero-width) dropped, '&lt;' and '&gt;'
        /// dropped (the labels show plain text; this is defence in depth against rich-text tags), at most one combining mark per
        /// letter, spaces folded, cut to <see cref="MaxNameLength"/>; an empty result becomes "Cook n" (n = the connection's id + 1).
        /// </summary>
        public static string CleanName(string raw, int clientId)
        {
            var sb = new StringBuilder(MaxNameLength);
            bool space = false, mark = true;
            if (raw != null)
            {
                foreach (char c in raw)
                {
                    if (sb.Length >= MaxNameLength) break;
                    if (char.IsControl(c) || char.IsSurrogate(c) || c == '<' || c == '>' || IsFormat(c)) continue;
                    // At most one combining mark per letter (stacked marks draw over the other rows), none at the start.
                    bool isMark = IsMark(c);
                    if (isMark && mark) continue;
                    mark = isMark;
                    if (char.IsWhiteSpace(c))
                    {
                        space = sb.Length > 0;
                        continue;
                    }
                    if (space && sb.Length < MaxNameLength - 1) sb.Append(' ');
                    space = false;
                    sb.Append(c);
                }
            }
            return sb.Length > 0 ? sb.ToString() : "Cook " + (clientId + 1);
        }

        /// <summary>
        /// Unicode format characters (bidi controls, zero-width spaces and joiners, the BOM). The bidi and zero-width ranges are
        /// also named by value, in case the runtime's Unicode tables predate them (the isolates U+2066..2069 came in Unicode 6.3).
        /// </summary>
        private static bool IsFormat(char c)
        {
            if (c >= 0x200B && c <= 0x200F) return true;
            if (c >= 0x202A && c <= 0x202E) return true;
            if (c >= 0x2060 && c <= 0x206F) return true;
            return char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format;
        }

        private static bool IsMark(char c)
        {
            System.Globalization.UnicodeCategory category = char.GetUnicodeCategory(c);
            return category == System.Globalization.UnicodeCategory.NonSpacingMark
                || category == System.Globalization.UnicodeCategory.SpacingCombiningMark
                || category == System.Globalization.UnicodeCategory.EnclosingMark;
        }

        /// <summary>Only the server (the host) kicks, and never its own player.</summary>
        public static bool CanKick(bool asServer, bool targetIsHost) => asServer && !targetIsHost;
    }
}
