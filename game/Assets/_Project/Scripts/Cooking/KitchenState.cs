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
        // Spec 016: food cooking in a pan or pot, and each stove's switch and mode, per station index.
        private readonly Dictionary<int, HeatSession> _heat = new Dictionary<int, HeatSession>();
        private readonly Dictionary<int, StoveSettings> _stoves = new Dictionary<int, StoveSettings>();
        private readonly Dictionary<int, Item> _items = new Dictionary<int, Item>();
        // Spec 013: contents of plating tables and bowls, per station index, in the order added.
        private readonly Dictionary<int, List<AssemblyEntry>> _stations = new Dictionary<int, List<AssemblyEntry>>();
        // Spec 019: food lying on a cutting board and how far it is cut, per board station index.
        private readonly Dictionary<int, Item> _boardFood = new Dictionary<int, Item>();
        private readonly Dictionary<int, CutProgress> _boardCut = new Dictionary<int, CutProgress>();

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
            _stoves.Clear();
            _boardFood.Clear();
            _boardCut.Clear();
        }

        // ---- Spec 019: cutting by hand on the board ----

        /// <summary>
        /// Puts the food on the cutting board, whole and not turned. On success the food no longer exists as an
        /// item in the world (the caller despawns its object). A full board gives StationFull; plated food, or
        /// food the sim would not cut, gives SimRefused; in both cases the food stays in the hands.
        /// </summary>
        public CookOutcome TryPutOnBoard(int foodObjectId, int boardIndex)
        {
            if (!_items.TryGetValue(foodObjectId, out Item item)) return CookOutcome.NotFood;
            if (_boardFood.ContainsKey(boardIndex)) return CookOutcome.StationFull;
            if (AssemblyRules.IsPlated(item) || !CanCut(item, FoodLook.ShapeHalves)) return CookOutcome.SimRefused;

            _boardFood[boardIndex] = item;
            _boardCut[boardIndex] = CutProgress.Whole;
            _items.Remove(foodObjectId);
            return CookOutcome.Ok;
        }

        /// <summary>
        /// One knife stroke on the board (<see cref="CutRules.Stroke"/>). NothingToCut on an empty board. A new
        /// shape the sim would refuse is refused now (SimRefused, nothing changes), so taking always works.
        /// The caller checks the knife and the reach.
        /// </summary>
        public CookOutcome TryBoardStroke(int boardIndex)
        {
            if (!_boardFood.TryGetValue(boardIndex, out Item item)) return CookOutcome.NothingToCut;
            CutProgress current = _boardCut[boardIndex];
            CutProgress next = CutRules.Stroke(current);
            if (next.Shape != current.Shape && !CanCut(item, next.Shape)) return CookOutcome.SimRefused;
            _boardCut[boardIndex] = next;
            return CookOutcome.Ok;
        }

        /// <summary>Turns the food on the board 90 degrees (<see cref="CutRules.Turn"/>). NothingToCut on an empty board.</summary>
        public CookOutcome TryBoardTurn(int boardIndex)
        {
            if (!_boardFood.ContainsKey(boardIndex)) return CookOutcome.NothingToCut;
            _boardCut[boardIndex] = CutRules.Turn(_boardCut[boardIndex]);
            return CookOutcome.Ok;
        }

        /// <summary>How far the food on the board is cut; false on an empty board.</summary>
        public bool TryGetBoard(int boardIndex, out Item food, out CutProgress progress)
        {
            progress = default;
            if (!_boardFood.TryGetValue(boardIndex, out food)) return false;
            progress = _boardCut[boardIndex];
            return true;
        }

        /// <summary>
        /// What taking the food from the board gives, without changing the board: uncut food unchanged, else the
        /// sim cut op with the shape reached (the same item the board's digit keys make). StationEmpty on an
        /// empty board. The caller hands the result over first, then calls <see cref="ClearBoard"/>.
        /// </summary>
        public CookOutcome TryPreviewBoardTake(int boardIndex, out Item result)
        {
            result = null;
            if (!_boardFood.TryGetValue(boardIndex, out Item item)) return CookOutcome.StationEmpty;
            string shape = CutRules.ShapeId(_boardCut[boardIndex].Shape);
            if (shape == null)
            {
                result = item;
                return CookOutcome.Ok;
            }
            OperationResult cut = _factory.Apply("cut", new[] { item }, CutParams(shape));
            if (!cut.Ok || cut.Item == null) return CookOutcome.SimRefused;
            result = cut.Item;
            return CookOutcome.Ok;
        }

        public void ClearBoard(int boardIndex)
        {
            _boardFood.Remove(boardIndex);
            _boardCut.Remove(boardIndex);
        }

        /// <summary>The look of the food on the board with the shape cut so far; null on an empty board.</summary>
        public FoodLook? BoardLook(int boardIndex)
        {
            if (!_boardFood.TryGetValue(boardIndex, out Item item)) return null;
            FoodLook look = FoodLookRules.From(item);
            byte shape = _boardCut[boardIndex].Shape;
            // Food that was cut before (third-person digits) keeps its shape until the knife changes it.
            return shape == FoodLook.ShapeWhole ? look : FoodLookRules.WithShape(look, shape);
        }

        /// <summary>Name of what taking the food would give, e.g. "potato slices"; null on an empty board.</summary>
        public string BoardName(int boardIndex)
        {
            return TryPreviewBoardTake(boardIndex, out Item result) == CookOutcome.Ok ? NameOf(result) : null;
        }

        private bool CanCut(Item item, byte shape)
        {
            string id = CutRules.ShapeId(shape);
            return id != null && _factory.Apply("cut", new[] { item }, CutParams(id)).Ok;
        }

        private static IReadOnlyDictionary<string, ParamValue> CutParams(string shape)
        {
            return new Dictionary<string, ParamValue> { ["shape"] = ParamValue.Choice(shape) };
        }

        // ---- Spec 016: cooking over time in the pan and pot ----

        /// <summary>The stove's switch and mode (created switched off, frying, on first use).</summary>
        public StoveSettings StoveOf(int stationIndex)
        {
            if (!_stoves.TryGetValue(stationIndex, out StoveSettings stove))
            {
                stove = new StoveSettings();
                _stoves[stationIndex] = stove;
            }
            return stove;
        }

        /// <summary>
        /// Option 1 switches the pan or pot on or off, also with food inside (cooking pauses while off).
        /// Option 2 switches the pan between fry and saute, only while it is empty.
        /// </summary>
        public CookOutcome TryUseStove(int stationIndex, StationKind kind, int option, uint tick)
        {
            if (!StationRules.IsHeat(kind) || option < 1 || option > StationRules.OptionCount(kind)) return CookOutcome.InvalidOption;
            StoveSettings stove = StoveOf(stationIndex);
            _heat.TryGetValue(stationIndex, out HeatSession session);
            if (option == StationRules.StoveSwitch)
            {
                stove.Toggle();
                if (session != null)
                {
                    if (stove.IsOn) session.Start(tick);
                    else session.Stop(tick);
                }
                return CookOutcome.Ok;
            }
            if (session != null) return CookOutcome.PanBusy;
            stove.ToggleMode();
            return CookOutcome.Ok;
        }

        public bool TryGetHeat(int stationIndex, out HeatSession session)
        {
            return _heat.TryGetValue(stationIndex, out session);
        }

        /// <summary>
        /// Puts the food into the pan or pot; it cooks from <paramref name="tick"/> if the stove is on. On success the
        /// food no longer exists as an item in the world (the caller despawns its object). Food the sim
        /// would refuse to heat (e.g. a finished plate) is refused now, while it is still in the hands.
        /// </summary>
        public CookOutcome TryPutOnHeat(int foodObjectId, int stationIndex, StationKind kind, uint tick)
        {
            if (!StationRules.IsHeat(kind)) return CookOutcome.InvalidOption;
            if (!_items.TryGetValue(foodObjectId, out Item item)) return CookOutcome.NotFood;
            if (_heat.ContainsKey(stationIndex)) return CookOutcome.StationFull;

            StoveSettings stove = StoveOf(stationIndex);
            string opId = stove.OperationIdFor(kind);
            int? tempC = kind == StationKind.Pan ? StoveSettings.PanTempC : (int?)null;
            if (!TryGetTimeParam(opId, out ParamDef time)) return CookOutcome.SimRefused;
            if (!_factory.Apply(opId, new[] { item }, HeatParams(tempC, time.Max)).Ok) return CookOutcome.SimRefused;

            _heat[stationIndex] = new HeatSession(item, opId, tempC, stove.IsOn, tick);
            _items.Remove(foodObjectId);
            return CookOutcome.Ok;
        }

        /// <summary>
        /// What taking the food out at <paramref name="tick"/> gives, without changing the station. Ok: the
        /// cooked item. TooShort: the food came out before the op's minimum time and is handed back
        /// unchanged. The caller hands the result over first, then calls <see cref="ClearHeat"/>.
        /// </summary>
        public CookOutcome TryPreviewHeatTake(int stationIndex, uint tick, double tickRate, out Item result)
        {
            result = null;
            if (!_heat.TryGetValue(stationIndex, out HeatSession session)) return CookOutcome.StationEmpty;
            if (!TryGetTimeParam(session.OperationId, out ParamDef time)) return CookOutcome.SimRefused;

            double seconds = CookingTime.SimSeconds(session.CookedTicks(tick), tickRate);
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
        /// Label line for what is in the pan or pot, e.g. "potato sticks · medium". Before the first doneness
        /// bucket: "warming up" while the stove is on, "raw" while it is off. Null if it is empty.
        /// </summary>
        public string DescribeHeat(int stationIndex, uint tick, double tickRate)
        {
            if (!_heat.TryGetValue(stationIndex, out HeatSession session)) return null;
            string bucket = CurrentBucket(session, tick, tickRate);
            if (bucket.Length == 0) bucket = session.IsRunning ? "warming up" : "raw";
            return NameOf(session.Food) + " · " + bucket;
        }

        /// <summary>
        /// Spec 017: the look of what is in the pan or pot, with the doneness bucket reached so far (the same
        /// bucket the label shows). Null if it is empty.
        /// </summary>
        public FoodLook? HeatLook(int stationIndex, uint tick, double tickRate)
        {
            if (!_heat.TryGetValue(stationIndex, out HeatSession session)) return null;
            return FoodLookRules.Cooking(session.Food, session.OperationId, CurrentBucket(session, tick, tickRate));
        }

        /// <summary>The doneness bucket id reached so far ("short", "medium", "long"), empty before the first.</summary>
        private string CurrentBucket(HeatSession session, uint tick, double tickRate)
        {
            if (!TryGetTimeParam(session.OperationId, out ParamDef time)) return string.Empty;
            return CookingTime.BucketId(time, CookingTime.SimSeconds(session.CookedTicks(tick), tickRate));
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
        public CookOutcome TryAddToStation(int foodObjectId, int stationIndex, StationKind kind)
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
            contents.Add(new AssemblyEntry(item));
            _items.Remove(foodObjectId);
            return CookOutcome.Ok;
        }

        /// <summary>
        /// Spec 013: takes everything from the station as one item (plate or mix of all, in order).
        /// A bowl with a single item gives it back unchanged. On failure the contents stay.
        /// </summary>
        public CookOutcome TryTakeFromStation(int stationIndex, StationKind kind, out Item result)
        {
            CookOutcome outcome = TryPreviewTake(stationIndex, kind, out result);
            if (outcome == CookOutcome.Ok) ClearStation(stationIndex);
            return outcome;
        }

        /// <summary>
        /// What "take" would produce, without changing the station. The caller hands the result over
        /// first and then calls <see cref="ClearStation"/>, so a failed hand-over loses nothing.
        /// </summary>
        public CookOutcome TryPreviewTake(int stationIndex, StationKind kind, out Item result)
        {
            result = null;
            if (!AssemblyRules.IsAssembly(kind)) return CookOutcome.InvalidOption;
            List<AssemblyEntry> contents = ContentsOf(stationIndex);
            if (contents.Count == 0) return CookOutcome.StationEmpty;

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

        /// <summary>Spec 017: looks of what is on the station, in order (same order as the names).</summary>
        public IReadOnlyList<FoodLook> LooksOnStation(int stationIndex)
        {
            List<AssemblyEntry> contents = ContentsOf(stationIndex);
            var looks = new List<FoodLook>(contents.Count);
            foreach (AssemblyEntry entry in contents) looks.Add(FoodLookRules.From(entry.Item));
            return looks;
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
