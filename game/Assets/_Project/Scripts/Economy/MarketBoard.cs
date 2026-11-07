using FishNet.Object;
using FishNet.Object.Synchronizing;

namespace Worktown.Game.Economy
{
    /// <summary>Global scene object (spec 009): clock and current dish prices for every client.</summary>
    public sealed class MarketBoard : NetworkBehaviour
    {
        public readonly SyncVar<string> Clock = new SyncVar<string>(string.Empty);
        public readonly SyncVar<string> Prices = new SyncVar<string>(string.Empty);

        internal void ServerSet(string clock, string prices)
        {
            Clock.Value = clock;
            Prices.Value = prices;
        }
    }
}
