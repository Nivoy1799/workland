using System;
using System.Collections.Generic;
using UnityEngine;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 045, server, pure: where the scene's own items (the two scene plates) stood when the server first started, so a server
    /// restart in the same process puts them back there. FishNet keeps a scene object where it last was over a stop; without this a
    /// scene plate left on the pass or the counter would start the next session there. The first start records (<see cref="Record"/>
    /// while open, then <see cref="Seal"/>); later starts record nothing, so the spots stay the scene's.
    /// </summary>
    public sealed class SceneStartSpots<T> where T : class
    {
        private readonly struct Spot
        {
            public Spot(T item, Vector3 position, Quaternion rotation)
            {
                Item = item;
                Position = position;
                Rotation = rotation;
            }

            public T Item { get; }
            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
        }

        private readonly List<Spot> _spots = new List<Spot>();

        /// <summary>The first start is over: nothing more is recorded.</summary>
        public bool Sealed { get; private set; }

        public int Count => _spots.Count;

        /// <summary>
        /// Before <see cref="Seal"/>: remembers <paramref name="item"/>'s start spot (an item already recorded keeps its first spot).
        /// False when sealed, for null, or a repeat.
        /// </summary>
        public bool Record(T item, Vector3 position, Quaternion rotation)
        {
            if (Sealed || item == null) return false;
            foreach (Spot s in _spots)
                if (ReferenceEquals(s.Item, item)) return false;
            _spots.Add(new Spot(item, position, rotation));
            return true;
        }

        public void Seal() => Sealed = true;

        /// <summary>
        /// The server stopped: every recorded item that is still <paramref name="alive"/> goes back to its start spot through
        /// <paramref name="move"/>. Returns how many were moved.
        /// </summary>
        public int ResetAll(Func<T, bool> alive, Action<T, Vector3, Quaternion> move)
        {
            if (move == null) return 0;
            int moved = 0;
            foreach (Spot s in _spots)
            {
                if (alive != null && !alive(s.Item)) continue;
                move(s.Item, s.Position, s.Rotation);
                moved++;
            }
            return moved;
        }
    }
}
