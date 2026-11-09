using FishNet.Object;
using FishNet.Object.Synchronizing;

namespace Worktown.Game.Shop
{
    /// <summary>
    /// Spec 035: the shop's hygiene score as every client sees it (0-100, one byte), for the HUD's meter. Presentation only: the server's
    /// CleaningRoom computes it from the sim (Sim.Economy.HygieneRules) and decides what guests make of it. A global scene object (like
    /// the stock counts). No RPC.
    /// </summary>
    public sealed class ShopHygiene : NetworkBehaviour
    {
        public readonly SyncVar<byte> Percent = new SyncVar<byte>(100);

        /// <summary>Server: publishes the score (only a change is sent).</summary>
        internal void ServerSet(int percent)
        {
            if (!IsServerInitialized) return;
            byte b = (byte)(percent < 0 ? 0 : percent > 100 ? 100 : percent);
            if (Percent.Value != b) Percent.Value = b;
        }
    }
}
