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

    }
}
