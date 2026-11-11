using System.Text;
using FishNet.Managing;
using UnityEngine;
using Worktown.Game.Diagnostics;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Net
{
    /// <summary>
    /// Spec 049: watches the server's network tick from the main thread (a server or a host, every build). When the tick stops while
    /// the game keeps drawing (TickStallRules: the host freeze of two cook tours), it logs it loudly (the player log and the crash log,
    /// with FishNet's state for the next diagnosis) and closes the shop: every player gets a HostStalled notice (NetBootstrap.CloseAfterStall),
    /// instead of timing out and failing to rejoin. Added by NetBootstrap; holds no game rules.
    /// </summary>
    public sealed class TickWatchdog : MonoBehaviour
    {
        private NetBootstrap _bootstrap;
        private NetworkManager _net;
        private bool _armed;
        private bool _fired;
        private double _lastTickAt;
        private int _lastTickFrame;
        private uint _lastTick;

        /// <summary>Stalls seen in this process (the stall test and the logs).</summary>
        public int Stalls { get; private set; }

        public void Bind(NetBootstrap bootstrap)
        {
            _bootstrap = bootstrap;
            _net = bootstrap != null ? bootstrap.NetworkManager : null;
            if (_net != null) _net.TimeManager.OnTick += OnTick;
        }

        private void OnDestroy()
        {
            if (_net != null && _net.TimeManager != null) _net.TimeManager.OnTick -= OnTick;
        }

        private void OnTick()
        {
            _lastTickAt = Time.realtimeSinceStartupAsDouble;
            _lastTickFrame = Time.frameCount;
            _lastTick = _net.TimeManager.Tick;
        }

        private void Update()
        {
            if (_net == null) return;
            bool up = _net.IsServerStarted;
            if (!up)
            {
                _armed = false;
                _fired = false;
                return;
            }
            double now = Time.realtimeSinceStartupAsDouble;
            if (!_armed)
            {
                // The server just started: count from now.
                _armed = true;
                _lastTickAt = now;
                _lastTickFrame = Time.frameCount;
                return;
            }
            if (_fired) return;
            double since = now - _lastTickAt;
            int frames = Time.frameCount - _lastTickFrame;
            if (!TickStallRules.IsStalled(true, since, frames)) return;

            _fired = true;
            Stalls++;
            int clients = _net.ServerManager.Clients.Count;
            string line = TickStallRules.Describe(since, frames, _lastTick, clients);
            Debug.LogError("[Net] " + line);
            CrashLog.Note("host tick stall", line, State());
            _bootstrap.CloseAfterStall();
        }

        /// <summary>FishNet's state at the stall, for the crash log (what the next diagnosis needs).</summary>
        private string State()
        {
            var sb = new StringBuilder(256);
            var loop = _net.TimeManager.GetComponent("NetworkReaderLoop") as Behaviour;
            sb.Append("TimeManager.Tick ").Append(_net.TimeManager.Tick).Append(", TickRate ").Append(_net.TimeManager.TickRate).Append('\n');
            sb.Append("server started ").Append(_net.IsServerStarted).Append(", client started ").Append(_net.IsClientStarted).Append('\n');
            sb.Append("NetworkReaderLoop ").Append(loop == null ? "missing" : loop.isActiveAndEnabled ? "active" : "disabled").Append('\n');
            sb.Append("NetworkManager active ").Append(_net.isActiveAndEnabled).Append(", frame ").Append(Time.frameCount)
                .Append(", timeScale ").Append(Time.timeScale).Append(", unscaledDeltaTime ").Append(Time.unscaledDeltaTime).Append('\n');
            sb.Append("connections ").Append(_net.ServerManager.Clients.Count).Append(", spawned ").Append(_net.ServerManager.Objects.Spawned.Count);
            return sb.ToString();
        }
    }
}
