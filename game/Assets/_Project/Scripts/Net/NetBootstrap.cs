using System;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Transporting.Multipass;
using FishNet.Transporting.Tugboat;
using UnityEngine;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Net
{
    /// <summary>
    /// Starts server, host or client from the command line (see <see cref="LaunchArgs"/>).
    /// Logs "Server started" as soon as the first transport is listening (spec 003 AC1).
    /// </summary>
    public sealed class NetBootstrap : MonoBehaviour
    {
        /// <summary>Type name of the FishySteamworks transport. It compiles into Assembly-CSharp,
        /// so this assembly cannot reference the type directly.</summary>
        private const string SteamTransportTypeName = "FishySteamworks";

        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private SteamBootstrap _steam;

        private bool _serverStartLogged;

        public LaunchArgs Args { get; private set; } = new LaunchArgs();
        public NetworkManager NetworkManager => _networkManager;
        public SteamBootstrap Steam => _steam;

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
            _networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
            _networkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            _networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;

            Args =LaunchArgs.Parse(Environment.GetCommandLineArgs());
            if (!Args.IsValid)
                Debug.LogError("[Net] bad command line: " + Args.Error);

            switch (Args.Mode)
            {
                case LaunchMode.Server:
                    StartServer();
                    break;
                case LaunchMode.Host:
                    StartHost();
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
                    StartServer();
#else
                    Status = "menu: pick Host, Server or Join";
#endif
                    break;
            }
        }

        private void OnDestroy()
        {
            if (_networkManager == null) return;
            _networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
            _networkManager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
            _networkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
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
            StartServer();
            ConnectTugboat("127.0.0.1", LaunchArgs.DefaultTugboatPort);
        }

        public void ConnectTugboat(string address, ushort port)
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
            Transport steamTransport = FindSteamTransport();
            if (steamTransport == null)
            {
                Status = "no Steam transport in Multipass";
                Debug.LogError("[Net] " + Status);
                return;
            }
            if (!_steam.IsReady)
            {
                Status = "Steam not ready: " + _steam.Status;
                Debug.LogError("[Net] " + Status);
                return;
            }
            Multipass.SetClientTransport(steamTransport);
            Status = "connecting via Steam to " + steamId;
            _networkManager.ClientManager.StartConnection(steamId);
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

        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            Status = "client " + args.ConnectionState;
            Debug.Log("[Net] " + Status);
        }

        private string TransportName(int index)
        {
            Transport t = index >= 0 ? Multipass.GetTransport(index) : null;
            return t != null ? t.GetType().Name : "transport " + index;
        }
    }
}
