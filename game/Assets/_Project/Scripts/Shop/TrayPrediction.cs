namespace Worktown.Game.Shop
{
    /// <summary>
    /// Spec 032, pure, client (the clicking player only): the change tray as this player sees it right after a click (feel.md 1:
    /// never wait for the server to show it). Each coin or note click is a pending add until the server's tray byte rises by it; a
    /// refusal, the timeout, a take back or a settled payment (the tray drops) end it. Shown = the server's tray plus what is pending.
    /// Fixed storage, no allocations.
    /// </summary>
    public sealed class TrayPrediction
    {
        public const int MaxPending = 8;

        private readonly int[] _values = new int[MaxPending];
        private readonly float[] _until = new float[MaxPending];
        private int _count;
        private int _server;
        private bool _known;

        public int PendingCount => _count;

        /// <summary>The sum this player sees.</summary>
        public int Shown
        {
            get
            {
                int sum = _server;
                for (int i = 0; i < _count; i++) sum += _values[i];
                return sum;
            }
        }

        /// <summary>A click added <paramref name="value"/>; it shows until <paramref name="until"/> at the latest.</summary>
        public void Add(int value, float until)
        {
            if (value <= 0) return;
            if (_count == MaxPending) RemoveAt(0);
            _values[_count] = value;
            _until[_count] = until;
            _count++;
        }

        /// <summary>
        /// The server's tray is now <paramref name="tray"/>. A rise confirms the oldest pending adds it covers; a drop (take back,
        /// paid) ends every pending add. True when the rise was all our own (the sound already played at the click).
        /// </summary>
        public bool OnServer(int tray)
        {
            if (!_known)
            {
                _known = true;
                _server = tray;
                return false;
            }
            int delta = tray - _server;
            _server = tray;
            if (delta <= 0)
            {
                if (delta < 0) _count = 0;
                return false;
            }
            int covered = 0;
            while (_count > 0 && covered + _values[0] <= delta)
            {
                covered += _values[0];
                RemoveAt(0);
            }
            return covered == delta;
        }

        /// <summary>The server refused a click: the oldest pending add ends (requests are answered in order).</summary>
        public void Refused()
        {
            if (_count > 0) RemoveAt(0);
        }

        /// <summary>Pending adds past their time end.</summary>
        public void Expire(float now)
        {
            while (_count > 0 && now > _until[0]) RemoveAt(0);
        }

        public void Reset()
        {
            _count = 0;
            _known = false;
            _server = 0;
        }

        private void RemoveAt(int index)
        {
            for (int i = index; i < _count - 1; i++)
            {
                _values[i] = _values[i + 1];
                _until[i] = _until[i + 1];
            }
            _count--;
        }
    }
}
