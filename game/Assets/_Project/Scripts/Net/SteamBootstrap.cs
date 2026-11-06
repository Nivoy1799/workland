using System;
using Steamworks;
using UnityEngine;

namespace Worktown.Game.Net
{
    /// <summary>
    /// Initializes Steam before FishNet starts (FishySteamworks expects it initialized).
    /// Client builds use the Steam client API; dedicated server builds (UNITY_SERVER) run as an
    /// anonymous Steam game server. If Steam is unavailable the game logs a warning and the
    /// Tugboat transport still works.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class SteamBootstrap : MonoBehaviour
    {
        public const ushort SteamGamePort = 7771;
        public const ushort SteamQueryPort = 27016;
        private const string ServerVersion = "0.1.0";

        /// <summary>True after a successful Steam (client or game server) init.</summary>
        public bool IsReady { get; private set; }

        /// <summary>SteamID64 of this client's user, or of the game server once it has logged on. 0 if unknown.</summary>
        public ulong LocalSteamId { get; private set; }

        /// <summary>Short human-readable status for the debug overlay.</summary>
        public string Status { get; private set; } = "not started";

#if UNITY_SERVER
        private Callback<SteamServersConnected_t> _serversConnected;
        private Callback<SteamServerConnectFailure_t> _serverConnectFailure;
#endif

        private void Awake()
        {
            try
            {
                if (!Packsize.Test() || !DllCheck.Test())
                {
                    Fail("Steamworks.NET native library check failed");
                    return;
                }
#if UNITY_SERVER
                IsReady = GameServer.Init(0, SteamGamePort, SteamQueryPort,
                    EServerMode.eServerModeNoAuthentication, ServerVersion);
                if (!IsReady)
                {
                    Fail("GameServer.Init failed (is steamclient.so from SteamCMD installed?)");
                    return;
                }
                _serversConnected = Callback<SteamServersConnected_t>.CreateGameServer(OnServersConnected);
                _serverConnectFailure = Callback<SteamServerConnectFailure_t>.CreateGameServer(OnServerConnectFailure);
                SteamGameServer.SetProduct("worktown");
                SteamGameServer.SetModDir("worktown");
                SteamGameServer.SetDedicatedServer(true);
                SteamGameServer.LogOnAnonymous();
                Status = "game server logging on";
                Debug.Log("[Steam] game server initialized, logging on anonymously");
#else
                IsReady = SteamAPI.Init();
                if (!IsReady)
                {
                    Fail("SteamAPI.Init failed (is Steam running?)");
                    return;
                }
                LocalSteamId = SteamUser.GetSteamID().m_SteamID;
                Status = "client ready";
                Debug.Log("[Steam] client ready, SteamID " + LocalSteamId);
#endif
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException || e is InvalidOperationException)
            {
                Fail(e.GetType().Name + ": " + e.Message);
            }
        }

        private void Fail(string reason)
        {
            IsReady = false;
            Status = "unavailable: " + reason;
            Debug.LogWarning("[Steam] " + Status + ". Steam transport disabled, Tugboat still works.");
        }

#if UNITY_SERVER
        private void OnServersConnected(SteamServersConnected_t _)
        {
            LocalSteamId = SteamGameServer.GetSteamID().m_SteamID;
            Status = "game server online";
            Debug.Log("[Steam] game server logged on, SteamID " + LocalSteamId + " (clients join with -steam " + LocalSteamId + ")");
        }

        private void OnServerConnectFailure(SteamServerConnectFailure_t failure)
        {
            Status = "game server logon failed: " + failure.m_eResult;
            Debug.LogWarning("[Steam] " + Status);
        }
#endif

        private void Update()
        {
            if (!IsReady) return;
#if UNITY_SERVER
            GameServer.RunCallbacks();
#else
            SteamAPI.RunCallbacks();
#endif
        }

        private void OnDestroy()
        {
            if (!IsReady) return;
            IsReady = false;
#if UNITY_SERVER
            SteamGameServer.LogOff();
            GameServer.Shutdown();
#else
            SteamAPI.Shutdown();
#endif
        }
    }
}
