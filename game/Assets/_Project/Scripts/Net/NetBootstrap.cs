using System;
using System.Collections;
using System.Collections.Generic;
using FishNet.Component.Spawning;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using FishNet.Transporting.Multipass;
using FishNet.Transporting.Tugboat;
using UnityEngine;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Net
{
    /// <summary>
    /// Starts server, host or client from the command line (see <see cref="LaunchArgs"/>), or from the title screen (spec 047:
    /// MainMenuView calls <see cref="StartHost"/>, <see cref="ConnectTugboat"/>, <see cref="ConnectSteam"/>, <see cref="Leave"/>).
    /// Logs "Server started" as soon as the first transport is listening (spec 003 AC1).
    /// Spec 047: feeds the connection's events into <see cref="Flow"/> (the loading, error and rejoin screens) and makes its rejoin
    /// attempts; the version check, the places and the kick belong to the JoinAuthenticator; Steam invites and rich presence to
    /// <see cref="Friends"/>.
    /// Follow-up 047: a join while hosting asks first (<see cref="RequestJoin"/>, <see cref="LeaveQuestion"/>, Rules.LeaveShopRules);
    /// quitting while hosting holds the quit until the guests have the HostClosed notice (Application.wantsToQuit, Rules.HostQuitRules).
    /// </summary>
    public sealed class NetBootstrap : MonoBehaviour
    {
        /// <summary>Type name of the FishySteamworks transport. It compiles into Assembly-CSharp,
        /// so this assembly cannot reference the type directly.</summary>
        private const string SteamTransportTypeName = "FishySteamworks";

        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private SteamBootstrap _steam;

        private bool _serverStartLogged;
        private JoinAuthenticator _join;
        private JoinTarget _queuedJoin;
        private bool _closing;
        /// <summary>Follow-up 047: the held quit may now go through (the close is done, or its limit passed).</summary>
        private bool _quitLetThrough;
        private bool _quitHeld;
        private float _quitHeldAt;
        /// <summary>A dedicated server (<c>-server</c>, or a server build's default): nobody plays on it, so a stall ends the process.</summary>
        private bool _dedicated;

        /// <summary>Spec 047: the client's connection as the menus show it.</summary>
        public JoinFlow Flow { get; } = new JoinFlow();
        /// <summary>Spec 047: the version check, the roster, the kick (on the NetworkManager object).</summary>
        public JoinAuthenticator Join => _join;
        /// <summary>Spec 049: the server's tick watchdog (closes the shop when the network tick stalls).</summary>
        public TickWatchdog Watchdog { get; private set; }
        /// <summary>Spec 047: Steam friends, invites and rich presence (does nothing without Steam).</summary>
        public SteamFriendsLink Friends { get; private set; }

        public LaunchArgs Args { get; private set; } = new LaunchArgs();

        /// <summary>
        /// Spec 053: how the next (or running) hosted game starts: the open kitchen (<c>-host</c>), a New shop or a Continue (the title
        /// screen, <c>-new-shop</c>, <c>-continue</c>). The SaveService on the server applies it when the server starts.
        /// </summary>
        public Save.ShopStart HostStart { get; private set; } = Save.ShopStart.OpenKitchen;

        /// <summary>
        /// Spec 053: the host's save files (the folder from <c>-save-dir</c>, else the player's; none in a scripted run without it). Only
        /// this process's own server and title screen use it; a client never reads or writes a shop save.
        /// </summary>
        public Save.SaveStore Saves { get; private set; }
        public NetworkManager NetworkManager => _networkManager;
        public SteamBootstrap Steam => _steam;

        /// <summary>
        /// Follow-up 047: a join waiting for the host's answer to "Leave your shop?" (LobbyView shows the question), or null.
        /// <see cref="AnswerLeave"/> answers it.
        /// </summary>
        public JoinTarget LeaveQuestion { get; private set; }

        /// <summary>Last connection message for the debug overlay.</summary>
        public string Status { get; private set; } = string.Empty;

        private Multipass Multipass => (Multipass)_networkManager.TransportManager.Transport;

        private void Awake()
        {
            // Startup is decided here, never by FishNet's own "start on headless".
            _networkManager.ServerManager.SetStartOnHeadless(false);
        }

        private void Start()
        {
            SetUpJoin();
            Application.wantsToQuit += OnWantsToQuit;
            Watchdog = gameObject.AddComponent<TickWatchdog>();
            Watchdog.Bind(this);
            _networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
            _networkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            _networkManager.ServerManager.OnAuthenticationResult += OnAuthenticationResult;
            _networkManager.SceneManager.OnClientLoadedStartScenes += OnClientLoadedStartScenes;
            _networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
            PlayerSpawner spawner = _networkManager.GetComponent<PlayerSpawner>();
            if (spawner != null)
                spawner.OnSpawned += OnPlayerSpawned;

            Args =LaunchArgs.Parse(Environment.GetCommandLineArgs());
            if (!Args.IsValid)
                Debug.LogError("[Net] bad command line: " + Args.Error);
            // Spec 053: scripted runs never touch the player's saves (only a folder they bring with -save-dir).
            string saveFolder = Save.SaveStore.FolderFor(Args.SaveDir, Args.IsScripted, Application.persistentDataPath);
            Saves = new Save.SaveStore(saveFolder, new Save.DiskSaveFiles(), Save.ShopSaveCodec.Default, () => DateTime.UtcNow);
            Debug.Log("[Save] save folder: " + (saveFolder ?? "none (a scripted run without -save-dir)"));
            if (_join != null && Args.MaxPlayers > 0)
            {
                _join.MaxPlayers = Args.MaxPlayers;
                Debug.Log("[Net] places: " + Args.MaxPlayers);
            }
            if (Args.LatencyMs > 0)
            {
                // Testing only: FishNet's latency simulator delays this process's traffic.
                _networkManager.TransportManager.LatencySimulator.SetLatency(Args.LatencyMs);
                _networkManager.TransportManager.LatencySimulator.SetEnabled(true);
                Debug.Log("[Net] simulated latency " + Args.LatencyMs + " ms");
            }

            switch (Args.Mode)
            {
                case LaunchMode.Server:
                    _dedicated = true;
                    StartServer();
                    break;
                case LaunchMode.Host:
                    // Spec 053: -new-shop and -continue host with a save; -host is the open kitchen (dev and tests).
                    // 053 review: -continue checks the save first; a broken one shows its error on the title (or ends a scripted run).
                    if (Args.ContinueShop && (Saves == null || Saves.Newest() is not { Ok: true }))
                    {
                        Save.SaveLoadResult newest = Saves?.Newest();
                        FailSaveStart(newest != null ? newest.Error : Save.SaveError.NotJson);
                        Status = "title screen";
                        break;
                    }
                    StartHost(Args.NewShop ? Save.ShopStart.NewShop : Args.ContinueShop ? Save.ShopStart.Continue : Save.ShopStart.OpenKitchen);
                    break;
                case LaunchMode.ClientTugboat:
                    ConnectTugboat(Args.Address, Args.Port);
                    break;
                case LaunchMode.ClientSteam:
                    ConnectSteam(Args.SteamTarget);
                    break;
                default:
#if UNITY_SERVER && !UNITY_EDITOR
                    // A dedicated server build has nobody to click the menu.
                    _dedicated = true;
                    StartServer();
#else
                    Status = "title screen";
#endif
                    break;
            }
        }

        private void OnDestroy()
        {
            Application.wantsToQuit -= OnWantsToQuit;
            Friends?.Shutdown();
            if (_networkManager == null) return;
            _networkManager.ClientManager.OnAuthenticated -= OnClientAuthenticated;
            if (_join != null) _join.NoticeReceived -= OnNotice;
            _networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
            _networkManager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
            _networkManager.ServerManager.OnAuthenticationResult -= OnAuthenticationResult;
            _networkManager.SceneManager.OnClientLoadedStartScenes -= OnClientLoadedStartScenes;
            _networkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
            PlayerSpawner spawner = _networkManager.GetComponent<PlayerSpawner>();
            if (spawner != null)
                spawner.OnSpawned -= OnPlayerSpawned;
        }

        /// <summary>Starts the server on every transport (Tugboat, and Steam when available).</summary>
        public void StartServer()
        {
            ConfigureServerPorts();
            if (!_steam.IsReady)
                Debug.LogWarning("[Net] Steam not ready: the Steam transport will fail to start, Tugboat still listens");
            _networkManager.ServerManager.StartConnection();
        }

        /// <summary>Server plus a local client. The local client uses Tugboat on loopback;
        /// remote players join over Steam with this machine's SteamID.</summary>
        public void StartHost()
        {
            if (IsRunning) return;
            Debug.Log("[Net] hosting: " + HostStart);
            Flow.BeginHost();
            StartServer();
            ClientTugboat("127.0.0.1", LaunchArgs.DefaultTugboatPort);
        }

        /// <summary>Spec 053: hosts a game that starts as <paramref name="start"/> (kept for a retry of the same host).</summary>
        public void StartHost(Save.ShopStart start)
        {
            if (IsRunning) return;
            HostStart = start;
            StartHost();
        }

        /// <summary>Spec 053, the title screen's New shop: a new food corner with its own new save file.</summary>
        public void StartNewShop() => StartHost(Save.ShopStart.NewShop);

        /// <summary>Spec 053, the title screen's Continue: the newest save.</summary>
        public void ContinueShop() => StartHost(Save.ShopStart.Continue);

        /// <summary>Spec 053 review: the exit code of a scripted run whose <c>-continue</c> found no save that loads.</summary>
        public const int SaveStartExitCode = 3;

        private Save.SaveError? _saveStartError;

        /// <summary>
        /// Spec 053 review: Continue (the title, or <c>-continue</c>) cannot load the newest save: never a new shop instead. A scripted run
        /// exits with <see cref="SaveStartExitCode"/>; otherwise a running host closes again and the title shows the save's error card
        /// (<see cref="TakeSaveStartError"/>). The file is kept.
        /// </summary>
        public void FailSaveStart(Save.SaveError error)
        {
            Debug.LogError("[Net] Continue: the newest save does not load (" + error + ")");
            if (Args.IsScripted)
            {
                _quitLetThrough = true;
                Application.Quit(SaveStartExitCode);
                return;
            }
            _saveStartError = error;
            if (IsRunning) Leave();
        }

        /// <summary>Spec 053 review, the title screen: the save error of a failed Continue, once.</summary>
        public bool TakeSaveStartError(out Save.SaveError error)
        {
            error = _saveStartError ?? Save.SaveError.None;
            bool any = _saveStartError.HasValue;
            _saveStartError = null;
            return any;
        }

        /// <summary>Spec 053: the host's save service (on the NetworkManager), or null.</summary>
        public Save.SaveService SaveService => _networkManager != null ? _networkManager.GetComponent<Save.SaveService>() : null;

        /// <summary>Spec 053: the host saves its game now (a closing shop, a quit); nothing on a client or without a save.</summary>
        private void SaveHostGame(Save.SaveTrigger trigger)
        {
            if (_networkManager == null || !_networkManager.IsServerStarted) return;
            GuardedCall.Run(() => SaveService?.SaveNow(trigger), "[Net] saving the shop (" + trigger + ")", Debug.LogWarning);
        }

        /// <summary>Joins a host by address over Tugboat (spec 047: "Join by address"; the tours and bots use <c>-connect</c>).</summary>
        public void ConnectTugboat(string address, ushort port)
        {
            Flow.BeginJoin(JoinTarget.ByAddress(address, port));
            ClientTugboat(address, port);
        }

        private void ClientTugboat(string address, ushort port)
        {
            Tugboat tugboat = Multipass.GetTransport<Tugboat>();
            tugboat.SetPort(port);
            Multipass.SetClientTransport(tugboat);
            Status = "connecting via Tugboat to " + address + ":" + port;
            _networkManager.ClientManager.StartConnection(address);
        }

        /// <summary>Joins a host or the dedicated server over Steam P2P by SteamID64.</summary>
        public void ConnectSteam(string steamId)
        {
            Flow.BeginJoin(JoinTarget.BySteam(steamId));
            if (!ClientSteam(steamId)) Flow.Fail(DisconnectReason.Unreachable, Status);
        }

        /// <summary>
        /// Spec 047: a join from Steam (a friend's Join Game or an invite while the game runs). Follow-up 047: through
        /// <see cref="RequestJoin"/>, so a host is asked first.
        /// </summary>
        public void JoinSteam(string steamId) => RequestJoin(JoinTarget.BySteam(steamId));

        /// <summary>
        /// Follow-up 047: a join while this game may be in a session (Rules.LeaveShopRules): not in one, it joins at once; a guest
        /// leaves its shop, then joins; the host is asked "Leave your shop?" first (<see cref="LeaveQuestion"/>); a dedicated server
        /// ignores it. A newer join replaces a waiting question's.
        /// </summary>
        public void RequestJoin(JoinTarget target)
        {
            if (target == null || target.Kind == JoinTargetKind.Host) return;
            switch (LeaveShopRules.Decide(_dedicated, _closing, _networkManager.IsServerStarted, _networkManager.IsClientStarted))
            {
                case JoinChoice.JoinNow:
                    Connect(target);
                    break;
                case JoinChoice.JoinWhenClosed:
                    _queuedJoin = target; // the own shop is already closing: join once it is shut (Update)
                    break;
                case JoinChoice.LeaveThenJoin:
                    _queuedJoin = target;
                    Leave();
                    break;
                case JoinChoice.AskFirst:
                    LeaveQuestion = target;
                    Debug.Log("[Net] a join to " + target.Describe() + " while hosting: asking first");
                    break;
                default:
                    Debug.Log("[Net] a dedicated server joins nothing (" + target.Describe() + ")");
                    break;
            }
        }

        /// <summary>
        /// Follow-up 047: the host's answer to "Leave your shop?": Leave closes the shop (the guests get the HostClosed notice) and
        /// joins once it is shut; Stay keeps the shop open and drops the join.
        /// </summary>
        public void AnswerLeave(bool leave)
        {
            JoinTarget target = LeaveQuestion;
            LeaveQuestion = null;
            if (target == null) return;
            Debug.Log("[Net] leave the shop for " + target.Describe() + "? " + (leave ? "Leave" : "Stay"));
            if (!leave) return;
            _queuedJoin = target;
            Leave();
        }

        private void Connect(JoinTarget target)
        {
            if (target.Kind == JoinTargetKind.Address) ConnectTugboat(target.Address, target.Port);
            else ConnectSteam(target.Address);
        }

        private bool ClientSteam(string steamId)
        {
            Transport steamTransport = FindSteamTransport();
            if (steamTransport == null)
            {
                Status = "no Steam transport in Multipass";
                Debug.LogError("[Net] " + Status);
                return false;
            }
            if (!_steam.IsReady)
            {
                Status = "Steam is not running (" + _steam.Status + ")";
                Debug.LogWarning("[Net] " + Status);
                return false;
            }
            Multipass.SetClientTransport(steamTransport);
            Status = "connecting via Steam to " + steamId;
            _networkManager.ClientManager.StartConnection(steamId);
            return true;
        }

        /// <summary>The client or the server runs in this process.</summary>
        public bool IsRunning => _networkManager != null && (_networkManager.IsClientStarted || _networkManager.IsServerStarted);

        /// <summary>
        /// Spec 047: back to the title screen. A host closes its shop (every other player gets a HostClosed notice, then the server
        /// stops a moment later so the notice goes out); a client leaves; a loading or rejoining client cancels.
        /// </summary>
        public void Leave()
        {
            bool running = IsRunning;
            Flow.Leave(running);
            if (!running || _closing) return;
            if (_networkManager.IsServerStarted)
            {
                SaveHostGame(Save.SaveTrigger.HostClosing); // spec 053: saved before the notice and the stop
                _join?.NotifyClosing();
                StartCoroutine(CloseSoon());
            }
            else
            {
                _networkManager.ClientManager.StopConnection();
            }
        }

        private IEnumerator CloseSoon()
        {
            _closing = true;
            Debug.Log("[Net] the host closes the shop");
            // 1 s, not less: the HostClosed notice must survive a resend over the Steam relay before the server stops.
            yield return new WaitForSecondsRealtime(1f);
            // 049 review: each stop on its own, so a throw from a broken FishNet (the stall close) cannot skip the other one, nor what
            // follows (the stall's error screen and the transport pump).
            GuardedCall.Run(StopClient, "[Net] stopping the client", Debug.LogWarning);
            GuardedCall.Run(StopServer, "[Net] stopping the server", Debug.LogWarning);
            _closing = false;
        }

        private void StopClient()
        {
            if (_networkManager.IsClientStarted) _networkManager.ClientManager.StopConnection();
        }

        private void StopServer()
        {
            if (_networkManager.IsServerStarted) _networkManager.ServerManager.StopConnection(true);
        }

        /// <summary>
        /// Spec 049 (TickWatchdog): the server's network tick stalled, so nothing goes out by itself. Every other player gets a HostStalled
        /// notice, flushed with FishNet's disconnect messages (they send at once, without a tick); the host's own screen shows the same;
        /// a second later the server stops (as when the host closes the shop).
        /// </summary>
        public void CloseAfterStall()
        {
            if (_closing || _networkManager == null || !_networkManager.IsServerStarted) return;
            Flow.Notice(DisconnectReason.HostStalled, string.Empty);
            _join?.NotifyClosing(DisconnectReason.HostStalled);
            var remote = new List<NetworkConnection>();
            foreach (NetworkConnection connection in _networkManager.ServerManager.Clients.Values)
                if (connection != null && connection.IsActive && !connection.IsLocalClient) remote.Add(connection);
            try
            {
                if (remote.Count > 0) _networkManager.ServerManager.SendDisconnectMessages(remote, true);
            }
            catch (Exception e) // a stalled FishNet may be in any state: the close must still happen
            {
                Debug.LogWarning("[Net] stall: the notices could not be flushed: " + e.Message);
            }
            Debug.Log("[Net] stall: closing the shop, " + remote.Count + " players told");
            StartCoroutine(CloseAfterStallSoon());
        }

        /// <summary>
        /// Spec 049: like CloseSoon, but FishNet's tick no longer delivers the transports' state changes, so the stop is pumped here
        /// (Transport.IterateIncoming, a few frames) and the host's own error screen is shown directly.
        /// </summary>
        private IEnumerator CloseAfterStallSoon()
        {
            yield return CloseSoon();
            Flow.Fail(DisconnectReason.HostStalled, string.Empty);
            Transport transport = _networkManager.TransportManager.Transport;
            for (int i = 0; i < 30 && (_networkManager.IsServerStarted || _networkManager.IsClientStarted); i++)
            {
                try
                {
                    transport.IterateIncoming(true);
                    transport.IterateIncoming(false);
                }
                catch (Exception e) // a stalled FishNet may be in any state
                {
                    Debug.LogWarning("[Net] stall: pumping the stop failed: " + e.Message);
                    break;
                }
                yield return null;
            }
            Debug.Log("[Net] stall: the shop is closed (server " + (_networkManager.IsServerStarted ? "still started" : "stopped") + ")");
            if (TickStallRules.QuitsAfterStall(_dedicated, Args.StallTestSeconds > 0))
            {
                // 049 review: a dedicated server has no player to read the screen; a non-zero exit tells its supervisor.
                Debug.LogError("[Net] stall: the dedicated server quits (exit " + TickStallRules.DedicatedExitCode + ")");
                _quitLetThrough = true; // follow-up 047 review: never held, so the exit code survives (HostQuitRules also skips dedicated)
                Application.Quit(TickStallRules.DedicatedExitCode);
            }
        }

        // ---- Follow-up 047: quitting while hosting ----

        /// <summary>
        /// Unity asks before it quits (Alt-F4, the window's close button, Application.Quit). A host with guests sends the HostClosed
        /// notice and holds the quit while it goes out (CloseSoon), then quits (Rules.HostQuitRules). A crash never gets here.
        /// </summary>
        private bool OnWantsToQuit()
        {
            int guests = RemoteGuests();
            if (!HostQuitRules.HoldsQuit(_networkManager != null && _networkManager.IsServerStarted, guests, _quitLetThrough,
                    Application.isEditor, Args.IsScripted, _dedicated))
                return true;
            if (_quitHeld)
            {
                // A second quit while the first is held: the first one goes through soon. Review: unless the hold outlived its limit
                // (the coroutine that ends it died), then this one goes through.
                if (HostQuitRules.StillHeld(Time.unscaledTime - _quitHeldAt)) return false;
                Debug.LogWarning("[Net] the held quit outlived " + HostQuitRules.LimitSeconds + " s: quitting now");
                _quitLetThrough = true;
                return true;
            }
            _quitHeld = true;
            _quitHeldAt = Time.unscaledTime;
            Debug.Log("[Net] quitting while hosting: telling " + guests + " guests the shop closed first");
            Flow.Leave(true);
            if (!_closing)
            {
                SaveHostGame(Save.SaveTrigger.HostClosing); // spec 053: the shop closes with the quit; saved before the notice
                // Review: a throw from the notice must not skip the close and the quit below.
                GuardedCall.Run(() => _join?.NotifyClosing(), "[Net] quit: the HostClosed notice", Debug.LogWarning);
                StartCoroutine(CloseSoon());
            }
            StartCoroutine(QuitWhenClosed());
            return false;
        }

        private IEnumerator QuitWhenClosed()
        {
            float start = Time.unscaledTime;
            yield return null;
            while (!HostQuitRules.QuitNow(_closing, _networkManager != null && _networkManager.IsServerStarted, Time.unscaledTime - start))
                yield return null;
            Debug.Log("[Net] the shop is closed: quitting (" + (Time.unscaledTime - start).ToString("0.0") + " s)");
            _quitLetThrough = true;
            Application.Quit();
        }

        /// <summary>Active connections other than the host's own; 0 when not the server, or when FishNet throws (then the quit is not
        /// held: better to quit than to hang).</summary>
        private int RemoteGuests()
        {
            try
            {
                if (_networkManager == null || !_networkManager.IsServerStarted) return 0;
                int count = 0;
                foreach (NetworkConnection connection in _networkManager.ServerManager.Clients.Values)
                    if (connection != null && connection.IsActive && !connection.IsLocalClient) count++;
                return count;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Net] quit: the guests could not be counted (" + e.Message + "), quitting without the notice");
                return 0;
            }
        }

        /// <summary>Spec 047, the host: removes a player from the shop (JoinAuthenticator.Kick).</summary>
        public bool Kick(int clientId) => _join != null && _join.Kick(clientId);

        /// <summary>Spec 047: the authenticator (from the scene, or added here if the scene predates it), Steam, the flow events.</summary>
        private void SetUpJoin()
        {
            _join = _networkManager.GetComponent<JoinAuthenticator>();
            if (_join == null)
            {
                Debug.LogWarning("[Net] the NetworkManager has no JoinAuthenticator (scene not rebuilt?); adding one");
                _join = _networkManager.gameObject.AddComponent<JoinAuthenticator>();
                _networkManager.ServerManager.SetAuthenticator(_join);
            }
            Friends = new SteamFriendsLink(_steam);
            Friends.JoinRequested += host => JoinSteam(host.ToString());
            _join.LocalName = Friends.PersonaName();
            _join.NoticeReceived += OnNotice;
            _networkManager.ClientManager.OnAuthenticated += OnClientAuthenticated;
        }

        private void OnNotice(DisconnectReason reason, string detail)
        {
            if (reason == DisconnectReason.VersionMismatch)
                detail = (detail.Length > 0 ? detail + ", " : string.Empty) + "yours " + BuildHash.Short(_join.BuildHash);
            Flow.Notice(reason, detail);
        }

        private void OnClientAuthenticated() => Flow.Authenticated();

        private void Update()
        {
            if (_networkManager == null) return;
            Friends?.Update(_networkManager.IsServerStarted && _networkManager.IsClientStarted,
                !_networkManager.IsServerStarted && Flow.State == JoinState.InGame, _join != null ? _join.PlayerCount : 0,
                _join != null ? _join.MaxPlayers : JoinRules.MaxPlayers);
            if ((Flow.State == JoinState.Loading || Flow.State == JoinState.Rejoining) && OwnPlayerSpawned()) Flow.PlayerSpawned();

            double now = Time.unscaledTimeAsDouble;
            if (Flow.Tick(now))
            {
                JoinTarget target = Flow.Target;
                Debug.Log("[Net] rejoin attempt to " + target.Describe() + ", " + Flow.RejoinSecondsLeft(now).ToString("0") + " s left");
                if (target.Kind == JoinTargetKind.Steam)
                {
                    if (!ClientSteam(target.Address)) Flow.TransportStopped(now);
                }
                else
                {
                    ClientTugboat(target.Address, target.Port);
                }
            }
            // A rejoin attempt still hanging when the flow gave up: stop it.
            if (Flow.State == JoinState.Message && _networkManager.IsClientStarted && !_networkManager.IsServerStarted)
                _networkManager.ClientManager.StopConnection();

            if (_queuedJoin != null && !IsRunning && !_closing)
            {
                JoinTarget queued = _queuedJoin;
                _queuedJoin = null;
                Connect(queued);
            }
            if (LeaveQuestion != null
                && !LeaveShopRules.StillAsking(true, _dedicated, _closing, _networkManager.IsServerStarted, _networkManager.IsClientStarted))
            {
                Debug.Log("[Net] the shop closed before the answer: the join to " + LeaveQuestion.Describe() + " is dropped");
                LeaveQuestion = null;
            }
        }

        private bool OwnPlayerSpawned()
        {
            if (!_networkManager.IsClientStarted) return false;
            foreach (NetworkObject obj in _networkManager.ClientManager.Connection.Objects)
                if (obj != null && obj.GetComponent<PlayerController>() != null) return true;
            return false;
        }

        private void ConfigureServerPorts()
        {
            Multipass.GetTransport<Tugboat>().SetPort(LaunchArgs.DefaultTugboatPort);
            Transport steamTransport = FindSteamTransport();
            if (steamTransport != null)
                steamTransport.SetPort(SteamBootstrap.SteamGamePort);
        }

        private Transport FindSteamTransport()
        {
            foreach (Transport t in Multipass.Transports)
                if (t != null && t.GetType().Name == SteamTransportTypeName)
                    return t;
            return null;
        }

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            string transportName = TransportName(args.TransportIndex);
            if (args.ConnectionState == LocalConnectionState.Started)
            {
                if (!_serverStartLogged)
                {
                    _serverStartLogged = true;
                    Debug.Log("Server started (" + transportName + ")");
                }
                else
                {
                    Debug.Log("[Net] server also listening on " + transportName);
                }
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                Debug.Log("[Net] server stopped on " + transportName);
            }
        }

        /// <summary>Server log line per join/leave; FishNet logs these only in the Editor.
        /// The format is stable so the spec 004 load test can count players from the log.</summary>
        private void OnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Started && args.ConnectionState != RemoteConnectionState.Stopped)
                return;
            string verb = args.ConnectionState == RemoteConnectionState.Started ? "joined" : "left";
            // The count is updated after this event for leaves, so adjust it here.
            int players = _networkManager.ServerManager.Clients.Count
                - (args.ConnectionState == RemoteConnectionState.Stopped && _networkManager.ServerManager.Clients.ContainsKey(connection.ClientId) ? 1 : 0);
            Debug.Log("[Net] player " + connection.ClientId + " " + verb + " via " + TransportName(args.TransportIndex)
                + ", players " + players);
        }

        // Join pipeline on the server: connected -> authenticated -> start scenes loaded -> player spawned.
        private void OnAuthenticationResult(NetworkConnection connection, bool passed)
        {
            Debug.Log("[Net] player " + connection.ClientId + (passed ? " authenticated" : " failed authentication"));
        }

        private void OnClientLoadedStartScenes(NetworkConnection connection, bool asServer)
        {
            if (asServer)
                Debug.Log("[Net] player " + connection.ClientId + " loaded start scenes");
        }

        private void OnPlayerSpawned(NetworkObject player)
        {
            Debug.Log("[Net] player " + player.OwnerId + " spawned as object " + player.ObjectId
                + " at " + player.transform.position + ", observers " + player.Observers.Count);
        }

        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            Status = "client " + args.ConnectionState;
            Debug.Log("[Net] " + Status);
            if (args.ConnectionState == LocalConnectionState.Started) Flow.TransportStarted();
            else if (args.ConnectionState == LocalConnectionState.Stopped) Flow.TransportStopped(Time.unscaledTimeAsDouble);
        }

        private string TransportName(int index)
        {
            Transport t = index >= 0 ? Multipass.GetTransport(index) : null;
            return t != null ? t.GetType().Name : "transport " + index;
        }
    }
}
