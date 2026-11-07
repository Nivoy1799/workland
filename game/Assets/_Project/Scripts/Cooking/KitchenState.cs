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
        // Spec 013: contents of plating tables and bowls, per station index, in the order added.
        private readonly Dictionary<int, List<AssemblyEntry>> _stations = new Dictionary<int, List<AssemblyEntry>>();

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

        /// <summary>
        /// Spec 009: the plated version of the food, for selling. Does not change the stored item,
        /// so a refused sale leaves the food as it was.
        /// </summary>
        public CookOutcome TryPlate(int objectId, out Item plated)
        {
            plated = null;
            if (!_items.TryGetValue(objectId, out Item item)) return CookOutcome.NotFood;
            if (AssemblyRules.IsPlated(item))
            {
                plated = item; // spec 013: built on a plating table, sell it as it is
                return CookOutcome.Ok;
            }
            OperationResult result = _factory.Apply("plate", new[] { item }, NoParams);
            if (!result.Ok || result.Item == null) return CookOutcome.SimRefused;
            plated = result.Item;
            return CookOutcome.Ok;
        }

        private static readonly IReadOnlyDictionary<string, ParamValue> NoParams = new Dictionary<string, ParamValue>();

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
            _stations.Clear();
        }

        /// <summary>
        /// Spec 013: moves a food item onto a plating table or into a bowl. On success the food no
        /// longer exists as an item in the world (the caller despawns its object).
        /// </summary>
        public CookOutcome TryAddToStation(int foodObjectId, int stationIndex, StationKind kind, byte color)
        {
            if (!_items.TryGetValue(foodObjectId, out Item item)) return CookOutcome.NotFood;
            List<AssemblyEntry> contents = ContentsOf(stationIndex);
            CookOutcome allowed = AssemblyRules.CanAdd(kind, contents.Count, item);
            if (allowed != CookOutcome.Ok) return allowed;
            contents.Add(new AssemblyEntry(item, color));
            _items.Remove(foodObjectId);
            return CookOutcome.Ok;
        }

        /// <summary>
        /// Spec 013: takes everything from the station as one item (plate or mix of all, in order).
        /// A bowl with a single item gives it back unchanged. On failure the contents stay.
        /// </summary>
        public CookOutcome TryTakeFromStation(int stationIndex, StationKind kind, out Item result, out byte color)
        {
            result = null;
            color = 0;
            if (!AssemblyRules.IsAssembly(kind)) return CookOutcome.InvalidOption;
            List<AssemblyEntry> contents = ContentsOf(stationIndex);
            if (contents.Count == 0) return CookOutcome.StationEmpty;
            color = contents[0].Color;

            if (kind == StationKind.Bowl && contents.Count == 1)
            {
                result = contents[0].Item;
                contents.Clear();
                return CookOutcome.Ok;
            }

            var inputs = new List<Item>(contents.Count);
            foreach (AssemblyEntry entry in contents) inputs.Add(entry.Item);
            OperationResult combined = _factory.Apply(AssemblyRules.OperationFor(kind), inputs, NoParams);
            if (!combined.Ok || combined.Item == null) return CookOutcome.SimRefused;
            result = combined.Item;
            contents.Clear();
            return CookOutcome.Ok;
        }

        /// <summary>Display names of what is on the station, in order.</summary>
        public IReadOnlyList<string> NamesOnStation(int stationIndex)
        {
            List<AssemblyEntry> contents = ContentsOf(stationIndex);
            var names = new List<string>(contents.Count);
            foreach (AssemblyEntry entry in contents) names.Add(NameOf(entry.Item));
            return names;
        }

        private List<AssemblyEntry> ContentsOf(int stationIndex)
        {
            if (!_stations.TryGetValue(stationIndex, out List<AssemblyEntry> contents))
            {
                contents = new List<AssemblyEntry>();
                _stations[stationIndex] = contents;
            }
            return contents;
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
