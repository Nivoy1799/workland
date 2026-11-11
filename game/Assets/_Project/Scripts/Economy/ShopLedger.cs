using FishNet.Object;
using FishNet.Object.Synchronizing;

namespace Worktown.Game.Economy
{
    /// <summary>
    /// Spec 053 review (netcode): the one shop balance every client sees, on one global session object. The wallet is the group's
    /// (pillar 2), so it is synced once, not once per player to every observer (that grew with the square of the players). Display only:
    /// the real balance lives in the server's TownEconomy; EconomyService publishes every change here. No RPC. Each player's
    /// <see cref="Wallet"/> is a thin reader of it for the UI code.
    /// </summary>
    public sealed class ShopLedger : NetworkBehaviour
    {
        public readonly SyncVar<long> Balance = new SyncVar<long>(0L);

        private long _balance;
        private bool _known;

        /// <summary>The balance this process sees, and whether it is known (the server's value, or the synced one on a client).</summary>
        public bool TryGet(out long balance)
        {
            if (IsServerInitialized)
            {
                balance = _balance;
                return _known;
            }
            balance = Balance.Value;
            return IsClientInitialized;
        }

        /// <summary>Server: the shop balance changed (kept while the object is not spawned yet; sent when it is, only a change).</summary>
        internal void ServerSet(long balance)
        {
            _balance = balance < 0 ? 0 : balance;
            _known = true;
            Push();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            Push();
        }

        private void Push()
        {
            if (IsServerInitialized && Balance.Value != _balance) Balance.Value = _balance;
        }
    }
}
