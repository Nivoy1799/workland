namespace Worktown.Game.Npc
{
    /// <summary>
    /// Spec 038, pulled out in 038b (pure, tested): the host's own tutorial as its <see cref="CustomerService"/> sees it. A host is server
    /// and client in one process, so the local TutorialRunner tells it directly (no RPC); a server build ignores everything here.
    /// <see cref="Running"/> makes the guests calm (with Tutorial.TutorialRules.HostCalms) and a start scripts the first guests again
    /// (<see cref="Orders"/>). 038b: the flag never goes stale within a session: when the server starts, the runner's state wins
    /// (<see cref="OnServerStarted"/>), since the tutorial may have finished while this process was a remote client, or been replayed from
    /// the menu before hosting.
    /// </summary>
    public sealed class HostTutorial
    {
        private readonly bool _serverBuild;

        public HostTutorial(bool serverBuild)
        {
            _serverBuild = serverBuild;
        }

        /// <summary>The host's local tutorial runs (never on a server build).</summary>
        public bool Running { get; private set; }

        /// <summary>The scripted first guests of the current run (null before the first start).</summary>
        public TutorialOrders Orders { get; private set; }

        /// <summary>
        /// The local tutorial runs or not. A start (from not running) scripts the first guests again; a server build ignores it. True when
        /// <see cref="Running"/> changed.
        /// </summary>
        public bool Set(bool running)
        {
            if (_serverBuild || running == Running) return false;
            if (running) Orders = new TutorialOrders();
            Running = running;
            return true;
        }

        /// <summary>
        /// 038b: the server starts. With a local runner (<paramref name="hasRunner"/>) its state is taken
        /// (Tutorial.TutorialRules.RunsForHost of <paramref name="runnerDecided"/>, <paramref name="runnerRunning"/> and the launch rule
        /// <paramref name="startsOnLaunch"/>); without one (batch mode) the flag stays. True when <see cref="Running"/> changed.
        /// </summary>
        public bool OnServerStarted(bool hasRunner, bool runnerDecided, bool runnerRunning, bool startsOnLaunch)
        {
            if (!hasRunner) return false;
            return Set(Worktown.Game.Tutorial.TutorialRules.RunsForHost(runnerDecided, runnerRunning, startsOnLaunch));
        }

        /// <summary>A new game (the server stopped): while the tutorial runs its first guests are scripted again.</summary>
        public void NewGame()
        {
            if (Running) Orders = new TutorialOrders();
        }
    }
}
