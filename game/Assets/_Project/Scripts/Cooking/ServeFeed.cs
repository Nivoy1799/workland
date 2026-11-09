using FishNet.Object;
using FishNet.Object.Synchronizing;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// The last served dishes, synced to every client for the overlay (spec 007).
    /// A global scene object, so it is visible regardless of distance-based interest.
    /// Spec 027c: <see cref="LastReward"/> is the newest sale as numbers (counter, stars, paid, tip) for the reward moment
    /// over the counter; a SyncVar, no RPC. The serial tells a new sale from the one a joining client already finds.
    /// </summary>
    public sealed class ServeFeed : NetworkBehaviour
    {
        public const int MaxEntries = 5;

        public readonly SyncList<string> Entries = new SyncList<string>();
        public readonly SyncVar<ServeReward> LastReward = new SyncVar<ServeReward>(default);

        /// <summary>Server: newest entry first, oldest dropped after <see cref="MaxEntries"/>.</summary>
        internal void ServerAdd(string entry)
        {
            Entries.Insert(0, entry);
            while (Entries.Count > MaxEntries)
                Entries.RemoveAt(Entries.Count - 1);
        }

        /// <summary>Server: a sale at the counter with station index <paramref name="counter"/> (spec 027c reward moment).</summary>
        internal void ServerReward(int counter, int stars, long paid, long tip)
        {
            LastReward.Value = ServeReward.Next(LastReward.Value, counter, stars, paid, tip);
        }

    }
}
