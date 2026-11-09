namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 027b, pure: one predicted visual (docs/feel.md 1). The client shows the expected result at once; this tracks
    /// whether the synced state confirms it. It reads only state every client already has (<see cref="ItemSnapshot"/>:
    /// holder, position, the cookware's content count) and, as a faster "no", the outcome the server already reports.
    /// The timeout counts from the click or from the last relevant state change, whichever is later, so a slow host or a
    /// long round trip that is still moving things never snaps a success back. A deadline is only acted on when two
    /// observations in a row are past it, so one long frame (a hitch while the answer was already arriving) does not deny.
    /// </summary>
    public sealed class FeelPrediction
    {
        private readonly float _timeout;
        private ItemSnapshot _last;
        private bool _pastDeadlineOnce;

        public FeelPrediction(PredictionKind kind, int me, ItemSnapshot start, float now, float timeout)
        {
            Kind = kind;
            Me = me;
            Start = start;
            _last = start;
            StartTime = now;
            _timeout = timeout > 0f ? timeout : 0.01f;
            Deadline = now + _timeout;
            Phase = PredictionPhase.Pending;
        }

        public PredictionKind Kind { get; }
        public int Me { get; }
        public ItemSnapshot Start { get; }
        public float StartTime { get; }
        public float Deadline { get; private set; }
        public PredictionPhase Phase { get; private set; }
        /// <summary>When it was confirmed or denied (NaN while pending).</summary>
        public float ResolvedTime { get; private set; } = float.NaN;
        /// <summary>Denied by the timeout (no state change came), not by a refusal.</summary>
        public bool TimedOut { get; private set; }

        public bool IsPending => Phase == PredictionPhase.Pending;

        /// <summary>Reads the synced state at <paramref name="now"/>: confirms, denies, extends the timeout or times out.</summary>
        public PredictionPhase Observe(ItemSnapshot state, float now)
        {
            if (Phase != PredictionPhase.Pending) return Phase;
            PredictionPhase judged = FeelRules.Judge(Kind, Me, Start, state);
            if (judged != PredictionPhase.Pending)
            {
                Resolve(judged, now);
                return Phase;
            }
            if (FeelRules.IsRelevantChange(_last, state)) Extend(now);
            _last = state;
            if (now > Deadline)
            {
                if (_pastDeadlineOnce)
                {
                    TimedOut = true;
                    Resolve(PredictionPhase.Denied, now);
                }
                _pastDeadlineOnce = true;
            }
            else
            {
                _pastDeadlineOnce = false;
            }
            return Phase;
        }

        /// <summary>
        /// The server's reported outcome for this request: a refusal denies at once; "ok" is a relevant change (the state
        /// change is on its way), so the timeout counts from now.
        /// </summary>
        public void NoteAnswer(bool ok, float now)
        {
            if (Phase != PredictionPhase.Pending) return;
            if (ok) Extend(now);
            else Resolve(PredictionPhase.Denied, now);
        }

        /// <summary>Ends a pending prediction without a verdict (a newer click replaced it): treated as confirmed, so
        /// nothing flies back; the real state then wins through the normal tweens.</summary>
        public void Supersede(float now)
        {
            if (Phase == PredictionPhase.Pending) Resolve(PredictionPhase.Confirmed, now);
        }

        private void Extend(float now)
        {
            if (now + _timeout > Deadline) Deadline = now + _timeout;
            _pastDeadlineOnce = false;
        }

        private void Resolve(PredictionPhase phase, float now)
        {
            Phase = phase;
            ResolvedTime = now;
        }
    }
}
