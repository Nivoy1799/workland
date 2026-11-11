using System.Collections.Generic;
using FishNet.Managing;
using UnityEngine;
using UnityEngine.UIElements;
using Worktown.Game.Art;
using Worktown.Game.Cooking;
using Worktown.Game.Economy;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;
using Worktown.Game.Player;
using Worktown.Game.Social;

namespace Worktown.Game.UI
{
    /// <summary>
    /// The always-visible player HUD (spec 011): wallet, clock, held item, station prompt and
    /// toasts for sales and refused actions. No technical text; that lives in the F1 debug menu.
    /// Spec 022: the shift next to the clock and the open orders from the ticket rail.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DefaultExecutionOrder(100)] // spec 044 (027b feels off): after PickupInteractor.Update, so the prompt shows this frame's aim, not the last
    public sealed class HudView : MonoBehaviour
    {
        private const float ToastSeconds = 4f;
        private const int MaxToasts = 5;

        [SerializeField] private NetBootstrap _bootstrap;
        [SerializeField] private StyleSheet _style;

        private VisualElement _root;
        private Label _wallet;
        // Spec 049 (the profiling tour's top GC offender: this view rebuilt its strings every frame): the inputs last shown; a text
        // is only rebuilt when one of them changed.
        private long _walletShown = long.MinValue;
        private bool _walletKnown;
        private string _clockShown;
        private (bool Known, bool Open, int Tickets, int Served, int Missed) _shiftShown;
        private Pickup _heldShown;
        private float _nextHeldText;
        private const float HeldTextSeconds = 0.1f;
        private Label _clock;
        private Label _orders;
        // Spec 053: the food corner's goal line under the wallet ("Save for the rent: $230 / $400"), rebuilt only when it changed.
        private Label _goal;
        private Worktown.Game.Climb.ShopStage _shopStage;
        private float _nextStageLookup;
        private (byte Stage, long Balance, long Goal, bool Reached) _goalShown = (byte.MaxValue, 0, 0, false);
        // Spec 034: "Low stock: tomato, cups" (lines below a quarter of their capacity), rebuilt only when the counts change.
        private Label _lowStock;
        private Worktown.Game.Shop.StockCounts _stockCounts;
        private int _stockVersion = -1;
        private float _nextStockLookup;
        // Spec 035: the hygiene meter: a small icon and "Hygiene 92 %", rebuilt only when the synced score changes.
        private VisualElement _hygieneRow;
        private VisualElement _hygieneIcon;
        private VisualElement _hygieneFill;
        private Label _hygiene;
        private Worktown.Game.Shop.ShopHygiene _shopHygiene;
        private int _hygieneShown = -1;
        private float _nextHygieneLookup;
        private TicketRailView _rail;
        private VisualElement _promptBox;
        private Label _prompt;
        private VisualElement _heldBox;
        private Label _held;
        private VisualElement _toasts;
        // Spec 018: first-person crosshair dot and the prompt under it.
        private const float CrosshairSize = 4f;
        private static readonly Color CrosshairIdle = Palette.WithAlpha(Palette.Cream, 0.55f); // spec 026: palette
        private static readonly Color CrosshairOnTarget = Palette.Interact;
        // Spec 020: a grey prompt says why a click would do nothing.
        private static readonly Color PromptAction = Palette.Cream;
        private static readonly Color PromptReason = Palette.Steel;
        private VisualElement _crosshair;
        private Label _aimPrompt;
        private VisualElement _aimPromptRow;
        private PlayerInputReader _input;
        private readonly List<(VisualElement element, float until)> _activeToasts = new List<(VisualElement, float)>();

        private int _lastResultVersion = -1;
        private string _lastFeedTop;
        private int _lastFeedCount;
        private MarketBoard _board;
        private ServeFeed _feed;

        private void Start()
        {
            if (Application.isBatchMode) return; // headless server: no UI
            VisualElement documentRoot = GetComponent<UIDocument>().rootVisualElement;
            if (_style != null && !documentRoot.styleSheets.Contains(_style)) documentRoot.styleSheets.Add(_style);

            _root = new VisualElement { name = "hud", pickingMode = PickingMode.Ignore };
            _root.AddToClassList("hud-root");

            var walletBox = Panel("wallet-box");
            _wallet = AddLabel(walletBox, "wallet");
            _goal = AddLabel(walletBox, "clock"); // spec 053
            _clock = AddLabel(walletBox, "clock");
            _orders = AddLabel(walletBox, "clock"); // spec 022
            _lowStock = AddLabel(walletBox, "clock"); // spec 034
            CreateHygieneMeter(walletBox); // spec 035
            _root.Add(walletBox);

            _promptBox = Panel("prompt-box");
            _prompt = AddLabel(_promptBox, "prompt");
            _root.Add(_promptBox);

            _heldBox = Panel("held-box");
            _held = AddLabel(_heldBox, "held");
            _root.Add(_heldBox);

            _toasts = new VisualElement { pickingMode = PickingMode.Ignore };
            _toasts.AddToClassList("toast-column");
            _root.Add(_toasts);

            CreateCrosshair(); // spec 018

            documentRoot.Add(_root);
        }

        private void OnEnable()
        {
            SocialFeed.Message += OnSocialMessage;
        }

        private void OnDisable()
        {
            SocialFeed.Message -= OnSocialMessage;
            _root?.RemoveFromHierarchy();
            _root = null;
        }

        /// <summary>Spec 012: quick chat and pings from nearby players as toasts.</summary>
        private void OnSocialMessage(int playerId, string text)
        {
            if (_root == null) return;
            Toast("Player " + playerId + ": " + text, bad: false);
        }

        private void Update()
        {
            if (_root == null) return;
            NetworkManager nm = _bootstrap != null ? _bootstrap.NetworkManager : null;
            PickupInteractor player = LocalPlayer.Find(nm);
            _root.style.display = player != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (player == null) return;

            long balance = 0L;
            bool hasWallet = player.TryGetComponent(out Wallet wallet) && wallet.TryGetBalance(out balance); // spec 053: the shop ledger
            if (hasWallet != _walletKnown || balance != _walletShown)
            {
                _walletKnown = hasWallet;
                _walletShown = balance;
                _wallet.text = hasWallet ? EconomyRules.Money(balance) : string.Empty;
            }
            UpdateGoal(balance);
            if (_board == null) _board = FindFirstObjectByType<MarketBoard>();
            string clock = _board != null && _board.IsSpawned ? _board.Clock.Value : string.Empty;
            bool clockChanged = !ReferenceEquals(clock, _clockShown);
            _clockShown = clock;
            UpdateShift(clock, clockChanged);
            UpdateLowStock();
            UpdateHygiene();

            Pickup held = player.FindHeldPickup();
            Station station = player.NearestStation;
            bool firstPerson = player.IsFirstPerson;
            // Third person: the bottom prompt as before. First person: the dot and its prompt instead.
            Show(_promptBox, !firstPerson && station != null);
            if (!firstPerson && station != null)
                _prompt.text = player.AimClosed ? Worktown.Game.Climb.ShopStageRules.NotOpenText : station.Prompt; // spec 053
            UpdateCrosshair(player, firstPerson, held);

            Show(_heldBox, held != null);
            // A held item's text changes with cooking: rebuilt when the item changes and then 10 times a second, not every frame.
            if (held != null && (held != _heldShown || Time.unscaledTime >= _nextHeldText))
            {
                _heldShown = held;
                _nextHeldText = Time.unscaledTime + HeldTextSeconds;
                _held.text = HeldText(held);
            }
            if (held == null) _heldShown = null;

            UpdateResultToast(player);
            UpdateFeedToast();
            ExpireToasts();
        }

        /// <summary>Spec 053: the goal line at the food corner (hidden in the open kitchen); the wallet is the one shop balance.</summary>
        private void UpdateGoal(long balance)
        {
            if (_shopStage == null && Time.unscaledTime >= _nextStageLookup)
            {
                _nextStageLookup = Time.unscaledTime + 1f;
                _shopStage = Worktown.Game.Climb.ShopStage.Find();
            }
            byte stage = _shopStage != null ? _shopStage.StageNow : Worktown.Game.Climb.ShopStageRules.OpenKitchen;
            long goal = _shopStage != null ? _shopStage.GoalMoney.Value : 0L;
            bool reached = _shopStage != null && _shopStage.GoalReached.Value;
            var shown = (stage, balance, goal, reached);
            if (shown == _goalShown) return;
            _goalShown = shown;
            _goal.text = GoalText(stage, balance, goal, reached);
            _goal.style.color = reached ? Palette.Mustard : Palette.Cream;
            Show(_goal, _goal.text.Length > 0);
        }

        /// <summary>Spec 053: the HUD's goal text: empty in the open kitchen, the rent line at the food corner, a short line once reached.</summary>
        public static string GoalText(byte stage, long balance, long goal, bool reached)
        {
            if (stage == Worktown.Game.Climb.ShopStageRules.OpenKitchen || goal <= 0) return string.Empty;
            return reached ? "Rent goal reached" : Worktown.Game.Climb.FoodCornerRules.GoalLine(balance, goal);
        }

        /// <summary>Spec 022: "Shift until 14:00" after the clock, and "Orders open: 3 · served 2 · missed 1".</summary>
        /// <summary>Spec 034: the low-stock line under the clock (empty when nothing is low).</summary>
        private void UpdateLowStock()
        {
            if (_stockCounts == null)
            {
                if (Time.unscaledTime < _nextStockLookup) return;
                _nextStockLookup = Time.unscaledTime + 1f;
                _stockCounts = FindFirstObjectByType<Worktown.Game.Shop.StockCounts>();
                if (_stockCounts == null) return;
            }
            if (_stockCounts.Version == _stockVersion) return;
            _stockVersion = _stockCounts.Version;
            _lowStock.text = LowStockText(_stockCounts.Counts);
            _lowStock.style.color = Palette.Mustard;
        }

        private const float HygieneIconSize = 12f;
        private const float HygieneBarWidth = 46f;

        /// <summary>Spec 035: the meter's row: a drop icon (its fill the score's colour), a small bar and "Hygiene 92 %".</summary>
        private void CreateHygieneMeter(VisualElement parent)
        {
            _hygieneRow = new VisualElement { name = "hygiene", pickingMode = PickingMode.Ignore };
            _hygieneRow.style.flexDirection = FlexDirection.Row;
            _hygieneRow.style.alignItems = Align.Center;
            _hygieneIcon = new VisualElement { pickingMode = PickingMode.Ignore };
            _hygieneIcon.style.width = HygieneIconSize;
            _hygieneIcon.style.height = HygieneIconSize;
            _hygieneIcon.style.borderTopLeftRadius = HygieneIconSize * 0.5f; // a drop: round at the bottom, a point at the top right
            _hygieneIcon.style.borderBottomLeftRadius = HygieneIconSize * 0.5f;
            _hygieneIcon.style.borderBottomRightRadius = HygieneIconSize * 0.5f;
            _hygieneIcon.style.marginRight = 5f;
            _hygieneRow.Add(_hygieneIcon);
            var bar = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.style.width = HygieneBarWidth;
            bar.style.height = 5f;
            bar.style.backgroundColor = Palette.WithAlpha(Palette.Cream, 0.25f);
            bar.style.marginRight = 6f;
            _hygieneFill = new VisualElement { pickingMode = PickingMode.Ignore };
            _hygieneFill.style.height = 5f;
            bar.Add(_hygieneFill);
            _hygieneRow.Add(bar);
            _hygiene = AddLabel(_hygieneRow, "clock");
            parent.Add(_hygieneRow);
            _hygieneRow.style.display = DisplayStyle.None;
        }

        /// <summary>Spec 035: the meter from the shop's synced score (no per-frame work: only a changed byte rebuilds it).</summary>
        private void UpdateHygiene()
        {
            if (_shopHygiene == null)
            {
                if (Time.unscaledTime < _nextHygieneLookup) return;
                _nextHygieneLookup = Time.unscaledTime + 1f;
                _shopHygiene = FindFirstObjectByType<Worktown.Game.Shop.ShopHygiene>();
                if (_shopHygiene == null) return;
            }
            if (!_shopHygiene.IsSpawned) return;
            int percent = _shopHygiene.Percent.Value;
            if (percent == _hygieneShown) return;
            _hygieneShown = percent;
            _hygieneRow.style.display = DisplayStyle.Flex;
            Color colour = HygieneColour(percent);
            _hygieneIcon.style.backgroundColor = colour;
            _hygieneFill.style.backgroundColor = colour;
            _hygieneFill.style.width = HygieneBarWidth * Mathf.Clamp01(percent / 100f);
            _hygiene.text = HygieneText(percent);
            _hygiene.style.color = percent < Worktown.Sim.Economy.HygieneRules.ReviewBelow ? colour : Palette.Cream;
        }

        /// <summary>Spec 035, pure: "Hygiene 92 %".</summary>
        public static string HygieneText(int percent) => "Hygiene " + Mathf.Clamp(percent, 0, 100) + " %";

        /// <summary>Spec 035, pure: Sage while guests do not mind, Mustard below 50 % (reviews lose a star), Terracotta below 30 % (patience too).</summary>
        public static Color HygieneColour(int percent) =>
            percent < Worktown.Sim.Economy.HygieneRules.PatienceBelow ? Palette.Terracotta
            : percent < Worktown.Sim.Economy.HygieneRules.ReviewBelow ? Palette.Mustard : Palette.Sage;

        /// <summary>Spec 034, pure: "Low stock: tomato, cups" for every line below a quarter (StockCatalog.IsLow), or empty.</summary>
        public static string LowStockText(IReadOnlyList<byte> counts)
        {
            if (counts == null) return string.Empty;
            var low = new System.Text.StringBuilder();
            for (int line = 0; line < Worktown.Game.Shop.StockCatalog.Count && line < counts.Count; line++)
            {
                if (!Worktown.Game.Shop.StockCatalog.IsLow(line, counts[line])) continue;
                low.Append(low.Length == 0 ? "Low stock: " : ", ").Append(Worktown.Game.Shop.StockCatalog.NameOf(line));
            }
            return low.ToString();
        }

        private void UpdateShift(string clock, bool clockChanged)
        {
            if (_rail == null) _rail = FindFirstObjectByType<TicketRailView>();
            bool known = _rail != null && _rail.IsSpawned;
            Show(_orders, known);
            if (!known)
            {
                if (clockChanged || _shiftShown.Known) _clock.text = clock;
                _shiftShown = default;
                return;
            }
            // Spec 049: the same numbers, the same text: nothing is built.
            var shift = (true, _rail.ShiftOpen.Value, _rail.Tickets.Count, _rail.LiveServed.Value, _rail.LiveMissed.Value);
            if (!clockChanged && shift == _shiftShown) return;
            _shiftShown = shift;
            _clock.text = clock;
            if (_rail.ShiftOpen.Value)
            {
                if (_clock.text.Length > 0) _clock.text += "  ·  shift until " + Npc.Shift.CloseHour.ToString("00") + ":00";
                _orders.text = "Orders open: " + _rail.Tickets.Count + "  ·  served " + _rail.LiveServed.Value
                    + "  ·  missed " + _rail.LiveMissed.Value;
            }
            else
            {
                if (_clock.text.Length > 0) _clock.text += "  ·  kitchen closed";
                _orders.text = "Next shift " + Npc.Shift.OpenHour.ToString("00") + ":00  ·  F3 shift summary";
            }
        }

        /// <summary>The held-item line; spec 021: cookware shows what it would give ("Pan: crispy fried potato sticks").</summary>
        private static string HeldText(Pickup held)
        {
            if (held.TryGetComponent(out Food food)) return food.Describe();
            if (held.TryGetComponent(out Knife _)) return "Knife";
            if (held.TryGetComponent(out Spatula _)) return "Spatula"; // spec 030
            if (held.TryGetComponent(out WipeCloth _)) return "Cloth"; // spec 035
            if (held.TryGetComponent(out Worktown.Game.Shop.DirtyDish _)) return "Dirty plate";
            if (held.TryGetComponent(out Worktown.Game.Shop.TrashBag _)) return "Trash bag";
            if (held.TryGetComponent(out Vessel vessel))
            {
                string name = VesselRules.Name(vessel.Kind);
                name = char.ToUpperInvariant(name[0]) + name.Substring(1);
                if (vessel.ContentCount == 0) return name + " (empty)";
                return string.IsNullOrEmpty(vessel.Title.Value) ? name : name + ": " + vessel.Title.Value;
            }
            return "Carrying a box";
        }

        /// <summary>
        /// Spec 018: the centre dot, first person only, hidden under menus. Spec 020: the prompt under it comes from
        /// InteractionRules (the same action the click and the highlight use): white for an action, grey for a
        /// reason; with full hands and nothing else to say it names the right click (spec 041).
        /// </summary>
        private void UpdateCrosshair(PickupInteractor player, bool firstPerson, Pickup held)
        {
            if (_input == null) _input = FindFirstObjectByType<PlayerInputReader>();
            bool visible = firstPerson && (_input == null || !_input.IsUiOpen);
            Show(_crosshair, visible);
            Show(_aimPrompt, false);
            if (!visible) return;

            InteractionAction action = player.CurrentAction;
            _crosshair.style.backgroundColor = action != InteractionAction.None ? CrosshairOnTarget : CrosshairIdle;
            string text = player.AimClosed ? Worktown.Game.Climb.ShopStageRules.NotOpenText // spec 053: a closed station says why
                : InteractionRules.PromptFor(player.CurrentHeld, player.CurrentAim);
            bool reason = action == InteractionAction.None;
            if (text == null && held != null)
            {
                text = InteractionRules.PutDownPrompt(player.PutDownSurface != null); // spec 041: the right click puts down
                reason = true;
            }
            if (text == null) return;
            _aimPrompt.text = text;
            _aimPrompt.style.color = reason ? PromptReason : PromptAction;
            Show(_aimPrompt, true);
        }

        private void CreateCrosshair()
        {
            _crosshair = new VisualElement { name = "crosshair", pickingMode = PickingMode.Ignore };
            IStyle dot = _crosshair.style;
            dot.position = Position.Absolute;
            dot.left = Length.Percent(50);
            dot.top = Length.Percent(50);
            dot.width = CrosshairSize;
            dot.height = CrosshairSize;
            dot.marginLeft = -CrosshairSize * 0.5f;
            dot.marginTop = -CrosshairSize * 0.5f;
            dot.borderTopLeftRadius = CrosshairSize * 0.5f;
            dot.borderTopRightRadius = CrosshairSize * 0.5f;
            dot.borderBottomLeftRadius = CrosshairSize * 0.5f;
            dot.borderBottomRightRadius = CrosshairSize * 0.5f;
            dot.backgroundColor = CrosshairIdle;
            _root.Add(_crosshair);

            // Spec 023: a full-width row centres a label with a dark rounded background, so the text reads over any glow.
            _aimPromptRow = new VisualElement { name = "aim-prompt-row", pickingMode = PickingMode.Ignore };
            IStyle row = _aimPromptRow.style;
            row.position = Position.Absolute;
            row.left = 0;
            row.right = 0;
            row.top = Length.Percent(50);
            row.marginTop = 12;
            row.flexDirection = FlexDirection.Row;
            row.justifyContent = Justify.Center;
            _root.Add(_aimPromptRow);

            _aimPrompt = new Label { name = "aim-prompt", pickingMode = PickingMode.Ignore };
            IStyle prompt = _aimPrompt.style;
            prompt.fontSize = 16;
            prompt.unityTextAlign = TextAnchor.MiddleCenter;
            prompt.color = PromptAction;
            prompt.backgroundColor = Palette.Panel;
            prompt.paddingLeft = 12;
            prompt.paddingRight = 12;
            prompt.paddingTop = 4;
            prompt.paddingBottom = 4;
            prompt.borderTopLeftRadius = 8;
            prompt.borderTopRightRadius = 8;
            prompt.borderBottomLeftRadius = 8;
            prompt.borderBottomRightRadius = 8;
            _aimPromptRow.Add(_aimPrompt);
        }

        private void UpdateResultToast(PickupInteractor player)
        {
            if (player.ResultVersion == _lastResultVersion) return;
            bool first = _lastResultVersion < 0;
            _lastResultVersion = player.ResultVersion;
            if (first) return;
            string text = FriendlyResult(player);
            if (text != null) Toast(text, bad: true);
        }

        private void UpdateFeedToast()
        {
            if (_feed == null) _feed = FindFirstObjectByType<ServeFeed>();
            if (_feed == null || !_feed.IsSpawned || _feed.Entries.Count == 0) return;
            string top = _feed.Entries[0];
            if (top == _lastFeedTop && _feed.Entries.Count == _lastFeedCount) return;
            bool first = _lastFeedTop == null;
            _lastFeedTop = top;
            _lastFeedCount = _feed.Entries.Count;
            if (!first) Toast(top, bad: false);
        }

        /// <summary>Player-facing text for the last answer; null for successes (they are visible anyway).
        /// Maps the enums, so a renamed value is a compile error instead of a silent missing toast.</summary>
        private static string FriendlyResult(PickupInteractor player)
        {
            if (player.LastCookOutcome.HasValue) return CookText(player.LastCookOutcome.Value);
            if (player.LastPickupOutcome.HasValue)
                return player.LastPickupWasPlace ? PlaceText(player.LastPickupOutcome.Value) : PickupText(player.LastPickupOutcome.Value);
            return string.IsNullOrEmpty(player.LastResult) ? null : player.LastResult; // local notes
        }

        private static string CookText(CookOutcome outcome)
        {
            switch (outcome)
            {
                case CookOutcome.Ok: return null;
                case CookOutcome.TooFar: return "Too far away";
                case CookOutcome.NothingHeld: return "You are not holding anything";
                case CookOutcome.HandsFull: return "Your hands are full";
                case CookOutcome.UnknownStation: return "That station does not work";
                case CookOutcome.InvalidOption: return "That station has no such option";
                case CookOutcome.NotFood: return "That's not food";
                case CookOutcome.SimRefused: return "That does not work with this food";
                case CookOutcome.NotEnoughMoney: return "Not enough money";
                case CookOutcome.CannotSell: return "Nobody wants to buy that";
                case CookOutcome.StationFull: return "No more room there";
                case CookOutcome.AlreadyPlated: return "That is already a finished plate";
                case CookOutcome.StationEmpty: return "Nothing there yet";
                case CookOutcome.TooShort: return "Too short: it came back unchanged";
                case CookOutcome.PanBusy: return "Take the food out before switching fry/saute";
                case CookOutcome.NoKnife: return "Take a knife to cut";
                case CookOutcome.NothingToCut: return "Put food on the board first";
                case CookOutcome.VesselFull: return "No more room in there";
                case CookOutcome.NotAVessel: return "You can't use that";
                case CookOutcome.CannotPour: return "That does not pour in there";
                case CookOutcome.PourInstead: return "Pour it onto a plate or into a bowl";
                case CookOutcome.NoPlatesLeft: return "No clean plates left: sell a meal first";
                case CookOutcome.FryerOff: return "Switch the fryer on first"; // spec 029
                case CookOutcome.BasketLowered: return "Raise the basket first";
                case CookOutcome.NotLowered: return "Lower the basket with food first";
                case CookOutcome.NoSpatula: return Worktown.Game.Net.Rules.InteractionRules.UseTheSpatula; // spec 030
                case CookOutcome.BuildClosed: return "It is closed already"; // spec 031
                case CookOutcome.FoldFirst: return "Fold it first";
                case CookOutcome.DispenserBusy: return "Someone is using that"; // the sauce (031) or the tap (033)
                case CookOutcome.NothingToFold: return "Only a wrap or a flatbread folds";
                case CookOutcome.NoPayment: return "Nobody is paying"; // spec 032
                case CookOutcome.PaysByCard: return "They pay by card";
                case CookOutcome.PaysCash: return "They pay cash";
                case CookOutcome.TrayFull: return "The tray is full";
                case CookOutcome.TrayEmpty: return "The tray is empty";
                case CookOutcome.NeedsCup: return "Put a cup on the drip tray first"; // spec 033; 044 (042 known limit): the cup stands on the tray now
                case CookOutcome.CupClosed: return "It has its lid";
                case CookOutcome.OtherDrink: return "That cup has another drink";
                case CookOutcome.CupFull: return "The cup is full";
                case CookOutcome.NotFilled: return "Fill the cup first";
                case CookOutcome.NoCupsLeft: return "Enough empty cups about: use one first";
                case CookOutcome.OutOfStock: return "Out of stock: order more at the terminal"; // spec 034
                case CookOutcome.StockFull: return "That slot is full";
                case CookOutcome.WrongBox: return "That box goes somewhere else";
                case CookOutcome.TooManyOrders: return "Enough orders on the way already";
                case CookOutcome.NoCleanPlates: return "No clean plates: wash some at the sink"; // spec 035
                case CookOutcome.NoDirtyDishes: return "No dirty dishes here";
                case CookOutcome.NotDirty: return "Only dirty dishes go in the sink";
                case CookOutcome.SinkFull: return "The sink is full: scrub a dish first";
                case CookOutcome.SinkEmpty: return "Nothing to scrub";
                case CookOutcome.TrashFull: return "The trash is full: take the bag out";
                case CookOutcome.TrashEmpty: return "The trash is empty";
                case CookOutcome.CannotDiscard: return "That doesn't go in the trash";
                case CookOutcome.NotABag: return "Only the trash bag goes in the bin";
                case CookOutcome.NoCloth: return "Take the cloth to wipe";
                case CookOutcome.NoSpill: return "Nothing to wipe";
                case CookOutcome.StillHolding: return "Finish the other one first"; // 035 review
                case CookOutcome.NoCone: return "Load a seitan cone first"; // spec 036
                case CookOutcome.ConeLoaded: return "The cone still has seitan on it";
                case CookOutcome.NotOpenYet: return Worktown.Game.Climb.ShopStageRules.NotOpenText; // spec 053
                default: return outcome.ToString();
            }
        }

        private static string PickupText(PickupOutcome outcome)
        {
            switch (outcome)
            {
                case PickupOutcome.Ok: return null;
                case PickupOutcome.NotFound: return "Nothing to pick up there";
                case PickupOutcome.TooFar: return "Too far away";
                case PickupOutcome.Taken: return "Someone else has it";
                case PickupOutcome.AlreadyHolding: return "You are already carrying something";
                case PickupOutcome.NothingHeld: return "You are not holding anything";
                default: return outcome.ToString();
            }
        }

        /// <summary>Spec 020: answers to "put it on this spot".</summary>
        private static string PlaceText(PickupOutcome outcome)
        {
            switch (outcome)
            {
                case PickupOutcome.Ok: return null;
                case PickupOutcome.NotFound: return "You can't put it there";
                case PickupOutcome.TooFar: return "Too far away";
                case PickupOutcome.Taken: return "Something is already there";
                case PickupOutcome.NothingHeld: return "You are not holding anything";
                default: return PickupText(outcome);
            }
        }

        private void Toast(string text, bool bad)
        {
            VisualElement box = Panel("toast");
            Label label = AddLabel(box, "toast-text");
            if (bad) label.AddToClassList("toast-bad");
            label.text = text;
            _toasts.Insert(0, box);
            _activeToasts.Add((box, Time.unscaledTime + ToastSeconds));
            while (_activeToasts.Count > MaxToasts)
            {
                _activeToasts[0].element.RemoveFromHierarchy();
                _activeToasts.RemoveAt(0);
            }
        }

        private void ExpireToasts()
        {
            for (int i = _activeToasts.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime < _activeToasts[i].until) continue;
                _activeToasts[i].element.RemoveFromHierarchy();
                _activeToasts.RemoveAt(i);
            }
        }

        private static VisualElement Panel(string className)
        {
            var panel = new VisualElement { pickingMode = PickingMode.Ignore };
            panel.AddToClassList("panel");
            panel.AddToClassList(className);
            return panel;
        }

        private static Label AddLabel(VisualElement parent, string className)
        {
            var label = new Label { pickingMode = PickingMode.Ignore };
            label.AddToClassList(className);
            parent.Add(label);
            return label;
        }

        private static void Show(VisualElement element, bool visible)
        {
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
