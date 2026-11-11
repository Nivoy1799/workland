namespace Worktown.Game.Settings
{
    /// <summary>
    /// Spec 048 (review), pure: when a key press belongs to the game and when to a menu. While the settings or any other menu is open,
    /// the game's own keys (jump, crouch, the lobby's toggle) do nothing, so a key pressed to rebind never also jumps. A rebind capture
    /// takes input only from a later frame than the one it began on (the Enter or the A press that clicked "rebind" is not the new
    /// key), and a gamepad capture only after every pad button was up once since it began (a held A never binds itself or comes back
    /// as "Reserved").
    /// </summary>
    public static class CaptureGate
    {
        /// <summary>Jump and crouch (and the other one-press game keys) count: no menu or banner is open.</summary>
        public static bool GameKeysLive(bool uiOpen, bool settingsOpen) => !uiOpen && !settingsOpen;

        /// <summary>
        /// The lobby's key (Tab) toggles the lobby: in the shop, the settings closed, and no other menu open (<paramref name="uiOpenCount"/>
        /// counts every open menu; the lobby's own counts once while <paramref name="lobbyShown"/>).
        /// </summary>
        public static bool LobbyToggleLive(bool inGame, bool settingsOpen, int uiOpenCount, bool lobbyShown) =>
            inGame && !settingsOpen && uiOpenCount - (lobbyShown ? 1 : 0) <= 0;

        /// <summary>
        /// A capture that began on frame <paramref name="beganFrame"/> reads input on <paramref name="frame"/>: a later frame, and for the
        /// gamepad once the buttons were all released since it began (<paramref name="padReleasedSince"/>).
        /// </summary>
        public static bool CaptureReads(int beganFrame, int frame, bool pad, bool padReleasedSince) =>
            frame > beganFrame && (!pad || padReleasedSince);
    }
}
