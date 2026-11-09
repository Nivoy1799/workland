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
        public const string ClickKey = "Click";
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
                    return held.IsHolding && target.SpotIndex >= 0 ? InteractionAction.Place : InteractionAction.None;
                case AimKind.Pickup: return held.IsHolding ? InteractionAction.None : InteractionAction.PickUp;
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
        /// Anything else (the knife, a box) does nothing. A flip by hand is the right click (refused: "Use the spatula").
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

        /// <summary>Spec 031: the key word of a held action ("Hold · squeeze garlic-herb sauce").</summary>
        public const string HoldKey = "Hold";

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
        /// Spec 033, a tool of the drink dispenser: a tap fills the cup in the hands (an empty cup, or an open cup of its drink with room;
        /// the tap free), the cup stack gives empty hands a cup, the lid stack lids a filled open cup. A pick-up in flight gets nothing.
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
                CookOutcome pour = DrinkRules.CanPour(held.IsCup, held.CupLidded, held.CupTap, held.CupLevel, tool, target.StoveFlags);
                return pour == CookOutcome.Ok && IsOutOfStock(target) ? CookOutcome.OutOfStock : pour; // spec 034: the tap's kegs
            }
            if (tool == DrinkRules.CupTool)
            {
                CookOutcome cup = DrinkRules.CanTakeCup(held.IsHolding);
                return cup == CookOutcome.Ok && IsOutOfStock(target) ? CookOutcome.OutOfStock : cup; // spec 034: the stack's cups
            }
            if (tool == DrinkRules.LidTool) return DrinkRules.CanLid(held.IsCup, held.CupLidded, held.CupLevel);
            return CookOutcome.InvalidOption;
        }

        /// <summary>Spec 033: why a dispenser tool does nothing now (shown grey), or null.</summary>
        public static string DrinkToolReason(Held held, AimTarget target)
        {
            if (held.IsPending) return null;
            switch (DrinkCheck(held, target))
            {
                case CookOutcome.NeedsCup: return held.IsHolding ? "That needs a cup" : "Take a cup first";
                case CookOutcome.CupClosed: return "It has its lid";
                case CookOutcome.OtherDrink: return "That cup has another drink";
                case CookOutcome.CupFull: return "The cup is full";
                case CookOutcome.NotFilled: return "Fill the cup first";
                case CookOutcome.DispenserBusy: return "Someone is using that tap";
                case CookOutcome.OutOfStock: return OutOfStockText; // spec 034
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
        /// Spec 030: a right click on a griddle spot with food asks for a flip whatever the hands hold (the server checks the
        /// spatula: without it the answer is "Use the spatula"). False for anything else and beyond reach.
        /// </summary>
        public static bool RightClickFlips(AimTarget target)
        {
            return !target.IsTooFar && target.Kind == AimKind.Vessel && target.Vessel == VesselKind.Griddle && target.ContentCount > 0;
        }

        /// <summary>Spec 030: a flip is predicted (the arc at the click) only with the spatula in the hands; the server would refuse it otherwise.</summary>
        public static bool PredictsFlip(Held held, AimTarget target) => held.IsSpatula && !held.IsPending && RightClickFlips(target);

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
                case StationKind.PlateShelf: return held.IsHolding ? InteractionAction.None : InteractionAction.Take;
                case StationKind.Counter: return SellsAtCounter(held) ? InteractionAction.Sell : InteractionAction.None;
                case StationKind.Assembly: // spec 031: a dispenser or the packaging rack; the board's body itself does nothing
                    return target.Vessel == VesselKind.Assembly ? ToolAction(held, target) : InteractionAction.None;
                case StationKind.DrinkDispenser: // spec 033: a tap, the cup stack or the lid stack; the body itself does nothing
                    return target.Option >= 0 && target.Option < DrinkRules.ToolCount ? DrinkToolAction(held, target) : InteractionAction.None;
                case StationKind.Register: // spec 032: a slot of the register (any hands: the food is already handed over)
                    return target.Option >= 0 && target.Option < Worktown.Game.Shop.RegisterRules.SlotCount && RegisterReason(target) == null
                        ? InteractionAction.Pay : InteractionAction.None;
                default: return InteractionAction.None; // the stove's body: its knobs, buttons and burners do the work
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
                case AimKind.Spot: return held.IsHolding ? "No room here" : null;
                case AimKind.Vessel: return VesselReason(held, target);
                case AimKind.Station:
                    if (target.Station == StationKind.Assembly) return target.Vessel == VesselKind.Assembly ? ToolReason(target) : null; // spec 031
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
            switch (ActionFor(held, target))
            {
                case InteractionAction.Press:
                    if (target.Station == StationKind.Griddle) return Click(GriddleRules.PowerText(GriddleRules.IsOn(target.StoveFlags))); // spec 030
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
                case InteractionAction.Cut: return "Drag down · cut   Right click · turn";
                case InteractionAction.PutInto: return Click("put " + held.Name + " " + IntoWhat(target.Vessel));
                case InteractionAction.Pour: return Click("pour into the " + VesselRules.Name(target.Vessel));
                case InteractionAction.Sell: return Click("sell " + (held.IsVessel ? held.ContentName ?? "the plate" : held.Name));
                case InteractionAction.Place:
                    return Click("put " + held.Name + (target.IsBurner ? " on the burner" : " here"));
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
        /// Spec 020 AC7, key map: E and the digit keys act only in third person (spec 007); first person uses the mouse
        /// (and Q to drop).
        /// </summary>
        public static bool KeysActInView(bool firstPerson) => !firstPerson;

        /// <summary>Spec 020: Q (gamepad X) drops what you hold, first person only; third person keeps E.</summary>
        public static bool DropKeyActs(bool firstPerson) => firstPerson;

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
