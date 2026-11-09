namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 031, pure: holding left click on a sauce dispenser. The look follows the button at once (<see cref="Held"/>: the
    /// ribbon flows from the press to the release, docs/feel.md 1); the requests go out as a press/release pair
    /// (RequestStation start and stop), each only when the request interval allows it (<paramref name="canSend"/> in
    /// <see cref="Poll"/>), so neither is ever swallowed by the server's rate limit. The server measures the time between the
    /// two arrivals; a release that comes before the press could be sent still sends both, in order (the server then sees a
    /// short squeeze: a dash of sauce). A new press while the release still waits continues the same squeeze.
    /// </summary>
    public sealed class SqueezeHold
    {
        public enum Send : byte { None, Start, Stop }

        /// <summary>The button is held on a dispenser (the predicted look is on).</summary>
        public bool Held { get; private set; }
        /// <summary>A start went out and its stop has not.</summary>
        public bool Started { get; private set; }
        /// <summary>The dispenser held (or the last one, while its stop waits); -1 before the first press.</summary>
        public int Dispenser { get; private set; } = -1;
        /// <summary>The board of that dispenser (station index).</summary>
        public int Board { get; private set; } = -1;

        /// <summary>Nothing held and nothing waiting to be sent.</summary>
        public bool Idle => !Held && !Started && !_pressPending;

        private bool _pressPending;

        /// <summary>The press on dispenser <paramref name="dispenser"/> of board <paramref name="board"/>. Ignored while another
        /// dispenser's stop still waits (finish that one first).</summary>
        public bool Press(int board, int dispenser)
        {
            if (Started && (board != Board || dispenser != Dispenser)) return false;
            Held = true;
            _pressPending = !Started;
            Board = board;
            Dispenser = dispenser;
            return true;
        }

        /// <summary>The release (or the aim left the dispenser, or the view changed).</summary>
        public void Release() => Held = false;

        /// <summary>Every frame: what to send now. A start while held, a stop once released after a start.</summary>
        public Send Poll(bool canSend)
        {
            if (!canSend) return Send.None;
            if ((Held || _pressPending) && !Started)
            {
                Started = true;
                _pressPending = false;
                return Send.Start;
            }
            if (!Held && Started)
            {
                Started = false;
                return Send.Stop;
            }
            return Send.None;
        }

        /// <summary>Forget everything (shutdown). A start already sent is stopped by the server after its auto-stop.</summary>
        public void Reset()
        {
            Held = false;
            Started = false;
            _pressPending = false;
        }
    }
}
