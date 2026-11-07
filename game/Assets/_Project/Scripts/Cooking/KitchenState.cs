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
        private readonly OperationCatalog _operations;
        // Spec 016: food cooking in a pan or pot, and each pan's knob, per station index.
        private readonly Dictionary<int, HeatSession> _heat = new Dictionary<int, HeatSession>();
        private readonly Dictionary<int, PanSettings> _pans = new Dictionary<int, PanSettings>();
        private readonly Dictionary<int, Item> _items = new Dictionary<int, Item>();
        // Spec 013: contents of plating tables and bowls, per station index, in the order added.
        private readonly Dictionary<int, List<AssemblyEntry>> _stations = new Dictionary<int, List<AssemblyEntry>>();

        public KitchenState(ItemFactory factory, OperationCatalog operations)
        {
            _factory = factory;
            _operations = operations;
        }

        public static KitchenState CreateDemo()
        {
            OperationCatalog operations = OperationCatalog.CreateDefault();
            return new KitchenState(new ItemFactory(new ItemCatalog(DemoIngredients.All), operations), operations);
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
            _heat.Clear();
            _pans.Clear();
        }

        // ---- Spec 016: cooking over time in the pan and pot ----

        /// <summary>The pan's knob and mode (created at the defaults on first use).</summary>
        public PanSettings PanOf(int stationIndex)
        {
            if (!_pans.TryGetValue(stationIndex, out PanSettings pan))
            {
                pan = new PanSettings();
                _pans[stationIndex] = pan;
            }
            return pan;
        }

        /// <summary>
        /// Pan option 1 heat down, 2 heat up, 3 fry/saute. Only while the pan is empty. At the ends of
        /// the knob nothing changes and the answer is still Ok (the label shows the level).
        /// </summary>
        public CookOutcome TryChangePan(int stationIndex, StationKind kind, int option)
        {
            if (kind != StationKind.Pan) return CookOutcome.InvalidOption;
            if (option < StationRules.PanHeatDown || option > StationRules.PanToggleMode) return CookOutcome.InvalidOption;
            if (_heat.ContainsKey(stationIndex)) return CookOutcome.PanBusy;
            PanSettings pan = PanOf(stationIndex);
            if (option == StationRules.PanHeatDown) pan.TryStep(-1);
            else if (option == StationRules.PanHeatUp) pan.TryStep(+1);
            else pan.ToggleMode();
            return CookOutcome.Ok;
        }

        public bool TryGetHeat(int stationIndex, out HeatSession session)
        {
            return _heat.TryGetValue(stationIndex, out session);
        }

        /// <summary>
        /// Puts the food into the pan or pot and starts cooking at <paramref name="tick"/>. On success the
        /// food no longer exists as an item in the world (the caller despawns its object). Food the sim
        /// would refuse to heat (e.g. a finished plate) is refused now, while it is still in the hands.
        /// </summary>
        public CookOutcome TryPutOnHeat(int foodObjectId, int stationIndex, StationKind kind, byte color, uint tick)
        {
            if (!StationRules.IsHeat(kind)) return CookOutcome.InvalidOption;
            if (!_items.TryGetValue(foodObjectId, out Item item)) return CookOutcome.NotFood;
            if (_heat.ContainsKey(stationIndex)) return CookOutcome.StationFull;

            string opId = kind == StationKind.Pan ? PanOf(stationIndex).OperationId : "boil";
            int? tempC = kind == StationKind.Pan ? PanOf(stationIndex).TempC : (int?)null;
            if (!TryGetTimeParam(opId, out ParamDef time)) return CookOutcome.SimRefused;
            if (!_factory.Apply(opId, new[] { item }, HeatParams(tempC, time.Max)).Ok) return CookOutcome.SimRefused;

            _heat[stationIndex] = new HeatSession(item, color, opId, tempC, tick);
            _items.Remove(foodObjectId);
            return CookOutcome.Ok;
        }

        /// <summary>
        /// What taking the food out at <paramref name="tick"/> gives, without changing the station. Ok: the
        /// cooked item. TooShort: the food came out before the op's minimum time and is handed back
        /// unchanged. The caller hands the result over first, then calls <see cref="ClearHeat"/>.
        /// </summary>
        public CookOutcome TryPreviewHeatTake(int stationIndex, uint tick, double tickRate, out Item result, out byte color)
        {
            result = null;
            color = 0;
            if (!_heat.TryGetValue(stationIndex, out HeatSession session)) return CookOutcome.StationEmpty;
            color = session.Color;
            if (!TryGetTimeParam(session.OperationId, out ParamDef time)) return CookOutcome.SimRefused;

            double seconds = CookingTime.SimSeconds(CookingTime.ElapsedTicks(session.StartTick, tick), tickRate);
            if (CookingTime.IsTooShort(time, seconds))
            {
                result = session.Food;
                return CookOutcome.TooShort;
            }
            OperationResult cooked = _factory.Apply(session.OperationId, new[] { session.Food },
                HeatParams(session.TempC, CookingTime.Clamp(time, seconds)));
            if (!cooked.Ok || cooked.Item == null) return CookOutcome.SimRefused;
            result = cooked.Item;
            return CookOutcome.Ok;
        }

        public void ClearHeat(int stationIndex)
        {
            _heat.Remove(stationIndex);
        }

        /// <summary>
        /// Label line for what is cooking, e.g. "potato sticks · medium" ("warming up" before the first
        /// doneness bucket). Null if nothing is cooking.
        /// </summary>
        public string DescribeHeat(int stationIndex, uint tick, double tickRate)
        {
            if (!_heat.TryGetValue(stationIndex, out HeatSession session)) return null;
            string bucket = string.Empty;
            if (TryGetTimeParam(session.OperationId, out ParamDef time))
                bucket = CookingTime.BucketId(time, CookingTime.SimSeconds(CookingTime.ElapsedTicks(session.StartTick, tick), tickRate));
            return NameOf(session.Food) + " · " + (bucket.Length == 0 ? "warming up" : bucket);
        }

        private bool TryGetTimeParam(string opId, out ParamDef time)
        {
            time = null;
            return _operations.TryGet(opId, out OperationDef op) && CookingTime.TryGetTimeParam(op, out time);
        }

        /// <summary>Heat op parameters; no temperature for the pot (boil has a fixed one).</summary>
        private static IReadOnlyDictionary<string, ParamValue> HeatParams(int? tempC, float seconds)
        {
            var parameters = new Dictionary<string, ParamValue> { ["time"] = ParamValue.Number(seconds) };
            if (tempC.HasValue) parameters["temp"] = ParamValue.Number(tempC.Value);
            return parameters;
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

            // Dry run: the sim flattens nested mixes and has depth/component limits, so the entry
            // count alone does not tell whether "take" will work. Refuse now, while the food is
            // still in the player's hands, instead of locking the station later (review blocker).
            // A first item in a bowl is handed back unchanged, so it needs no mix check.
            bool singleInBowl = kind == StationKind.Bowl && contents.Count == 0;
            if (!singleInBowl)
            {
                var inputs = new List<Item>(contents.Count + 1);
                foreach (AssemblyEntry entry in contents) inputs.Add(entry.Item);
                inputs.Add(item);
                if (!_factory.Apply(AssemblyRules.OperationFor(kind), inputs, NoParams).Ok)
                    return CookOutcome.SimRefused;
            }
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
            CookOutcome outcome = TryPreviewTake(stationIndex, kind, out result, out color);
            if (outcome == CookOutcome.Ok) ClearStation(stationIndex);
            return outcome;
        }

        /// <summary>
        /// What "take" would produce, without changing the station. The caller hands the result over
        /// first and then calls <see cref="ClearStation"/>, so a failed hand-over loses nothing.
        /// </summary>
        public CookOutcome TryPreviewTake(int stationIndex, StationKind kind, out Item result, out byte color)
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
                return CookOutcome.Ok;
            }

            var inputs = new List<Item>(contents.Count);
            foreach (AssemblyEntry entry in contents) inputs.Add(entry.Item);
            OperationResult combined = _factory.Apply(AssemblyRules.OperationFor(kind), inputs, NoParams);
            if (!combined.Ok || combined.Item == null) return CookOutcome.SimRefused;
            result = combined.Item;
            return CookOutcome.Ok;
        }

        public void ClearStation(int stationIndex)
        {
            ContentsOf(stationIndex).Clear();
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
