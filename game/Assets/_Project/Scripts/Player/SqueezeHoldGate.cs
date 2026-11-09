namespace Worktown.Game.Player
{
    /// <summary>
    /// 031b review (orchestrator decision, option a), pure: third person, E (or the use input) at the assembly board. A squeeze
    /// starts only once the input has been held <see cref="HoldSeconds"/> on an offered dispenser, and its time is measured from
    /// that point (the press goes out then). Let go earlier and it was a tap: E keeps its normal meaning (pick up, put, drop).
    /// 032 review: if the aim leaves the dispenser before the hold time the press does nothing (no E action on the new target);
    /// after it, the squeeze ends. Time comes in from the caller.
    /// </summary>
    public sealed class SqueezeHoldGate
    {
        /// <summary>How long E must be held before a squeeze starts (s).</summary>
        public const float HoldSeconds = 0.2f;

        public enum Step : byte
        {
            /// <summary>Nothing for the squeeze; E (if pressed) acts as usual.</summary>
            None,
            /// <summary>E is held on a dispenser, not long enough yet: E does nothing else.</summary>
            Waiting,
            /// <summary>E was let go (or the aim left) before the hold time: do the normal E action now.</summary>
            Tap,
            /// <summary>The hold time is reached this frame: press the dispenser now.</summary>
            Start,
            /// <summary>The squeeze goes on.</summary>
            Holding,
            /// <summary>032 review: the aim left the dispenser before the hold time: this press does nothing at all.</summary>
            Cancelled,
        }

        private bool _armed;
        private double _pressAt;

        /// <summary>A squeeze is running (from <see cref="Step.Start"/> until the input or the aim lets go).</summary>
        public bool Squeezing { get; private set; }

        /// <summary>E is held on a dispenser and owned by the gate (waiting or squeezing): no other E action.</summary>
        public bool OwnsInput => _armed || Squeezing;

        /// <summary>
        /// One frame: <paramref name="offered"/> the rules offer a squeeze on the dispenser in front, <paramref name="down"/> the input
        /// is held, <paramref name="pressedNow"/> it went down this frame, <paramref name="now"/> the time (s).
        /// </summary>
        public Step Update(bool offered, bool down, bool pressedNow, double now)
        {
            if (Squeezing)
            {
                if (down && offered) return Step.Holding;
                Squeezing = false;
                return Step.None;
            }
            if (_armed)
            {
                if (!offered)
                {
                    _armed = false;
                    return Step.Cancelled;
                }
                if (!down)
                {
                    _armed = false;
                    return Step.Tap;
                }
                if (now - _pressAt < HoldSeconds - 1e-4) return Step.Waiting; // a tolerance: 0.2f is a little over 0.2
                _armed = false;
                Squeezing = true;
                return Step.Start;
            }
            if (pressedNow && down && offered)
            {
                _armed = true;
                _pressAt = now;
                return Step.Waiting;
            }
            return Step.None;
        }

        public void Reset()
        {
            _armed = false;
            Squeezing = false;
        }
    }
}
