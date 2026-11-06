using System.Collections.Generic;

namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Server-side record of who holds which item. One holder per item, one item per client.
    /// The server processes RPCs one after another, so two requests for the same item
    /// "at the same time" arrive as two claims in a row: the first wins (spec 003 AC5).
    /// </summary>
    public sealed class PickupRegistry
    {
        public const int NoHolder = -1;

        private readonly Dictionary<int, int> _holderByItem = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _itemByClient = new Dictionary<int, int>();

        /// <summary>Gives the item to the client if the item is free and the client holds nothing.</summary>
        public bool TryClaim(int itemId, int clientId)
        {
            if (clientId < 0) return false;
            if (_holderByItem.ContainsKey(itemId)) return false;
            if (_itemByClient.ContainsKey(clientId)) return false;
            _holderByItem[itemId] = clientId;
            _itemByClient[clientId] = itemId;
            return true;
        }

        /// <summary>Frees the item. Only its current holder can release it.</summary>
        public bool TryRelease(int itemId, int clientId)
        {
            if (!_holderByItem.TryGetValue(itemId, out int holder) || holder != clientId) return false;
            _holderByItem.Remove(itemId);
            _itemByClient.Remove(clientId);
            return true;
        }

        /// <summary>Client id holding the item, or <see cref="NoHolder"/>.</summary>
        public int HolderOf(int itemId)
        {
            return _holderByItem.TryGetValue(itemId, out int holder) ? holder : NoHolder;
        }

        public bool TryGetHeldItem(int clientId, out int itemId)
        {
            return _itemByClient.TryGetValue(clientId, out itemId);
        }

        /// <summary>Forgets every claim, e.g. when the server stops.</summary>
        public void Clear()
        {
            _holderByItem.Clear();
            _itemByClient.Clear();
        }

        /// <summary>Releases whatever the client holds, e.g. on disconnect. Returns false if nothing was held.</summary>
        public bool ReleaseAllFor(int clientId, out int releasedItemId)
        {
            if (!_itemByClient.TryGetValue(clientId, out releasedItemId)) return false;
            _itemByClient.Remove(clientId);
            _holderByItem.Remove(releasedItemId);
            return true;
        }
    }
}
