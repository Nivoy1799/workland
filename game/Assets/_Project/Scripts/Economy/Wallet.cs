using FishNet.Object;
using FishNet.Object.Synchronizing;

namespace Worktown.Game.Economy
{
    /// <summary>On the player (spec 009): the balance the server publishes. Display only;
    /// the real balance lives in the server's TownEconomy.</summary>
    public sealed class Wallet : NetworkBehaviour
    {
        public readonly SyncVar<long> Balance = new SyncVar<long>(0);

        internal void ServerSet(long balance)
        {
            Balance.Value = balance;
        }
    }
}
