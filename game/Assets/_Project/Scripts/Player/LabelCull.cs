using System.Collections.Generic;
using UnityEngine;

namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 027d (second review: no per-frame allocations): the overlap culling of <see cref="LabelRules.Cull"/> with buffers
    /// that are kept between frames. Rectangles are taken in order of priority, then distance (nearest first), then index;
    /// one that overlaps a rectangle already kept is dropped. The order is found by an insertion sort (a few dozen labels),
    /// so no comparer or closure is allocated.
    /// </summary>
    public sealed class LabelCull
    {
        private int[] _order = new int[32];
        private readonly List<Rect> _kept = new List<Rect>(32);

        /// <summary>Fills <paramref name="keep"/> (at least as long as <paramref name="rects"/>) with one flag per rectangle.</summary>
        public void Run(IReadOnlyList<Rect> rects, IReadOnlyList<int> priorities, IReadOnlyList<float> distances, bool[] keep)
        {
            int count = rects.Count;
            if (_order.Length < count) _order = new int[Mathf.NextPowerOfTwo(count)];
            for (int i = 0; i < count; i++)
            {
                int j = i;
                while (j > 0 && Before(i, _order[j - 1], priorities, distances))
                {
                    _order[j] = _order[j - 1];
                    j--;
                }
                _order[j] = i;
            }
            _kept.Clear();
            for (int n = 0; n < count; n++)
            {
                int index = _order[n];
                bool clear = true;
                for (int k = 0; k < _kept.Count; k++)
                    if (rects[index].Overlaps(_kept[k])) { clear = false; break; }
                keep[index] = clear;
                if (clear) _kept.Add(rects[index]);
            }
        }

        private static bool Before(int a, int b, IReadOnlyList<int> priorities, IReadOnlyList<float> distances)
        {
            if (priorities[a] != priorities[b]) return priorities[a] < priorities[b];
            if (distances[a] != distances[b]) return distances[a] < distances[b];
            return a < b;
        }
    }
}
