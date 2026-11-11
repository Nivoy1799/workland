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

        /// <summary>
        /// Server: the shop balance changed (kept while the object is not spawned yet; sent when it is, only a change).
        /// Follow-up 2026-11-11 evening: no clamp at 0. The shop balance cannot go below 0: every sim debit (grocer, supply orders) is
        /// refused with InsufficientFunds, TownEconomy.AdjustBalance keeps an account in 0..MaxBalance and the pitch fee takes at most
        /// the balance (PayFee). The only negative value is BalanceOf's -1 for "no account yet", which EconomyService never publishes.
        /// So the value is sent as it is; if a later rule ever allowed debt, the HUD would read "-$120" (EconomyRules.Money), not $0.
        /// </summary>
        internal void ServerSet(long balance)
        {
            _balance = balance;
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
