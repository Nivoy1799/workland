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
    public sealed class HudView : MonoBehaviour
    {
        private const float ToastSeconds = 4f;
        private const int MaxToasts = 5;

        [SerializeField] private NetBootstrap _bootstrap;
        [SerializeField] private StyleSheet _style;

        private VisualElement _root;
        private Label _wallet;
        private Label _clock;
        private Label _orders;
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
            _clock = AddLabel(walletBox, "clock");
            _orders = AddLabel(walletBox, "clock"); // spec 022
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

            _wallet.text = player.TryGetComponent(out Wallet wallet) ? EconomyRules.Money(wallet.Balance.Value) : string.Empty;
            if (_board == null) _board = FindFirstObjectByType<MarketBoard>();
            _clock.text = _board != null && _board.IsSpawned ? _board.Clock.Value : string.Empty;
            UpdateShift();

            Pickup held = player.FindHeldPickup();
            Station station = player.NearestStation;
            bool firstPerson = player.IsFirstPerson;
            // Third person: the bottom prompt as before. First person: the dot and its prompt instead.
            Show(_promptBox, !firstPerson && station != null);
            if (!firstPerson && station != null) _prompt.text = station.Prompt;
            UpdateCrosshair(player, firstPerson, held);

            Show(_heldBox, held != null);
            if (held != null) _held.text = HeldText(held);

            UpdateResultToast(player);
            UpdateFeedToast();
            ExpireToasts();
        }

        /// <summary>Spec 022: "Shift until 14:00" after the clock, and "Orders open: 3 · served 2 · missed 1".</summary>
        private void UpdateShift()
        {
            if (_rail == null) _rail = FindFirstObjectByType<TicketRailView>();
            bool known = _rail != null && _rail.IsSpawned;
            Show(_orders, known);
            if (!known) return;
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
        /// reason; with full hands and nothing else to say it reminds of Q.
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
            string text = InteractionRules.PromptFor(player.CurrentHeld, player.CurrentAim);
            bool reason = action == InteractionAction.None;
            if (text == null && held != null)
            {
                text = "Q · drop";
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
                case CookOutcome.DispenserBusy: return "Someone is using that sauce";
                case CookOutcome.NothingToFold: return "Only a wrap or a flatbread folds";
                case CookOutcome.NoPayment: return "Nobody is paying"; // spec 032
                case CookOutcome.PaysByCard: return "They pay by card";
                case CookOutcome.PaysCash: return "They pay cash";
                case CookOutcome.TrayFull: return "The tray is full";
                case CookOutcome.TrayEmpty: return "The tray is empty";
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
