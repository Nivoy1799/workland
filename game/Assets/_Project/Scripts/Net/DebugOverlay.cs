using FishNet.Managing;
using FishNet.Object;
using UnityEngine;
using Worktown.Game.Cooking;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Net
{
    /// <summary>
    /// IMGUI debug overlay (spec 003): ping, server tick, player count, held object.
    /// When started without a mode flag it also shows Host / Server / Join buttons.
    /// </summary>
    public sealed class DebugOverlay : MonoBehaviour
    {
        [SerializeField] private NetBootstrap _bootstrap;

        private string _joinAddress = "127.0.0.1:" + LaunchArgs.DefaultTugboatPort;
        private string _joinSteamId = string.Empty;
        private string _simStatus;
        private ServeFeed _feed;

        private void Start()
        {
            // Calls into /sim from a game script at runtime (spec 003 AC6 in a build, not only in tests).
            try
            {
                _simStatus = "sim linked, potato " + SimLink.PotatoHash().Substring(0, 8);
            }
            catch (System.Exception e)
            {
                _simStatus = "sim error: " + e.Message;
                Debug.LogError("[Sim] " + e);
            }
        }

        private void OnGUI()
        {
            if (Application.isBatchMode) return;
            NetworkManager nm = _bootstrap.NetworkManager;

            GUILayout.BeginArea(new Rect(10, 10, 480, 560), GUI.skin.box);
            GUILayout.Label("Worktown netcode graybox (spec 003)");
            GUILayout.Label("Mode: " + _bootstrap.Args.Mode + (_bootstrap.Args.IsValid ? string.Empty : "  (" + _bootstrap.Args.Error + ")"));
            GUILayout.Label("Status: " + _bootstrap.Status);
            GUILayout.Label("Steam: " + _bootstrap.Steam.Status
                + (_bootstrap.Steam.LocalSteamId != 0 ? "  id " + _bootstrap.Steam.LocalSteamId : string.Empty));
            GUILayout.Label(_simStatus);

            if (nm == null || nm.TimeManager == null)
            {
                // FishNet did not initialize (e.g. missing prefab collection); its own error explains why.
                GUILayout.Label("NetworkManager not initialized, see log");
            }
            else if (nm.IsServerStarted || nm.IsClientStarted)
            {
                GUILayout.Label("Server tick: " + nm.TimeManager.Tick);
                if (nm.IsClientStarted)
                    GUILayout.Label("Ping: " + nm.TimeManager.RoundTripTime + " ms");
                int players = nm.IsServerStarted ? nm.ServerManager.Clients.Count : nm.ClientManager.Clients.Count;
                GUILayout.Label("Players: " + players);
                DrawHeld(nm);
                GUILayout.Label("Move WASD, turn arrows or right mouse, E pick up / drop");
                GUILayout.Label("Kitchen (north): crate E, board 1-4, pan 1-3, pot 1-3, counter E serves");
                DrawServeFeed();
            }
            else
            {
                DrawMenu();
            }
            GUILayout.EndArea();
        }

        private static void DrawHeld(NetworkManager nm)
        {
            if (!nm.IsClientStarted) return;
            PickupInteractor local = null;
            foreach (NetworkObject obj in nm.ClientManager.Connection.Objects)
                if (obj != null && obj.TryGetComponent(out local))
                    break;
            if (local == null)
            {
                GUILayout.Label("Held: (no local player yet)");
                return;
            }
            Pickup held = local.FindHeldPickup();
            string heldText = "nothing";
            if (held != null)
                heldText = held.TryGetComponent(out Food food) ? food.Describe() : held.name + " #" + held.ObjectId;
            GUILayout.Label("Held: " + heldText);
            if (local.NearestStation != null)
                GUILayout.Label(">> " + local.NearestStation.Prompt, GUI.skin.box);
            if (local.LastResult.Length > 0)
                GUILayout.Label("Last: " + local.LastResult);
        }

        private void DrawServeFeed()
        {
            if (_feed == null) _feed = FindFirstObjectByType<ServeFeed>();
            if (_feed == null || !_feed.IsSpawned || _feed.Entries.Count == 0) return;
            GUILayout.Label("Served:");
            foreach (string entry in _feed.Entries)
                GUILayout.Label("  " + entry);
        }

        private void DrawMenu()
        {
            if (GUILayout.Button("Host (server + play, others join via Steam)"))
                _bootstrap.StartHost();
            if (GUILayout.Button("Server only"))
                _bootstrap.StartServer();

            GUILayout.BeginHorizontal();
            _joinAddress = GUILayout.TextField(_joinAddress, GUILayout.Width(200));
            if (GUILayout.Button("Join IP (Tugboat)")
                && LaunchArgs.TrySplitEndpoint(_joinAddress, out string address, out ushort port))
                _bootstrap.ConnectTugboat(address, port);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            _joinSteamId = GUILayout.TextField(_joinSteamId, GUILayout.Width(200));
            if (GUILayout.Button("Join SteamID"))
                _bootstrap.ConnectSteam(_joinSteamId.Trim());
            GUILayout.EndHorizontal();
        }
    }
}
