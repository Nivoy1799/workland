using UnityEngine;

namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 027b: what a client can see of an item and its target at one moment, all of it synced state (the holder
    /// SyncVar, the networked position, the target vessel's content count). <see cref="FeelPrediction"/> compares two.
    /// </summary>
    public readonly struct ItemSnapshot
    {
        public ItemSnapshot(bool exists, int holder, Vector3 position, int vesselCount)
        {
            Exists = exists;
            Holder = holder;
            Position = position;
            VesselCount = vesselCount;
        }

        /// <summary>False once the item is despawned (food put into a pot).</summary>
        public bool Exists { get; }
        /// <summary>Holder client id, or -1 (PickupRegistry.NoHolder).</summary>
        public int Holder { get; }
        public Vector3 Position { get; }
        /// <summary>Content count of the cookware it is put into (PutInto only, else 0).</summary>
        public int VesselCount { get; }

        public static ItemSnapshot Gone => new ItemSnapshot(false, -1, Vector3.zero, 0);
    }
}
