namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 031, pure: holding the use input on a sauce dispenser. The look follows the button at once (<see cref="Held"/>: the
    /// ribbon flows from the press to the release, docs/feel.md 1); the requests go out as a press/release pair
    /// (RequestStation start and stop), each only when the request interval allows it (<c>canSend</c> in <see cref="Poll"/>),
    /// so neither is ever swallowed by the server's rate limit. The server measures the time between the two arrivals; a
    /// release that comes before the press could be sent still sends both, in order (the server then sees a short squeeze: a
    /// dash of sauce). A new press on the same dispenser while its release still waits continues the same squeeze.
    /// 031b (review of 031):
    /// - how late the start went out is recorded, and the stop waits as long after the release (<see cref="Lag"/>), so the
    ///   server's measured time equals the hold;
    /// - a press on another dispenser while the first stop still waits is queued (its start goes out after that stop), never
    ///   dropped.
    /// Times are in seconds on any clock that only goes forward (the interactor's unscaled time); without them (0) it behaves as
    /// without the lag.
    /// </summary>
    public sealed class SqueezeHold
    {
        public enum Send : byte { None, Start, Stop }

        private struct Squeeze
        {
            public int Board;
            public int Dispenser;
            public bool Held;
            public bool Started;
            public double PressTime;
            public double ReleaseTime;
            public double Lag;
        }

        private Squeeze _head;
        private Squeeze _next;
        private bool _hasHead;
        private bool _hasNext;

        /// <summary>The button is held on a dispenser (the predicted look is on).</summary>
        public bool Held => _hasNext ? _next.Held : _hasHead && _head.Held;
        /// <summary>A start went out and its stop has not.</summary>
        public bool Started => _hasHead && _head.Started;
        /// <summary>The dispenser held (or the last one, while its stop waits); -1 before the first press.</summary>
        public int Dispenser { get; private set; } = -1;
        /// <summary>The board of that dispenser (station index).</summary>
        public int Board { get; private set; } = -1;
        /// <summary>The board of the start or stop <see cref="Poll"/> returned last.</summary>
        public int SendBoard { get; private set; } = -1;
        /// <summary>The dispenser of the start or stop <see cref="Poll"/> returned last.</summary>
        public int SendDispenser { get; private set; } = -1;
        /// <summary>s the running squeeze's start went out after its press (its stop waits as long after the release).</summary>
        public double Lag => _hasHead && _head.Started ? _head.Lag : 0d;
        /// <summary>A press on another dispenser waits for the first one's stop.</summary>
        public bool Queued => _hasNext;

        /// <summary>Nothing held and nothing waiting to be sent.</summary>
        public bool Idle => !_hasHead;

        /// <summary>Dispenser <paramref name="dispenser"/> of board <paramref name="board"/> is held, running or queued here.</summary>
        public bool Involves(int board, int dispenser) =>
            (_hasHead && _head.Board == board && _head.Dispenser == dispenser)
            || (_hasNext && _next.Board == board && _next.Dispenser == dispenser);

        /// <summary>
        /// The press on dispenser <paramref name="dispenser"/> of board <paramref name="board"/> at <paramref name="now"/>. On the
        /// same dispenser as the last squeeze (its stop not sent yet) it continues it; on another one while a squeeze still runs
        /// it is queued behind it (the running one is let go). Always true.
        /// </summary>
        public bool Press(int board, int dispenser, double now = 0d)
        {
            Board = board;
            Dispenser = dispenser;
            if (_hasNext)
            {
                if (_next.Board == board && _next.Dispenser == dispenser) _next.Held = true;
                else _next = Fresh(board, dispenser, now);
                return true;
            }
            if (_hasHead)
            {
                if (_head.Board == board && _head.Dispenser == dispenser)
                {
                    _head.Held = true;
                    return true;
                }
                if (_head.Held) LetGo(ref _head, now);
                _next = Fresh(board, dispenser, now);
                _hasNext = true;
                return true;
            }
            _head = Fresh(board, dispenser, now);
            _hasHead = true;
            return true;
        }

        /// <summary>The release at <paramref name="now"/> (or the aim left the dispenser, or the view changed).</summary>
        public void Release(double now = 0d)
        {
            if (_hasNext)
            {
                if (_next.Held) LetGo(ref _next, now);
                return;
            }
            if (_hasHead && _head.Held) LetGo(ref _head, now);
        }

        /// <summary>
        /// Every frame at <paramref name="now"/>: what to send now (for <see cref="SendBoard"/> / <see cref="SendDispenser"/>). A
        /// start once pressed; a stop once released and <see cref="Lag"/> later; then the queued press's start.
        /// </summary>
        public Send Poll(bool canSend, double now = 0d)
        {
            if (!canSend || !_hasHead) return Send.None;
            if (!_head.Started)
            {
                _head.Started = true;
                _head.Lag = now > _head.PressTime ? now - _head.PressTime : 0d;
                SendBoard = _head.Board;
                SendDispenser = _head.Dispenser;
                return Send.Start;
            }
            if (!_head.Held && now >= _head.ReleaseTime + _head.Lag)
            {
                SendBoard = _head.Board;
                SendDispenser = _head.Dispenser;
                _head = _next;
                _hasHead = _hasNext;
                _hasNext = false;
                _next = default;
                return Send.Stop;
            }
            return Send.None;
        }

        /// <summary>Forget everything (shutdown). A start already sent is stopped by the server after its auto-stop.</summary>
        public void Reset()
        {
            _hasHead = false;
            _hasNext = false;
            _head = default;
            _next = default;
        }

        private static Squeeze Fresh(int board, int dispenser, double now) =>
            new Squeeze { Board = board, Dispenser = dispenser, Held = true, PressTime = now };

        private static void LetGo(ref Squeeze squeeze, double now)
        {
            squeeze.Held = false;
            squeeze.ReleaseTime = now;
        }
    }
}
