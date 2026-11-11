using FishNet.Managing;
using UnityEngine;
using UnityEngine.UIElements;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;
using Worktown.Game.Player;
using Worktown.Game.Settings;

namespace Worktown.Game.UI
{
    /// <summary>
    /// Spec 047: the lobby is the host's shop itself; Tab opens this panel over it: who is in (the roster from the JoinAuthenticator,
    /// players appear as they join), Kick next to each other player for the host, Invite friends (Steam) for the host, and Leave the
    /// shop (a client) or Close the shop (the host). Thin: rows from Rules.LobbyLine, actions on NetBootstrap.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class LobbyView : MonoBehaviour
    {
        [SerializeField] private NetBootstrap _bootstrap;
        [SerializeField] private StyleSheet _style;

        private VisualElement _box;
        private Label _heading;
        private VisualElement _rows;
        private Button _invite;
        private Button _leave;
        private Label _note;
        private Label _hint;
        private int _hintVersion = -1;
        private bool _open;
        private bool _dirty = true;
        private bool _reportedOpen;
        private PlayerInputReader _input;
        private JoinAuthenticator _subscribed;

        public bool IsOpen => _open;

        public static LobbyView Find() => FindFirstObjectByType<LobbyView>();

        private void Start()
        {
            if (Application.isBatchMode) return;
            VisualElement documentRoot = GetComponent<UIDocument>().rootVisualElement;
            if (_style != null && !documentRoot.styleSheets.Contains(_style)) documentRoot.styleSheets.Add(_style);
            _box = new VisualElement { name = "lobby" };
            _box.AddToClassList("panel");
            _box.AddToClassList("lobby-box");
            _heading = new Label();
            _heading.AddToClassList("lobby-heading");
            _box.Add(_heading);
            _rows = new VisualElement();
            _box.Add(_rows);
            _note = new Label();
            _note.AddToClassList("lobby-note");
            _box.Add(_note);
            _invite = new Button(Invite) { text = "Invite friends (Steam)" };
            _invite.AddToClassList("menu-button");
            _box.Add(_invite);
            _leave = new Button(() => _bootstrap.Leave());
            _leave.AddToClassList("menu-button");
            _box.Add(_leave);
            _hint = new Label();
            var hint = _hint;
            hint.AddToClassList("lobby-hint");
            _box.Add(hint);
            _box.style.display = DisplayStyle.None;
            documentRoot.Add(_box);
        }

        private void OnDisable()
        {
            _box?.RemoveFromHierarchy();
            _box = null;
            if (_subscribed != null) _subscribed.RosterChanged -= MarkDirty;
            _subscribed = null;
            Report(false);
        }

        private void MarkDirty() => _dirty = true;

        /// <summary>Opens or closes the panel (Tab; the menu tour).</summary>
        public void SetOpen(bool open)
        {
            _open = open;
            _dirty = true;
        }

        /// <summary>The menu tour: the host kicks this player, as the Kick button would.</summary>
        public bool PressKick(int clientId)
        {
            Debug.Log("[Menu] kick " + clientId);
            return _bootstrap.Kick(clientId);
        }

        private void Invite()
        {
            if (_bootstrap.Friends == null || !_bootstrap.Friends.OpenInviteDialog())
                _note.text = "Steam is not running: friends on this network can join by address.";
        }

        private void Update()
        {
            if (_box == null || _bootstrap == null) return;
            if (_subscribed == null && _bootstrap.Join != null)
            {
                _subscribed = _bootstrap.Join;
                _subscribed.RosterChanged += MarkDirty;
            }
            bool inGame = _bootstrap.Flow.State == JoinState.InGame;
            // Spec 048: the bound key (Tab); review: not while the settings or another menu is open (a rebind capture of Tab).
            if (_input == null && inGame) _input = FindFirstObjectByType<PlayerInputReader>();
            bool live = CaptureGate.LobbyToggleLive(inGame, SettingsView.AnyOpen, _input != null ? _input.UiOpenCount : 0, _reportedOpen);
            if (live && GameInput.Pressed(ControlAction.Players)) SetOpen(!_open);
            if (_hintVersion != GameSettings.Version)
            {
                _hintVersion = GameSettings.Version;
                _hint.text = KeyPrompts.KeyName(GameSettings.CurrentBindings, ControlAction.Players) + " closes";
            }
            if (!inGame && _open) SetOpen(false);
            bool visible = _open && inGame;
            _box.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            Report(visible);
            if (visible && _dirty) Rebuild();
        }

        private void Rebuild()
        {
            _dirty = false;
            NetworkManager nm = _bootstrap.NetworkManager;
            bool asServer = nm != null && nm.IsServerStarted;
            LobbyRoster roster = _bootstrap.Join != null ? _bootstrap.Join.Roster : default;
            int ownId = nm != null && nm.IsClientStarted ? nm.ClientManager.Connection.ClientId : -1;
            LobbyLine[] lines = LobbyLine.Build(roster.Ids, roster.Names, roster.HostId, ownId, asServer);
            _heading.text = LobbyLine.Heading(lines.Length, roster.MaxPlayers > 0 ? roster.MaxPlayers : JoinRules.MaxPlayers);
            _rows.Clear();
            foreach (LobbyLine line in lines)
            {
                var row = new VisualElement();
                row.AddToClassList("lobby-row");
                // Player-supplied names: plain text, never rich-text tags (JoinRules.CleanName drops '<' and '>' as well).
                var name = new Label(line.Text) { enableRichText = false };
                name.AddToClassList("lobby-name");
                row.Add(name);
                if (line.CanKick)
                {
                    int id = line.ClientId;
                    var kick = new Button(() => PressKick(id)) { text = "Kick" };
                    kick.AddToClassList("lobby-kick");
                    row.Add(kick);
                }
                _rows.Add(row);
            }
            bool steam = _bootstrap.Friends != null && _bootstrap.Friends.Available;
            _invite.style.display = asServer ? DisplayStyle.Flex : DisplayStyle.None;
            _invite.SetEnabled(steam);
            _note.text = asServer && !steam ? "Steam is not running: friends on this network can join by address." : string.Empty;
            _leave.text = asServer ? "Close the shop" : "Leave the shop";
        }

        /// <summary>Frees the cursor while the panel is open (spec 010 locks it otherwise).</summary>
        private void Report(bool open)
        {
            if (open == _reportedOpen) return;
            if (_input == null) _input = FindFirstObjectByType<PlayerInputReader>();
            if (_input == null) return;
            _reportedOpen = open;
            _input.SetUiOpen(open);
        }
    }
}
