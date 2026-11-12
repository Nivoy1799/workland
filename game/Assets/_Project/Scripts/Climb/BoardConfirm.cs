using System.Collections.Generic;

namespace Worktown.Game.Climb
{
    /// <summary>
    /// Spec 057 (open question 2), pure: the goal board spends the shop's money only on a second press of the same row by the same player
    /// within <see cref="WindowSeconds"/>. The first press arms; another player's press arms only that player's own confirm; a press after
    /// the window arms again. The caller passes the time (no clock here).
    /// </summary>
    public sealed class BoardConfirm
    {
        public const float WindowSeconds = 4f;

        private readonly Dictionary<int, (int Row, float At)> _armed = new Dictionary<int, (int, float)>();

        /// <summary>A press of <paramref name="row"/> by <paramref name="player"/> at <paramref name="now"/>: true confirms (and disarms), false armed it.</summary>
        public bool Press(int player, int row, float now)
        {
            if (_armed.TryGetValue(player, out (int Row, float At) armed) && armed.Row == row && now >= armed.At
                && now - armed.At <= WindowSeconds)
            {
                _armed.Remove(player);
                return true;
            }
            _armed[player] = (row, now);
            return false;
        }

        /// <summary>Whether <paramref name="player"/> has <paramref name="row"/> armed at <paramref name="now"/>.</summary>
        public bool IsArmed(int player, int row, float now) =>
            _armed.TryGetValue(player, out (int Row, float At) armed) && armed.Row == row && now >= armed.At && now - armed.At <= WindowSeconds;

        /// <summary>Forgets <paramref name="player"/>'s armed press (a refused press, a player who left).</summary>
        public void Disarm(int player) => _armed.Remove(player);

        /// <summary>Forgets every armed press (a new game, the stage moved).</summary>
        public void Clear() => _armed.Clear();
    }
}
