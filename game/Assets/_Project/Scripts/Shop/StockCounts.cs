using FishNet.Object;
using FishNet.Object.Synchronizing;

namespace Worktown.Game.Shop
{
    /// <summary>
    /// Spec 034: the shop's stock as every client sees it: one byte per stock line (StockCatalog order; StockCatalog.Presented: sealed
    /// units, or a keg's cups left), for the slots' samples and empty look, the terminal's list and the HUD's "Low stock". Presentation
    /// only: the server's StockRoom decides every take, draw and unpack. A global scene object (like the feed). No RPC.
    /// </summary>
    public sealed class StockCounts : NetworkBehaviour
    {
        public readonly SyncList<byte> Counts = new SyncList<byte>();

        /// <summary>Client and server: counts every change seen (views redraw only when it moved; no per-frame work).</summary>
        public int Version { get; private set; }

        private void Awake()
        {
            Counts.OnChange += (_, _, _, _, _) => Version++;
        }

        /// <summary>The synced count of line <paramref name="line"/>; false while unknown.</summary>
        public bool TryGet(int line, out byte count)
        {
            count = 0;
            if (line < 0 || line >= Counts.Count) return false;
            count = Counts[line];
            return true;
        }

        /// <summary>Server: publishes the counts, sending only the bytes that changed.</summary>
        internal void ServerPublish(byte[] counts, int length)
        {
            if (!IsServerInitialized || counts == null) return;
            if (Counts.Count != length)
            {
                Counts.Clear();
                for (int i = 0; i < length; i++) Counts.Add(counts[i]);
                return;
            }
            for (int i = 0; i < length; i++)
                if (Counts[i] != counts[i]) Counts[i] = counts[i];
        }
    }
}
