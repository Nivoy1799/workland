namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Spec 047, client side, pure: the connection as the player sees it, from the title screen through the loading screen into
    /// the shop, and out again with a clear reason. NetBootstrap feeds it the transport's and FishNet's events with the time and
    /// asks <see cref="Tick"/> when to try a rejoin; the menu views only read it.
    /// Rules: a notice from the server (refused, kicked, the shop closed) wins over everything when the connection then stops, and
    /// never starts a rejoin; leaving on purpose goes back to the menu; a drop in the shop of a joined game (not the host's own)
    /// starts the rejoin: a new attempt <see cref="RejoinRetrySeconds"/> after the last one failed, for
    /// <see cref="RejoinWindowSeconds"/> from the drop, then ConnectionLost.
    /// </summary>
    public sealed class JoinFlow
    {
        public const float RejoinWindowSeconds = 30f;
        public const float RejoinRetrySeconds = 3f;
        /// <summary>The first attempt comes this soon after the drop.</summary>
        public const float RejoinFirstSeconds = 1f;
        /// <summary>An attempt still under way this long after the window ends is given up.</summary>
        public const float AttemptGraceSeconds = 10f;

        private DisconnectReason _notice = DisconnectReason.None;
        private string _noticeDetail = string.Empty;
        private bool _leaving;
        private double _nextAttempt;

        public JoinState State { get; private set; } = JoinState.Menu;
        public JoinStage Stage { get; private set; } = JoinStage.Connecting;
        /// <summary>Why the last session ended (the error screen); None while all is well.</summary>
        public DisconnectReason Reason { get; private set; } = DisconnectReason.None;
        public string Detail { get; private set; } = string.Empty;
        /// <summary>The last session's target (null before the first).</summary>
        public JoinTarget Target { get; private set; }
        /// <summary>Until when the rejoin tries (Rejoining only).</summary>
        public double RejoinUntil { get; private set; }
        /// <summary>A rejoin attempt is under way (Rejoining only).</summary>
        public bool Attempting { get; private set; }

        /// <summary>The loading bar, 0 to 1.</summary>
        public float Progress
        {
            get
            {
                switch (Stage)
                {
                    case JoinStage.Connecting: return 0.15f;
                    case JoinStage.CheckingVersion: return 0.4f;
                    case JoinStage.LoadingShop: return 0.75f;
                    default: return 1f;
                }
            }
        }

        /// <summary>The menu, the loading and the error screens cover the shop; in the shop only the game's own UI shows.</summary>
        public bool ShowsMenu => State != JoinState.InGame;

        public float RejoinSecondsLeft(double now) => State == JoinState.Rejoining ? (float)System.Math.Max(0d, RejoinUntil - now) : 0f;

        public void BeginHost() => Begin(JoinTarget.Host());

        public void BeginJoin(JoinTarget target) => Begin(target ?? JoinTarget.Host());

        private void Begin(JoinTarget target)
        {
            Target = target;
            State = JoinState.Loading;
            Stage = JoinStage.Connecting;
            Reason = DisconnectReason.None;
            Detail = string.Empty;
            _notice = DisconnectReason.None;
            _noticeDetail = string.Empty;
            _leaving = false;
            Attempting = false;
        }

        /// <summary>The transport reached the host (the join request goes out now).</summary>
        public void TransportStarted()
        {
            if (State == JoinState.Loading || State == JoinState.Rejoining) Stage = JoinStage.CheckingVersion;
        }

        /// <summary>The host admitted this client (FishNet's authentication passed).</summary>
        public void Authenticated()
        {
            if (State == JoinState.Loading || State == JoinState.Rejoining) Stage = JoinStage.LoadingShop;
        }

        /// <summary>The own player spawned: in the shop.</summary>
        public void PlayerSpawned()
        {
            if (State == JoinState.Menu || State == JoinState.Message) return;
            State = JoinState.InGame;
            Stage = JoinStage.Ready;
            Attempting = false;
        }

        /// <summary>The server said why it is about to disconnect this client (a ConnectionNotice).</summary>
        public void Notice(DisconnectReason reason, string detail)
        {
            if (reason == DisconnectReason.None) return;
            _notice = reason;
            _noticeDetail = detail ?? string.Empty;
        }

        /// <summary>
        /// The player leaves (back to the menu). <paramref name="anythingRunning"/> false: nothing to stop, the menu shows at once;
        /// else the menu shows when the connection has stopped.
        /// </summary>
        public void Leave(bool anythingRunning)
        {
            if (anythingRunning && State != JoinState.Menu && State != JoinState.Message)
            {
                _leaving = true;
                return;
            }
            ToMenu();
        }

        /// <summary>The error screen's "back to the menu" (or cancel on the rejoin screen: the caller stops the attempt).</summary>
        public void Dismiss() => ToMenu();

        private void ToMenu()
        {
            State = JoinState.Menu;
            Stage = JoinStage.Connecting;
            Reason = DisconnectReason.None;
            Detail = string.Empty;
            _notice = DisconnectReason.None;
            _noticeDetail = string.Empty;
            _leaving = false;
            Attempting = false;
        }

        /// <summary>The client connection stopped at <paramref name="now"/> (seconds).</summary>
        public void TransportStopped(double now)
        {
            if (State == JoinState.Menu || State == JoinState.Message) return;
            if (_leaving)
            {
                ToMenu();
                return;
            }
            if (_notice != DisconnectReason.None)
            {
                Show(_notice, _noticeDetail);
                return;
            }
            switch (State)
            {
                case JoinState.InGame:
                    if (Target != null && Target.Kind != JoinTargetKind.Host)
                    {
                        State = JoinState.Rejoining;
                        Stage = JoinStage.Connecting;
                        RejoinUntil = now + RejoinWindowSeconds;
                        _nextAttempt = now + RejoinFirstSeconds;
                        Attempting = false;
                    }
                    else
                    {
                        Show(DisconnectReason.ConnectionLost, string.Empty);
                    }
                    break;
                case JoinState.Rejoining:
                    Attempting = false;
                    Stage = JoinStage.Connecting;
                    if (now >= RejoinUntil) Show(DisconnectReason.ConnectionLost, string.Empty);
                    else _nextAttempt = now + RejoinRetrySeconds;
                    break;
                case JoinState.Loading:
                    if (Stage == JoinStage.Connecting)
                        Show(DisconnectReason.Unreachable, Target != null ? Target.Describe() : string.Empty);
                    else
                        Show(DisconnectReason.ConnectionLost, string.Empty);
                    break;
            }
        }

        /// <summary>
        /// Called every frame: true when the caller should connect to <see cref="Target"/> again now (a rejoin attempt). Gives up
        /// with ConnectionLost once the window is over and no attempt is under way.
        /// </summary>
        public bool Tick(double now)
        {
            if (State != JoinState.Rejoining) return false;
            if (Attempting)
            {
                // An attempt that hangs past the window (plus a transport's own connect timeout) ends the rejoin; the caller stops it.
                if (now >= RejoinUntil + AttemptGraceSeconds) Show(DisconnectReason.ConnectionLost, string.Empty);
                return false;
            }
            if (now >= RejoinUntil)
            {
                Show(DisconnectReason.ConnectionLost, string.Empty);
                return false;
            }
            if (now < _nextAttempt) return false;
            Attempting = true;
            return true;
        }

        /// <summary>Starting failed before any connection (no Steam, no transport): the error screen at once.</summary>
        public void Fail(DisconnectReason reason, string detail)
        {
            if (reason == DisconnectReason.None) return;
            Show(reason, detail);
        }

        private void Show(DisconnectReason reason, string detail)
        {
            State = JoinState.Message;
            Reason = reason;
            Detail = detail ?? string.Empty;
            _notice = DisconnectReason.None;
            _noticeDetail = string.Empty;
            _leaving = false;
            Attempting = false;
        }
    }
}
