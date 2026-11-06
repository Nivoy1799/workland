using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// The last served dishes, synced to every client for the overlay (spec 007).
    /// A global scene object, so it is visible regardless of distance-based interest.
    /// </summary>
    public sealed class ServeFeed : NetworkBehaviour
    {
        public const int MaxEntries = 5;

        public readonly SyncList<string> Entries = new SyncList<string>();

        /// <summary>Server: newest entry first, oldest dropped after <see cref="MaxEntries"/>.</summary>
        internal void ServerAdd(string entry)
        {
            Entries.Insert(0, entry);
            while (Entries.Count > MaxEntries)
                Entries.RemoveAt(Entries.Count - 1);
        }

        public IReadOnlyList<string> Snapshot()
        {
            var copy = new List<string>(Entries.Count);
            foreach (string entry in Entries) copy.Add(entry);
            return copy;
        }
    }
}
