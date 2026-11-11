namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Follow-up 047, pure: quitting the game while hosting (Alt-F4, the window's close button, a normal quit). Unity asks
    /// Application.wantsToQuit first; the host then sends every guest the HostClosed notice (JoinAuthenticator.NotifyClosing) and holds
    /// the quit for <see cref="NoticeSeconds"/> while it goes out (NetBootstrap's CloseSoon), then quits for real. At most
    /// <see cref="LimitSeconds"/> in all, so a broken close never keeps the game open. A crash sends nothing: the guests then show
    /// "Connection lost" after FishNet's remote timeout (60 s) and try to rejoin for 30 s.
    /// </summary>
    public static class HostQuitRules
    {
        /// <summary>The close's own wait (NetBootstrap.CloseSoon: the notice survives a resend over the Steam relay).</summary>
        public const float NoticeSeconds = 1f;

        /// <summary>The longest the quit is held, whatever happens.</summary>
        public const float LimitSeconds = 2f;

        /// <summary>
        /// True: hold this quit (send the notice first). Only a host (not a dedicated server) with guests that has not already let the
        /// quit through; never in the Editor (leaving Play mode must not wait), never in a scripted run, never on a dedicated server
        /// (tours, bots, the stall and crash tests and the dedicated server's stall exit quit with their own exit code, which a second
        /// Application.Quit would lose).
        /// </summary>
        public static bool HoldsQuit(bool serverStarted, int remoteGuests, bool quitLetThrough, bool editor, bool scripted, bool dedicated) =>
            serverStarted && remoteGuests > 0 && !quitLetThrough && !editor && !scripted && !dedicated;

        /// <summary>A quit asked again while one is held: still held, unless the hold has lasted <see cref="LimitSeconds"/> (the
        /// coroutine that ends it may have died).</summary>
        public static bool StillHeld(float heldSeconds) => heldSeconds < LimitSeconds;

        /// <summary>The held quit goes through: the close finished (the server stopped), or the limit passed.</summary>
        public static bool QuitNow(bool closing, bool serverStarted, float heldSeconds) =>
            heldSeconds >= LimitSeconds || (!closing && !serverStarted);
    }
}
