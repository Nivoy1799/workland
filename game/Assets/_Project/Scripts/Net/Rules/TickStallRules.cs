namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Spec 049, pure: when a server's network tick counts as stalled. The host freeze seen in two cook tours (visual039b, visual047c):
    /// FishNet's tick stopped (no more kitchen events, clients timed out and could not rejoin) while the main thread kept drawing, and a
    /// native crash followed minutes later. Stalled: the server runs, no tick for more than <see cref="StallSeconds"/> of real time, and
    /// at least <see cref="MinFrames"/> frames were drawn since the last tick (one long frame, a scene load or a debugger pause is not
    /// a stall: the tick catches up in the next frame). The watchdog then logs it loudly and closes the shop with a notice.
    /// </summary>
    public static class TickStallRules
    {
        /// <summary>Over 2 s without a tick (the server ticks 30 times a second).</summary>
        public const double StallSeconds = 2d;
        /// <summary>Frames drawn since the last tick before it counts (a single hitch frame never does).</summary>
        public const int MinFrames = 10;

        public static bool IsStalled(bool serverStarted, double secondsSinceTick, int framesSinceTick) =>
            serverStarted && secondsSinceTick > StallSeconds && framesSinceTick >= MinFrames;

        /// <summary>049 review: the exit code of a dedicated server that quit after a stall (non-zero: its supervisor restarts or reports it).</summary>
        public const int DedicatedExitCode = 3;

        /// <summary>
        /// 049 review: after the stall close, a dedicated server (<c>-server</c>, or a server build) quits with
        /// <see cref="DedicatedExitCode"/>: nobody plays on it to read the error screen. A host stays on its "The shop stopped" screen,
        /// and a <c>-stall-test</c> run quits by itself (StallTestRunner decides its exit code).
        /// </summary>
        public static bool QuitsAfterStall(bool dedicatedServer, bool stallTest) => dedicatedServer && !stallTest;

        /// <summary>The loud log line.</summary>
        public static string Describe(double secondsSinceTick, int framesSinceTick, uint lastTick, int clients) =>
            "HOST TICK STALL: no network tick for " + secondsSinceTick.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
            + " s while the game drew " + framesSinceTick + " frames (last tick " + lastTick + ", " + clients
            + " connections). Closing the shop with a notice to every player.";
    }
}
