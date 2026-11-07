using System.Collections.Generic;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Server-side kitchen state (spec 007): which sim <see cref="Item"/> each food object is.
    /// Pure: the caller (KitchenService) owns networking, positions and spawning. Clients never
    /// send item data; they only pick a station option, and this class applies the sim operation.
    /// </summary>
    public sealed class KitchenState
    {
        private readonly ItemFactory _factory;
        private readonly Dictionary<int, Item> _items = new Dictionary<int, Item>();

        public KitchenState(ItemFactory factory)
        {
            _factory = factory;
        }

        public static KitchenState CreateDemo()
        {
            return new KitchenState(new ItemFactory(new ItemCatalog(DemoIngredients.All), OperationCatalog.CreateDefault()));
        }

        /// <summary>Creates a raw ingredient. Register it under its object id once spawned.</summary>
        public CookOutcome TryCreateIngredient(string baseId, out Item item)
        {
            item = null;
            OperationResult result = _factory.CreateBase(baseId);
            if (!result.Ok || result.Item == null) return CookOutcome.SimRefused;
            item = result.Item;
            return CookOutcome.Ok;
        }

        public void Register(int objectId, Item item)
        {
            _items[objectId] = item;
        }

        public bool TryGet(int objectId, out Item item)
        {
            return _items.TryGetValue(objectId, out item);
        }

        /// <summary>Applies the station option to the food. On failure nothing changes.</summary>
        public CookOutcome TryApply(int objectId, StationKind station, int option, out Item result, out OperationError simError)
        {
            result = null;
            simError = OperationError.None;
            if (!_items.TryGetValue(objectId, out Item item)) return CookOutcome.NotFood;
            if (!StationRules.TryGetAction(station, option, out StationAction action)) return CookOutcome.InvalidOption;

            OperationResult applied = _factory.Apply(action.OperationId, new[] { item }, action.Parameters);
            if (!applied.Ok || applied.Item == null)
            {
                simError = applied.Error;
                return CookOutcome.SimRefused;
            }
            _items[objectId] = applied.Item;
            result = applied.Item;
            return CookOutcome.Ok;
        }

        /// <summary>Removes the food (it was served or despawned).</summary>
        public bool TryRemove(int objectId, out Item item)
        {
            if (!_items.TryGetValue(objectId, out item)) return false;
            _items.Remove(objectId);
            return true;
        }

        public void Clear()
        {
            _items.Clear();
        }

        public static string NameOf(Item item)
        {
            return ItemNaming.FallbackName(item, NameCatalog.Empty);
        }

        public static float QualityOf(Item item)
        {
            return ItemRater.Rate(item).Quality;
        }
    }
}
