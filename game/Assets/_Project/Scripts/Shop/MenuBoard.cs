using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;

namespace Worktown.Game.Shop
{
    /// <summary>
    /// Spec 037: the menu board's synced state for every client: one price per MenuPrices line in cents (<see cref="Prices"/>) and the
    /// selected line (<see cref="Selected"/>). Two presentation fields, no RPC: the board's text and the prompts must show the prices to
    /// everyone, and nothing else carries them. The server decides every price (EconomyService's MenuBoardDesk, through the board's
    /// station options).
    /// </summary>
    public sealed class MenuBoard : NetworkBehaviour
    {
        public readonly SyncList<int> Prices = new SyncList<int>();
        public readonly SyncVar<byte> Selected = new SyncVar<byte>(0);

        private readonly List<int> _pending = new List<int>();
        private int _pendingSelected;

        /// <summary>Client and server: counts every change seen (the view redraws only when it moved).</summary>
        public int Version { get; private set; }

        private void Awake()
        {
            Prices.OnChange += (_, _, _, _, _) => Version++;
            Selected.OnChange += (_, _, _) => Version++;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            Push();
        }

        /// <summary>Server: the board's prices (cents) and selected line; kept until spawned, then only changes are sent.</summary>
        internal void ServerSet(IReadOnlyList<long> prices, int selected)
        {
            _pending.Clear();
            if (prices != null)
                foreach (long p in prices) _pending.Add(p < 0 ? 0 : p > int.MaxValue ? int.MaxValue : (int)p);
            _pendingSelected = selected < 0 ? 0 : selected > byte.MaxValue ? byte.MaxValue : selected;
            Push();
        }

        private void Push()
        {
            if (!IsServerInitialized) return;
            if (Prices.Count != _pending.Count)
            {
                Prices.Clear();
                foreach (int p in _pending) Prices.Add(p);
            }
            else
            {
                for (int i = 0; i < _pending.Count; i++)
                    if (Prices[i] != _pending[i]) Prices[i] = _pending[i];
            }
            if (Selected.Value != _pendingSelected) Selected.Value = (byte)_pendingSelected;
            Version++;
        }
    }
}
