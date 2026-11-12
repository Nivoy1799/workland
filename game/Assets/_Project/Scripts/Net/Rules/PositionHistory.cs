using UnityEngine;

namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Spec 054b option (a), pure: one player's position at the end of each recent server tick, in a ring of
    /// <see cref="LagCompensation.HistoryTicks"/> ticks (about a second). The server pushes a player against the others where that
    /// player's client drew them (<see cref="LagCompensation"/>), so it looks them up here. A tick that was never recorded, or has been
    /// overwritten, is not found.
    /// </summary>
    public sealed class PositionHistory
    {
        private readonly uint[] _ticks;
        private readonly Vector3[] _positions;
        private readonly bool[] _set;

        public PositionHistory(int capacity = LagCompensation.HistoryTicks)
        {
            if (capacity < 1) capacity = 1;
            _ticks = new uint[capacity];
            _positions = new Vector3[capacity];
            _set = new bool[capacity];
        }

        public int Capacity => _ticks.Length;

        /// <summary>The position at the end of server tick <paramref name="tick"/> (a later write of the same tick replaces it).</summary>
        public void Record(uint tick, Vector3 position)
        {
            int slot = (int)(tick % (uint)_ticks.Length);
            _ticks[slot] = tick;
            _positions[slot] = position;
            _set[slot] = true;
        }

        /// <summary>The position recorded for <paramref name="tick"/>; false when it is not (or no longer) in the ring.</summary>
        public bool TryGet(uint tick, out Vector3 position)
        {
            int slot = (int)(tick % (uint)_ticks.Length);
            if (_set[slot] && _ticks[slot] == tick)
            {
                position = _positions[slot];
                return true;
            }
            position = default;
            return false;
        }

        /// <summary>Forgets everything (a new session).</summary>
        public void Clear()
        {
            for (int i = 0; i < _set.Length; i++) _set[i] = false;
        }
    }
}
