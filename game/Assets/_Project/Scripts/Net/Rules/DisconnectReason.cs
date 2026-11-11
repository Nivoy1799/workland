namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Spec 047: why a client is not (or no longer) in a game, for the clear error screens. The server sends the first ones in a
    /// ConnectionNotice before it disconnects the client; the client finds the others itself. Sent as a byte: never renumber.
    /// </summary>
    public enum DisconnectReason : byte
    {
        /// <summary>Nothing went wrong (admitted, or still in the game).</summary>
        None = 0,
        /// <summary>The player left (or closed the shop) themselves.</summary>
        Left = 1,
        /// <summary>Server: the client's build hash is not the host's.</summary>
        VersionMismatch = 2,
        /// <summary>Server: every place (JoinRules.MaxPlayers, D36) is taken.</summary>
        ServerFull = 3,
        /// <summary>Server: the host removed the player.</summary>
        Kicked = 4,
        /// <summary>Server: the client never sent its join request (JoinRules.AnswerSeconds).</summary>
        NoAnswer = 5,
        /// <summary>Client: the connection dropped and the 30 s rejoin did not get back in.</summary>
        ConnectionLost = 6,
        /// <summary>Client: nobody answered at the address (or the Steam id).</summary>
        Unreachable = 7,
        /// <summary>Server: the host closed the shop.</summary>
        HostClosed = 8,
        /// <summary>Spec 049, server: the host's network tick stalled (TickWatchdog), so the host closed the shop.</summary>
        HostStalled = 9,
        /// <summary>Follow-up 047, server: the host kicked this player earlier in this session (SessionBans), so it cannot rejoin until
        /// the shop closes.</summary>
        KickedBefore = 10
    }
}
