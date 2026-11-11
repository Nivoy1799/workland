using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using FishNet.Managing;
using FishNet.Transporting;
using Unity.Profiling;
using UnityEngine;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;
using Debug = UnityEngine.Debug;

namespace Worktown.Game.Dev
{
    /// <summary>
    /// Spec 045 (the spec 004 load test), development builds only. Starts itself after the scene loads when the command line has
    /// <c>-bot</c> (with <c>-connect</c>: drive the own player like a load-test bot, <see cref="BotBrain"/>) or <c>-metrics path</c> on a
    /// <c>-server</c> or <c>-host</c>. Every second it appends one JSON line (<see cref="LoadStats.Line"/>) to the metrics file:
    /// a server: connected clients, spawned objects, frame and tick work times (avg, p95, max in ms), bytes received, CPU (% of one
    /// core) and memory (Unity reserved, managed heap) of the process; a bot: the bytes it received (what the server sent it) and its round trip. A bot does
    /// everything through the owner's normal intents and requests (TourActor); the server decides as always.
    /// </summary>
    public sealed class LoadTestRunner : MonoBehaviour
    {
        private const float SpawnWait = 120f;
        private const float RetryDelay = 1.5f;
        private const int BotFrameRate = 30;

        private LaunchArgs _args;
        private NetworkManager _net;
        private NetBootstrap _bootstrap;
        private StreamWriter _metrics;
        private LocalConnectionState _clientState = LocalConnectionState.Starting;
        private bool _server;

        private readonly List<float> _frames = new List<float>();
        private readonly List<float> _ticks = new List<float>();
        private readonly Stopwatch _tickWatch = new Stopwatch();
        private long _bytesIn;
        private float _nextSample;
        private float _started;
        private TimeSpan _lastCpu;
        // Spec 049 (the soak): GC bytes per frame and Unity's object counts (dev-build profiler counters; 0 where unavailable).
        private ProfilerRecorder _gcAlloc;
        private ProfilerRecorder _objectCount;
        private double _gcBytes;
        private int _gcFrames;
        private float _lastCpuAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            LaunchArgs args = LaunchArgs.Parse(Environment.GetCommandLineArgs());
            if (!args.IsValid || (!args.IsBot && args.MetricsPath.Length == 0)) return;
            Application.runInBackground = true;
            var go = new GameObject("LoadTestRunner");
            DontDestroyOnLoad(go);
            go.AddComponent<LoadTestRunner>()._args = args;
        }

        private void Start()
        {
            _bootstrap = FindFirstObjectByType<NetBootstrap>();
            _net = _bootstrap != null ? _bootstrap.NetworkManager : null;
            if (_net == null)
            {
                Debug.LogError("[Load] no NetworkManager in the scene");
                enabled = false;
                return;
            }
            _server = !_args.IsBot;
            _started = Time.unscaledTime;
            _nextSample = _started + 1f;
            _lastCpu = Process.GetCurrentProcess().TotalProcessorTime;
            _lastCpuAt = _started;
            if (_args.MetricsPath.Length > 0)
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(_args.MetricsPath));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                _metrics = new StreamWriter(_args.MetricsPath, false) { AutoFlush = true };
            }
            Transport transport = _net.TransportManager.Transport;
            if (_server)
            {
                transport.OnServerReceivedData += OnServerData;
                _gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
                _objectCount = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Object Count");
                _net.TimeManager.OnPreTick += OnPreTick;
                _net.TimeManager.OnPostTick += OnPostTick;
                Debug.Log("[Load] server metrics to " + _args.MetricsPath);
            }
            else
            {
                // A bot renders nothing: no need to spin. Through FishNet, which sets the frame rate itself on every connection change
                // (500 by default; Application.targetFrameRate alone is overwritten).
                _net.ClientManager.SetFrameRate(BotFrameRate);
                transport.OnClientReceivedData += OnClientData;
                _net.ClientManager.OnClientConnectionState += OnClientState;
                StartCoroutine(RunBot());
            }
        }

        private void OnDestroy()
        {
            if (_net != null && _net.TransportManager != null && _net.TransportManager.Transport != null)
            {
                _net.TransportManager.Transport.OnServerReceivedData -= OnServerData;
                _net.TransportManager.Transport.OnClientReceivedData -= OnClientData;
            }
            if (_net != null && _net.TimeManager != null)
            {
                _net.TimeManager.OnPreTick -= OnPreTick;
                _net.TimeManager.OnPostTick -= OnPostTick;
            }
            if (_net != null && _net.ClientManager != null) _net.ClientManager.OnClientConnectionState -= OnClientState;
            _metrics?.Dispose();
            _metrics = null;
            if (_gcAlloc.Valid) _gcAlloc.Dispose();
            if (_objectCount.Valid) _objectCount.Dispose();
        }

        private void OnServerData(ServerReceivedDataArgs args) => _bytesIn += args.Data.Count;
        private void OnClientData(ClientReceivedDataArgs args) => _bytesIn += args.Data.Count;
        private void OnClientState(ClientConnectionStateArgs args) => _clientState = args.ConnectionState;
        private void OnPreTick() => _tickWatch.Restart();

        private void OnPostTick()
        {
            if (!_tickWatch.IsRunning) return;
            _tickWatch.Stop();
            _ticks.Add((float)_tickWatch.Elapsed.TotalMilliseconds);
        }

        private void Update()
        {
            _frames.Add(Time.unscaledDeltaTime * 1000f);
            if (_gcAlloc.Valid)
            {
                _gcBytes += _gcAlloc.LastValue;
                _gcFrames++;
            }
            float now = Time.unscaledTime;
            if (now < _nextSample) return;
            _nextSample = now + 1f;
            Sample(now);
        }

        private void Sample(float now)
        {
            var fields = new List<KeyValuePair<string, double>>(16) { new KeyValuePair<string, double>("t", Math.Round(now - _started)) };
            fields.Add(Field("unix", DateTimeOffset.UtcNow.ToUnixTimeSeconds())); // the script cuts the ramp and the hold by wall time
            if (_server)
            {
                Process self = Process.GetCurrentProcess();
                TimeSpan cpu = self.TotalProcessorTime;
                float wall = Math.Max(0.001f, now - _lastCpuAt);
                double cpuPercent = (cpu - _lastCpu).TotalSeconds / wall * 100d;
                _lastCpu = cpu;
                _lastCpuAt = now;
                bool up = _net.IsServerStarted;
                fields.Add(Field("clients", up ? _net.ServerManager.Clients.Count : 0));
                fields.Add(Field("objects", up ? _net.ServerManager.Objects.Spawned.Count : 0));
                fields.Add(Field("ticks", _ticks.Count));
                fields.Add(Field("tick_avg_ms", LoadStats.Average(_ticks)));
                fields.Add(Field("tick_p95_ms", LoadStats.Percentile(_ticks, 95f)));
                fields.Add(Field("tick_max_ms", LoadStats.Max(_ticks)));
                fields.Add(Field("frame_avg_ms", LoadStats.Average(_frames)));
                fields.Add(Field("frame_p95_ms", LoadStats.Percentile(_frames, 95f)));
                fields.Add(Field("frame_max_ms", LoadStats.Max(_frames)));
                fields.Add(Field("in_bytes", _bytesIn));
                fields.Add(Field("cpu_pct", cpuPercent));
                // Mono reports no working set here: Unity's own reserved memory and the managed heap (the script reads the working set).
                fields.Add(Field("unity_reserved_mb", UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / (1024d * 1024d)));
                fields.Add(Field("managed_heap_mb", GC.GetTotalMemory(false) / (1024d * 1024d)));
                // Spec 049: the soak's leak checks.
                fields.Add(Field("gc_kb_per_frame", _gcFrames > 0 ? _gcBytes / _gcFrames / 1024d : 0d));
                fields.Add(Field("unity_objects", _objectCount.Valid ? _objectCount.LastValue : 0));
                fields.Add(Field("gc_collections", GC.CollectionCount(0)));
            }
            else
            {
                fields.Add(Field("connected", _net.IsClientStarted ? 1 : 0));
                fields.Add(Field("in_bytes", _bytesIn));
                fields.Add(Field("rtt_ms", _net.IsClientStarted ? _net.TimeManager.RoundTripTime : 0));
                fields.Add(Field("frame_avg_ms", LoadStats.Average(_frames)));
            }
            _ticks.Clear();
            _frames.Clear();
            _bytesIn = 0;
            _gcBytes = 0d;
            _gcFrames = 0;
            _metrics?.WriteLine(LoadStats.Line(fields));
        }

        private static KeyValuePair<string, double> Field(string name, double value) => new KeyValuePair<string, double>(name, value);

        // ---- the bot ----

        private IEnumerator RunBot()
        {
            var brain = new BotBrain(_args.Seed);
            PlayerController me = null;
            float spawnLimit = Time.unscaledTime + SpawnWait;
            while (me == null)
            {
                if (Time.unscaledTime > spawnLimit)
                {
                    Debug.LogError("[Load] bot " + _args.Seed + ": no own player after " + SpawnWait + " s");
                    Application.Quit(1);
                    yield break;
                }
                if (_clientState == LocalConnectionState.Stopped)
                {
                    yield return new WaitForSecondsRealtime(RetryDelay);
                    _clientState = LocalConnectionState.Starting;
                    _bootstrap.ConnectTugboat(_args.Address, _args.Port);
                }
                yield return new WaitForSecondsRealtime(0.25f);
                me = FindOwnPlayer();
            }
            yield return new WaitForSecondsRealtime(2f);
            var intents = new ScriptedIntentSource();
            me.IntentSource = intents;
            var actor = new TourActor(me, intents);
            FindFirstObjectByType<Worktown.Game.Player.PlayerInputReader>()?.DisableForScript(); // intents only, no devices
            Debug.Log("[Load] bot " + _args.Seed + " spawned at " + me.transform.position);

            var waypoints = new List<Vector3>();
            var candidates = new List<Vector3>();
            var free = new List<Pickup>();
            while (me != null && me.IsSpawned)
            {
                if (waypoints.Count == 0)
                    foreach (Pickup p in Pickup.ClientList)
                        if (p != null) waypoints.Add(p.transform.position);
                // Walk between waypoints until the next pick-up try.
                float tryAt = Time.unscaledTime + brain.NextPickupWait();
                while (Time.unscaledTime < tryAt && me != null && me.IsSpawned)
                    yield return WalkSomewhere(actor, brain, waypoints);
                if (me == null || !me.IsSpawned) break;

                // The nearest free item: walk there and ask for it like a click would.
                free.Clear();
                candidates.Clear();
                foreach (Pickup p in Pickup.ClientList)
                {
                    if (p == null || p.IsHeld || !p.IsSpawned) continue;
                    free.Add(p);
                    candidates.Add(p.transform.position);
                }
                int pick = BotBrain.Nearest(actor.Position, candidates, BotBrain.MaxPickupDistance);
                if (pick < 0) continue;
                Pickup target = free[pick];
                yield return actor.WalkTo(target.transform.position, false, false, 15f);
                if (target == null || !target.IsSpawned || target.IsHeld) continue;
                actor.Interactor.DevSendPickup(target.NetworkObject);
                yield return new WaitForSecondsRealtime(1f);
                if (target == null || !target.IsSpawned || target.HolderClientId.Value != me.OwnerId) continue;

                // Carry it around, then drop it in front of the feet.
                float dropAt = Time.unscaledTime + brain.NextCarry();
                while (Time.unscaledTime < dropAt && me != null && me.IsSpawned)
                    yield return WalkSomewhere(actor, brain, waypoints);
                if (me == null || !me.IsSpawned) break;
                actor.Interactor.DevDrop();
                yield return new WaitForSecondsRealtime(1f);
            }
            Debug.Log("[Load] bot " + _args.Seed + ": the own player is gone");
        }

        private static IEnumerator WalkSomewhere(TourActor actor, BotBrain brain, List<Vector3> waypoints)
        {
            int next = brain.NextWaypoint(waypoints.Count);
            if (next < 0)
            {
                yield return new WaitForSecondsRealtime(1f);
                yield break;
            }
            yield return actor.WalkTo(waypoints[next], false, false, 10f);
        }

        private static PlayerController FindOwnPlayer()
        {
            foreach (PlayerController player in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
                if (player != null && player.IsSpawned && player.IsOwner) return player;
            return null;
        }
    }
}
