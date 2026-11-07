using FishNet.Managing;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Worktown.Game.Cooking;
using Worktown.Game.Economy;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;
using Worktown.Game.Player;

namespace Worktown.Game.UI
{
    /// <summary>
    /// Debug menu (spec 011): all technical details of the former overlay, toggled with F1.
    /// Before connecting (menu mode) it always shows, because it holds the connect buttons.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class DebugMenuView : MonoBehaviour
    {
        [SerializeField] private NetBootstrap _bootstrap;
        [SerializeField] private StyleSheet _style;

        private VisualElement _box;
        private Label _lines;
        private VisualElement _menu;
        private TextField _address;
        private TextField _steamId;
        private string _simStatus;
        private bool _toggledOpen;
        private bool _reportedOpen;
        private MarketBoard _board;
        private ServeFeed _feed;
        private PlayerInputReader _input;

        private void Start()
        {
            // Calls into /sim from a game script at runtime (spec 003 AC6).
            try
            {
                _simStatus = "sim linked, potato " + SimLink.PotatoHash().Substring(0, 8);
            }
            catch (System.Exception e)
            {
                _simStatus = "sim error: " + e.Message;
                Debug.LogError("[Sim] " + e);
            }
            BuildUi();
        }

        private void BuildUi()
        {
            if (Application.isBatchMode) return;
            VisualElement documentRoot = GetComponent<UIDocument>().rootVisualElement;
            if (_style != null && !documentRoot.styleSheets.Contains(_style)) documentRoot.styleSheets.Add(_style);

            _box = new VisualElement();
            _box.AddToClassList("panel");
            _box.AddToClassList("debug-box");

            var title = new Label("Debug (F1)");
            title.AddToClassList("debug-title");
            _box.Add(title);

            _lines = new Label();
            _lines.AddToClassList("debug-line");
            _box.Add(_lines);

            _menu = new VisualElement();
            _menu.Add(new Button(() => _bootstrap.StartHost()) { text = "Host (server + play, others join via Steam)" });
            _menu.Add(new Button(() => _bootstrap.StartServer()) { text = "Server only" });
            var ipRow = Row();
            _address = new TextField { value = "127.0.0.1:" + LaunchArgs.DefaultTugboatPort };
            ipRow.Add(_address);
            ipRow.Add(new Button(JoinIp) { text = "Join IP (Tugboat)" });
            _menu.Add(ipRow);
            var steamRow = Row();
            _steamId = new TextField();
            steamRow.Add(_steamId);
            steamRow.Add(new Button(() => _bootstrap.ConnectSteam(_steamId.value.Trim())) { text = "Join SteamID" });
            _menu.Add(steamRow);
            _box.Add(_menu);

            documentRoot.Add(_box);
        }

        private void OnDisable()
        {
            _box?.RemoveFromHierarchy();
            _box = null;
            ReportOpen(false);
        }

        private void JoinIp()
        {
            if (LaunchArgs.TrySplitEndpoint(_address.value, out string address, out ushort port))
                _bootstrap.ConnectTugboat(address, port);
        }

        private void Update()
        {
            if (_box == null) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame) _toggledOpen = !_toggledOpen;

            NetworkManager nm = _bootstrap.NetworkManager;
            bool connected = nm != null && nm.TimeManager != null && (nm.IsServerStarted || nm.IsClientStarted);
            bool visible = _toggledOpen || !connected;
            _box.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _menu.style.display = connected ? DisplayStyle.None : DisplayStyle.Flex;
            ReportOpen(_toggledOpen && connected);
            if (visible) _lines.text = BuildText(nm, connected);
        }

        /// <summary>Frees the cursor while the menu is open in-game (spec 010 locks it otherwise).</summary>
        private void ReportOpen(bool open)
        {
            if (open == _reportedOpen) return;
            if (_input == null) _input = FindFirstObjectByType<PlayerInputReader>();
            if (_input == null) return;
            _reportedOpen = open;
            _input.SetUiOpen(open);
        }

        private string BuildText(NetworkManager nm, bool connected)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Mode: " + _bootstrap.Args.Mode + (_bootstrap.Args.IsValid ? string.Empty : "  (" + _bootstrap.Args.Error + ")"));
            sb.AppendLine("Status: " + _bootstrap.Status);
            sb.AppendLine("Steam: " + _bootstrap.Steam.Status
                + (_bootstrap.Steam.LocalSteamId != 0 ? "  id " + _bootstrap.Steam.LocalSteamId : string.Empty));
            sb.AppendLine(_simStatus);
            if (nm == null || nm.TimeManager == null)
            {
                sb.AppendLine("NetworkManager not initialized, see log");
                return sb.ToString();
            }
            if (!connected) return sb.ToString();

            sb.AppendLine("Server tick: " + nm.TimeManager.Tick);
            if (nm.IsClientStarted) sb.AppendLine("Ping: " + nm.TimeManager.RoundTripTime + " ms");
            int players = nm.IsServerStarted ? nm.ServerManager.Clients.Count : nm.ClientManager.Clients.Count;
            sb.AppendLine("Players: " + players);

            PickupInteractor local = LocalPlayer.Find(nm);
            if (local != null)
            {
                Pickup held = local.FindHeldPickup();
                sb.AppendLine("Held: " + (held != null ? held.name + " #" + held.ObjectId : "nothing"));
                if (local.LastResult.Length > 0) sb.AppendLine("Last result: " + local.LastResult);
                if (local.TryGetComponent(out PlayerController controller))
                    sb.AppendLine("Crouched: " + controller.IsCrouched);
            }

            if (_board == null) _board = FindFirstObjectByType<MarketBoard>();
            if (_board != null && _board.IsSpawned)
                sb.AppendLine(_board.Clock.Value + " | " + _board.Prices.Value);

            if (_feed == null) _feed = FindFirstObjectByType<ServeFeed>();
            if (_feed != null && _feed.IsSpawned && _feed.Entries.Count > 0)
            {
                sb.AppendLine("Served:");
                foreach (string entry in _feed.Entries) sb.AppendLine("  " + entry);
            }
            sb.Append("F1 closes. Esc/Alt frees the cursor.");
            return sb.ToString();
        }

        private static VisualElement Row()
        {
            var row = new VisualElement();
            row.AddToClassList("debug-row");
            return row;
        }
    }
}
