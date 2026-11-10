using Worktown.Game.Cooking;
using Worktown.Game.Economy;

namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Spec 020, pure: the one rule for first person. From what you hold and what the dot points at it gives the
    /// click's action, the prompt under the dot and (through the action) the highlight colour, so they can never
    /// disagree. Client side only: "fits" uses what clients can see (a vessel's synced contents and the food's coarse
    /// look) with the server's own limits (<see cref="VesselRules"/>); the server checks everything again and decides.
    /// Spec 021: the board, pan, pot, plate and bowl are cookware you carry; rows of the spec 021 table.
    /// </summary>
    public static class InteractionRules
    {
        public const string ClickKey = "Left click"; // spec 041: every prompt names the mouse button
        /// <summary>Spec 041: the put-down button's word ("Right click · put the knife here").</summary>
        public const string RightClickKey = "Right click";
        /// <summary>Spec 041: the turn's word ("Wheel · turn").</summary>
        public const string WheelKey = "Wheel";
        public const string TooFarText = "Too far";

        /// <summary>
        /// The action of a click (spec 021 table, first match wins): knob or button = Press; knife + board with food =
        /// Cut; food that fits + cookware with room = PutInto; pan, pot or bowl with food + plate or bowl with room =
        /// Pour; food or a plate with food + counter = Sell; holding + free spot or burner = Place; empty hands + board,
        /// pan or bowl with food = Take; empty hands + pot (also with food), plate or empty cookware or any free item =
        /// PickUp; empty hands + crate = Buy, + plate shelf = Take; anything else None.
        /// </summary>
        public static InteractionAction ActionFor(Held held, AimTarget target)
        {
            if (target.IsTooFar) return InteractionAction.None; // spec 020: the server would refuse it (TooFar)
            if (held.IsPending) return InteractionAction.None; // spec 030: a pick-up in flight only refuses (see Held.IsPending)
            // Spec 034: a delivery box goes into the slot that stocks its good; the terminal's rows order, its buttons page.
            if (held.IsBox && target.StockLine >= 0)
                return UnpackCheck(held, target) == CookOutcome.Ok ? InteractionAction.Unpack : InteractionAction.None;
            if (target.Kind == AimKind.Station && target.Station == StationKind.Terminal) return TerminalAction(target);
            switch (target.Kind)
            {
                case AimKind.Button: return InteractionAction.Press;
                case AimKind.Spot:
                    if (target.IsCupSpot && CupSpotCheck(held, target) != CookOutcome.Ok) return InteractionAction.None; // spec 042
                    return held.IsHolding && target.SpotIndex >= 0 ? InteractionAction.Place : InteractionAction.None;
                case AimKind.Pickup: return held.IsHolding || target.IsBusy ? InteractionAction.None : InteractionAction.PickUp; // 042: busy cup
                case AimKind.Vessel: return VesselAction(held, target);
                case AimKind.Station: return StationAction(held, target);
                default: return InteractionAction.None;
            }
        }

        private static InteractionAction VesselAction(Held held, AimTarget target)
        {
            bool hasFood = target.ContentCount > 0;
            if (held.IsCup) return InteractionAction.None; // spec 033: a cup goes into no cookware (the sim's ops refuse it)
            if (target.Vessel == VesselKind.Basket) return BasketAction(held, target, hasFood);
            if (target.Vessel == VesselKind.Griddle) return GriddleAction(held, target, hasFood); // spec 030
            if (target.Vessel == VesselKind.Assembly) return BuildAction(held, target); // spec 031
            if (target.Vessel == VesselKind.SpitTray) return TrayAction(held, target); // spec 036
            if (target.Vessel == VesselKind.SpitCone) return InteractionAction.None; // its station slot is the target
            if (held.IsKnife)
                return target.Vessel == VesselKind.Board && hasFood ? InteractionAction.Cut : InteractionAction.None;
            if (held.IsFood) return Fits(held, target) ? InteractionAction.PutInto : InteractionAction.None;
            if (held.IsVessel) return CanPour(held, target) ? InteractionAction.Pour : InteractionAction.None;
            if (held.IsHolding) return InteractionAction.None; // a box goes into nothing
            return hasFood && VesselRules.TakesOutByHand(target.Vessel) ? InteractionAction.Take : InteractionAction.PickUp;
        }

        /// <summary>
        /// Spec 029, a fryer basket (fixed to its fryer, never picked up): lowered, empty hands shake it; raised (or draining),
        /// food that fits goes in and empty hands take the food out. Anything else does nothing.
        /// </summary>
        private static InteractionAction BasketAction(Held held, AimTarget target, bool hasFood)
        {
            if (FryerRules.StateOf(target.Mode) == BasketState.Lowered)
                return !held.IsHolding && hasFood ? InteractionAction.Shake : InteractionAction.None;
            if (held.IsFood) return Fits(held, target) ? InteractionAction.PutInto : InteractionAction.None;
            if (held.IsVessel) return TipsInto(held, hasFood) ? InteractionAction.Take : InteractionAction.None;
            return !held.IsHolding && hasFood ? InteractionAction.Take : InteractionAction.None;
        }

        /// <summary>
        /// Spec 030, a griddle spot (fixed to its griddle, never picked up): the spatula flips the food on it, food that fits goes
        /// on, a plate or bowl with room takes the food (the spot's Take, like a basket's tip), empty hands take it off.
        /// Anything else (the knife, a box) does nothing. Spec 041: the flip is the left click with the spatula.
        /// </summary>
        private static InteractionAction GriddleAction(Held held, AimTarget target, bool hasFood)
        {
            if (held.IsSpatula) return hasFood ? InteractionAction.Flip : InteractionAction.None;
            if (held.IsFood) return Fits(held, target) ? InteractionAction.PutInto : InteractionAction.None;
            if (held.IsVessel) return TipsInto(held, hasFood) ? InteractionAction.Take : InteractionAction.None;
            return !held.IsHolding && hasFood ? InteractionAction.Take : InteractionAction.None;
        }

        // ---- Spec 034: stock, delivery boxes and the ordering terminal ----

        /// <summary>Spec 034: the grey reason of an empty slot, rack, cup stack or tap.</summary>
        public const string OutOfStockText = "Out of stock: order more at the terminal";


        /// <summary>Spec 034: a slot that shows its stock and has none left (as this client sees it).</summary>
        public static bool IsOutOfStock(AimTarget target) => target.StockLine >= 0 && target.StockCount == 0;

        /// <summary>
        /// Spec 034: the carried box into the aimed slot, as the server would answer it: the box holds that slot's good (WrongBox), the
        /// slot has room (StockFull, from its synced count against its capacity), else Ok.
        /// </summary>
        public static CookOutcome UnpackCheck(Held held, AimTarget target)
        {
            if (!held.IsBox || target.StockLine < 0) return CookOutcome.InvalidOption;
            if (held.BoxLine != target.StockLine) return CookOutcome.WrongBox;
            int room = Worktown.Game.Shop.StockCatalog.RoomFor(target.StockLine, target.StockCount);
            return room == 0 ? CookOutcome.StockFull : CookOutcome.Ok;
        }

        /// <summary>Spec 034: why the carried box does not go in here, or null.</summary>
        public static string UnpackReason(Held held, AimTarget target)
        {
            switch (UnpackCheck(held, target))
            {
                case CookOutcome.WrongBox: return "This box is for the " + Worktown.Game.Shop.StockCatalog.NameOf(held.BoxLine);
                case CookOutcome.StockFull: return "That slot is full";
                default: return null;
            }
        }

        /// <summary>"Click · unpack 6 lettuce" (", 4 fit" when the slot takes fewer than the box holds).</summary>
        public static string UnpackPrompt(Held held, AimTarget target)
        {
            int room = Worktown.Game.Shop.StockCatalog.RoomFor(target.StockLine, target.StockCount);
            string what = Worktown.Game.Shop.StockCatalog.Describe(held.BoxLine, held.BoxUnits);
            return Click("unpack " + what + (room >= 0 && room < held.BoxUnits ? " (" + room + " fit)" : string.Empty));
        }

        /// <summary>Spec 034: a row with a good orders it; a page or pack button pages; anything else (a row past the end) nothing.</summary>
        private static InteractionAction TerminalAction(AimTarget target)
        {
            if (string.IsNullOrEmpty(target.Name)) return InteractionAction.None;
            if (Worktown.Game.Shop.TerminalRules.IsRow(target.Option)) return InteractionAction.Order;
            return target.Option < Worktown.Game.Shop.TerminalRules.SlotCount && target.Option >= 0 ? InteractionAction.Page : InteractionAction.None;
        }

        // ---- Spec 031: the assembly board ----

        /// <summary>Spec 031: the key word of a held action ("Hold left click · squeeze garlic-herb sauce").</summary>
        public const string HoldKey = "Hold left click"; // spec 041: names the button

        /// <summary>
        /// Spec 031, the build spot (fixed to its board, never picked up): food goes on as the next layer while the build is open
        /// and has room; empty hands fold an open build on bread (<see cref="BuildRules.FoldsByHand"/>) and take anything else off.
        /// The knife, the spatula, cookware and finished food do nothing here.
        /// </summary>
        private static InteractionAction BuildAction(Held held, AimTarget target)
        {
            int layers = target.ContentCount;
            if (held.IsFood)
            {
                // 031b (review of 031): a build (open or finished) goes back only on an empty board, as the server allows; other
                // finished food (a plate of fries) goes on no board.
                if (held.IsBuild) return layers == 0 ? InteractionAction.PutInto : InteractionAction.None;
                if (held.IsPlated) return InteractionAction.None;
                return layers < VesselRules.Capacity(VesselKind.Assembly) && (layers == 0 || BuildRules.IsOpen(target.Mode))
                    ? InteractionAction.PutInto : InteractionAction.None;
            }
            if (held.IsHolding || layers == 0) return InteractionAction.None;
            return BuildRules.FoldsByHand(layers, target.Mode) ? InteractionAction.Fold : InteractionAction.Take;
        }

        /// <summary>
        /// Spec 031, a tool on the board: a dispenser squeezes onto an open build, a packaging fits as <see cref="BuildRules.CanPackage"/>
        /// says. 031b review: the hands count: a carried build gets no tool (the tools work on the board's build, and the carried one
        /// is what a click would seem to mean), and a pick-up in flight none either.
        /// </summary>
        private static InteractionAction ToolAction(Held held, AimTarget target)
        {
            if (held.IsBuild || held.IsPending) return InteractionAction.None;
            int tool = target.Option;
            if (BuildRules.IsDispenser(tool))
                return SqueezeCheck(target) == CookOutcome.Ok ? InteractionAction.Squeeze : InteractionAction.None;
            if (BuildRules.IsPackaging(tool))
                return BuildRules.CanPackage(BuildRules.PackagingOf(tool), target.ContentCount, target.Mode) == CookOutcome.Ok
                    && !IsOutOfStock(target) ? InteractionAction.Pack : InteractionAction.None; // spec 034: the rack's count
            return InteractionAction.None;
        }

        /// <summary>
        /// 031b (review of 031): a dispenser as the server sees it: the build first (<see cref="BuildRules.CanSqueeze"/>), then the
        /// board's squeezing bit (DispenserBusy: someone squeezes it; the interactor clears the bit of its own squeeze).
        /// </summary>
        private static CookOutcome SqueezeCheck(AimTarget target)
        {
            CookOutcome can = BuildRules.CanSqueeze(target.ContentCount, target.Mode);
            if (can != CookOutcome.Ok) return can;
            // 031b review: a full build takes no new layer (the server refuses the press; a merge onto the same sauce on top is the
            // one case the client cannot see, so it is not offered either).
            if (target.ContentCount >= VesselRules.Capacity(VesselKind.Assembly)) return CookOutcome.VesselFull;
            return BuildRules.IsSqueezing(target.StoveFlags, target.Option) ? CookOutcome.DispenserBusy : CookOutcome.Ok;
        }

        /// <summary>Spec 031: why a tool does nothing now (shown grey), or null.</summary>
        public static string ToolReason(AimTarget target)
        {
            int tool = target.Option;
            CookOutcome why = BuildRules.IsDispenser(tool) ? SqueezeCheck(target)
                : BuildRules.CanPackage(BuildRules.PackagingOf(tool), target.ContentCount, target.Mode);
            if (why == CookOutcome.Ok && BuildRules.IsPackaging(tool) && IsOutOfStock(target)) why = CookOutcome.OutOfStock; // spec 034
            switch (why)
            {
                case CookOutcome.OutOfStock: return OutOfStockText;
                case CookOutcome.DispenserBusy: return "Someone is using that sauce";
                case CookOutcome.VesselFull: return "The build is full";
                case CookOutcome.StationEmpty: return "Start with a wrap or flatbread on the board";
                case CookOutcome.BuildClosed: return BuildRules.IsDispenser(tool) ? "It is closed" : "It is packed already";
                case CookOutcome.FoldFirst: return "Fold it first";
                case CookOutcome.NothingToFold: return "Paper is for a wrap or a flatbread";
                default: return null;
            }
        }

        // ---- Spec 033: the drink dispenser ----

        /// <summary>
        /// Spec 033, a tool of the drink dispenser: a tap fills the cup on its spot (spec 042: an empty cup, or an open cup of its drink
        /// with room, standing on the drip tray under it; the tap free; whatever the hands hold), the cup stack gives empty hands a cup,
        /// the lid stack lids the filled open cup in the hands, else the first one on a spot. A pick-up in flight gets nothing.
        /// </summary>
        private static InteractionAction DrinkToolAction(Held held, AimTarget target)
        {
            if (held.IsPending) return InteractionAction.None;
            CookOutcome why = DrinkCheck(held, target);
            if (why != CookOutcome.Ok) return InteractionAction.None;
            if (DrinkRules.IsTap(target.Option)) return InteractionAction.Fill;
            if (target.Option == DrinkRules.CupTool) return InteractionAction.Take;
            return target.Option == DrinkRules.LidTool ? InteractionAction.Lid : InteractionAction.None;
        }

        /// <summary>Spec 033: a dispenser tool as the server would answer it now, or Ok.</summary>
        private static CookOutcome DrinkCheck(Held held, AimTarget target)
        {
            int tool = target.Option;
            if (DrinkRules.IsTap(tool))
            {
                CookOutcome pour = DrinkRules.CanPourOnSpot(target.Mode, tool, target.StoveFlags); // spec 042: the cup on its spot
                return pour == CookOutcome.Ok && IsOutOfStock(target) ? CookOutcome.OutOfStock : pour; // spec 034: the tap's kegs
            }
            if (tool == DrinkRules.CupTool)
            {
                CookOutcome cup = DrinkRules.CanTakeCup(held.IsHolding);
                return cup == CookOutcome.Ok && IsOutOfStock(target) ? CookOutcome.OutOfStock : cup; // spec 034: the stack's cups
            }
            if (tool == DrinkRules.LidTool) // spec 042: the cup in the hands, else the first filled open cup on a spot
                return held.IsCup ? DrinkRules.CanLid(true, held.CupLidded, held.CupLevel) : DrinkRules.CanLidOnSpot(target.Mode);
            return CookOutcome.InvalidOption;
        }

        /// <summary>Spec 042: whether what the hands hold goes on this cup spot (DrinkRules.CanStand), as the server would answer it.</summary>
        private static CookOutcome CupSpotCheck(Held held, AimTarget target)
        {
            if (!held.IsHolding) return CookOutcome.HandsFull; // nothing to put down (no prompt)
            return DrinkRules.CanStand(held.IsCup, held.CupLidded, held.CupTap, held.CupLevel, target.Option);
        }

        /// <summary>Spec 042: why the held thing does not go on a cup spot, or null.</summary>
        private static string CupSpotReason(Held held, AimTarget target)
        {
            if (held.IsPending) return null;
            switch (CupSpotCheck(held, target))
            {
                case CookOutcome.NeedsCup: return "Only a cup goes under the tap";
                case CookOutcome.CupClosed: return "It has its lid";
                case CookOutcome.OtherDrink: return "That cup has another drink";
                case CookOutcome.Ok: return target.SpotIndex < 0 ? "A cup is already there" : null;
                default: return null;
            }
        }

        /// <summary>Spec 042: "Left click · put the empty cup under the lemonade tap".</summary>
        public static string CupSpotPrompt(Held held, AimTarget target) =>
            Click("put " + (string.IsNullOrEmpty(held.Name) ? "the cup" : held.Name) + " under the " + DrinkRules.DrinkName(target.Option) + " tap");

        /// <summary>Spec 042: a cup another player pours into cannot be taken now.</summary>
        public const string BusyCupText = "Someone is pouring into it";

        /// <summary>Spec 033: why a dispenser tool does nothing now (shown grey), or null.</summary>
        public static string DrinkToolReason(Held held, AimTarget target)
        {
            if (held.IsPending) return null;
            switch (DrinkCheck(held, target))
            {
                case CookOutcome.NeedsCup: // spec 042: a tap needs a cup on its spot; the lid stack a filled cup
                    if (DrinkRules.IsTap(target.Option)) return held.IsCup ? "Put the cup on the tray under the tap" : "Put a cup under the tap first";
                    return held.IsHolding ? "That needs a cup" : "Take a cup first";
                case CookOutcome.CupClosed: return "It has its lid";
                case CookOutcome.OtherDrink: return "That cup has another drink";
                case CookOutcome.CupFull: return "The cup is full";
                case CookOutcome.NotFilled: return "Fill the cup first";
                case CookOutcome.DispenserBusy: return "Someone is using that tap";
                case CookOutcome.OutOfStock: return OutOfStockText; // spec 034; 036 review: a tap shows 0 only when its kegs are empty
                case CookOutcome.HandsFull: return null; // the cup stack with full hands: nothing to say
                default: return null;
            }
        }

        /// <summary>Spec 033: "Hold · fill lemonade" (like the sauce's squeeze; what it costs is charged on the release).</summary>
        public static string FillPrompt(string drink) => HoldKey + " · fill " + (string.IsNullOrEmpty(drink) ? "the cup" : drink);

        /// <summary>Spec 032: why a register slot does nothing now (RegisterRules.Reason), or null when it acts.</summary>
        private static string RegisterReason(AimTarget target) =>
            Worktown.Game.Shop.RegisterRules.Reason(target.Option, (ushort)(target.Price < 0 ? 0 : target.Price), target.ContentCount);

        /// <summary>Spec 031: "Click · wrap in paper ($35)", "Click · into a box ($40)", "Click · into a bag ($35)".</summary>
        public static string PackPrompt(int packaging, long price)
        {
            string what = packaging == BuildRules.Paper ? "wrap in paper" : packaging == BuildRules.Box ? "into a box" : "into a bag";
            return Click(what + (price >= 0 ? " (" + EconomyRules.Money(price) + ")" : string.Empty));
        }

        /// <summary>Spec 030: the text a flip by hand gets, as the prompt's hint and the server's refusal (CookOutcome.NoSpatula).</summary>
        public const string UseTheSpatula = "Use the spatula";

        /// <summary>
        /// Spec 030: a griddle spot with food in reach, where the spatula flips. Spec 041: the flip is the left click with the spatula
        /// (<see cref="InteractionAction.Flip"/>); the right click only puts down, so a flip by hand is no longer asked for.
        /// </summary>
        public static bool IsFlipTarget(AimTarget target)
        {
            return !target.IsTooFar && target.Kind == AimKind.Vessel && target.Vessel == VesselKind.Griddle && target.ContentCount > 0;
        }

        /// <summary>Spec 030: a flip is predicted (the arc at the click) only with the spatula in the hands; the server would refuse it otherwise.</summary>
        public static bool PredictsFlip(Held held, AimTarget target) => held.IsSpatula && !held.IsPending && IsFlipTarget(target);

        /// <summary>
        /// Spec 029b: holding a plate or bowl with room, a click on a raised basket with food tips the food onto it (the
        /// basket's Take, sent as the same RequestUse action; the server pours the basket into what you hold).
        /// </summary>
        public static bool TipsInto(Held held, bool basketHasFood)
        {
            return basketHasFood && held.IsVessel && VesselRules.IsAssembly(held.Vessel)
                && held.ContentCount < VesselRules.Capacity(held.Vessel);
        }

        /// <summary>
        /// Spec 029b: while a take from stock is on its way and nothing is really in the hands yet, no click does anything
        /// (no Place, PutInto or Sell of food that is not there).
        /// </summary>
        public static bool WaitsForTake(bool takePending, bool reallyHolding) => takePending && !reallyHolding;

        /// <summary>
        /// Spec 030 (029b review): the hands as the rules see them. What is really held wins; else a take from stock on its way
        /// counts as that food (<paramref name="pendingTakeName"/>, null when none; <see cref="WaitsForTake"/> then offers
        /// nothing); else an item on its predicted way into the hands (<paramref name="pendingPickUp"/>) counts as held only for
        /// refusals (<see cref="Held.Pending"/>: no take, no buy, and never a tip or pour with a plate that may not arrive).
        /// </summary>
        public static Held EffectiveHands(Held real, string pendingTakeName, Held? pendingPickUp)
        {
            if (real.IsHolding) return real;
            if (pendingTakeName != null) return Held.Food(pendingTakeName, false);
            if (pendingPickUp.HasValue && pendingPickUp.Value.IsHolding) return Held.Pending(pendingPickUp.Value);
            return real;
        }

        private static InteractionAction StationAction(Held held, AimTarget target)
        {
            switch (target.Station)
            {
                case StationKind.Crate: // spec 034: a fridge or shelf slot (described like a crate) gives nothing once it is empty
                    return held.IsHolding || IsOutOfStock(target) ? InteractionAction.None : InteractionAction.Buy;
                case StationKind.PlateShelf: // spec 035: the clean plates are counted (none left: wash some)
                    return held.IsHolding || (target.Known && target.StoveFlags == 0) ? InteractionAction.None : InteractionAction.Take;
                case StationKind.DishReturn:
                case StationKind.Sink:
                case StationKind.TrashCan:
                case StationKind.Bin:
                    return CleaningAction(held, target); // spec 035
                case StationKind.Counter: return SellsAtCounter(held) ? InteractionAction.Sell : InteractionAction.None;
                case StationKind.Assembly: // spec 031: a dispenser or the packaging rack; the board's body itself does nothing
                    return target.Vessel == VesselKind.Assembly ? ToolAction(held, target) : InteractionAction.None;
                case StationKind.Spit: // spec 036: the cone (its knob is a button); the body itself does nothing
                    return target.Option == SpitRules.ConeTool && target.Vessel == VesselKind.SpitCone ? SpitAction(held, target) : InteractionAction.None;
                case StationKind.DrinkDispenser: // spec 033: a tap, the cup stack or the lid stack; the body itself does nothing
                    if (target.Option == Worktown.Game.Shop.CleaningRules.SpillTool) return WipeAction(held, target); // spec 035: the spill
                    return target.Option >= 0 && target.Option < DrinkRules.ToolCount ? DrinkToolAction(held, target) : InteractionAction.None;
                case StationKind.Register: // spec 032: a slot of the register (any hands: the food is already handed over)
                    return target.Option >= 0 && target.Option < Worktown.Game.Shop.RegisterRules.SlotCount && RegisterReason(target) == null
                        ? InteractionAction.Pay : InteractionAction.None;
                default: return InteractionAction.None; // the stove's body: its knobs, buttons and burners do the work
            }
        }

        // ---- Spec 036: the seitan spit ----

        /// <summary>
        /// Spec 036, the spit's cone: the knife shaves it (a hold: a cone with seitan, room in the tray, nobody else shaving); empty hands at
        /// a bare spit load a cone while the stock has one. Anything else does nothing here (the tray is scooped from, the knob pressed).
        /// </summary>
        private static InteractionAction SpitAction(Held held, AimTarget target)
        {
            if (held.IsPending) return InteractionAction.None;
            byte b = target.StoveFlags;
            bool cone = SpitRules.HasCone(b);
            if (held.IsKnife)
                return SpitRules.CanShave(cone, true, target.ContentCount, SpitRules.IsShaving(b)) == CookOutcome.Ok
                    ? InteractionAction.Shave : InteractionAction.None;
            if (held.IsHolding) return InteractionAction.None;
            return SpitRules.CanLoad(false, cone, SpitRules.IsShaving(b)) == CookOutcome.Ok && !IsOutOfStock(target)
                ? InteractionAction.Load : InteractionAction.None;
        }

        /// <summary>Spec 036, the catch tray: empty hands take a portion, an open build in the hands gets one (the 031 layer).</summary>
        private static InteractionAction TrayAction(Held held, AimTarget target)
        {
            if (held.IsPending || held.IsCup) return InteractionAction.None;
            bool openBuild = held.IsBuild && !held.IsPlated;
            return SpitRules.CanScoop(target.ContentCount, held.IsHolding, openBuild) == CookOutcome.Ok ? InteractionAction.Take : InteractionAction.None;
        }

        /// <summary>Spec 036: why the spit's cone does nothing now (grey), or null.</summary>
        public static string SpitReason(Held held, AimTarget target)
        {
            byte b = target.StoveFlags;
            bool cone = SpitRules.HasCone(b);
            if (held.IsKnife)
            {
                switch (SpitRules.CanShave(cone, true, target.ContentCount, SpitRules.IsShaving(b)))
                {
                    case CookOutcome.NoCone: return "Load a seitan cone first";
                    case CookOutcome.TrayFull: return "The tray is full: scoop it first";
                    case CookOutcome.DispenserBusy: return "Someone is shaving";
                    default: return null;
                }
            }
            if (cone) return "Take the knife to shave";
            if (held.IsHolding) return "Put that down to load a cone";
            if (SpitRules.IsShaving(b)) return null;
            return IsOutOfStock(target) ? OutOfStockText : null;
        }

        /// <summary>Spec 036: why the catch tray does nothing now, or null.</summary>
        public static string TrayReason(Held held, AimTarget target)
        {
            if (target.ContentCount <= 0) return "Shave the cone with the knife";
            if (held.IsBuild) return "It is closed";
            return held.IsHolding ? "Scoop with empty hands or onto a wrap" : null;
        }

        /// <summary>"Hold · shave the cone: shaved seitan (1/3 in the tray)" or "Click · load a seitan cone (1 left)".</summary>
        public static string SpitPrompt(InteractionAction action, AimTarget target)
        {
            if (action == InteractionAction.Shave)
                return HoldKey + " · shave the cone" + (string.IsNullOrEmpty(target.Name) ? string.Empty : ": " + target.Name)
                    + (target.ContentCount > 0 ? " (" + target.ContentCount + "/" + SpitRules.TrayCapacity + " in the tray)" : string.Empty);
            if (action == InteractionAction.Load)
                return Click("load a seitan cone" + (target.StockCount > 0 ? " (" + target.StockCount + " left)" : string.Empty));
            return null;
        }

        // ---- Spec 035: cleaning ----

        public const string NoCleanPlatesText = "No clean plates: wash some at the sink";
        public const string WashItText = "Wash it at the sink";
        public const string ToTheBinText = "Take the bag to the bin outside";

        /// <summary>
        /// Spec 035: the cleaning stations as clients see their bytes (the server checks again): the dish return gives empty hands a
        /// dirty dish; the sink takes a dirty dish and is held to scrub with empty hands; the trash can takes food (and the food in
        /// cookware) and gives empty hands its bag; the bin takes the bag.
        /// </summary>
        private static InteractionAction CleaningAction(Held held, AimTarget target)
        {
            byte b = target.StoveFlags;
            switch (target.Station)
            {
                case StationKind.DishReturn:
                    return Worktown.Game.Shop.CleaningRules.CanTakeDirty(held.IsHolding, target.Known ? b : 0) == CookOutcome.Ok
                        ? InteractionAction.Take : InteractionAction.None;
                case StationKind.Sink:
                {
                    int basin = target.Known ? Worktown.Game.Shop.CleaningRules.BasinOf(b) : 0;
                    if (held.IsDirtyDish)
                        return Worktown.Game.Shop.CleaningRules.CanPutInSink(true, basin) == CookOutcome.Ok ? InteractionAction.Wash : InteractionAction.None;
                    return Worktown.Game.Shop.CleaningRules.CanScrub(held.IsHolding, basin, Worktown.Game.Shop.CleaningRules.IsScrubbing(b)) == CookOutcome.Ok
                        ? InteractionAction.Scrub : InteractionAction.None;
                }
                case StationKind.TrashCan:
                    if (!held.IsHolding)
                        return Worktown.Game.Shop.CleaningRules.CanTakeBag(false, target.Known ? b : 0) == CookOutcome.Ok
                            ? InteractionAction.Take : InteractionAction.None;
                    return Worktown.Game.Shop.CleaningRules.CanDiscard(held.IsDiscardable, b) == CookOutcome.Ok
                        ? InteractionAction.Discard : InteractionAction.None;
                case StationKind.Bin:
                    return held.IsTrashBag ? InteractionAction.Discard : InteractionAction.None;
                default:
                    return InteractionAction.None;
            }
        }

        /// <summary>Spec 035: the drink dispenser's spill: the cloth in the hands wipes it (a hold); nothing without a spill.</summary>
        private static InteractionAction WipeAction(Held held, AimTarget target) =>
            Worktown.Game.Shop.CleaningRules.CanWipe(held.IsCloth, DrinkRules.HasSpill(target.StoveFlags),
                Worktown.Game.Shop.CleaningRules.IsWiping(target.StoveFlags)) == CookOutcome.Ok ? InteractionAction.Wipe : InteractionAction.None;

        /// <summary>Spec 035: why a cleaning station (or the plate shelf, or a spill) does nothing now, or null.</summary>
        public static string CleaningReason(Held held, AimTarget target)
        {
            byte b = target.StoveFlags;
            switch (target.Station)
            {
                case StationKind.PlateShelf:
                    return !held.IsHolding && target.Known && b == 0 ? NoCleanPlatesText : null;
                case StationKind.DishReturn:
                    if (held.IsHolding) return held.IsDirtyDish ? WashItText : null;
                    return target.Known ? "No dirty dishes here" : null;
                case StationKind.Sink:
                {
                    int basin = target.Known ? Worktown.Game.Shop.CleaningRules.BasinOf(b) : 0;
                    if (held.IsDirtyDish) return "The sink is full: scrub a dish first";
                    if (held.IsHolding) return "Only dirty dishes go in the sink";
                    if (basin <= 0) return "Bring a dirty dish from the dish return";
                    return "Someone is scrubbing";
                }
                case StationKind.TrashCan:
                    if (!held.IsHolding) return target.Known ? "The trash is empty" : null;
                    if (held.IsDirtyDish) return WashItText;
                    if (held.IsTrashBag) return ToTheBinText;
                    if (!held.IsDiscardable) return "That doesn't go in the trash";
                    return "The trash is full: take the bag out";
                case StationKind.Bin:
                    return held.IsHolding ? "Only the trash bag goes in here" : null;
                case StationKind.DrinkDispenser:
                    if (!DrinkRules.HasSpill(b)) return null;
                    if (!held.IsCloth) return "Take the cloth to wipe the spill";
                    return "Someone is wiping it";
                default:
                    return null;
            }
        }

        /// <summary>Spec 035: the prompt of a cleaning action ("Hold · scrub the plate (2 in the sink)"), or null for another action.</summary>
        public static string CleaningPrompt(InteractionAction action, Held held, AimTarget target)
        {
            byte b = target.StoveFlags;
            switch (action)
            {
                case InteractionAction.Take when target.Station == StationKind.PlateShelf:
                    return Click("take a plate" + (target.Known ? " (" + b + " clean)" : string.Empty));
                case InteractionAction.Take when target.Station == StationKind.DishReturn:
                    return Click("take a dirty plate (" + b + " waiting)");
                case InteractionAction.Take when target.Station == StationKind.TrashCan:
                    return Click("take the trash bag out (" + b + "/" + Worktown.Sim.Economy.HygieneRules.TrashCapacity + ")");
                case InteractionAction.Wash: return Click("put the plate in the sink");
                case InteractionAction.Scrub:
                    int basin = Worktown.Game.Shop.CleaningRules.BasinOf(b);
                    return HoldKey + " · scrub the plate" + (basin > 1 ? " (" + basin + " in the sink)" : string.Empty);
                case InteractionAction.Discard:
                    return target.Station == StationKind.Bin ? Click("throw the bag in")
                        : Click("throw away " + (held.IsVessel ? "the food" : string.IsNullOrEmpty(held.Name) ? "it" : held.Name));
                case InteractionAction.Wipe: return HoldKey + " · wipe the spill";
                default: return null;
            }
        }

        /// <summary>
        /// The counter sells loose food (it plates it, spec 009) and a plate with food (spec 021); not an empty plate
        /// and not a pan, pot or bowl (pour it onto a plate first).
        /// </summary>
        public static bool SellsAtCounter(Held held)
        {
            if (held.IsCup) return held.CupLidded; // spec 033: a drink is sold with its lid on
            if (held.IsFood) return true;
            return held.IsVessel && held.Vessel == VesselKind.Plate && held.ContentCount > 0;
        }

        /// <summary>
        /// Whether the held food may go into the cookware, as far as a client can tell: a finished plate goes nowhere
        /// but the counter, and each kind holds <see cref="VesselRules.Capacity"/> things. Sim refusals (food the sim
        /// would not cut or heat) are server side, so such food shows green and the server refuses with a toast.
        /// </summary>
        public static bool Fits(Held held, AimTarget target)
        {
            if (!held.IsFood) return false;
            if (held.IsCup) return target.Kind == AimKind.Station && target.Station == StationKind.Counter && held.CupLidded; // spec 033
            if (target.Kind == AimKind.Station) return target.Station == StationKind.Counter;
            if (target.Kind != AimKind.Vessel) return false;
            // 031b review: a build goes into no pan, pot, basket, griddle, cutting board or bowl (the sim's ops forbid builds); only
            // an empty plate takes a lone build, as the server allows.
            if (held.IsBuild && (target.Vessel != VesselKind.Plate || target.ContentCount > 0)) return false;
            return VesselRules.CanPutIn(target.Vessel, target.ContentCount, held.IsPlated) == CookOutcome.Ok;
        }

        /// <summary>Holding a pan, pot or bowl with food over a plate or bowl that has room.</summary>
        public static bool CanPour(Held held, AimTarget target)
        {
            return held.HasContents && target.Kind == AimKind.Vessel && VesselRules.CanPour(held.Vessel, target.Vessel)
                && target.ContentCount < VesselRules.Capacity(target.Vessel);
        }

        /// <summary>
        /// Why a click would do nothing, shown grey under the dot; null when there is nothing to say (or the click does
        /// something). Spec 020: a target beyond reach says "Too far" when a step closer it would show anything.
        /// </summary>
        public static string Reason(Held held, AimTarget target)
        {
            if (target.IsTooFar)
            {
                AimTarget near = target.InReach();
                return ActionFor(held, near) != InteractionAction.None || Reason(held, near) != null ? TooFarText : null;
            }
            if (ActionFor(held, target) != InteractionAction.None) return null;
            if (held.IsBox && target.StockLine >= 0) return UnpackReason(held, target); // spec 034
            if (target.Kind == AimKind.Station && target.Station == StationKind.Crate && IsOutOfStock(target))
                return OutOfStockText; // spec 034: also with full hands (it says why the slot looks empty)
            switch (target.Kind)
            {
                case AimKind.Spot:
                    if (target.IsCupSpot) return CupSpotReason(held, target); // spec 042
                    return held.IsHolding ? "No room here" : null;
                case AimKind.Pickup: return target.IsBusy && !held.IsHolding ? BusyCupText : null; // spec 042
                case AimKind.Vessel: return VesselReason(held, target);
                case AimKind.Station:
                    if (Worktown.Game.Shop.CleaningRules.IsCleaningStation(target.Station) || target.Station == StationKind.PlateShelf
                        || (target.Station == StationKind.DrinkDispenser && target.Option == Worktown.Game.Shop.CleaningRules.SpillTool))
                        return CleaningReason(held, target); // spec 035
                    if (target.Station == StationKind.Assembly) return target.Vessel == VesselKind.Assembly ? ToolReason(target) : null; // spec 031
                    if (target.Station == StationKind.Spit) return target.Vessel == VesselKind.SpitCone ? SpitReason(held, target) : null; // spec 036
                    if (target.Station == StationKind.Register) return RegisterReason(target); // spec 032
                    if (target.Station == StationKind.DrinkDispenser) // spec 033
                        return target.Option >= 0 && target.Option < DrinkRules.ToolCount ? DrinkToolReason(held, target) : null;
                    if (target.Station == StationKind.Counter && held.IsCup) return "Put a lid on it first"; // spec 033
                    if (target.Station != StationKind.Counter || !held.IsVessel) return null;
                    if (held.Vessel == VesselKind.Plate) return "Put food on the plate first";
                    return held.HasContents ? "Pour it onto a plate first" : null;
                default: return null;
            }
        }

        private static string VesselReason(Held held, AimTarget target)
        {
            if (held.IsCup) return null; // spec 033: a cup goes into no cookware (nothing to explain)
            if (target.Vessel == VesselKind.SpitTray) return TrayReason(held, target); // spec 036
            if (target.Vessel == VesselKind.SpitCone) return null;
            if (target.Vessel == VesselKind.Assembly) // spec 031
            {
                if (!held.IsFood) return null;
                if (held.IsBuild) return "Take the other build off first"; // 031b: a build goes back only on an empty board
                if (target.ContentCount > 0 && !BuildRules.IsOpen(target.Mode)) return "It is closed";
                return target.ContentCount >= VesselRules.Capacity(VesselKind.Assembly) ? "The build is full" : "Take the finished one to the counter";
            }
            if (target.Vessel == VesselKind.Basket && FryerRules.StateOf(target.Mode) == BasketState.Lowered)
                return held.IsHolding ? "Raise the basket first" : null; // spec 029
            if (target.Vessel == VesselKind.Griddle && held.IsSpatula) return "Put food on the griddle first"; // spec 030
            if (held.IsKnife) return target.Vessel == VesselKind.Board ? "Put food on the board to cut" : null;
            if (held.IsFood)
            {
                CookOutcome fits = VesselRules.CanPutIn(target.Vessel, target.ContentCount, held.IsPlated);
                return fits == CookOutcome.VesselFull ? Full(target.Vessel) : held.Name + " can't go in there";
            }
            if (held.HasContents && VesselRules.CanPour(held.Vessel, target.Vessel)) return Full(target.Vessel);
            return null;
        }

        /// <summary>The text under the dot: what a click does, else the grey reason, else null (no prompt).</summary>
        public static string PromptFor(Held held, AimTarget target)
        {
            InteractionAction action = ActionFor(held, target);
            string cleaning = CleaningPrompt(action, held, target); // spec 035
            if (cleaning != null) return cleaning;
            string spit = target.Station == StationKind.Spit && target.Kind == AimKind.Station ? SpitPrompt(action, target) : null; // spec 036
            if (spit != null) return spit;
            switch (action)
            {
                case InteractionAction.Press:
                    if (target.Station == StationKind.Griddle) return Click(GriddleRules.PowerText(GriddleRules.IsOn(target.StoveFlags))); // spec 030
                    if (target.Station == StationKind.Spit) return Click(SpitRules.KnobText(target.StoveFlags)); // spec 036
                    return target.Station == StationKind.Fryer
                        ? FryerButtonPrompt(target.Option, target.StoveFlags)
                        : ButtonPrompt(target.Option, target.StoveFlags);
                case InteractionAction.Shake: return Click("shake the basket");
                case InteractionAction.Squeeze: return HoldKey + " · squeeze " + (string.IsNullOrEmpty(target.Name) ? "sauce" : target.Name); // spec 031
                case InteractionAction.Fold: return Click("fold " + TakeWhat(target));
                case InteractionAction.Pack: // spec 034: from the rack's stock (paid with the order), so what is left, not a price
                    if (target.StockCount > 0) return PackPrompt(BuildRules.PackagingOf(target.Option), -1) + " (" + target.StockCount + " left)";
                    return PackPrompt(BuildRules.PackagingOf(target.Option), target.Price);
                case InteractionAction.Pay: return Click(Worktown.Game.Shop.RegisterRules.ActionWords(target.Option, target.ContentCount)); // spec 032
                case InteractionAction.Fill: return FillPrompt(target.Name); // spec 033
                case InteractionAction.Lid: return Click("put a lid on it");
                case InteractionAction.Unpack: return UnpackPrompt(held, target); // spec 034
                case InteractionAction.Order:
                case InteractionAction.Page: return Click(target.Name);
                case InteractionAction.Flip: return Click("flip " + TakeWhat(target)); // spec 030
                case InteractionAction.Cut: return "Drag down · cut   " + WheelKey + " · turn"; // spec 041: the wheel turns the food
                case InteractionAction.PutInto: return Click("put " + held.Name + " " + IntoWhat(target.Vessel));
                case InteractionAction.Pour: return Click("pour into the " + VesselRules.Name(target.Vessel));
                case InteractionAction.Sell: return Click("sell " + (held.IsVessel ? held.ContentName ?? "the plate" : held.Name));
                case InteractionAction.Place when target.IsCupSpot: return CupSpotPrompt(held, target); // spec 042: no turn for a round cup
                case InteractionAction.Place:
                    // Spec 041: the right click is the put-down button (the left click places too); the wheel turns it first.
                    return RightClickKey + " · put " + held.Name + (target.IsBurner ? " on the burner" : " here") + "   " + WheelKey + " · turn";
                case InteractionAction.PickUp: return Click("pick up " + (string.IsNullOrEmpty(target.Name) ? "it" : target.Name));
                case InteractionAction.Buy:
                    string what = string.IsNullOrEmpty(target.Name) ? "one" : target.Name;
                    // Spec 034: a stocked slot says what is left (the take is free, the order paid for it); a crate still its price.
                    if (target.StockLine >= 0 && target.StockCount > 0) return Click("take " + what + " (" + target.StockCount + " left)");
                    return Click("take " + what + (target.Price >= 0 ? " (" + EconomyRules.Money(target.Price) + ")" : string.Empty));
                case InteractionAction.Take when target.Kind == AimKind.Station && target.Station == StationKind.DrinkDispenser:
                    return Click("take a cup"); // spec 033: the cup stack (its price comes with the first pour)
                case InteractionAction.Take:
                    // Spec 029: a basket that still drips gives greasy food; the prompt says so.
                    bool dripping = target.Vessel == VesselKind.Basket && FryerRules.StateOf(target.Mode) == BasketState.Draining;
                    string drip = dripping ? " (still dripping)" : string.Empty;
                    bool griddle = target.Vessel == VesselKind.Griddle && target.Kind == AimKind.Vessel; // spec 030
                    if (target.Vessel == VesselKind.SpitTray && target.Kind == AimKind.Vessel) // spec 036: the catch tray
                        return Click(held.IsBuild ? "scoop " + TakeWhat(target) + " onto the " + (string.IsNullOrEmpty(held.Name) ? "build" : held.Name)
                            : "take " + TakeWhat(target));
                    if (held.IsVessel) return Click((griddle ? "slide " : "tip ") + TakeWhat(target) + " " + IntoWhat(held.Vessel) + drip); // spec 029b
                    return Click("take " + TakeWhat(target) + drip) + (griddle ? "   Flip: " + UseTheSpatula.ToLowerInvariant() : string.Empty);
                default: return Reason(held, target);
            }
        }

        /// <summary>"Click · burner on", "Click · fry → sauté", ... for a stove knob or button (any burner).</summary>
        public static string ButtonPrompt(int option, byte stoveFlags)
        {
            if (!StationRules.TryParseStoveOption(option, out int burner, out int button)) return Click("press");
            if (button == StationRules.StoveSwitch)
                return Click(StationRules.IsBurnerOn(stoveFlags, burner) ? "burner off" : "burner on");
            return Click(StationRules.IsBurnerSaute(stoveFlags, burner) ? "sauté → fry" : "fry → sauté");
        }

        /// <summary>Spec 029: "Click · fryer on", "Click · lower the basket", ... for the fryer's knob and basket buttons.</summary>
        public static string FryerButtonPrompt(int option, byte fryerFlags)
        {
            if (option == FryerRules.PowerOption) return Click(FryerRules.IsOn(fryerFlags) ? "fryer off" : "fryer on");
            if (!FryerRules.TryParseBasketOption(option, out int basket)) return Click("press");
            return Click(FryerRules.BasketButtonText(FryerRules.IsLowered(fryerFlags, basket)));
        }

        /// <summary>
        /// Spec 020 AC7, key map: E and the digit keys act only in third person (spec 007); first person uses the mouse.
        /// </summary>
        public static bool KeysActInView(bool firstPerson) => !firstPerson;

        /// <summary>
        /// Spec 041: the HUD's line with something in the hands and nothing else to say: what the right click does now (put it down
        /// on the aimed spot, or drop it at your feet). Q is a hidden alias and never named.
        /// </summary>
        public static string PutDownPrompt(bool onSpot) => RightClickKey + " · " + (onSpot ? "put down here" : "drop");

        private static string Full(VesselKind kind)
        {
            string name = VesselRules.Name(kind);
            return char.ToUpperInvariant(name[0]) + name.Substring(1) + " is full";
        }

        private static string IntoWhat(VesselKind kind)
        {
            switch (kind)
            {
                case VesselKind.Board: return "on the board";
                case VesselKind.Griddle: return "on the griddle"; // spec 030
                case VesselKind.Assembly: return "on the board"; // spec 031
                case VesselKind.Plate: return "on the plate";
                default: return "into the " + VesselRules.Name(kind);
            }
        }

        private static string TakeWhat(AimTarget target)
        {
            if (target.Kind == AimKind.Station) return "a plate"; // the plate shelf
            if (target.Vessel == VesselKind.Bowl && target.ContentCount > 1) return "the mix";
            return string.IsNullOrEmpty(target.ContentName) ? "it" : target.ContentName;
        }

        private static string Click(string what) => ClickKey + " · " + what;
    }
}
