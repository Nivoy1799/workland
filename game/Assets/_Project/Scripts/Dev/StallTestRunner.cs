using System;
using System.Collections;
using FishNet.Managing;
using UnityEngine;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Dev
{
    /// <summary>
    /// Spec 049, development builds: <c>-stall-test s</c> on a host or server stops FishNet's network tick on purpose s seconds after the
    /// server started (it turns off FishNet's reader loop, the component that drives the tick, while the game keeps drawing: the
    /// host freeze's symptom). The TickWatchdog must then log the stall, write it to the crash log and close the shop; this runner
    /// quits a moment after the server stopped (exit 0), or after 60 s without that (exit 1). scripts/perf-check.sh runs it.
    /// </summary>
    public sealed class StallTestRunner : MonoBehaviour
    {
        private const float GiveUpSeconds = 60f;

        private int _seconds;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            LaunchArgs args = LaunchArgs.Parse(Environment.GetCommandLineArgs());
            if (!args.IsValid || args.StallTestSeconds <= 0) return;
            Application.runInBackground = true;
            var go = new GameObject("StallTestRunner");
            DontDestroyOnLoad(go);
            go.AddComponent<StallTestRunner>()._seconds = args.StallTestSeconds;
        }

        private IEnumerator Start()
        {
            var bootstrap = FindFirstObjectByType<NetBootstrap>();
            NetworkManager net = bootstrap != null ? bootstrap.NetworkManager : null;
            if (net == null)
            {
                Debug.LogError("[StallTest] no NetworkManager");
                Application.Quit(1);
                yield break;
            }
            float giveUp = Time.realtimeSinceStartup + GiveUpSeconds;
            while (!net.IsServerStarted && Time.realtimeSinceStartup < giveUp) yield return null;
            yield return new WaitForSecondsRealtime(_seconds);
            var loop = net.TimeManager.GetComponent("NetworkReaderLoop") as Behaviour;
            if (loop == null)
            {
                Debug.LogError("[StallTest] FishNet's NetworkReaderLoop was not found: cannot stop the tick");
                Application.Quit(1);
                yield break;
            }
            Debug.Log("[StallTest] stopping the network tick on purpose at tick " + net.TimeManager.Tick);
            loop.enabled = false;
            while (bootstrap.Flow.Reason != DisconnectReason.HostStalled && Time.realtimeSinceStartup < giveUp + _seconds) yield return null;
            yield return new WaitForSecondsRealtime(1f); // the stop is pumped for a few frames
            // Closed: the watchdog saw the stall and the host shows the HostStalled screen (the server stop itself is logged).
            bool closed = bootstrap.Watchdog != null && bootstrap.Watchdog.Stalls > 0 && bootstrap.Flow.State == JoinState.Message
                && bootstrap.Flow.Reason == DisconnectReason.HostStalled;
            Debug.Log("[StallTest] " + (closed ? "the watchdog closed the shop" : "the shop was NOT closed")
                + " (stalls " + (bootstrap.Watchdog != null ? bootstrap.Watchdog.Stalls : 0) + ", flow " + bootstrap.Flow.State + " "
                + bootstrap.Flow.Reason + ")");
            yield return new WaitForSecondsRealtime(2f);
            Application.Quit(closed ? 0 : 1);
        }
    }
}
