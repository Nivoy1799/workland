namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 029b, pure: which shakes in a basket's synced counter a viewer still has to play. The owner's own shake already
    /// rattled at the click, so its counter step is skipped once it arrives; such an owed step expires after the confirm
    /// timeout and is dropped when the basket's state changes, so a refused shake never swallows another player's real one.
    /// The first counter seen (a join, or the first after <see cref="Reset"/>) is a baseline: it plays nothing.
    /// </summary>
    public sealed class BasketShakeLedger
    {
        private int _seen = -1;
        private int _owed;
        private float _owedUntil = float.NegativeInfinity;

        /// <summary>Own shakes still owed (for tests).</summary>
        public int Owed => _owed;

        /// <summary>Owner: a shake at the click, owed until <paramref name="now"/> + <paramref name="timeout"/>.</summary>
        public void NoteOwn(float now, float timeout)
        {
            _owed++;
            _owedUntil = now + (timeout > 0f ? timeout : 0f);
        }

        /// <summary>The basket's state changed: an owed shake of the old state can no longer arrive.</summary>
        public void StateChanged() => _owed = 0;

        /// <summary>Forget everything (the view was enabled again, e.g. after a respawn); the next counter is a baseline.</summary>
        public void Reset()
        {
            _seen = -1;
            _owed = 0;
            _owedUntil = float.NegativeInfinity;
        }

        /// <summary>The synced counter is <paramref name="counter"/> now: how many shakes of other players to play.</summary>
        public int Observe(int counter, float now)
        {
            if (_owed > 0 && now > _owedUntil) _owed = 0; // expired: the server refused it, or it never came
            if (_seen < 0)
            {
                _seen = counter;
                return 0;
            }
            int steps = FryerArtRules.ShakesBetween(_seen, counter);
            _seen = counter;
            int own = steps < _owed ? steps : _owed;
            _owed -= own;
            return steps - own;
        }
    }
}
