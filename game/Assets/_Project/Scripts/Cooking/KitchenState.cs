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
            if (!_items.TryGetValue(objectId, out item)) return false;
            _items.Remove(objectId);
            return true;
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
        }

        /// <summary>Spec 021: a vessel left the world (sold plate, lost cookware): its contents and position go with it.</summary>
        public void RemoveVessel(int vesselId)
        {
            _heat.Remove(vesselId);
            _burnerOf.Remove(vesselId);
            _assembly.Remove(vesselId);
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
                case VesselKind.Pot: return HeatCount(vesselId);
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
                case VesselKind.Pan:
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
                case VesselKind.Pan:
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
                case VesselKind.Pan:
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
            _items.Remove(foodObjectId);
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
            OperationResult dry = _factory.Apply(opId, new[] { item }, HeatParams(tempC, time.Max));
            if (!dry.Ok || dry.Item == null) return CookOutcome.SimRefused;
            if (sessions.Count > 0)
            {
                // Dry run of the pour: every ingredient at its longest time, mixed (fewer cooked steps only make it simpler).
                var inputs = new List<Item>(sessions.Count + 1);
                foreach (HeatSession s in sessions)
                {
                    if (!TryGetTimeParam(s.OperationId, out ParamDef t)) return CookOutcome.SimRefused;
                    OperationResult longest = _factory.Apply(s.OperationId, new[] { s.Food }, HeatParams(s.TempC, t.Max));
                    if (!longest.Ok || longest.Item == null) return CookOutcome.SimRefused;
                    inputs.Add(longest.Item);
                }
                inputs.Add(dry.Item);
                if (!_factory.Apply("mix", inputs, NoParams).Ok) return CookOutcome.SimRefused;
            }

            var session = new HeatSession(item, opId, tempC, IsCooking(vesselId), tick);
            session.Timeline = TimelineOf(item, opId, tempC, time);
            sessions.Add(session);
            _items.Remove(foodObjectId);
            return CookOutcome.Ok;
        }

        /// <summary>How many ingredients are in the pan or pot.</summary>
        public int HeatCount(int vesselId)
        {
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
            if (!_heat.TryGetValue(vesselId, out List<HeatSession> sessions) || sessions.Count == 0) return CookOutcome.StationEmpty;
            if (sessions.Count == 1) return CookOne(sessions[0], tick, tickRate, out result);

            var cooked = new List<Item>(sessions.Count);
            foreach (HeatSession session in sessions)
            {
                CookOutcome one = CookOne(session, tick, tickRate, out Item item);
                if (one != CookOutcome.Ok && one != CookOutcome.TooShort) return one;
                cooked.Add(item);
            }
            OperationResult mixed = _factory.Apply("mix", cooked, NoParams);
            if (!mixed.Ok || mixed.Item == null) return CookOutcome.SimRefused;
            result = mixed.Item;
            return CookOutcome.Ok;
        }

        private CookOutcome CookOne(HeatSession session, uint tick, double tickRate, out Item result)
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
                HeatParams(session.TempC, CookingTime.Clamp(time, seconds)));
            if (!cooked.Ok || cooked.Item == null) return CookOutcome.SimRefused;
            result = cooked.Item;
            return CookOutcome.Ok;
        }

        public void ClearHeat(int vesselId)
        {
            _heat.Remove(vesselId);
        }

        /// <summary>
        /// Label lines for what is in the pan or pot, e.g. "potato sticks · medium". Before the first doneness bucket:
        /// "warming up" while it cooks, "raw" while it does not.
        /// </summary>
        public IReadOnlyList<string> HeatNames(int vesselId, uint tick, double tickRate)
        {
            if (!_heat.TryGetValue(vesselId, out List<HeatSession> sessions)) return new string[0];
            var names = new List<string>(sessions.Count);
            foreach (HeatSession session in sessions)
            {
                string bucket = CurrentBucket(session, tick, tickRate);
                if (bucket.Length == 0) bucket = session.IsRunning ? "warming up" : "raw";
                names.Add(NameOf(session.Food) + " · " + bucket);
            }
            return names;
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
                OperationResult cooked = _factory.Apply(opId, new[] { food }, HeatParams(tempC, seconds));
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
        public static string TimelineKeyOf(Item food, string opId, int? tempC)
        {
            var key = new System.Text.StringBuilder(64 + 24 * food.Components.Count);
            key.Append(food.Hash).Append('|').Append(opId).Append('|').Append(tempC.HasValue ? tempC.Value.ToString() : "-")
                .Append('|').Append(Bits(food.Surface)).Append('|').Append(Bits(food.Stats.HeatTolerance));
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

        /// <summary>Heat op parameters; no temperature for the pot (boil has a fixed one).</summary>
        private static IReadOnlyDictionary<string, ParamValue> HeatParams(int? tempC, float seconds)
        {
            var parameters = new Dictionary<string, ParamValue> { ["time"] = ParamValue.Number(seconds) };
            if (tempC.HasValue) parameters["temp"] = ParamValue.Number(tempC.Value);
            return parameters;
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
            _items.Remove(foodObjectId);
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
