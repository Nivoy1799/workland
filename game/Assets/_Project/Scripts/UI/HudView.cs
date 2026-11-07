using System.Collections.Generic;
using FishNet.Managing;
using UnityEngine;
using UnityEngine.UIElements;
using Worktown.Game.Cooking;
using Worktown.Game.Economy;
using Worktown.Game.Net;
using Worktown.Game.Player;
using Worktown.Game.Social;

namespace Worktown.Game.UI
{
    /// <summary>
    /// The always-visible player HUD (spec 011): wallet, clock, held item, station prompt and
    /// toasts for sales and refused actions. No technical text; that lives in the F1 debug menu.
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
        private VisualElement _promptBox;
        private Label _prompt;
        private VisualElement _heldBox;
        private Label _held;
        private VisualElement _toasts;
        // Spec 018: first-person crosshair dot and the prompt under it.
        private const float CrosshairSize = 4f;
        private static readonly Color CrosshairIdle = new Color(1f, 1f, 1f, 0.55f);
        private static readonly Color CrosshairOnTarget = new Color(1f, 0.85f, 0.3f, 1f);
        private VisualElement _crosshair;
        private Label _aimPrompt;
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

            Pickup held = player.FindHeldPickup();
            Station station = player.NearestStation;
            bool firstPerson = player.IsFirstPerson;
            // Third person: the bottom prompt as before. First person: the dot and its prompt instead.
            Show(_promptBox, !firstPerson && station != null);
            if (!firstPerson && station != null) _prompt.text = station.Prompt;
            UpdateCrosshair(player, firstPerson, held != null);

            Show(_heldBox, held != null);
            if (held != null)
                _held.text = held.TryGetComponent(out Food food) ? food.Describe() : "Carrying a box";

            UpdateResultToast(player);
            UpdateFeedToast();
            ExpireToasts();
        }

        /// <summary>Spec 018: the centre dot and what E / the digits would do, first person only, hidden under menus.</summary>
        private void UpdateCrosshair(PickupInteractor player, bool firstPerson, bool holding)
        {
            if (_input == null) _input = FindFirstObjectByType<PlayerInputReader>();
            bool visible = firstPerson && (_input == null || !_input.IsUiOpen);
            Show(_crosshair, visible);
            Show(_aimPrompt, false);
            if (!visible) return;

            Component target = player.Target;
            _crosshair.style.backgroundColor = target != null ? CrosshairOnTarget : CrosshairIdle;
            string text = AimPromptText(target, holding);
            if (text == null) return;
            _aimPrompt.text = text;
            Show(_aimPrompt, true);
        }

        private static string AimPromptText(Component target, bool holding)
        {
            if (target is Station station) return station.Prompt;
            if (holding) return "E · drop";
            if (target is Pickup pickup)
                return "E · pick up " + (pickup.TryGetComponent(out Food food) && food.DisplayName.Value.Length > 0 ? food.DisplayName.Value : "box");
            return null;
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

            _aimPrompt = new Label { name = "aim-prompt", pickingMode = PickingMode.Ignore };
            IStyle prompt = _aimPrompt.style;
            prompt.fontSize = 18;
            prompt.position = Position.Absolute;
            prompt.left = 0;
            prompt.right = 0;
            prompt.top = Length.Percent(50);
            prompt.marginTop = 12;
            prompt.unityTextAlign = TextAnchor.UpperCenter;
            prompt.color = Color.white;
            prompt.unityTextOutlineColor = new Color(0f, 0f, 0f, 0.85f);
            prompt.unityTextOutlineWidth = 1f;
            _root.Add(_aimPrompt);
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
            if (player.LastPickupOutcome.HasValue) return PickupText(player.LastPickupOutcome.Value);
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
                case CookOutcome.NotFood: return "That is not food";
                case CookOutcome.SimRefused: return "That does not work with this food";
                case CookOutcome.NotEnoughMoney: return "Not enough money";
                case CookOutcome.CannotSell: return "Nobody wants to buy that";
                case CookOutcome.StationFull: return "No more room there";
                case CookOutcome.AlreadyPlated: return "That is already a finished plate";
                case CookOutcome.StationEmpty: return "Nothing there yet";
                case CookOutcome.TooShort: return "Too short: it came back unchanged";
                case CookOutcome.PanBusy: return "Take the food out before switching fry/saute";
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
