using System.Collections.Generic;
using FishNet.Managing;
using UnityEngine;
using UnityEngine.UIElements;
using Worktown.Game.Cooking;
using Worktown.Game.Economy;
using Worktown.Game.Net;

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

            documentRoot.Add(_root);
        }

        private void OnDisable()
        {
            _root?.RemoveFromHierarchy();
            _root = null;
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

            Station station = player.NearestStation;
            Show(_promptBox, station != null);
            if (station != null) _prompt.text = station.Prompt;

            Pickup held = player.FindHeldPickup();
            Show(_heldBox, held != null);
            if (held != null)
                _held.text = held.TryGetComponent(out Food food) ? food.Describe() : "Carrying a box";

            UpdateResultToast(player);
            UpdateFeedToast();
            ExpireToasts();
        }

        private void UpdateResultToast(PickupInteractor player)
        {
            if (player.ResultVersion == _lastResultVersion) return;
            bool first = _lastResultVersion < 0;
            _lastResultVersion = player.ResultVersion;
            if (first) return;
            string text = FriendlyResult(player.LastResult);
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

        /// <summary>Player-facing text for a server answer; null for successes (they are visible anyway).</summary>
        private static string FriendlyResult(string result)
        {
            if (string.IsNullOrEmpty(result) || result.EndsWith(": Ok")) return null;
            if (result.Contains("NotEnoughMoney")) return "Not enough money";
            if (result.Contains("CannotSell")) return "Nobody wants to buy that";
            if (result.Contains("TooFar")) return "Too far away";
            if (result.Contains("HandsFull")) return "Your hands are full";
            if (result.Contains("NothingHeld")) return "You are not holding anything";
            if (result.Contains("AlreadyHolding")) return "You are already carrying something";
            if (result.Contains("Taken")) return "Someone else has it";
            if (result.Contains("NotFood")) return "That is not food";
            if (result.Contains("no cube within")) return "Nothing to pick up here";
            if (result.Contains("has no option")) return result;
            return null;
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
