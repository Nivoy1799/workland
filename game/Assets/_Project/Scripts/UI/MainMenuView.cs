using System;
using System.Collections.Generic;
using FishNet.Managing;
using UnityEngine;
using UnityEngine.UIElements;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.UI
{
    /// <summary>
    /// Spec 047: the title screen (New shop, Join a friend, Join by address, Settings, Quit) with the town behind it, the loading
    /// screen with its progress, the rejoin countdown and the error screens. Thin: it shows NetBootstrap.Flow (Rules.JoinFlow) and
    /// calls NetBootstrap; the words come from Rules.ConnectionMessages and MenuRules. While it shows, the cursor is free and the main
    /// camera sways slowly in front of the shop (MenuRules.CameraPose; the CinemachineBrain is paused and given back in the shop).
    /// <see cref="Press"/> clicks a button by its id (the menu tour; the same action the click runs).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MainMenuView : MonoBehaviour
    {
        public const string NewShopId = "new-shop";
        public const string JoinFriendId = "join-friend";
        public const string JoinAddressId = "join-address";
        public const string SettingsId = "settings";
        public const string QuitId = "quit";
        public const string BackId = "back";
        public const string ConnectAddressId = "connect-address";
        public const string CancelId = "cancel";
        public const string RetryId = "retry";
        public const string DismissId = "dismiss";

        private enum Page { Title, JoinFriend, JoinAddress, Settings }

        [SerializeField] private NetBootstrap _bootstrap;
        [SerializeField] private StyleSheet _style;

        private readonly Dictionary<string, Action> _actions = new Dictionary<string, Action>();
        private Page _page = Page.Title;
        private VisualElement _root;
        private VisualElement _side;
        private VisualElement _title;
        private VisualElement _friend;
        private VisualElement _address;
        private VisualElement _settings;
        private VisualElement _card;
        private Label _cardTitle;
        private Label _cardText;
        private VisualElement _track;
        private VisualElement _fill;
        private Button _cancel;
        private Button _retry;
        private Button _dismiss;
        private TextField _addressField;
        private Label _addressError;
        private VisualElement _friendList;
        private Label _friendHint;
        private Label _build;
        private Behaviour _pausedBrain;
        private bool _wasShowing;

        /// <summary>The title screen or one of its cards shows (not in the shop).</summary>
        public bool IsShowing => _root != null && _root.style.display == DisplayStyle.Flex;

        /// <summary>The title screen page that shows (Title, JoinFriend, JoinAddress, Settings), for the log and the tour.</summary>
        public string CurrentPage => _page.ToString();

        public static MainMenuView Find() => FindFirstObjectByType<MainMenuView>();

        private void Start()
        {
            if (Application.isBatchMode) return;
            VisualElement documentRoot = GetComponent<UIDocument>().rootVisualElement;
            if (_style != null && !documentRoot.styleSheets.Contains(_style)) documentRoot.styleSheets.Add(_style);
            BuildUi();
            documentRoot.Add(_root);
        }

        private void OnDisable()
        {
            _root?.RemoveFromHierarchy();
            _root = null;
            ResumeBrain();
        }

        // ---- Building ----

        private void BuildUi()
        {
            _root = new VisualElement { name = "main-menu" };
            _root.AddToClassList("menu-root");

            _side = new VisualElement();
            _side.AddToClassList("panel");
            _side.AddToClassList("menu-side");
            var heading = new Label(MenuRules.GameTitle);
            heading.AddToClassList("menu-title");
            _side.Add(heading);
            var subtitle = new Label(MenuRules.Subtitle);
            subtitle.AddToClassList("menu-subtitle");
            _side.Add(subtitle);

            _title = new VisualElement();
            foreach (string choice in MenuRules.HostChoices(false))
                _title.Add(MenuButton(NewShopId, choice, () => _bootstrap.StartHost()));
            _title.Add(MenuButton(JoinFriendId, "Join a friend", () => Open(Page.JoinFriend)));
            _title.Add(MenuButton(JoinAddressId, "Join by address", () => Open(Page.JoinAddress)));
            _title.Add(MenuButton(SettingsId, "Settings", () => Open(Page.Settings)));
            _title.Add(MenuButton(QuitId, "Quit", Quit));
            _side.Add(_title);

            _friend = SubScreen("Join a friend");
            _friendHint = new Label();
            _friendHint.AddToClassList("menu-text");
            _friend.Add(_friendHint);
            _friendList = new VisualElement();
            _friend.Add(_friendList);
            _friend.Add(MenuButton("refresh-friends", "Refresh", RefreshFriends));
            _friend.Add(MenuButton(BackId + "-friend", "Back", () => Open(Page.Title)));
            _side.Add(_friend);

            _address = SubScreen("Join by address");
            var addressHint = new Label("The host's address and port, for testing on one network. Players normally join friends through Steam.");
            addressHint.AddToClassList("menu-text");
            _address.Add(addressHint);
            _addressField = new TextField { value = "127.0.0.1:" + LaunchArgs.DefaultTugboatPort };
            _addressField.AddToClassList("menu-field");
            _address.Add(_addressField);
            _addressError = new Label();
            _addressError.AddToClassList("menu-error");
            _address.Add(_addressError);
            _address.Add(MenuButton(ConnectAddressId, "Join", JoinAddress));
            _address.Add(MenuButton(BackId + "-address", "Back", () => Open(Page.Title)));
            _side.Add(_address);

            _settings = SubScreen("Settings");
            var controls = new Label(ControlsHelp.Text() + "\n\nMore settings come in a later update.");
            controls.AddToClassList("menu-text");
            _settings.Add(controls);
            _settings.Add(MenuButton(BackId + "-settings", "Back", () => Open(Page.Title)));
            _side.Add(_settings);
            _root.Add(_side);

            _card = new VisualElement();
            _card.AddToClassList("panel");
            _card.AddToClassList("menu-card");
            _cardTitle = new Label();
            _cardTitle.AddToClassList("card-title");
            _card.Add(_cardTitle);
            _cardText = new Label();
            _cardText.AddToClassList("card-text");
            _card.Add(_cardText);
            _track = new VisualElement();
            _track.AddToClassList("progress-track");
            _fill = new VisualElement();
            _fill.AddToClassList("progress-fill");
            _track.Add(_fill);
            _card.Add(_track);
            var row = new VisualElement();
            row.AddToClassList("menu-row");
            _retry = MenuButton(RetryId, "Try again", Retry);
            _dismiss = MenuButton(DismissId, "Back to the menu", () => _bootstrap.Flow.Dismiss());
            _cancel = MenuButton(CancelId, "Cancel", () => _bootstrap.Leave());
            row.Add(_retry);
            row.Add(_dismiss);
            row.Add(_cancel);
            _card.Add(row);
            _root.Add(_card);

            var corner = new VisualElement();
            corner.AddToClassList("menu-build");
            _build = new Label();
            _build.AddToClassList("menu-build-text");
            corner.Add(_build);
            _root.Add(corner);
            Open(Page.Title);
        }

        private VisualElement SubScreen(string heading)
        {
            var screen = new VisualElement();
            var label = new Label(heading);
            label.AddToClassList("menu-heading");
            screen.Add(label);
            return screen;
        }

        private Button MenuButton(string id, string text, Action action)
        {
            _actions[id] = action;
            var button = new Button(() => Run(id)) { text = text, name = id };
            button.AddToClassList("menu-button");
            return button;
        }

        private void Run(string id)
        {
            if (_actions.TryGetValue(id, out Action action)) action();
        }

        /// <summary>The menu tour (and tests): runs the button's action, as a click would. False for an unknown id.</summary>
        public bool Press(string id)
        {
            if (!_actions.ContainsKey(id)) return false;
            Debug.Log("[Menu] press " + id);
            Run(id);
            return true;
        }

        /// <summary>The menu tour: types an address into the Join by address field.</summary>
        public void SetAddress(string value)
        {
            if (_addressField != null) _addressField.value = value;
        }

        private void Open(Page page)
        {
            _page = page;
            Show(_title, page == Page.Title);
            Show(_friend, page == Page.JoinFriend);
            Show(_address, page == Page.JoinAddress);
            Show(_settings, page == Page.Settings);
            if (page == Page.JoinFriend) RefreshFriends();
            if (page == Page.JoinAddress) _addressError.text = string.Empty;
        }

        private void RefreshFriends()
        {
            _friendList.Clear();
            SteamFriendsLink friends = _bootstrap.Friends;
            if (friends == null || !friends.Available)
            {
                _friendHint.text = "Steam is not running. Start Steam to join your friends, or use Join by address.";
                return;
            }
            List<FriendHost> hosts = friends.HostingFriends();
            _friendHint.text = hosts.Count == 0
                ? "None of your Steam friends has a shop open right now. When one does, they can invite you (Tab in their shop), or use Join Game in Steam."
                : "Friends with a shop open:";
            foreach (FriendHost host in hosts)
            {
                ulong id = host.SteamId;
                _friendList.Add(MenuButton("friend-" + id, "Join " + host.Name, () => _bootstrap.ConnectSteam(id.ToString())));
            }
        }

        private void JoinAddress()
        {
            if (!LaunchArgs.TrySplitEndpoint(_addressField.value.Trim(), out string address, out ushort port))
            {
                _addressError.text = "Write it as address:port, for example 192.168.1.20:" + LaunchArgs.DefaultTugboatPort;
                return;
            }
            _bootstrap.ConnectTugboat(address, port);
        }

        private void Retry()
        {
            JoinTarget target = _bootstrap.Flow.Target;
            if (target == null) return;
            switch (target.Kind)
            {
                case JoinTargetKind.Host: _bootstrap.StartHost(); break;
                case JoinTargetKind.Address: _bootstrap.ConnectTugboat(target.Address, target.Port); break;
                default: _bootstrap.ConnectSteam(target.Address); break;
            }
        }

        private static void Quit()
        {
            Debug.Log("[Menu] quit");
            Application.Quit();
        }

        // ---- Every frame ----

        private void Update()
        {
            if (_root == null || _bootstrap == null) return;
            NetworkManager nm = _bootstrap.NetworkManager;
            bool serverOnly = nm != null && nm.IsServerStarted && !nm.IsClientStarted;
            JoinFlow flow = _bootstrap.Flow;
            bool showing = flow.ShowsMenu && !serverOnly && _bootstrap.Args.Mode != LaunchMode.Server;
            Show(_root, showing);
            if (!showing)
            {
                if (_wasShowing) ResumeBrain();
                _wasShowing = false;
                return;
            }
            if (!_wasShowing) Open(Page.Title);
            _wasShowing = true;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
            DriveCamera();

            if (_bootstrap.Join != null)
                _build.text = MenuRules.BuildLine(Application.version, BuildHash.Short(_bootstrap.Join.BuildHash));
            Show(_side, flow.State == JoinState.Menu);
            Show(_card, flow.State != JoinState.Menu);
            double now = Time.unscaledTimeAsDouble;
            switch (flow.State)
            {
                case JoinState.Loading:
                    _cardTitle.text = flow.Target != null && flow.Target.Kind == JoinTargetKind.Host ? "Opening your shop" : "Joining a shop";
                    _cardText.text = ConnectionMessages.Stage(flow.Stage, flow.Target != null ? flow.Target.Describe() : string.Empty);
                    SetBar(flow.Progress);
                    ShowButtons(cancel: true, retry: false, dismiss: false);
                    break;
                case JoinState.Rejoining:
                    _cardTitle.text = ConnectionMessages.Title(DisconnectReason.ConnectionLost);
                    _cardText.text = ConnectionMessages.Rejoining(flow.RejoinSecondsLeft(now));
                    SetBar(flow.RejoinSecondsLeft(now) / JoinFlow.RejoinWindowSeconds);
                    ShowButtons(cancel: true, retry: false, dismiss: false);
                    break;
                case JoinState.Message:
                    _cardTitle.text = ConnectionMessages.Title(flow.Reason);
                    _cardText.text = ConnectionMessages.Text(flow.Reason, flow.Detail);
                    Show(_track, false);
                    bool canRetry = flow.Reason != DisconnectReason.Kicked && flow.Reason != DisconnectReason.VersionMismatch
                        && flow.Reason != DisconnectReason.HostClosed && flow.Target != null;
                    ShowButtons(cancel: false, retry: canRetry, dismiss: true);
                    break;
            }
        }

        private void SetBar(float fraction)
        {
            Show(_track, true);
            _fill.style.width = Length.Percent(Mathf.Clamp01(fraction) * 100f);
        }

        private void ShowButtons(bool cancel, bool retry, bool dismiss)
        {
            Show(_cancel, cancel);
            Show(_retry, retry);
            Show(_dismiss, dismiss);
        }

        private void DriveCamera()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            if (_pausedBrain == null && cam.GetComponent("CinemachineBrain") is Behaviour brain && brain.enabled)
            {
                brain.enabled = false;
                _pausedBrain = brain;
            }
            Pose pose = MenuRules.CameraPose(Time.unscaledTimeAsDouble);
            cam.transform.SetPositionAndRotation(pose.position, pose.rotation);
        }

        private void ResumeBrain()
        {
            if (_pausedBrain != null) _pausedBrain.enabled = true;
            _pausedBrain = null;
        }

        private static void Show(VisualElement element, bool visible)
        {
            if (element == null) return;
            DisplayStyle wanted = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (element.style.display != wanted) element.style.display = wanted;
        }
    }
}
