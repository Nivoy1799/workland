namespace Worktown.Game.Player
{
    /// <summary>
    /// 031b (review of 031), pure: the predicted mode of an assembly board's build. Each fold or packaging click gets its own token
    /// (<see cref="Predict"/>); only the newest prediction is shown, and only the end of that one ends the look. An older one that
    /// resolves (its step arrived) changes nothing, so a quick fold and paper never show wrapped, folded, wrapped and never play the
    /// paper twice.
    /// </summary>
    public sealed class BuildStepLook
    {
        private int _token;
        private int _current;
        private byte _mode;
        private float _until;

        /// <summary>A prediction is shown.</summary>
        public bool Predicting => _current != 0;

        /// <summary>A click predicts <paramref name="mode"/> until <paramref name="until"/> (at the latest); its token.</summary>
        public int Predict(byte mode, float until)
        {
            _token = _token == int.MaxValue ? 1 : _token + 1;
            _current = _token;
            _mode = mode;
            _until = until;
            return _token;
        }

        /// <summary>The prediction <paramref name="token"/> is over; the look ends only when it is the newest one.</summary>
        public void End(int token)
        {
            if (token != 0 && token == _current) _current = 0;
        }

        /// <summary>Forget the prediction (a respawn).</summary>
        public void Reset() => _current = 0;

        /// <summary>The mode to show at <paramref name="now"/>: the newest pending prediction, else <paramref name="synced"/>.</summary>
        public byte Seen(float now, byte synced)
        {
            if (_current != 0 && now > _until) _current = 0;
            return _current != 0 ? _mode : synced;
        }
    }
}
