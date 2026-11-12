using System.Collections.Generic;

namespace Worktown.Sim.Layout
{
    /// <summary>
    /// Spec 062b: one player's undo history: the inverse edits of their last <see cref="Depth"/> build actions, newest on top (an older one
    /// falls off when an eleventh comes). The server keeps one per player and clears them when the shop opens (question 4).
    /// </summary>
    public sealed class UndoHistory
    {
        /// <summary>How many actions a player can undo (question 4: 10).</summary>
        public const int Depth = 10;

        private readonly List<LayoutEdit> _inverses = new List<LayoutEdit>(Depth);

        public int Count => _inverses.Count;

        /// <summary>Remembers the edit that undoes the action just done.</summary>
        public void Push(LayoutEdit inverse)
        {
            if (inverse == null) return;
            if (_inverses.Count == Depth) _inverses.RemoveAt(0);
            _inverses.Add(inverse);
        }

        /// <summary>The newest inverse edit, left in place (false when empty).</summary>
        public bool TryPeek(out LayoutEdit inverse)
        {
            inverse = null!;
            if (_inverses.Count == 0) return false;
            inverse = _inverses[_inverses.Count - 1];
            return true;
        }

        /// <summary>Takes the newest inverse edit off (false when empty).</summary>
        public bool TryPop(out LayoutEdit inverse)
        {
            if (!TryPeek(out inverse)) return false;
            _inverses.RemoveAt(_inverses.Count - 1);
            return true;
        }

        public void Clear() => _inverses.Clear();
    }
}
