using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using FishNet.Managing;
using Unity.Profiling;
using UnityEngine;
using Worktown.Game.Cooking;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;
using Worktown.Game.Settings;

namespace Worktown.Game.Dev
{
    /// <summary>
    /// Spec 049 AC2, development builds: the profiling tour. On a host with <c>-perf-tour path</c> it waits for the players
    /// (<c>-max-players</c>: the load-test bots plus the host, up to 4 minutes), then walks the host's own player to every Station in the
    /// scene (found at run time, sorted by index: a new station is measured without editing anything), aims at it and records
    /// <see cref="MeasureSeconds"/> of frames there: the frame time and the GC bytes allocated in each frame (Unity's "GC Allocated In
    /// Frame" counter). One JSON line per station and one "ALL" line go to the file (PerfStats.Line); "[Perf] tour done" ends it. Vsync
    /// and the frame cap are off for the run (in memory only: a scripted run never stores settings), so the numbers are the frame's
    /// real cost. The host keeps running afterwards (the soak); scripts/perf-check.sh decides when it ends.
    /// </summary>
    public sealed class PerfTourRunner : MonoBehaviour
    {
        public const float MeasureSeconds = 6f;
        private const float PlayersWait = 240f;
        private const float SettleSeconds = 8f;
        private const float StandOff = 1.3f;

        private LaunchArgs _args;
        private NetworkManager _net;
        private ProfilerRecorder _gcAlloc;
        private bool _measuring;
        private readonly List<float> _frames = new List<float>(1024);
        private readonly List<long> _gc = new List<long>(1024);
        private readonly List<float> _allFrames = new List<float>(16384);
        private readonly List<long> _allGc = new List<long>(16384);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            LaunchArgs args = LaunchArgs.Parse(Environment.GetCommandLineArgs());
            if (!args.IsValid || args.PerfTourPath.Length == 0) return;
            Application.runInBackground = true;
            var go = new GameObject("PerfTourRunner");
            DontDestroyOnLoad(go);
            go.AddComponent<PerfTourRunner>()._args = args;
        }

        private void OnDestroy()
        {
            if (_gcAlloc.Valid) _gcAlloc.Dispose();
        }

        private void Update()
        {
            if (!_measuring) return;
            float ms = Time.unscaledDeltaTime * 1000f;
            long gc = _gcAlloc.Valid ? _gcAlloc.LastValue : 0L;
            _frames.Add(ms);
            _gc.Add(gc);
            _allFrames.Add(ms);
            _allGc.Add(gc);
        }

        private IEnumerator Start()
        {
            var bootstrap = FindFirstObjectByType<NetBootstrap>();
            _net = bootstrap != null ? bootstrap.NetworkManager : null;
            if (_net == null)
            {
                Debug.LogError("[Perf] no NetworkManager in the scene");
                yield break;
            }
            _gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            if (!_gcAlloc.Valid) Debug.LogWarning("[Perf] the GC Allocated In Frame counter is not available: GC per frame is 0");

            // The frame's own cost: no vsync, no cap (in memory only).
            GameSettings uncapped = GameSettings.Current.Clone();
            uncapped.VSync = false;
            uncapped.FrameCap = 0;
            GameSettings.Use(uncapped);

            int wanted = Math.Max(1, _args.MaxPlayers);
            float giveUp = Time.realtimeSinceStartup + PlayersWait;
            while (Time.realtimeSinceStartup < giveUp && (!_net.IsServerStarted || _net.ServerManager.Clients.Count < wanted))
                yield return new WaitForSecondsRealtime(1f);
            int players = _net.IsServerStarted ? _net.ServerManager.Clients.Count : 0;
            Debug.Log("[Perf] " + players + " of " + wanted + " players in the shop, settling " + SettleSeconds + " s");

            PlayerController me = null;
            while (me == null && Time.realtimeSinceStartup < giveUp + 30f)
            {
                me = FindOwnPlayer();
                if (me == null) yield return new WaitForSecondsRealtime(0.5f);
            }
            if (me == null)
            {
                Debug.LogError("[Perf] the host's own player never spawned");
                yield break;
            }
            var intents = new ScriptedIntentSource();
            me.IntentSource = intents;
            var actor = new TourActor(me, intents);
            FindFirstObjectByType<Worktown.Game.Player.PlayerInputReader>()?.DisableForScript();
            yield return new WaitForSecondsRealtime(SettleSeconds);

            var stations = new List<Station>(FindObjectsByType<Station>(FindObjectsSortMode.None));
            stations.Sort((a, b) => a.Id.CompareTo(b.Id));
            Vector3 centre = Vector3.zero;
            foreach (Station s in stations) centre += s.transform.position;
            if (stations.Count > 0) centre /= stations.Count;
            Debug.Log("[Perf] tour of " + stations.Count + " stations, " + MeasureSeconds + " s each");

            var lines = new List<string>(stations.Count + 1);
            for (int i = 0; i < stations.Count; i++)
            {
                Station station = stations[i];
                if (station == null) continue;
                Vector3 at = station.transform.position;
                Vector3 toCentre = centre - at;
                toCentre.y = 0f;
                Vector3 stand = at + (toCentre.sqrMagnitude > 0.01f ? toCentre.normalized * StandOff : Vector3.back * StandOff);
                yield return actor.WalkTo(stand, false, false, 20f);
                yield return actor.AimAt(at, 2f);
                _frames.Clear();
                _gc.Clear();
                _measuring = true;
                yield return new WaitForSecondsRealtime(MeasureSeconds);
                _measuring = false;
                players = _net.IsServerStarted ? _net.ServerManager.Clients.Count : 0;
                PerfStats.Summary sum = PerfStats.Summarize(_frames, _gc);
                lines.Add(PerfStats.Line(station.DisplayName, station.Kind.ToString(), station.Id, players, sum));
                Debug.Log("[Perf] station " + (i + 1) + "/" + stations.Count + " " + station.DisplayName + ": avg " + sum.AvgMs.ToString("0.00")
                    + " ms, p95 " + sum.P95Ms.ToString("0.00") + ", p99 " + sum.P99Ms.ToString("0.00") + ", GC "
                    + sum.GcBytesPerFrame.ToString("0") + " B/frame, " + sum.Frames + " frames");
            }
            PerfStats.Summary all = PerfStats.Summarize(_allFrames, _allGc);
            lines.Add(PerfStats.Line("ALL", "ALL", -1, players, all));
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(_args.PerfTourPath));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllLines(_args.PerfTourPath, lines);
            }
            catch (IOException e)
            {
                Debug.LogError("[Perf] could not write " + _args.PerfTourPath + ": " + e.Message);
            }
            Debug.Log("[Perf] tour done: avg " + all.AvgMs.ToString("0.00") + " ms, p95 " + all.P95Ms.ToString("0.00") + ", p99 "
                + all.P99Ms.ToString("0.00") + ", GC " + all.GcBytesPerFrame.ToString("0") + " B/frame over " + all.Frames + " frames");
            // Back to the middle of the room for the soak.
            yield return actor.WalkTo(centre, false, false, 20f);
        }

        private static PlayerController FindOwnPlayer()
        {
            foreach (PlayerController player in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
                if (player != null && player.IsSpawned && player.IsOwner) return player;
            return null;
        }
    }
}
