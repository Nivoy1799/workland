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

        /// <summary>Follow-up 2026-11-11 evening: <paramref name="button"/>'s bit in a pad state mask (0 for None).</summary>
        public static uint PadBit(PadButton button) => button == PadButton.None ? 0u : 1u << (int)button;

        /// <summary>
        /// Follow-up 2026-11-11 evening: one frame of a gamepad capture over a pad state (<paramref name="held"/>: the buttons down now,
        /// <paramref name="pressed"/>: the ones pressed this frame, as <see cref="PadBit"/> masks; the real pad's or a tour's simulated
        /// one). <paramref name="releasedSince"/> becomes true once every button was up since the capture began; then, from a later frame
        /// than <paramref name="beganFrame"/>, the first pressed button (in PadButton order) is the new binding. None: keep waiting
        /// (also with no pad: <paramref name="padPresent"/> false).
        /// </summary>
        public static PadButton PadCapture(int beganFrame, int frame, bool padPresent, uint held, uint pressed, ref bool releasedSince)
        {
            if (!padPresent) return PadButton.None;
            if (!releasedSince) releasedSince = held == 0u;
            if (!CaptureReads(beganFrame, frame, true, releasedSince)) return PadButton.None;
            for (PadButton button = PadButton.South; button <= PadButton.Select; button++)
                if ((pressed & PadBit(button)) != 0u) return button;
            return PadButton.None;
        }

        /// <summary>
        /// Follow-up 2026-11-11: the cursor locks again when the last open menu closes (<paramref name="anyMenuOpen"/> false), but only
        /// while playing (<paramref name="playing"/>: the player's input is active). On the title screen it stays free.
        /// </summary>
        public static bool LocksOnClose(bool playing, bool anyMenuOpen) => playing && !anyMenuOpen;

        /// <summary>
        /// Follow-up 2026-11-11: Esc or Alt (<paramref name="escOrAlt"/>) frees the cursor while playing, except on the frame the last
        /// menu closed (<paramref name="lastMenuClosedFrame"/> equals <paramref name="frame"/>): that Esc closed the settings over the
        /// shop and locked the cursor again, so the same press must not free it at once (it needed one more click before).
        /// </summary>
        public static bool FreesCursor(bool escOrAlt, int lastMenuClosedFrame, int frame) => escOrAlt && lastMenuClosedFrame != frame;
    }
}
