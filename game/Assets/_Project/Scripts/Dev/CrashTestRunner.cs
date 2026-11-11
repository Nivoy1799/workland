using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Worktown.Game.Diagnostics;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Dev
{
    /// <summary>
    /// Spec 049 AC4, development builds: <c>-crash-test</c> forces one exception of every kind the crash log (Diagnostics.CrashLog) must
    /// catch: on the main thread (escaping an Update, as a bug would), in a task nobody observes, and unhandled on a background thread
    /// (as in the 045 host freeze: the runtime may end the process right after, which is part of the test). Then it quits.
    /// scripts/perf-check.sh reads the crash log for the three entries and the build hash. Each line it waits for starts with
    /// <c>[CrashTest]</c>.
    /// </summary>
    public sealed class CrashTestRunner : MonoBehaviour
    {
        public const string MainMessage = "crash test 049: main thread";
        public const string TaskMessage = "crash test 049: task";
        public const string ThreadMessage = "crash test 049: background thread";

        private bool _throwNow;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            LaunchArgs args = LaunchArgs.Parse(Environment.GetCommandLineArgs());
            if (!args.IsValid || !args.CrashTest) return;
            Application.runInBackground = true;
            var go = new GameObject("CrashTestRunner");
            DontDestroyOnLoad(go);
            go.AddComponent<CrashTestRunner>();
        }

        private IEnumerator Start()
        {
            Debug.Log("[CrashTest] start, crash log " + CrashLog.FilePath);
            yield return new WaitForSecondsRealtime(2f);
            _throwNow = true;
            yield return new WaitForSecondsRealtime(1f);

            Debug.Log("[CrashTest] task");
            StartFaultingTask();
            yield return new WaitForSecondsRealtime(0.5f);
            // The task's exception is reported when the task is collected: force it.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            yield return new WaitForSecondsRealtime(1f);

            Debug.Log("[CrashTest] background thread");
            var thread = new Thread(() => throw new InvalidOperationException(ThreadMessage)) { IsBackground = true, Name = "crash-test" };
            thread.Start();
            yield return new WaitForSecondsRealtime(3f);
            Debug.Log("[CrashTest] done (" + (CrashLog.Recorder != null ? CrashLog.Recorder.Written : 0) + " entries written)");
            Application.Quit(0);
        }

        private static void StartFaultingTask() => Task.Run(() => throw new InvalidOperationException(TaskMessage));

        private void Update()
        {
            if (!_throwNow) return;
            _throwNow = false;
            Debug.Log("[CrashTest] main thread");
            throw new InvalidOperationException(MainMessage);
        }
    }
}
