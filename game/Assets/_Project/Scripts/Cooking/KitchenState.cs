using System.Collections.Generic;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Server-side kitchen state (spec 007): which sim <see cref="Item"/> each food object is, and (spec 021) what lies
    /// in each piece of cookware, keyed by the vessel's object id: food on a board and how far it is cut, food cooking
    /// in a pan or pot, the parts on a plate or in a bowl. Also the burners (on/off, fry/saute) keyed by the burner's
    /// place surface index, and which burner each pan or pot stands on. Pure: the caller (KitchenService) owns
    /// networking, positions and spawning. Clients never send item data; this class applies the sim operations.
    /// </summary>
    public sealed class KitchenState
    {
        /// <summary>A vessel that stands on no burner.</summary>
        public const int NoBurner = -1;

        private readonly ItemFactory _factory;
        private readonly OperationCatalog _operations;
        private readonly Dictionary<int, Item> _items = new Dictionary<int, Item>();
        // Spec 016/021: food cooking in a pan (one) or a pot (up to four, each with its own clock), per vessel id.
        private readonly Dictionary<int, List<HeatSession>> _heat = new Dictionary<int, List<HeatSession>>();
        private readonly Dictionary<string, HeatTimeline> _timelines = new Dictionary<string, HeatTimeline>();
        // Spec 021: each burner's switch and mode (per burner id), and the burner a pan or pot stands on.
        private readonly Dictionary<int, StoveSettings> _burners = new Dictionary<int, StoveSettings>();
        private readonly Dictionary<int, int> _burnerOf = new Dictionary<int, int>();
        // Spec 013/021: parts on a plate or in a bowl, per vessel id, in the order added.
        private readonly Dictionary<int, List<AssemblyEntry>> _assembly = new Dictionary<int, List<AssemblyEntry>>();
        // Spec 019/021: food lying on a cutting board and how far it is cut, per board vessel id.
        private readonly Dictionary<int, Item> _boardFood = new Dictionary<int, Item>();
        private readonly Dictionary<int, CutProgress> _boardCut = new Dictionary<int, CutProgress>();
        // Spec 029: each fryer's switch (per fryer station index) and each basket (per basket vessel id). The basket's food is a
        // heat session in _heat like the pan's; it runs while the basket is lowered and its fryer is on.
        private readonly Dictionary<int, bool> _fryerOn = new Dictionary<int, bool>();
        private readonly Dictionary<int, BasketRecord> _baskets = new Dictionary<int, BasketRecord>();
        // Spec 030: each griddle's switch (per griddle station index) and each spot (per spot vessel id) with its item and sides.
        private readonly Dictionary<int, bool> _griddleOn = new Dictionary<int, bool>();
        private readonly Dictionary<int, GrillRecord> _grill = new Dictionary<int, GrillRecord>();

        private sealed class BasketRecord
        {
            public int Fryer;
            public int Index;
            public BasketState State;
            public uint DrainStart;
            public uint DrainTicks;
            public bool Shaken;
            public int Shakes;
        }

        public KitchenState(ItemFactory factory, OperationCatalog operations)
        {
            _factory = factory;
            _operations = operations;
        }

        public static KitchenState CreateDemo()
        {
            OperationCatalog operations = OperationCatalog.CreateDefault();
            return new KitchenState(new ItemFactory(new ItemCatalog(DemoIngredients.Catalog), operations), operations); // spec 033: + cups, drinks
        }

        private static readonly IReadOnlyDictionary<string, ParamValue> NoParams = new Dictionary<string, ParamValue>();

        // ---- food objects ----

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
            _cupMl.Remove(objectId); // spec 033: a new item under this id holds no drink yet (CommitCup sets it)
            CancelPoursOfCup(objectId); // 033 review: a pour never fills a new item under a reused id
        }

        /// <summary>
        /// 033 review: item <paramref name="objectId"/> leaves the registry (into cookware, a vessel, served or despawned): its exact ml
        /// and any pour into it go with it.
        /// </summary>
        private void Forget(int objectId)
        {
            _items.Remove(objectId);
            _cupMl.Remove(objectId);
            CancelPoursOfCup(objectId);
        }

        public bool TryGet(int objectId, out Item item)
        {
            return _items.TryGetValue(objectId, out item);
        }

        /// <summary>
        /// Spec 009: the plated version of loose food, for selling at the counter. Does not change the stored item,
        /// so a refused sale leaves the food as it was.
        /// </summary>
        public CookOutcome TryPlate(int objectId, out Item plated)
        {
            plated = null;
            if (!_items.TryGetValue(objectId, out Item item)) return CookOutcome.NotFood;
            if (AssemblyRules.IsPlated(item))
            {
                plated = item; // already a finished plate: sell it as it is
                return CookOutcome.Ok;
            }
            OperationResult result = _factory.Apply("plate", new[] { item }, NoParams);
            if (!result.Ok || result.Item == null) return CookOutcome.SimRefused;
            plated = result.Item;
            return CookOutcome.Ok;
        }

        /// <summary>Removes the food (it was served or despawned).</summary>
        public bool TryRemove(int objectId, out Item item)
        {
            bool had = _items.TryGetValue(objectId, out item);
            Forget(objectId); // 034 review: the one path (the item, its ml, its pours)
            return had;
        }

        /// <summary>Everything goes: food, contents, burners (switched off) and positions.</summary>
        public void Clear()
        {
            _items.Clear();
            _assembly.Clear();
            _heat.Clear();
            _burners.Clear();
            _burnerOf.Clear();
            _boardFood.Clear();
            _boardCut.Clear();
            _fryerOn.Clear();
            _baskets.Clear();
            _griddleOn.Clear();
            _grill.Clear();
            _builds.Clear();
            _squeezes.Clear();
            _pours.Clear(); // spec 033
            _cupMl.Clear();
            _spills.Clear();
        }

        /// <summary>Spec 021: a vessel left the world (sold plate, lost cookware): its contents and position go with it.</summary>
        public void RemoveVessel(int vesselId)
        {
            _heat.Remove(vesselId);
            _burnerOf.Remove(vesselId);
            _assembly.Remove(vesselId);
            _baskets.Remove(vesselId);
            _grill.Remove(vesselId);
            _builds.Remove(vesselId); // spec 031
            ClearBoard(vesselId);
        }

        // ---- spec 021: any cookware ----

        /// <summary>How many things lie in the vessel.</summary>
        public int ContentCount(int vesselId, VesselKind kind)
        {
            switch (kind)
            {
                case VesselKind.Board: return _boardFood.ContainsKey(vesselId) ? 1 : 0;
                case VesselKind.Pan:
                case VesselKind.Basket: // spec 029
                case VesselKind.Pot: return HeatCount(vesselId);
                case VesselKind.Griddle: return HeatCount(vesselId); // spec 030
                case VesselKind.Assembly: return LayerCount(vesselId); // spec 031
                default: return CountIn(vesselId);
            }
        }

        /// <summary>
        /// Puts the held food into the vessel (board, pan, pot, plate, bowl). On success the food no longer exists as
        /// an item in the world (the caller despawns its object). Refusals change nothing: NotFood, AlreadyPlated,
        /// VesselFull, SimRefused (the sim would refuse to cut, heat or combine it).
        /// </summary>
        public CookOutcome TryPutInto(int foodObjectId, int vesselId, VesselKind kind, uint tick)
        {
            switch (kind)
            {
                case VesselKind.Board: return TryPutOnBoard(foodObjectId, vesselId);
                case VesselKind.Pan:
                case VesselKind.Pot: return TryPutOnHeat(foodObjectId, vesselId, kind, tick);
                case VesselKind.Basket: return TryPutInBasket(foodObjectId, vesselId, tick);
                case VesselKind.Griddle: return TryPutOnGriddle(foodObjectId, vesselId, tick); // spec 030
                case VesselKind.Assembly: return TryPutOnBuild(foodObjectId, vesselId); // spec 031
                default: return TryAddToVessel(foodObjectId, vesselId, kind);
            }
        }

        /// <summary>
        /// What empty hands take out, without changing the vessel: the board's food cut as far as it is, the pan's
        /// food cooked (or TooShort, unchanged), the bowl's mix. A pot is poured (PourInstead), a plate is picked up
        /// and sold (InvalidOption). The caller hands the result over first, then calls <see cref="ClearContents"/>.
        /// </summary>
        public CookOutcome TryPreviewTakeOut(int vesselId, VesselKind kind, uint tick, double tickRate, out Item result)
        {
            result = null;
            switch (kind)
            {
                case VesselKind.Board: return TryPreviewBoardTake(vesselId, out result);
                case VesselKind.Pan: return TryPreviewHeatTake(vesselId, tick, tickRate, out result);
                case VesselKind.Basket:
                    // Spec 029: only from a raised (or draining) basket.
                    if (BasketStateOf(vesselId, tick) == BasketState.Lowered) return CookOutcome.BasketLowered;
                    return TryPreviewHeatTake(vesselId, tick, tickRate, out result);
                case VesselKind.Griddle: return TryPreviewGriddleTake(vesselId, tick, tickRate, out result); // spec 030
                case VesselKind.Assembly: return TryPreviewBuildTake(vesselId, out result); // spec 031
                case VesselKind.Bowl: return TryPreviewTake(vesselId, kind, out result);
                case VesselKind.Pot: return HeatCount(vesselId) > 0 ? CookOutcome.PourInstead : CookOutcome.StationEmpty;
                default: return CookOutcome.InvalidOption;
            }
        }

        public void ClearContents(int vesselId, VesselKind kind)
        {
            switch (kind)
            {
                case VesselKind.Board: ClearBoard(vesselId); break;
                case VesselKind.Pan:
                case VesselKind.Pot: ClearHeat(vesselId); break;
                case VesselKind.Basket:
                    ClearHeat(vesselId);
                    if (_baskets.TryGetValue(vesselId, out BasketRecord basket)) basket.Shaken = false; // the next fry starts fresh
                    break;
                case VesselKind.Griddle: ClearGriddle(vesselId); break; // spec 030
                case VesselKind.Assembly: ClearBuild(vesselId); break; // spec 031
                default: ClearVessel(vesselId); break;
            }
        }

        /// <summary>
        /// Holding <paramref name="sourceId"/> (pan, pot or bowl with food), pours it onto the plate or into the bowl
        /// <paramref name="targetId"/>: the server applies the heat or mix ops now, exactly like a take, and adds the
        /// result as one part. Everything is checked before anything changes: CannotPour, StationEmpty (nothing to
        /// pour), AlreadyPlated, VesselFull, SimRefused (the target's sim op would refuse it). Ok or TooShort
        /// (poured unchanged) on success; then the source is empty.
        /// </summary>
        public CookOutcome TryPour(int sourceId, VesselKind sourceKind, int targetId, VesselKind targetKind, uint tick,
            double tickRate, out Item poured)
        {
            poured = null;
            if (sourceId == targetId || !VesselRules.CanPour(sourceKind, targetKind)) return CookOutcome.CannotPour;
            CookOutcome made = VesselRules.IsHeat(sourceKind)
                ? TryPreviewHeatTake(sourceId, tick, tickRate, out Item result)
                : TryPreviewTake(sourceId, sourceKind, out result);
            if (made != CookOutcome.Ok && made != CookOutcome.TooShort) return made;

            CookOutcome fits = CanAddItem(targetId, targetKind, result);
            if (fits != CookOutcome.Ok) return fits;
            AssemblyOf(targetId).Add(new AssemblyEntry(result));
            ClearContents(sourceId, sourceKind);
            poured = result;
            return made;
        }

        /// <summary>
        /// Spec 029b: tips a raised (or draining) basket's food onto the plate or into the bowl <paramref name="targetId"/>
        /// that the player holds: the basket's take (shake bonus, greasy while it drains) poured like a pan. BasketLowered
        /// while it is in the oil; otherwise as <see cref="TryPour"/>.
        /// </summary>
        public CookOutcome TryTipBasket(int basketId, int targetId, VesselKind targetKind, uint tick, double tickRate, out Item poured)
        {
            poured = null;
            if (!_baskets.ContainsKey(basketId)) return CookOutcome.NotAVessel;
            if (BasketStateOf(basketId, tick) == BasketState.Lowered) return CookOutcome.BasketLowered;
            return TryPour(basketId, VesselKind.Basket, targetId, targetKind, tick, tickRate, out poured);
        }

        /// <summary>
        /// Name of what taking, pouring or selling the contents gives now ("crispy fried potato sticks", the plated
        /// dish's name); null when empty or when the sim would refuse.
        /// </summary>
        public string ResultName(int vesselId, VesselKind kind, uint tick, double tickRate)
        {
            Item result;
            CookOutcome outcome;
            switch (kind)
            {
                case VesselKind.Board: return BoardName(vesselId);
                case VesselKind.Assembly: return BuildName(vesselId); // spec 031
                case VesselKind.Pan:
                case VesselKind.Griddle: // spec 030
                case VesselKind.Basket: // spec 029
                case VesselKind.Pot: outcome = TryPreviewHeatTake(vesselId, tick, tickRate, out result); break;
                default: outcome = TryPreviewTake(vesselId, kind, out result); break;
            }
            return (outcome == CookOutcome.Ok || outcome == CookOutcome.TooShort) && result != null ? NameOf(result) : null;
        }

        /// <summary>Label lines of the contents, in order ("potato sticks · medium" while cooking).</summary>
        public IReadOnlyList<string> ContentNames(int vesselId, VesselKind kind, uint tick, double tickRate)
        {
            switch (kind)
            {
                case VesselKind.Board:
                    string name = BoardName(vesselId);
                    return name == null ? new string[0] : new[] { name };
                case VesselKind.Assembly: // spec 031: the label is the take name
                    string build = BuildName(vesselId);
                    return build == null ? new string[0] : new[] { build };
                case VesselKind.Pan:
                case VesselKind.Griddle: // spec 030
                case VesselKind.Basket: // spec 029
                case VesselKind.Pot: return HeatNames(vesselId, tick, tickRate);
                default: return NamesIn(vesselId);
            }
        }

        /// <summary>Spec 017 looks of the contents, in the same order as the names.</summary>
        public IReadOnlyList<FoodLook> ContentLooks(int vesselId, VesselKind kind, uint tick, double tickRate)
        {
            switch (kind)
            {
                case VesselKind.Board:
                    FoodLook? look = BoardLook(vesselId);
                    return look.HasValue ? new[] { look.Value } : new FoodLook[0];
                case VesselKind.Assembly: return BuildLooks(vesselId); // spec 031: one look per layer, in order
                case VesselKind.Pan:
                case VesselKind.Griddle: return GriddleLooks(vesselId); // spec 030
                case VesselKind.Basket: // spec 029
                case VesselKind.Pot: return HeatLooks(vesselId, tick, tickRate);
                default: return LooksIn(vesselId);
            }
        }

        // ---- Spec 019: cutting by hand on the board ----

        /// <summary>
        /// Puts the food on the cutting board, whole and not turned. On success the food no longer exists as an item in
        /// the world. A full board gives VesselFull, plated food AlreadyPlated, food the sim would not cut SimRefused;
        /// in all cases the food stays in the hands.
        /// </summary>
        public CookOutcome TryPutOnBoard(int foodObjectId, int boardId)
        {
            if (!_items.TryGetValue(foodObjectId, out Item item)) return CookOutcome.NotFood;
            CookOutcome fits = VesselRules.CanPutIn(VesselKind.Board, _boardFood.ContainsKey(boardId) ? 1 : 0, AssemblyRules.IsPlated(item));
            if (fits != CookOutcome.Ok) return fits;
            if (!CanCut(item, FoodLook.ShapeHalves)) return CookOutcome.SimRefused;

            _boardFood[boardId] = item;
            _boardCut[boardId] = CutProgress.Whole;
            Forget(foodObjectId); // 033 review: the cup's ml and pours go with it
            return CookOutcome.Ok;
        }

        /// <summary>
        /// One knife stroke on the board (<see cref="CutRules.Stroke"/>). NothingToCut on an empty board. A new shape the
        /// sim would refuse is refused now (SimRefused, nothing changes), so taking always works.
        /// </summary>
        public CookOutcome TryBoardStroke(int boardId)
        {
            if (!_boardFood.TryGetValue(boardId, out Item item)) return CookOutcome.NothingToCut;
            CutProgress current = _boardCut[boardId];
            CutProgress next = CutRules.Stroke(current);
            if (next.Shape != current.Shape && !CanCut(item, next.Shape)) return CookOutcome.SimRefused;
            _boardCut[boardId] = next;
            return CookOutcome.Ok;
        }

        /// <summary>Turns the food on the board 90 degrees (<see cref="CutRules.Turn"/>). NothingToCut on an empty board.</summary>
        public CookOutcome TryBoardTurn(int boardId)
        {
            if (!_boardFood.ContainsKey(boardId)) return CookOutcome.NothingToCut;
            _boardCut[boardId] = CutRules.Turn(_boardCut[boardId]);
            return CookOutcome.Ok;
        }

        /// <summary>How far the food on the board is cut; false on an empty board.</summary>
        public bool TryGetBoard(int boardId, out Item food, out CutProgress progress)
        {
            progress = default;
            if (!_boardFood.TryGetValue(boardId, out food)) return false;
            progress = _boardCut[boardId];
            return true;
        }

        /// <summary>
        /// What taking the food from the board gives, without changing the board: uncut food unchanged, else the sim cut
        /// op with the shape reached. StationEmpty on an empty board.
        /// </summary>
        public CookOutcome TryPreviewBoardTake(int boardId, out Item result)
        {
            result = null;
            if (!_boardFood.TryGetValue(boardId, out Item item)) return CookOutcome.StationEmpty;
            string shape = CutRules.ShapeId(_boardCut[boardId].Shape);
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

        public void ClearBoard(int boardId)
        {
            _boardFood.Remove(boardId);
            _boardCut.Remove(boardId);
        }

        /// <summary>The look of the food on the board with the shape cut so far; null on an empty board.</summary>
        public FoodLook? BoardLook(int boardId)
        {
            if (!_boardFood.TryGetValue(boardId, out Item item)) return null;
            FoodLook look = FoodLookRules.From(item);
            byte shape = _boardCut[boardId].Shape;
            // Food that was cut before keeps its shape until the knife changes it.
            return shape == FoodLook.ShapeWhole ? look : FoodLookRules.WithShape(look, shape);
        }

        /// <summary>Name of what taking the food would give, e.g. "potato slices"; null on an empty board.</summary>
        public string BoardName(int boardId)
        {
            return TryPreviewBoardTake(boardId, out Item result) == CookOutcome.Ok ? NameOf(result) : null;
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

        // ---- Spec 021: burners ----

        /// <summary>The burner's switch and mode (created switched off, frying, on first use).</summary>
        public StoveSettings BurnerOf(int burnerId)
        {
            if (!_burners.TryGetValue(burnerId, out StoveSettings burner))
            {
                burner = new StoveSettings();
                _burners[burnerId] = burner;
            }
            return burner;
        }

        /// <summary>
        /// Button <see cref="StationRules.StoveSwitch"/> switches the burner on or off, also with a pan or pot on it
        /// (cooking pauses while off). <see cref="StationRules.PanMode"/> switches fry and saute, refused (PanBusy) while
        /// a pan with food stands on it (the fry or saute op was chosen when the food went in). Anything else InvalidOption.
        /// </summary>
        public CookOutcome TryUseBurner(int burnerId, int button, uint tick)
        {
            StoveSettings burner = BurnerOf(burnerId);
            if (button == StationRules.StoveSwitch)
            {
                burner.Toggle();
                foreach (KeyValuePair<int, int> standing in _burnerOf)
                    if (standing.Value == burnerId) SetRunning(standing.Key, burner.IsOn, tick);
                return CookOutcome.Ok;
            }
            if (button != StationRules.PanMode) return CookOutcome.InvalidOption;
            foreach (KeyValuePair<int, int> standing in _burnerOf)
                if (standing.Value == burnerId && IsPanWithFood(standing.Key)) return CookOutcome.PanBusy;
            burner.ToggleMode();
            return CookOutcome.Ok;
        }

        /// <summary>
        /// The pan or pot now stands on <paramref name="burnerId"/> (<see cref="NoBurner"/>: picked up, on a worktop, on
        /// the floor). Its food cooks only while it stands on a burner that is on; picking it up stops the clocks,
        /// putting it back continues them.
        /// </summary>
        public void SetVesselBurner(int vesselId, int burnerId, uint tick)
        {
            if (burnerId < 0) _burnerOf.Remove(vesselId);
            else _burnerOf[vesselId] = burnerId;
            SetRunning(vesselId, IsCooking(vesselId), tick);
        }

        /// <summary>The burner the vessel stands on, or <see cref="NoBurner"/>.</summary>
        public int BurnerUnder(int vesselId)
        {
            return _burnerOf.TryGetValue(vesselId, out int burner) ? burner : NoBurner;
        }

        /// <summary>True while the vessel stands on a burner that is on.</summary>
        public bool IsCooking(int vesselId)
        {
            return _burnerOf.TryGetValue(vesselId, out int burner) && BurnerOf(burner).IsOn;
        }

        /// <summary>
        /// Spec 021: the pan mode shown on the pan: its food's op while it has food, else the mode of the burner it
        /// stands on (fry when on none).
        /// </summary>
        public bool PanIsSaute(int vesselId)
        {
            if (_heat.TryGetValue(vesselId, out List<HeatSession> sessions) && sessions.Count > 0)
                return sessions[0].OperationId == "saute";
            return _burnerOf.TryGetValue(vesselId, out int burner) && BurnerOf(burner).Saute;
        }

        private bool IsPanWithFood(int vesselId)
        {
            return _heat.TryGetValue(vesselId, out List<HeatSession> sessions) && sessions.Count > 0 && sessions[0].TempC.HasValue;
        }

        private void SetRunning(int vesselId, bool running, uint tick)
        {
            if (!_heat.TryGetValue(vesselId, out List<HeatSession> sessions)) return;
            foreach (HeatSession session in sessions)
            {
                if (running) session.Start(tick);
                else session.Stop(tick);
            }
        }

        // ---- Spec 016/021: cooking over time in the pan and pot ----

        /// <summary>
        /// Puts the food into the pan or pot; it cooks from <paramref name="tick"/> while the vessel stands on a burner
        /// that is on. The pan takes one food (fry or saute: the mode of its burner, fry when on none); the pot up to
        /// <see cref="VesselRules.PotCapacity"/>, each counting its own on-ticks. Food the sim would refuse to heat, or
        /// a pot whose pour the sim would refuse to mix, is refused now while it is still in the hands.
        /// </summary>
        public CookOutcome TryPutOnHeat(int foodObjectId, int vesselId, VesselKind kind, uint tick)
        {
            if (!VesselRules.IsHeat(kind)) return CookOutcome.InvalidOption;
            if (!_items.TryGetValue(foodObjectId, out Item item)) return CookOutcome.NotFood;
            List<HeatSession> sessions = HeatOf(vesselId);
            CookOutcome fits = VesselRules.CanPutIn(kind, sessions.Count, AssemblyRules.IsPlated(item));
            if (fits != CookOutcome.Ok) return fits;

            int burner = BurnerUnder(vesselId);
            string opId = burner == NoBurner ? new StoveSettings().OperationIdFor(kind) : BurnerOf(burner).OperationIdFor(kind);
            int? tempC = kind == VesselKind.Pan ? StoveSettings.PanTempC : (int?)null;
            if (!TryGetTimeParam(opId, out ParamDef time)) return CookOutcome.SimRefused;
            OperationResult dry = _factory.Apply(opId, new[] { item }, HeatParams(opId, tempC, time.Max));
            if (!dry.Ok || dry.Item == null) return CookOutcome.SimRefused;
            if (sessions.Count > 0)
            {
                // Dry run of the pour: every ingredient at its longest time, mixed (fewer cooked steps only make it simpler).
                var inputs = new List<Item>(sessions.Count + 1);
                foreach (HeatSession s in sessions)
                {
                    if (!TryGetTimeParam(s.OperationId, out ParamDef t)) return CookOutcome.SimRefused;
                    OperationResult longest = _factory.Apply(s.OperationId, new[] { s.Food }, HeatParams(s.OperationId, s.TempC, t.Max));
                    if (!longest.Ok || longest.Item == null) return CookOutcome.SimRefused;
                    inputs.Add(longest.Item);
                }
                inputs.Add(dry.Item);
                if (!_factory.Apply("mix", inputs, NoParams).Ok) return CookOutcome.SimRefused;
            }

            var session = new HeatSession(item, opId, tempC, IsCooking(vesselId), tick);
            session.Timeline = TimelineOf(item, opId, tempC, time);
            sessions.Add(session);
            Forget(foodObjectId); // 033 review: the cup's ml and pours go with it
            return CookOutcome.Ok;
        }

        /// <summary>How many ingredients are in the pan or pot.</summary>
        public int HeatCount(int vesselId)
        {
            if (_grill.TryGetValue(vesselId, out GrillRecord spot)) return spot.Food != null ? 1 : 0; // spec 030
            return _heat.TryGetValue(vesselId, out List<HeatSession> sessions) ? sessions.Count : 0;
        }

        /// <summary>The cooking session of ingredient <paramref name="index"/> (in the order put in).</summary>
        public bool TryGetHeat(int vesselId, int index, out HeatSession session)
        {
            session = null;
            if (!_heat.TryGetValue(vesselId, out List<HeatSession> sessions) || index < 0 || index >= sessions.Count) return false;
            session = sessions[index];
            return true;
        }

        /// <summary>
        /// What taking (or pouring) the contents out at <paramref name="tick"/> gives, without changing the vessel. One
        /// ingredient: the cooked item, or TooShort when it came out before the op's minimum time (handed back unchanged).
        /// Several (pot): each cooked for its own time (unchanged if too short), then mixed (sim mix, canonical order): Ok.
        /// StationEmpty when empty. The caller hands the result over first, then calls <see cref="ClearHeat"/>.
        /// </summary>
        public CookOutcome TryPreviewHeatTake(int vesselId, uint tick, double tickRate, out Item result)
        {
            result = null;
            if (_grill.ContainsKey(vesselId)) return TryPreviewGriddleTake(vesselId, tick, tickRate, out result); // spec 030
            if (!_heat.TryGetValue(vesselId, out List<HeatSession> sessions) || sessions.Count == 0) return CookOutcome.StationEmpty;
            // Spec 029: a basket's fry carries the shake bonus and, while it still drains, the greasy penalty.
            bool shaken = false, greasy = false;
            if (_baskets.TryGetValue(vesselId, out BasketRecord basket))
            {
                shaken = basket.Shaken;
                greasy = BasketStateOf(vesselId, tick) == BasketState.Draining;
            }
            if (sessions.Count == 1) return CookOne(sessions[0], tick, tickRate, shaken, greasy, out result);

            var cooked = new List<Item>(sessions.Count);
            foreach (HeatSession session in sessions)
            {
                CookOutcome one = CookOne(session, tick, tickRate, shaken, greasy, out Item item);
                if (one != CookOutcome.Ok && one != CookOutcome.TooShort) return one;
                cooked.Add(item);
            }
            OperationResult mixed = _factory.Apply("mix", cooked, NoParams);
            if (!mixed.Ok || mixed.Item == null) return CookOutcome.SimRefused;
            result = mixed.Item;
            return CookOutcome.Ok;
        }

        private CookOutcome CookOne(HeatSession session, uint tick, double tickRate, bool shaken, bool greasy, out Item result)
        {
            result = null;
            if (!TryGetTimeParam(session.OperationId, out ParamDef time)) return CookOutcome.SimRefused;
            double seconds = CookingTime.SimSeconds(session.CookedTicks(tick), tickRate);
            if (CookingTime.IsTooShort(time, seconds))
            {
                result = session.Food;
                return CookOutcome.TooShort;
            }
            OperationResult cooked = _factory.Apply(session.OperationId, new[] { session.Food },
                HeatParams(session.OperationId, session.TempC, CookingTime.Clamp(time, seconds), shaken, greasy));
            if (!cooked.Ok || cooked.Item == null) return CookOutcome.SimRefused;
            result = cooked.Item;
            return CookOutcome.Ok;
        }

        public void ClearHeat(int vesselId)
        {
            _heat.Remove(vesselId);
        }

        /// <summary>
        /// Label lines for what is in the pan, pot or basket. Spec 029b: in player words from each food's 027c zone
        /// (<see cref="HeatLabelRules"/>): "potato sticks · cooked", "golden fries".
        /// </summary>
        public IReadOnlyList<string> HeatNames(int vesselId, uint tick, double tickRate)
        {
            if (_grill.ContainsKey(vesselId)) return GriddleNames(vesselId, tick, tickRate); // spec 030
            if (!_heat.TryGetValue(vesselId, out List<HeatSession> sessions)) return new string[0];
            // Spec 030 (orchestrator decision): each line is the name that food would get if taken out now (the basket's shake
            // and drain included), so the label never contradicts the item; too short gives the food's own name.
            bool shaken = false, greasy = false;
            if (_baskets.TryGetValue(vesselId, out BasketRecord basket))
            {
                shaken = basket.Shaken;
                greasy = BasketStateOf(vesselId, tick) == BasketState.Draining;
            }
            var names = new List<string>(sessions.Count);
            foreach (HeatSession session in sessions)
            {
                CookOne(session, tick, tickRate, shaken, greasy, out Item preview);
                names.Add(HeatLabelRules.Label(NameOf(preview ?? session.Food)));
            }
            return names;
        }

        /// <summary>
        /// 030 review: the label lines and the title (what a take would give, <see cref="ResultName"/>) of any cookware from ONE
        /// preview per food, so a refresh runs the sim op once per food instead of once for the label and again for the title.
        /// Gives exactly what <see cref="ContentNames"/> and <see cref="ResultName"/> give.
        /// </summary>
        public IReadOnlyList<string> ContentNamesAndTitle(int vesselId, VesselKind kind, uint tick, double tickRate, out string title)
        {
            if (!VesselRules.IsHeat(kind))
            {
                title = ResultName(vesselId, kind, tick, tickRate);
                return ContentNames(vesselId, kind, tick, tickRate);
            }
            title = null;
            if (_grill.TryGetValue(vesselId, out GrillRecord spot))
            {
                if (spot.Food == null) return new string[0];
                CookOutcome took = TryPreviewGriddleTake(vesselId, tick, tickRate, out Item grilled);
                if ((took == CookOutcome.Ok || took == CookOutcome.TooShort) && grilled != null) title = NameOf(grilled);
                return new[] { HeatLabelRules.GriddleLabel(NameOf(grilled ?? spot.Food), spot.BDown ? 1 : 0) };
            }
            if (!_heat.TryGetValue(vesselId, out List<HeatSession> sessions) || sessions.Count == 0) return new string[0];
            bool shaken = false, greasy = false;
            if (_baskets.TryGetValue(vesselId, out BasketRecord basket))
            {
                shaken = basket.Shaken;
                greasy = BasketStateOf(vesselId, tick) == BasketState.Draining;
            }
            var names = new List<string>(sessions.Count);
            var cooked = new List<Item>(sessions.Count);
            bool allMade = true;
            foreach (HeatSession session in sessions)
            {
                CookOutcome one = CookOne(session, tick, tickRate, shaken, greasy, out Item preview);
                names.Add(HeatLabelRules.Label(NameOf(preview ?? session.Food)));
                if ((one == CookOutcome.Ok || one == CookOutcome.TooShort) && preview != null) cooked.Add(preview);
                else allMade = false;
            }
            if (!allMade) return names;
            if (cooked.Count == 1) title = NameOf(cooked[0]);
            else
            {
                OperationResult mixed = _factory.Apply("mix", cooked, NoParams);
                if (mixed.Ok && mixed.Item != null) title = NameOf(mixed.Item);
            }
            return names;
        }

        /// <summary>
        /// 030 review: a stamp of how long the food in a heat vessel has cooked (the sum of its sessions' cooked ticks, both
        /// griddle sides), -1 when it is empty. The periodic label refresh skips a vessel whose stamp did not change: food on a
        /// heat that is off is not previewed again. Changes that are not time (a flip, a drain, a put-in) refresh at once.
        /// </summary>
        public long HeatStamp(int vesselId, uint tick)
        {
            if (_grill.TryGetValue(vesselId, out GrillRecord spot))
            {
                if (spot.Food == null) return -1;
                long a = spot.Sides[0] != null ? spot.Sides[0].CookedTicks(tick) : 0;
                long b = spot.Sides[1] != null ? spot.Sides[1].CookedTicks(tick) : 0;
                return a + b;
            }
            if (!_heat.TryGetValue(vesselId, out List<HeatSession> sessions) || sessions.Count == 0) return -1;
            long sum = 0;
            foreach (HeatSession session in sessions) sum += session.CookedTicks(tick);
            return sum;
        }

        /// <summary>All label lines joined with " + "; null if the vessel is empty.</summary>
        public string DescribeHeat(int vesselId, uint tick, double tickRate)
        {
            IReadOnlyList<string> names = HeatNames(vesselId, tick, tickRate);
            return names.Count == 0 ? null : string.Join(" + ", names);
        }

        /// <summary>Spec 017: the looks of what is in the pan or pot, with the doneness bucket reached so far.</summary>
        public IReadOnlyList<FoodLook> HeatLooks(int vesselId, uint tick, double tickRate)
        {
            if (!_heat.TryGetValue(vesselId, out List<HeatSession> sessions)) return new FoodLook[0];
            var looks = new List<FoodLook>(sessions.Count);
            foreach (HeatSession session in sessions)
                looks.Add(FoodLookRules.Cooking(session.Food, session.OperationId, CurrentBucket(session, tick, tickRate)));
            return looks;
        }

        /// <summary>
        /// Spec 027c: what the pan or pot shows on the heat at <paramref name="tick"/> (zone, progress, warning), from each
        /// food's time on the heat and its timeline; <see cref="HeatReading.Empty"/> when it is empty. A pure read.
        /// </summary>
        public HeatReading HeatReadingOf(int vesselId, uint tick, double tickRate)
        {
            if (_grill.ContainsKey(vesselId))
            {
                GriddleReadings(vesselId, tick, tickRate, out HeatReading down, out _); // spec 030: the side that cooks now
                return down;
            }
            if (!_heat.TryGetValue(vesselId, out List<HeatSession> sessions)) return HeatReading.Empty;
            HeatReading reading = HeatReading.Empty;
            foreach (HeatSession session in sessions)
            {
                double seconds = CookingTime.SimSeconds(session.CookedTicks(tick), tickRate);
                reading = HeatZoneRules.Combine(reading, HeatZoneRules.Read(seconds, session.Timeline));
            }
            return reading;
        }

        /// <summary>
        /// Spec 027c: the zone timeline of <paramref name="food"/> in the heat op, from the sim op itself. Review of spec 027c:
        /// building it runs the sim op about a hundred times, so it is cached. The key is everything the heat op reads from
        /// the food (<see cref="TimelineKeyOf"/>), not only its hash: the hash is made of bucket ids, while the doneness
        /// inside a bucket comes from the exact seconds (two onions sautéed 60 s and 170 s share a hash). The cache is
        /// cleared when it reaches <see cref="TimelineCacheCapacity"/> entries.
        /// </summary>
        private HeatTimeline TimelineOf(Item food, string opId, int? tempC, ParamDef time)
        {
            string key = TimelineKeyOf(food, opId, tempC);
            if (_timelines.TryGetValue(key, out HeatTimeline cached)) return cached;
            HeatTimeline built = BuildTimeline(food, opId, tempC, time);
            if (_timelines.Count >= TimelineCacheCapacity) _timelines.Clear();
            _timelines[key] = built;
            return built;
        }

        private HeatTimeline BuildTimeline(Item food, string opId, int? tempC, ParamDef time)
        {
            return HeatZoneRules.Build(time, seconds =>
            {
                OperationResult cooked = _factory.Apply(opId, new[] { food }, HeatParams(opId, tempC, seconds));
                return cooked.Ok && cooked.Item != null ? cooked.Item.Components : null;
            });
        }

        /// <summary>Review of spec 027c: the cached timeline for this food, op and temperature (as a put-on gets it).</summary>
        public HeatTimeline TimelineFor(Item food, string opId, int? tempC)
        {
            return food != null && TryGetTimeParam(opId, out ParamDef time) ? TimelineOf(food, opId, tempC, time) : default;
        }

        /// <summary>Review of spec 027c: the same timeline built fresh, never cached (for tests).</summary>
        public HeatTimeline FreshTimeline(Item food, string opId, int? tempC)
        {
            return food != null && TryGetTimeParam(opId, out ParamDef time) ? BuildTimeline(food, opId, tempC, time) : default;
        }

        /// <summary>
        /// Review of spec 027c: the cache key: the item hash, the op, the temperature, and the exact bits of what the heat op
        /// reads (the surface, the heat tolerance, each component's doneness and ideal window).
        /// </summary>
        public static string TimelineKeyOf(Item food, string opId, int? tempC) => TimelineKeyOf(food, opId, tempC, null);

        /// <summary>
        /// Spec 030: the cache key with the side state the timeline was built for (<see cref="SideTimelineKey"/> for a griddle
        /// side; null for a whole item in a pan, pot or basket, the key of 027c unchanged).
        /// </summary>
        public static string TimelineKeyOf(Item food, string opId, int? tempC, string sides)
        {
            var key = new System.Text.StringBuilder(64 + 24 * food.Components.Count);
            key.Append(food.Hash).Append('|').Append(opId).Append('|').Append(tempC.HasValue ? tempC.Value.ToString() : "-")
                .Append('|').Append(Bits(food.Surface)).Append('|').Append(Bits(food.Stats.HeatTolerance));
            if (sides != null) key.Append("|sides:").Append(sides);
            foreach (ItemComponent c in food.Components)
                key.Append('|').Append(c.BaseId).Append(':').Append(Bits(c.Doneness)).Append(':')
                    .Append(Bits(c.Ideal.Min)).Append(':').Append(Bits(c.Ideal.Max));
            return key.ToString();
        }

        private static int Bits(float value) => System.BitConverter.SingleToInt32Bits(value);

        /// <summary>Review of spec 027c: how many timelines are kept at most.</summary>
        public const int TimelineCacheLimit = 256;

        /// <summary>Review of spec 027c: the cache size actually used (<see cref="TimelineCacheLimit"/>; tests set it lower).</summary>
        public int TimelineCacheCapacity { get; set; } = TimelineCacheLimit;

        /// <summary>Review of spec 027c: how many heat timelines are cached (for tests).</summary>
        public int CachedTimelines => _timelines.Count;

        /// <summary>The look of the first ingredient in the pan or pot; null if it is empty.</summary>
        public FoodLook? HeatLook(int vesselId, uint tick, double tickRate)
        {
            IReadOnlyList<FoodLook> looks = HeatLooks(vesselId, tick, tickRate);
            return looks.Count == 0 ? (FoodLook?)null : looks[0];
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

        /// <summary>Heat op parameters; no temperature for the pot (boil has a fixed one). Spec 029: deep fry also takes the shake and
        /// drain choices.</summary>
        private static IReadOnlyDictionary<string, ParamValue> HeatParams(string opId, int? tempC, float seconds, bool shaken = false,
            bool greasy = false)
        {
            var parameters = new Dictionary<string, ParamValue> { ["time"] = ParamValue.Number(seconds) };
            if (tempC.HasValue) parameters["temp"] = ParamValue.Number(tempC.Value);
            if (opId == DeepFry.OperationId)
            {
                parameters[DeepFry.ShakeParam] = ParamValue.Choice(shaken ? DeepFry.Shaken : DeepFry.NotShaken);
                parameters[DeepFry.DrainParam] = ParamValue.Choice(greasy ? DeepFry.Greasy : DeepFry.Drained);
            }
            return parameters;
        }

        // ---- Spec 029: the deep fryer and its baskets ----

        /// <summary>
        /// The basket vessel <paramref name="vesselId"/> is basket <paramref name="basket"/> of fryer station <paramref name="fryer"/>
        /// (raised, empty). Registering it again with the same fryer and basket keeps its state.
        /// </summary>
        public void RegisterBasket(int vesselId, int fryer, int basket)
        {
            if (_baskets.TryGetValue(vesselId, out BasketRecord known) && known.Fryer == fryer && known.Index == basket) return;
            _baskets[vesselId] = new BasketRecord { Fryer = fryer, Index = basket, State = BasketState.Raised };
        }

        /// <summary>The fryer's switch (fryers start off).</summary>
        public bool FryerIsOn(int fryer) => _fryerOn.TryGetValue(fryer, out bool on) && on;

        /// <summary>Switches the fryer on or off; its lowered baskets cook only while it is on (off pauses them).</summary>
        public CookOutcome TryToggleFryer(int fryer, uint tick)
        {
            bool on = !FryerIsOn(fryer);
            _fryerOn[fryer] = on;
            foreach (KeyValuePair<int, BasketRecord> b in _baskets)
                if (b.Value.Fryer == fryer && b.Value.State == BasketState.Lowered) SetRunning(b.Key, on, tick);
            return CookOutcome.Ok;
        }

        /// <summary>
        /// Spec 029b: "lower the basket" (an intent, not a toggle): only with food in it (StationEmpty) and the fryer on
        /// (FryerOff); a basket already in the oil refuses it (BasketLowered), so two players clicking at once never cancel each
        /// other. A draining basket may go back in. Its food fries while it is lowered and the fryer is on.
        /// </summary>
        public CookOutcome TryLowerBasket(int vesselId, uint tick)
        {
            if (!_baskets.TryGetValue(vesselId, out BasketRecord basket)) return CookOutcome.NotAVessel;
            if (basket.State == BasketState.Lowered) return CookOutcome.BasketLowered;
            if (HeatCount(vesselId) == 0) return CookOutcome.StationEmpty;
            if (!FryerIsOn(basket.Fryer)) return CookOutcome.FryerOff;
            basket.State = BasketState.Lowered;
            SetRunning(vesselId, true, tick);
            return CookOutcome.Ok;
        }

        /// <summary>
        /// Spec 029b: "raise the basket": always allowed for a basket in the oil; it then drains for
        /// <see cref="FryerRules.DrainSeconds"/>. A basket that is not in the oil refuses it (NotLowered).
        /// </summary>
        public CookOutcome TryRaiseBasket(int vesselId, uint tick, double tickRate)
        {
            if (!_baskets.TryGetValue(vesselId, out BasketRecord basket)) return CookOutcome.NotAVessel;
            if (basket.State != BasketState.Lowered) return CookOutcome.NotLowered;
            basket.State = BasketState.Draining;
            basket.DrainStart = tick;
            basket.DrainTicks = FryerRules.DrainTicks(tickRate);
            SetRunning(vesselId, false, tick);
            return CookOutcome.Ok;
        }

        /// <summary>
        /// A shake of a lowered basket with food (NotLowered otherwise). Every shake rattles (the counter in the mode byte); the
        /// even-fry bonus counts once per fry, for a shake inside the good window (<see cref="FryerRules.ShakeCounts"/>) while the
        /// fryer is on.
        /// </summary>
        public CookOutcome TryShake(int vesselId, uint tick, double tickRate)
        {
            if (!_baskets.TryGetValue(vesselId, out BasketRecord basket)) return CookOutcome.NotAVessel;
            if (basket.State != BasketState.Lowered || HeatCount(vesselId) == 0) return CookOutcome.NotLowered;
            basket.Shakes++;
            if (!basket.Shaken && FryerIsOn(basket.Fryer) && FryerRules.ShakeCounts(HeatReadingOf(vesselId, tick, tickRate).Zone))
                basket.Shaken = true;
            return CookOutcome.Ok;
        }

        /// <summary>True once this fry earned the shake bonus (for tests and the log).</summary>
        public bool BasketShaken(int vesselId) => _baskets.TryGetValue(vesselId, out BasketRecord b) && b.Shaken;

        /// <summary>Where the basket is at <paramref name="tick"/> (a drain that has run its time reads Raised).</summary>
        public BasketState BasketStateOf(int vesselId, uint tick)
        {
            if (!_baskets.TryGetValue(vesselId, out BasketRecord b)) return BasketState.Raised;
            if (b.State == BasketState.Draining && CookingTime.ElapsedTicks(b.DrainStart, tick) >= b.DrainTicks) return BasketState.Raised;
            return b.State;
        }

        /// <summary>Ends a finished drain; true when the basket's state changed (the caller republishes it).</summary>
        public bool SettleDrain(int vesselId, uint tick)
        {
            if (!_baskets.TryGetValue(vesselId, out BasketRecord b) || b.State != BasketState.Draining) return false;
            if (BasketStateOf(vesselId, tick) != BasketState.Raised) return false;
            b.State = BasketState.Raised;
            return true;
        }

        /// <summary>The basket's synced mode byte (state and shake counter, <see cref="FryerRules.Mode"/>).</summary>
        public byte BasketMode(int vesselId, uint tick)
        {
            int shakes = _baskets.TryGetValue(vesselId, out BasketRecord b) ? b.Shakes : 0;
            return FryerRules.Mode(BasketStateOf(vesselId, tick), shakes);
        }

        /// <summary>The fryer's synced byte: on, and which of its baskets are lowered.</summary>
        public byte FryerFlags(int fryer, uint tick)
        {
            bool l0 = false, l1 = false;
            foreach (KeyValuePair<int, BasketRecord> b in _baskets)
            {
                if (b.Value.Fryer != fryer || BasketStateOf(b.Key, tick) != BasketState.Lowered) continue;
                if (b.Value.Index == 0) l0 = true;
                else if (b.Value.Index == 1) l1 = true;
            }
            return FryerRules.FryerFlags(FryerIsOn(fryer), l0, l1);
        }

        /// <summary>
        /// Puts the held food into a raised basket (BasketLowered while it is in the oil): one portion, any food the sim would deep
        /// fry. It starts frying when the basket is lowered.
        /// </summary>
        public CookOutcome TryPutInBasket(int foodObjectId, int vesselId, uint tick)
        {
            if (!_baskets.TryGetValue(vesselId, out BasketRecord basket)) return CookOutcome.NotAVessel;
            if (!_items.TryGetValue(foodObjectId, out Item item)) return CookOutcome.NotFood;
            if (BasketStateOf(vesselId, tick) == BasketState.Lowered) return CookOutcome.BasketLowered;
            List<HeatSession> sessions = HeatOf(vesselId);
            CookOutcome fits = VesselRules.CanPutIn(VesselKind.Basket, sessions.Count, AssemblyRules.IsPlated(item));
            if (fits != CookOutcome.Ok) return fits;
            const string opId = DeepFry.OperationId;
            int tempC = FryerRules.OilTempC;
            if (!TryGetTimeParam(opId, out ParamDef time)) return CookOutcome.SimRefused;
            OperationResult dry = _factory.Apply(opId, new[] { item }, HeatParams(opId, tempC, time.Max, true, true));
            if (!dry.Ok || dry.Item == null) return CookOutcome.SimRefused;

            var session = new HeatSession(item, opId, tempC, false, tick);
            session.Timeline = TimelineOf(item, opId, tempC, time);
            sessions.Add(session);
            basket.Shaken = false;
            Forget(foodObjectId); // 033 review: the cup's ml and pours go with it
            return CookOutcome.Ok;
        }

        private List<HeatSession> HeatOf(int vesselId)
        {
            if (!_heat.TryGetValue(vesselId, out List<HeatSession> sessions))
            {
                sessions = new List<HeatSession>();
                _heat[vesselId] = sessions;
            }
            return sessions;
        }

        // ---- Spec 030: the griddle and its spots ----

        /// <summary>
        /// The griddle spot vessel <paramref name="vesselId"/> is spot <paramref name="spot"/> of griddle station
        /// <paramref name="griddle"/> (empty). Registering it again with the same griddle and spot keeps its state.
        /// </summary>
        public void RegisterGriddleSpot(int vesselId, int griddle, int spot)
        {
            if (_grill.TryGetValue(vesselId, out GrillRecord known) && known.Griddle == griddle && known.Spot == spot) return;
            _grill[vesselId] = new GrillRecord { Griddle = griddle, Spot = spot };
        }

        /// <summary>The griddle's switch (griddles start off).</summary>
        public bool GriddleIsOn(int griddle) => _griddleOn.TryGetValue(griddle, out bool on) && on;

        /// <summary>Switches the griddle on or off; the down side of every item on it cooks only while it is on.</summary>
        public CookOutcome TryToggleGriddle(int griddle, uint tick)
        {
            bool on = !GriddleIsOn(griddle);
            _griddleOn[griddle] = on;
            foreach (GrillRecord spot in _grill.Values)
                if (spot.Griddle == griddle && spot.Food != null) spot.SetRunning(on, tick);
            return CookOutcome.Ok;
        }

        /// <summary>
        /// Puts the held food on the griddle spot, side 1 down: one item per spot (VesselFull), not a finished plate
        /// (AlreadyPlated), only food the sim would grill or toast (SimRefused). Bread toasts (<see cref="Griddle.OperationFor"/>).
        /// Its down side cooks from <paramref name="tick"/> while the griddle is on.
        /// </summary>
        public CookOutcome TryPutOnGriddle(int foodObjectId, int vesselId, uint tick)
        {
            if (!_grill.TryGetValue(vesselId, out GrillRecord spot)) return CookOutcome.NotAVessel;
            if (!_items.TryGetValue(foodObjectId, out Item item)) return CookOutcome.NotFood;
            CookOutcome fits = VesselRules.CanPutIn(VesselKind.Griddle, spot.Food != null ? 1 : 0, AssemblyRules.IsPlated(item));
            if (fits != CookOutcome.Ok) return fits;
            string opId = Griddle.OperationFor(item);
            if (!TryGetTimeParam(opId, out ParamDef time)) return CookOutcome.SimRefused;
            OperationResult dry = _factory.Apply(opId, new[] { item }, Griddle.Params(time.Max, time.Max));
            if (!dry.Ok || dry.Item == null) return CookOutcome.SimRefused;

            spot.Food = item;
            spot.OperationId = opId;
            spot.BDown = false;
            spot.Sides[0] = new HeatSession(item, opId, null, GriddleIsOn(spot.Griddle), tick);
            spot.Sides[1] = new HeatSession(item, opId, null, false, tick);
            spot.Timeline = SideTimelineOf(item, opId, time);
            Forget(foodObjectId); // 033 review: the cup's ml and pours go with it
            return CookOutcome.Ok;
        }

        /// <summary>
        /// Turns the item on the spot over (any time, also a raw side): the side that was up cooks now, the other stops. The
        /// flip counter in the mode byte goes up, so every viewer sees the flip. StationEmpty on an empty spot. The spatula and
        /// the reach are the caller's checks (<see cref="VesselRules.CheckFlip"/>).
        /// </summary>
        public CookOutcome TryFlip(int vesselId, uint tick)
        {
            if (!_grill.TryGetValue(vesselId, out GrillRecord spot)) return CookOutcome.NotAVessel;
            if (spot.Food == null) return CookOutcome.StationEmpty;
            spot.Down.Stop(tick);
            spot.BDown = !spot.BDown;
            if (GriddleIsOn(spot.Griddle)) spot.Down.Start(tick);
            spot.Flips++;
            return CookOutcome.Ok;
        }

        /// <summary>How often the spot's item was flipped (for tests and the log).</summary>
        public int FlipsOf(int vesselId) => _grill.TryGetValue(vesselId, out GrillRecord spot) ? spot.Flips : 0;

        /// <summary>
        /// The two sides of the item on the spot at <paramref name="tick"/>: each side's sim seconds down on a griddle that was
        /// on, and which side is down now (the sim's <see cref="GriddleSides"/>). Fresh for an empty spot.
        /// </summary>
        public GriddleSides GriddleSidesOf(int vesselId, uint tick, double tickRate)
        {
            if (!_grill.TryGetValue(vesselId, out GrillRecord spot) || spot.Food == null) return GriddleSides.Fresh;
            return new GriddleSides((float)CookingTime.SimSeconds(spot.Sides[0].CookedTicks(tick), tickRate),
                (float)CookingTime.SimSeconds(spot.Sides[1].CookedTicks(tick), tickRate), spot.BDown);
        }

        /// <summary>
        /// What taking the item off the spot gives now, without changing it: the griddle (or toast) op with both sides' times,
        /// or TooShort (the food unchanged) when its longer side had less than <see cref="GriddleRules.MinCookSimSeconds"/>.
        /// StationEmpty on an empty spot.
        /// </summary>
        public CookOutcome TryPreviewGriddleTake(int vesselId, uint tick, double tickRate, out Item result)
        {
            result = null;
            if (!_grill.TryGetValue(vesselId, out GrillRecord spot) || spot.Food == null) return CookOutcome.StationEmpty;
            GriddleSides sides = GriddleSidesOf(vesselId, tick, tickRate);
            if (System.Math.Max(sides.SideA, sides.SideB) < GriddleRules.MinCookSimSeconds)
            {
                result = spot.Food;
                return CookOutcome.TooShort;
            }
            OperationResult cooked = _factory.Apply(spot.OperationId, new[] { spot.Food }, Griddle.Params(sides));
            if (!cooked.Ok || cooked.Item == null) return CookOutcome.SimRefused;
            result = cooked.Item;
            return CookOutcome.Ok;
        }

        /// <summary>
        /// The zone readings of the spot's two sides, from each side's own time and the item's side timeline: the down side
        /// (the Vessel's heat bytes) and the up side (in the mode byte). Empty readings for an empty spot.
        /// </summary>
        public void GriddleReadings(int vesselId, uint tick, double tickRate, out HeatReading down, out HeatReading up)
        {
            down = HeatReading.Empty;
            up = HeatReading.Empty;
            if (!_grill.TryGetValue(vesselId, out GrillRecord spot) || spot.Food == null) return;
            GriddleSides sides = GriddleSidesOf(vesselId, tick, tickRate);
            down = HeatZoneRules.Read(sides.Down, spot.Timeline);
            up = HeatZoneRules.Read(sides.Up, spot.Timeline);
        }

        /// <summary>The spot's synced mode byte: the down side, the flip counter and the up side (<see cref="GriddleRules.Mode"/>).</summary>
        public byte GriddleMode(int vesselId, uint tick, double tickRate)
        {
            if (!_grill.TryGetValue(vesselId, out GrillRecord spot)) return 0;
            GriddleReadings(vesselId, tick, tickRate, out _, out HeatReading up);
            return GriddleRules.Mode(spot.BDown ? 1 : 0, spot.Flips, up);
        }

        /// <summary>The griddle's synced byte (on).</summary>
        public byte GriddleFlags(int griddle) => GriddleRules.Flags(GriddleIsOn(griddle));

        /// <summary>The label of the item on the spot: its preview name and the side that is down (<see cref="HeatLabelRules"/>).</summary>
        private IReadOnlyList<string> GriddleNames(int vesselId, uint tick, double tickRate)
        {
            if (!_grill.TryGetValue(vesselId, out GrillRecord spot) || spot.Food == null) return new string[0];
            TryPreviewGriddleTake(vesselId, tick, tickRate, out Item preview);
            return new[] { HeatLabelRules.GriddleLabel(NameOf(preview ?? spot.Food), spot.BDown ? 1 : 0) };
        }

        /// <summary>The look of the item on the spot: as it went on (its top's colour and the grill marks are drawn by the client
        /// from the up side's zone).</summary>
        private IReadOnlyList<FoodLook> GriddleLooks(int vesselId)
        {
            if (!_grill.TryGetValue(vesselId, out GrillRecord spot) || spot.Food == null) return new FoodLook[0];
            return new[] { FoodLookRules.From(spot.Food) };
        }

        private void ClearGriddle(int vesselId)
        {
            if (!_grill.TryGetValue(vesselId, out GrillRecord spot)) return;
            spot.Food = null;
            spot.OperationId = null;
            spot.Sides[0] = null;
            spot.Sides[1] = null;
            spot.BDown = false;
        }

        /// <summary>
        /// The zone timeline of ONE side of <paramref name="food"/> on the griddle (cached like <see cref="TimelineOf"/>): built
        /// from the real sim op with both sides at the same time, so the item's doneness is that side's
        /// (<see cref="Griddle.CombineSides"/> of two equal sides). Each side cooks on its own along it; the key carries the side
        /// state it was built for (<see cref="SideTimelineKey"/>), so it never mixes with a whole-item timeline.
        /// </summary>
        private HeatTimeline SideTimelineOf(Item food, string opId, ParamDef time)
        {
            string key = TimelineKeyOf(food, opId, null, SideTimelineKey);
            if (_timelines.TryGetValue(key, out HeatTimeline cached)) return cached;
            HeatTimeline built = BuildSideTimeline(food, opId, time);
            if (_timelines.Count >= TimelineCacheCapacity) _timelines.Clear();
            _timelines[key] = built;
            return built;
        }

        private HeatTimeline BuildSideTimeline(Item food, string opId, ParamDef time)
        {
            return HeatZoneRules.Build(time, seconds =>
            {
                OperationResult cooked = _factory.Apply(opId, new[] { food }, Griddle.Params(seconds, seconds));
                return cooked.Ok && cooked.Item != null ? cooked.Item.Components : null;
            });
        }

        /// <summary>Spec 030: the side timeline of a food on the griddle, built fresh (for tests and the measured timings).</summary>
        public HeatTimeline FreshSideTimeline(Item food)
        {
            string opId = Griddle.OperationFor(food);
            return food != null && TryGetTimeParam(opId, out ParamDef time) ? BuildSideTimeline(food, opId, time) : default;
        }

        /// <summary>Spec 030: the side state a griddle timeline is built for: one side, the other side cooked the same.</summary>
        public const string SideTimelineKey = "one-side";

        private sealed class GrillRecord
        {
            public int Griddle;
            public int Spot;
            public Item Food;
            public string OperationId;
            /// <summary>Side 1 and side 2: each counts its own ticks down on a griddle that is on.</summary>
            public readonly HeatSession[] Sides = new HeatSession[2];
            public bool BDown;
            public int Flips;
            public HeatTimeline Timeline;

            public HeatSession Down => Sides[BDown ? 1 : 0];

            public void SetRunning(bool on, uint tick)
            {
                if (Down == null) return;
                if (on) Down.Start(tick);
                else Down.Stop(tick);
            }
        }

        // ---- Spec 031: the assembly board ----

        private sealed class BuildRecord
        {
            public int Board;
            /// <summary>The build on the spot (the sim item is the whole state), null when empty.</summary>
            public Item Build;
            /// <summary>The top layer is a squeeze of this sauce for this many seconds (the next squeeze of it adds to it).</summary>
            public string TopSauce;
            public float TopSauceSeconds;
        }

        private sealed class SqueezeRecord
        {
            public int Owner;
            public uint StartTick;
        }

        private readonly Dictionary<int, BuildRecord> _builds = new Dictionary<int, BuildRecord>();
        private readonly Dictionary<int, int> _buildSpotOf = new Dictionary<int, int>();
        private readonly Dictionary<long, SqueezeRecord> _squeezes = new Dictionary<long, SqueezeRecord>();

        private static long SqueezeKey(int board, int dispenser) => ((long)board << 8) | (uint)dispenser;

        /// <summary>The build spot vessel <paramref name="vesselId"/> belongs to assembly board <paramref name="board"/> (empty).</summary>
        public void RegisterAssemblySpot(int vesselId, int board)
        {
            if (!_builds.TryGetValue(vesselId, out BuildRecord known) || known.Board != board)
                _builds[vesselId] = new BuildRecord { Board = board };
            _buildSpotOf[board] = vesselId;
        }

        /// <summary>The build spot of board <paramref name="board"/>, or -1.</summary>
        public int BuildSpotOf(int board) => _buildSpotOf.TryGetValue(board, out int id) ? id : -1;

        /// <summary>The build on the spot, or null.</summary>
        public Item BuildOn(int vesselId) => _builds.TryGetValue(vesselId, out BuildRecord r) ? r.Build : null;

        /// <summary>How many layers the build on the spot has.</summary>
        public int LayerCount(int vesselId) => Assembly.LayersOf(BuildOn(vesselId)).Count;

        /// <summary>The spot's synced mode byte (<see cref="BuildRules.Mode"/>).</summary>
        public byte BuildMode(int vesselId) => BuildRules.ModeOf(BuildOn(vesselId));

        private string BuildName(int vesselId)
        {
            Item build = BuildOn(vesselId);
            return build != null ? NameOf(build) : null;
        }

        private IReadOnlyList<FoodLook> BuildLooks(int vesselId)
        {
            IReadOnlyList<Item> layers = Assembly.LayersOf(BuildOn(vesselId));
            var looks = new FoodLook[layers.Count];
            for (int i = 0; i < layers.Count; i++) looks[i] = FoodLookRules.From(layers[i]);
            return looks;
        }

        /// <summary>
        /// What the build with <paramref name="layer"/> on top would be, without changing anything: the next layer of an open
        /// build (a new build when the spot is empty); a squeeze of the sauce that is already on top adds its seconds to that layer
        /// (one squeeze held twice is one amount). StationEmpty never; BuildClosed on a closed build; VesselFull at
        /// <see cref="CookingLimits.MaxBuildLayers"/>; AlreadyPlated for finished food; SimRefused when the sim says no.
        /// </summary>
        public CookOutcome TryPreviewLayer(int vesselId, Item layer, out Item build, float squeezeSeconds = -1f, string sauce = null)
        {
            build = null;
            if (!_builds.TryGetValue(vesselId, out BuildRecord spot)) return CookOutcome.NotAVessel;
            if (layer == null) return CookOutcome.NotFood;
            if (AssemblyRules.IsPlated(layer)) return CookOutcome.AlreadyPlated;
            var layers = new List<Item>(Assembly.LayersOf(spot.Build));
            if (spot.Build != null && spot.Build.OperationId != Assembly.AssembleOperationId) return CookOutcome.BuildClosed; // closed or packed
            if (spot.Build != null && Assembly.PackOf(spot.Build) != null) return CookOutcome.BuildClosed;
            if (sauce != null && spot.TopSauce == sauce && layers.Count > 0)
            {
                OperationResult more = _factory.Apply(Assembly.SqueezeOperationId, new[] { SauceBase(sauce) },
                    Assembly.SqueezeParams(spot.TopSauceSeconds + squeezeSeconds));
                if (!more.Ok || more.Item == null) return CookOutcome.SimRefused;
                layers[layers.Count - 1] = more.Item;
            }
            else
            {
                if (layers.Count >= CookingLimits.MaxBuildLayers) return CookOutcome.VesselFull;
                layers.Add(layer);
            }
            OperationResult made = _factory.Apply(Assembly.AssembleOperationId, layers, NoParams);
            if (!made.Ok || made.Item == null) return CookOutcome.SimRefused;
            build = made.Item;
            return CookOutcome.Ok;
        }

        private Item SauceBase(string sauce) => _factory.CreateBase(sauce).Item;

        /// <summary>
        /// Puts the held food on the build: the next layer (a build taken off earlier goes back on an empty spot as it was). On
        /// success the food no longer exists as an item in the world. Refusals change nothing.
        /// </summary>
        public CookOutcome TryPutOnBuild(int foodObjectId, int vesselId)
        {
            if (!_builds.TryGetValue(vesselId, out BuildRecord spot)) return CookOutcome.NotAVessel;
            if (!_items.TryGetValue(foodObjectId, out Item item)) return CookOutcome.NotFood;
            if (Assembly.IsBuild(item))
            {
                // A build back on the board: only on an empty spot, and never on a plate (a plate of a build is sold as it is);
                // a finished build (in paper, a box or a bag) goes back as it was.
                if (spot.Build != null) return CookOutcome.VesselFull;
                if (Assembly.BuildNode(item) == null || item.OperationId == "plate") return CookOutcome.AlreadyPlated;
                spot.Build = item;
                spot.TopSauce = null;
                Forget(foodObjectId); // 033 review: the cup's ml and pours go with it
                return CookOutcome.Ok;
            }
            CookOutcome fits = TryPreviewLayer(vesselId, item, out Item build);
            if (fits != CookOutcome.Ok) return fits;
            spot.Build = build;
            spot.TopSauce = null;
            Forget(foodObjectId); // 033 review: the cup's ml and pours go with it
            return CookOutcome.Ok;
        }

        /// <summary>Folds the open build on bread (sim close "folded"). StationEmpty, BuildClosed, NothingToFold; no change then.</summary>
        public CookOutcome TryFold(int vesselId)
        {
            if (!_builds.TryGetValue(vesselId, out BuildRecord spot)) return CookOutcome.NotAVessel;
            if (spot.Build == null) return CookOutcome.StationEmpty;
            if (spot.Build.OperationId != Assembly.AssembleOperationId) return CookOutcome.BuildClosed;
            if (!Assembly.HasBreadBase(spot.Build) || Assembly.LayersOf(spot.Build).Count < 2) return CookOutcome.NothingToFold;
            OperationResult folded = _factory.Apply(Assembly.CloseOperationId, new[] { spot.Build }, Assembly.CloseParams(Assembly.Folded));
            if (!folded.Ok || folded.Item == null) return CookOutcome.SimRefused;
            spot.Build = folded.Item;
            spot.TopSauce = null;
            return CookOutcome.Ok;
        }

        /// <summary>
        /// What packaging <paramref name="packaging"/> (<see cref="BuildRules.Paper"/>, Box, Bag) makes of the build, without changing
        /// it: paper wraps a folded build, a box or bag packs a folded one on bread or an open one without. StationEmpty,
        /// BuildClosed (finished), FoldFirst (bread not folded), NothingToFold (paper without bread), SimRefused.
        /// </summary>
        public CookOutcome TryPreviewPackaging(int vesselId, int packaging, out Item result)
        {
            result = null;
            if (!_builds.TryGetValue(vesselId, out BuildRecord spot)) return CookOutcome.NotAVessel;
            if (!BuildRules.TryStep(packaging, out string opId, out IReadOnlyDictionary<string, ParamValue> parameters))
                return CookOutcome.InvalidOption;
            CookOutcome fits = BuildRules.CanPackage(packaging, LayerCount(vesselId), BuildMode(vesselId));
            if (fits != CookOutcome.Ok) return fits;
            OperationResult made = _factory.Apply(opId, new[] { spot.Build }, parameters);
            if (!made.Ok || made.Item == null) return made.Error == OperationError.StepOrder ? CookOutcome.FoldFirst : CookOutcome.SimRefused;
            result = made.Item;
            return CookOutcome.Ok;
        }

        /// <summary>The build on the spot becomes <paramref name="build"/> (a preview the caller has paid for).</summary>
        public bool CommitBuild(int vesselId, Item build, string topSauce = null, float topSauceSeconds = 0f)
        {
            // Spec 034: false when there is no such build spot (the caller's refund backstop gives back what it charged).
            if (!_builds.TryGetValue(vesselId, out BuildRecord spot)) return false;
            spot.Build = build;
            spot.TopSauce = topSauce;
            spot.TopSauceSeconds = topSauceSeconds;
            return true;
        }

        /// <summary>Taking the build off: it as it is (StationEmpty when there is none). The caller hands it over, then clears.</summary>
        public CookOutcome TryPreviewBuildTake(int vesselId, out Item result)
        {
            result = BuildOn(vesselId);
            return result != null ? CookOutcome.Ok : CookOutcome.StationEmpty;
        }

        private void ClearBuild(int vesselId)
        {
            if (!_builds.TryGetValue(vesselId, out BuildRecord spot)) return;
            spot.Build = null;
            spot.TopSauce = null;
        }

        /// <summary>
        /// A player presses dispenser <paramref name="dispenser"/> of board <paramref name="board"/>: the squeeze starts at
        /// <paramref name="tick"/> (the server's clock). StationEmpty (nothing on the board), BuildClosed, DispenserBusy (someone
        /// else squeezes it); a second press by the same player while it runs keeps the first start. Nothing is charged or changed but the squeeze record.
        /// </summary>
        public CookOutcome TryStartSqueeze(int board, int dispenser, int owner, uint tick)
        {
            if (BuildRules.SauceOf(dispenser) == null) return CookOutcome.InvalidOption;
            int spot = BuildSpotOf(board);
            if (spot < 0) return CookOutcome.UnknownStation;
            CookOutcome can = BuildRules.CanSqueeze(LayerCount(spot), BuildMode(spot));
            if (can != CookOutcome.Ok) return can;
            long key = SqueezeKey(board, dispenser);
            // 031b review: a press that could never become a layer (a full build) is refused now, so nobody holds a dispenser busy
            // for free. The preview merges with the same sauce on top, as the release would.
            if (!_squeezes.ContainsKey(key))
            {
                string sauce = BuildRules.SauceOf(dispenser);
                OperationResult probe = _factory.Apply(Assembly.SqueezeOperationId, new[] { SauceBase(sauce) }, Assembly.SqueezeParams(0f));
                if (!probe.Ok || probe.Item == null) return CookOutcome.SimRefused;
                CookOutcome fits = TryPreviewLayer(spot, probe.Item, out _, 0f, sauce);
                if (fits != CookOutcome.Ok) return fits;
            }
            if (_squeezes.TryGetValue(key, out SqueezeRecord running))
            {
                // Spec 031b 10.0: a repeated press by the same player keeps the original start (Ok, nothing changes), so pressing
                // again before the auto-stop can never hold the dispenser for ever.
                return running.Owner == owner ? CookOutcome.Ok : CookOutcome.DispenserBusy;
            }
            _squeezes[key] = new SqueezeRecord { Owner = owner, StartTick = tick };
            return CookOutcome.Ok;
        }

        /// <summary>Whether dispenser <paramref name="dispenser"/> of board <paramref name="board"/> is squeezing, and by whom.</summary>
        public bool IsSqueezing(int board, int dispenser, out int owner, out uint startTick)
        {
            owner = -1;
            startTick = 0;
            if (!_squeezes.TryGetValue(SqueezeKey(board, dispenser), out SqueezeRecord r)) return false;
            owner = r.Owner;
            startTick = r.StartTick;
            return true;
        }

        /// <summary>
        /// The player lets go (or the server stops the squeeze at <see cref="BuildRules.AutoStopSeconds"/>): the squeeze ends now and
        /// the result is previewed: the sauce for the seconds between the press and <paramref name="tick"/> as the next layer (or
        /// added to the same sauce on top). The record is gone whatever happens next. InvalidOption when this player was not
        /// squeezing it; the preview's refusals (StationEmpty, BuildClosed, VesselFull, SimRefused). Nothing changes on the board:
        /// the caller charges the sauce, then <see cref="CommitBuild"/>s the build with the top sauce and seconds.
        /// </summary>
        public CookOutcome TryEndSqueeze(int board, int dispenser, int owner, uint tick, double tickRate, out Item build, out float seconds) =>
            TryEndSqueeze(board, dispenser, owner, tick, tickRate, out build, out seconds, out _);

        /// <summary>
        /// 031b: as <see cref="TryEndSqueeze(int, int, int, uint, double, out Item, out float)"/>, plus this squeeze's own seconds
        /// (<paramref name="ownSeconds"/>, what its portion costs), set as soon as the record is found.
        /// </summary>
        public CookOutcome TryEndSqueeze(int board, int dispenser, int owner, uint tick, double tickRate, out Item build, out float seconds,
            out float ownSeconds)
        {
            build = null;
            seconds = 0f;
            ownSeconds = 0f;
            long key = SqueezeKey(board, dispenser);
            if (!_squeezes.TryGetValue(key, out SqueezeRecord r) || r.Owner != owner) return CookOutcome.InvalidOption;
            _squeezes.Remove(key);
            seconds = Cap(BuildRules.SqueezeSeconds(r.StartTick, tick, tickRate), BuildRules.AutoStopSeconds);
            ownSeconds = seconds;
            string sauce = BuildRules.SauceOf(dispenser);
            int spot = BuildSpotOf(board);
            if (spot < 0) return CookOutcome.UnknownStation;
            if (BuildOn(spot) == null) return CookOutcome.StationEmpty;
            _builds.TryGetValue(spot, out BuildRecord rec);
            bool merge = rec.TopSauce == sauce;
            float total = merge ? rec.TopSauceSeconds + seconds : seconds;
            OperationResult squeezed = _factory.Apply(Assembly.SqueezeOperationId, new[] { SauceBase(sauce) }, Assembly.SqueezeParams(seconds));
            if (!squeezed.Ok || squeezed.Item == null) return CookOutcome.SimRefused;
            CookOutcome made = TryPreviewLayer(spot, squeezed.Item, out build, seconds, sauce);
            if (made != CookOutcome.Ok) return made;
            seconds = total; // what the top layer holds now
            return CookOutcome.Ok;
        }

        private static float Cap(float seconds, float cap) => seconds > cap ? cap : seconds;

        /// <summary>A squeeze that started before <paramref name="tick"/> minus <see cref="BuildRules.AutoStopSeconds"/>, or false.</summary>
        public bool TryFindOverdueSqueeze(uint tick, double tickRate, out int board, out int dispenser, out int owner)
        {
            board = dispenser = owner = -1;
            foreach (KeyValuePair<long, SqueezeRecord> kv in _squeezes)
            {
                if (BuildRules.SqueezeSeconds(kv.Value.StartTick, tick, tickRate) < BuildRules.AutoStopSeconds) continue;
                board = (int)(kv.Key >> 8);
                dispenser = (int)(kv.Key & 0xFF);
                owner = kv.Value.Owner;
                return true;
            }
            return false;
        }

        /// <summary>Drops a squeeze without a result (the player left).</summary>
        public void CancelSqueeze(int board, int dispenser) => _squeezes.Remove(SqueezeKey(board, dispenser));

        /// <summary>The board's synced byte: a squeezing bit per dispenser.</summary>
        public byte AssemblyFlags(int board)
        {
            byte flags = 0;
            for (int d = 0; d < BuildRules.DispenserCount; d++)
                if (_squeezes.ContainsKey(SqueezeKey(board, d))) flags = BuildRules.WithSqueezing(flags, d, true);
            return flags;
        }

        // ---- Spec 033: the drink dispenser ----

        private sealed class PourRecord
        {
            public int Owner;
            public uint StartTick;
            /// <summary>The cup's object id in the hands at the press (the release fills only that cup).</summary>
            public int Cup;
            /// <summary>The exact ml in the cup at the press (0 for an empty cup).</summary>
            public float Before;
            public bool WasEmpty;
            /// <summary>033 review: the cup's item at the press; the release fills only that same item.</summary>
            public Item CupItem;
        }

        private readonly Dictionary<long, PourRecord> _pours = new Dictionary<long, PourRecord>();
        // The exact ml in each filled cup (server only, D21: clients see the bucket), per food object id.
        private readonly Dictionary<int, float> _cupMl = new Dictionary<int, float>();
        // The dispensers with a spill on the counter (station index). Spec 035 (cleaning) consumes it (ClearSpill).
        private readonly HashSet<int> _spills = new HashSet<int>();

        private static long PourKey(int station, int tap) => ((long)station << 8) | (uint)(tap & 0xFF);

        /// <summary>A new empty cup (the base item "cup"). Register it under its object id once spawned.</summary>
        public CookOutcome TryCreateCup(out Item cup) => TryCreateIngredient(Drinks.CupId, out cup);

        /// <summary>How many empty cups exist as food objects now (DrinkRules.MaxEmptyCups caps the free cups).</summary>
        public int EmptyCupCount()
        {
            int n = 0;
            foreach (KeyValuePair<int, Item> kv in _items)
                if (Drinks.IsEmptyCup(kv.Value)) n++;
            return n;
        }

        /// <summary>The exact ml in cup <paramref name="cupObjectId"/> (server only), 0 when empty or unknown.</summary>
        public float CupMl(int cupObjectId) => _cupMl.TryGetValue(cupObjectId, out float ml) ? ml : 0f;

        /// <summary>
        /// Whether the item <paramref name="cupObjectId"/> can take a pour from tap <paramref name="tap"/>: an empty cup, or an open cup of
        /// that drink not yet at the server's stop. NeedsCup (no cup), CupClosed (lidded), OtherDrink, CupFull.
        /// </summary>
        public CookOutcome CanPourInto(int cupObjectId, int tap)
        {
            if (!DrinkRules.IsTap(tap)) return CookOutcome.InvalidOption;
            if (!_items.TryGetValue(cupObjectId, out Item cup) || !Drinks.IsCup(cup)) return CookOutcome.NeedsCup;
            if (Drinks.IsLidded(cup)) return CookOutcome.CupClosed;
            if (Drinks.IsEmptyCup(cup)) return CookOutcome.Ok;
            if (Drinks.DrinkOf(cup) != DrinkRules.DrinkOf(tap)) return CookOutcome.OtherDrink;
            return CupMl(cupObjectId) >= Drinks.MaxMl || Drinks.LevelOf(cup) == Drinks.Spilled ? CookOutcome.CupFull : CookOutcome.Ok;
        }

        /// <summary>
        /// A player presses tap <paramref name="tap"/> of dispenser <paramref name="station"/> holding cup <paramref name="cupObjectId"/>:
        /// the pour starts at <paramref name="tick"/> (the server's clock). The cup's refusals (<see cref="CanPourInto"/>), DispenserBusy
        /// (someone else pours that tap, or this player already pours another one); a second press by the same player while it runs
        /// keeps the first start (031b, 10.0). Nothing is charged or changed but the pour record.
        /// </summary>
        public CookOutcome TryStartPour(int station, int tap, int owner, int cupObjectId, uint tick)
        {
            if (!DrinkRules.IsTap(tap)) return CookOutcome.InvalidOption;
            long key = PourKey(station, tap);
            if (_pours.TryGetValue(key, out PourRecord running)) return running.Owner == owner ? CookOutcome.Ok : CookOutcome.DispenserBusy;
            CookOutcome can = CanPourInto(cupObjectId, tap);
            if (can != CookOutcome.Ok) return can;
            foreach (KeyValuePair<long, PourRecord> kv in _pours)
                if (kv.Value.Owner == owner || kv.Value.Cup == cupObjectId) return CookOutcome.DispenserBusy; // one pour per player and cup
            _items.TryGetValue(cupObjectId, out Item cup);
            _pours[key] = new PourRecord
            {
                Owner = owner, StartTick = tick, Cup = cupObjectId, Before = CupMl(cupObjectId), WasEmpty = Drinks.IsEmptyCup(cup),
                CupItem = cup,
            };
            return CookOutcome.Ok;
        }

        /// <summary>Whether tap <paramref name="tap"/> of dispenser <paramref name="station"/> pours, by whom and into which cup.</summary>
        public bool IsPouring(int station, int tap, out int owner, out uint startTick, out int cupObjectId)
        {
            owner = -1;
            startTick = 0;
            cupObjectId = -1;
            if (!_pours.TryGetValue(PourKey(station, tap), out PourRecord r)) return false;
            owner = r.Owner;
            startTick = r.StartTick;
            cupObjectId = r.Cup;
            return true;
        }

        /// <summary>
        /// The player lets go (or the server stops the pour at <see cref="Drinks.MaxMl"/>): the pour ends at <paramref name="tick"/> and the
        /// cup is previewed: the drink for the ml held since the press onto what was in it (capped at the stop). The record is gone
        /// whatever happens next. InvalidOption when this player was not pouring that tap; NeedsCup when the player no longer holds the
        /// cup of the press (<paramref name="heldCupId"/>, -1 for nothing) or it is gone; SimRefused. Nothing changes on the cup: the caller
        /// charges <paramref name="poured"/> (and the cup when <paramref name="wasEmpty"/>), then <see cref="CommitCup"/>s it.
        /// </summary>
        public CookOutcome TryEndPour(int station, int tap, int owner, uint tick, double tickRate, int heldCupId, out Item cup, out float total,
            out float poured, out bool wasEmpty, out int cupObjectId)
        {
            cup = null;
            total = poured = 0f;
            wasEmpty = false;
            cupObjectId = -1;
            long key = PourKey(station, tap);
            if (!_pours.TryGetValue(key, out PourRecord r) || r.Owner != owner) return CookOutcome.InvalidOption;
            _pours.Remove(key);
            cupObjectId = r.Cup;
            wasEmpty = r.WasEmpty;
            total = DrinkRules.Total(r.Before, DrinkRules.PourMl(r.StartTick, tick, tickRate));
            poured = total - r.Before;
            if (heldCupId != r.Cup || !_items.TryGetValue(r.Cup, out Item now) || !ReferenceEquals(now, r.CupItem) || !Drinks.IsCup(now)
                || Drinks.IsLidded(now))
                return CookOutcome.NeedsCup;
            OperationResult filled = _factory.Apply(Drinks.FillOperationId, new[] { _factory.CreateBase(DrinkRules.DrinkOf(tap)).Item },
                Drinks.FillParams(total));
            if (!filled.Ok || filled.Item == null) return CookOutcome.SimRefused;
            cup = filled.Item;
            return CookOutcome.Ok;
        }

        /// <summary>Cup <paramref name="cupObjectId"/> becomes <paramref name="cup"/> holding exactly <paramref name="ml"/> (a paid preview).</summary>
        public bool CommitCup(int cupObjectId, Item cup, float ml)
        {
            // Spec 034: false when the cup is no longer an item here (the caller's refund backstop gives back what it charged).
            if (cup == null || !_items.ContainsKey(cupObjectId)) return false;
            _items[cupObjectId] = cup;
            _cupMl[cupObjectId] = Drinks.ClampMl(ml);
            return true;
        }

        /// <summary>A pour whose ml reached <see cref="Drinks.MaxMl"/> by <paramref name="tick"/>, or false.</summary>
        public bool TryFindOverduePour(uint tick, double tickRate, out int station, out int tap, out int owner)
        {
            station = tap = owner = -1;
            foreach (KeyValuePair<long, PourRecord> kv in _pours)
            {
                if (kv.Value.Before + DrinkRules.PourMl(kv.Value.StartTick, tick, tickRate) < Drinks.MaxMl) continue;
                station = (int)(kv.Key >> 8);
                tap = (int)(kv.Key & 0xFF);
                owner = kv.Value.Owner;
                return true;
            }
            return false;
        }

        /// <summary>Drops a pour without a result (the player left).</summary>
        public void CancelPour(int station, int tap) => _pours.Remove(PourKey(station, tap));

        private readonly List<long> _cancelledPours = new List<long>();

        /// <summary>033 review: every pour into cup <paramref name="cupObjectId"/> is dropped without a result (the cup left the registry).</summary>
        public void CancelPoursOfCup(int cupObjectId)
        {
            if (_pours.Count == 0) return;
            _cancelledPours.Clear();
            foreach (KeyValuePair<long, PourRecord> kv in _pours)
                if (kv.Value.Cup == cupObjectId) _cancelledPours.Add(kv.Key);
            foreach (long key in _cancelledPours) _pours.Remove(key);
        }

        /// <summary>
        /// The lid on cup <paramref name="cupObjectId"/>, without changing it: an open cup with a drink in it. NeedsCup, CupClosed,
        /// NotFilled, DispenserBusy (it is under a tap), SimRefused.
        /// </summary>
        public CookOutcome TryPreviewLid(int cupObjectId, out Item lidded)
        {
            lidded = null;
            if (!_items.TryGetValue(cupObjectId, out Item cup) || !Drinks.IsCup(cup)) return CookOutcome.NeedsCup;
            if (Drinks.IsLidded(cup)) return CookOutcome.CupClosed;
            if (Drinks.IsEmptyCup(cup)) return CookOutcome.NotFilled;
            foreach (KeyValuePair<long, PourRecord> kv in _pours)
                if (kv.Value.Cup == cupObjectId) return CookOutcome.DispenserBusy;
            OperationResult made = _factory.Apply(Drinks.LidOperationId, new[] { cup }, NoParams);
            if (!made.Ok || made.Item == null) return CookOutcome.SimRefused;
            lidded = made.Item;
            return CookOutcome.Ok;
        }

        /// <summary>A lidded cup keeps its exact ml (a preview of <see cref="TryPreviewLid"/> the caller commits).</summary>
        public void CommitLid(int cupObjectId, Item lidded)
        {
            if (lidded == null || !_items.ContainsKey(cupObjectId)) return;
            _items[cupObjectId] = lidded;
        }

        /// <summary>Spec 033: a pour ran over the brim at dispenser <paramref name="station"/>: the spill stays until spec 035 cleans it.</summary>
        public void MarkSpill(int station) => _spills.Add(station);

        /// <summary>Whether dispenser <paramref name="station"/> has a spill on the counter.</summary>
        public bool HasSpill(int station) => _spills.Contains(station);

        /// <summary>Spec 035's hook: the spill at dispenser <paramref name="station"/> is wiped (true when there was one).</summary>
        public bool ClearSpill(int station) => _spills.Remove(station);

        /// <summary>The dispenser's synced byte: a pouring bit per tap and the spill bit.</summary>
        public byte DrinkFlags(int station)
        {
            byte flags = 0;
            for (int t = 0; t < DrinkRules.TapCount; t++)
                if (_pours.ContainsKey(PourKey(station, t))) flags = DrinkRules.WithPouring(flags, t, true);
            return DrinkRules.WithSpill(flags, HasSpill(station));
        }

        // ---- Spec 013/021: plate and bowl ----

        /// <summary>
        /// Moves a food item onto a plate or into a bowl. On success the food no longer exists as an item in the world
        /// (the caller despawns its object). AlreadyPlated, VesselFull or SimRefused change nothing.
        /// </summary>
        public CookOutcome TryAddToVessel(int foodObjectId, int vesselId, VesselKind kind)
        {
            if (!_items.TryGetValue(foodObjectId, out Item item)) return CookOutcome.NotFood;
            CookOutcome allowed = CanAddItem(vesselId, kind, item);
            if (allowed != CookOutcome.Ok) return allowed;
            AssemblyOf(vesselId).Add(new AssemblyEntry(item));
            Forget(foodObjectId); // 033 review: the cup's ml and pours go with it
            return CookOutcome.Ok;
        }

        /// <summary>
        /// Whether the item may go onto the plate or into the bowl. Dry run: the sim flattens nested mixes and has
        /// depth/component limits, so the count alone does not tell whether taking will work; refuse now instead of
        /// locking the vessel later (spec 013 review blocker). A first item in a bowl comes back unchanged, no mix check.
        /// </summary>
        private CookOutcome CanAddItem(int vesselId, VesselKind kind, Item item)
        {
            List<AssemblyEntry> contents = AssemblyOf(vesselId);
            CookOutcome allowed = AssemblyRules.CanAdd(kind, contents.Count, item);
            if (allowed != CookOutcome.Ok) return allowed;
            bool singleInBowl = kind == VesselKind.Bowl && contents.Count == 0;
            if (singleInBowl) return CookOutcome.Ok;
            var inputs = new List<Item>(contents.Count + 1);
            foreach (AssemblyEntry entry in contents) inputs.Add(entry.Item);
            inputs.Add(item);
            return _factory.Apply(AssemblyRules.OperationFor(kind), inputs, NoParams).Ok ? CookOutcome.Ok : CookOutcome.SimRefused;
        }

        /// <summary>Takes everything from the plate or bowl as one item. On failure the contents stay.</summary>
        public CookOutcome TryTakeFromVessel(int vesselId, VesselKind kind, out Item result)
        {
            CookOutcome outcome = TryPreviewTake(vesselId, kind, out result);
            if (outcome == CookOutcome.Ok) ClearVessel(vesselId);
            return outcome;
        }

        /// <summary>
        /// What the plate (sim plate of all parts: the dish that is sold) or the bowl (sim mix; a single item comes back
        /// unchanged) gives, without changing it. The caller hands it over (or sells it) first, then clears.
        /// </summary>
        public CookOutcome TryPreviewTake(int vesselId, VesselKind kind, out Item result)
        {
            result = null;
            if (!AssemblyRules.IsAssembly(kind)) return CookOutcome.InvalidOption;
            List<AssemblyEntry> contents = AssemblyOf(vesselId);
            if (contents.Count == 0) return CookOutcome.StationEmpty;

            if (kind == VesselKind.Bowl && contents.Count == 1)
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

        public void ClearVessel(int vesselId)
        {
            _assembly.Remove(vesselId);
        }

        /// <summary>How many parts lie on the plate or in the bowl.</summary>
        public int CountIn(int vesselId)
        {
            return _assembly.TryGetValue(vesselId, out List<AssemblyEntry> contents) ? contents.Count : 0;
        }

        /// <summary>Display names of what is on the plate or in the bowl, in order.</summary>
        public IReadOnlyList<string> NamesIn(int vesselId)
        {
            if (!_assembly.TryGetValue(vesselId, out List<AssemblyEntry> contents)) return new string[0];
            var names = new List<string>(contents.Count);
            foreach (AssemblyEntry entry in contents) names.Add(NameOf(entry.Item));
            return names;
        }

        /// <summary>Spec 017: looks of what is on the plate or in the bowl, in order (same order as the names).</summary>
        public IReadOnlyList<FoodLook> LooksIn(int vesselId)
        {
            if (!_assembly.TryGetValue(vesselId, out List<AssemblyEntry> contents)) return new FoodLook[0];
            var looks = new List<FoodLook>(contents.Count);
            foreach (AssemblyEntry entry in contents) looks.Add(FoodLookRules.From(entry.Item));
            return looks;
        }

        private List<AssemblyEntry> AssemblyOf(int vesselId)
        {
            if (!_assembly.TryGetValue(vesselId, out List<AssemblyEntry> contents))
            {
                contents = new List<AssemblyEntry>();
                _assembly[vesselId] = contents;
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
