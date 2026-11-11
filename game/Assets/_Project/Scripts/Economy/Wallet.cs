using FishNet.Object;
using UnityEngine;

namespace Worktown.Game.Economy
{
    /// <summary>
    /// On the player (spec 009): the money the HUD shows. Spec 053 review (netcode): a thin reader of the one synced shop balance
    /// (<see cref="ShopLedger"/>, pillar 2: the money is the group's); it syncs nothing itself. Display only: the real balance lives in the
    /// server's TownEconomy.
    /// </summary>
    public sealed class Wallet : NetworkBehaviour
    {
        private const float LookupSeconds = 1f;

        private ShopLedger _ledger;
        private float _nextLookup;

        /// <summary>The shop balance and whether it is known yet (the ledger found and spawned).</summary>
        public bool TryGetBalance(out long balance)
        {
            balance = 0;
            if (_ledger == null)
            {
                if (Time.unscaledTime < _nextLookup) return false;
                _nextLookup = Time.unscaledTime + LookupSeconds;
                _ledger = FindFirstObjectByType<ShopLedger>();
                if (_ledger == null) return false;
            }
            return _ledger.TryGet(out balance);
        }

        /// <summary>The shop balance, or -1 while unknown (tours and logs).</summary>
        public long BalanceOrUnknown => TryGetBalance(out long balance) ? balance : -1L;
    }
}
