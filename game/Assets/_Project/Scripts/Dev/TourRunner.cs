using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.UIElements;
using Worktown.Game.Cooking;
using Worktown.Game.Net;
using Worktown.Game.Net.Rules;
using Worktown.Game.Player;

namespace Worktown.Game.Dev
{
    /// <summary>
    /// Spec 027, development builds only. Starts itself after the scene loads when the command line has
    /// <c>-tour name</c> (run that tour, write its images and sheet, quit with 0, or 1 if a step timed out),
    /// <c>-observe name</c> (stand at the tour's observer spot, watch from its fixed camera, take the same frame
    /// sequences when the tour player moves, quit when it leaves) or <c>-demo-cookware</c> (spec 021 AC8). Everything
    /// goes through the owner's normal intents and requests; the server decides as always. Both tour roles keep running
    /// without window focus (Application.runInBackground).
    /// </summary>
    public sealed class TourRunner : MonoBehaviour
    {
        private const string DemoCookwareArg = "-demo-cookware";
        private const float SpawnWait = 120f;
        private const float RetryDelay = 1.5f;
        private const float ObserverGoneSeconds = 3f;
        private const int MaxObserverShots = 40;
        private const float NeverSeenSeconds = 90f;

        private enum Role { Tour, Observer, CookDemo }

        private Role _role;
        private Tour _tour;
        private TourCapture _capture;
        private TourActor _actor;
        private NetBootstrap _bootstrap;
        private LocalConnectionState _clientState = LocalConnectionState.Starting;
        private float _deadline = float.PositiveInfinity;
        private int _framesRunning;
        private bool _finished;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Application.isBatchMode) return;
            string[] args = Environment.GetCommandLineArgs();
            bool tour = Array.IndexOf(args, "-tour") >= 0;
            bool observe = Array.IndexOf(args, "-observe") >= 0;
            bool demo = Array.IndexOf(args, DemoCookwareArg) >= 0;
            if (!tour && !observe && !demo) return;
            // Scripted runs must keep going while another window has focus (the player setting pauses them otherwise).
            Application.runInBackground = true;
            var go = new GameObject("TourRunner");
            DontDestroyOnLoad(go);
            go.AddComponent<TourRunner>()._role = tour ? Role.Tour : observe ? Role.Observer : Role.CookDemo;
        }

        private void Awake()
        {
            _bootstrap = FindFirstObjectByType<NetBootstrap>();
            if (_bootstrap != null && _bootstrap.NetworkManager != null)
                _bootstrap.NetworkManager.ClientManager.OnClientConnectionState += OnClientState;
        }

        private void OnDestroy()
        {
            if (_bootstrap != null && _bootstrap.NetworkManager != null)
                _bootstrap.NetworkManager.ClientManager.OnClientConnectionState -= OnClientState;
        }

        private void OnClientState(ClientConnectionStateArgs args) => _clientState = args.ConnectionState;

        private IEnumerator Start()
        {
            LaunchArgs args = LaunchArgs.Parse(Environment.GetCommandLineArgs());
            if (_role != Role.CookDemo)
            {
                if (!args.IsValid)
                {
                    Finish(1, "bad command line: " + args.Error);
                    yield break;
                }
                string name = _role == Role.Tour ? args.Tour : args.Observe;
                if (!TourRules.TryGet(name, out _tour))
                {
                    Finish(1, "unknown tour '" + name + "', known: " + string.Join(", ", TourRules.Names));
                    yield break;
                }
                string root = args.TourOut.Length > 0 ? args.TourOut : Path.Combine(Application.persistentDataPath, "visual");
                string dir = Path.Combine(root, _tour.Name + (_role == Role.Observer ? "-observer" : ""));
                _capture = new TourCapture(dir, TourRules.SheetCell);
                _deadline = Time.unscaledTime + SpawnWait + _tour.TimeoutSeconds;
                Debug.Log("[Tour] " + _role + " '" + _tour.Name + "', images to " + dir);
            }

            // The tour client may start before the server (local runs start the host last): retry until it is up.
            if (args.Mode == LaunchMode.ClientTugboat && _bootstrap != null) StartCoroutine(RetryConnect(args));
            PlayerController me = null;
            float spawnLimit = Time.unscaledTime + SpawnWait;
            while (me == null)
            {
                if (Time.unscaledTime > spawnLimit)
                {
                    Finish(1, "no own player after " + SpawnWait + " s");
                    yield break;
                }
                yield return new WaitForSecondsRealtime(0.25f);
                me = FindPlayer(owner: true);
            }
            yield return new WaitForSecondsRealtime(_role == Role.CookDemo ? 6f : 2f);

            var intents = new ScriptedIntentSource();
            me.IntentSource = intents;
            _actor = new TourActor(me, intents);
            switch (_role)
            {
                case Role.CookDemo:
                    yield return CookSequence.Run(_actor, null, 40f);
                    break; // the demo keeps the client open, as before
                case Role.Tour:
                    yield return RunTour();
                    yield return End();
                    break;
                case Role.Observer:
                    yield return RunObserver();
                    yield return End();
                    break;
            }
        }

        private void Update()
        {
            if (!_finished && Time.unscaledTime > _deadline)
            {
                _deadline = float.PositiveInfinity;
                StopAllCoroutines();
                _actor?.CountTimeout("whole tour");
                StartCoroutine(End());
            }
        }

        private IEnumerator RetryConnect(LaunchArgs args)
        {
            float until = Time.unscaledTime + SpawnWait;
            while (Time.unscaledTime < until && FindPlayer(owner: true) == null)
            {
                if (_clientState == LocalConnectionState.Stopped)
                {
                    yield return new WaitForSecondsRealtime(RetryDelay);
                    Debug.Log("[Tour] server not reachable yet, connecting again to " + args.Address + ":" + args.Port);
                    _clientState = LocalConnectionState.Starting;
                    _bootstrap.ConnectTugboat(args.Address, args.Port);
                }
                yield return null;
            }
        }

        private static PlayerController FindPlayer(bool owner)
        {
            foreach (PlayerController player in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
                if (player != null && player.IsSpawned && player.IsOwner == owner) return player;
            return null;
        }

        // ---- Tour client ----

        private IEnumerator RunTour()
        {
            for (int i = 0; i < _tour.Steps.Count; i++)
            {
                TourStep step = _tour.Steps[i];
                Debug.Log("[Tour] step " + (i + 1) + "/" + _tour.Steps.Count + ": " + step);
                switch (step.Kind)
                {
                    case TourStepKind.WalkTo:
                        yield return _actor.WalkTo(step.Point, step.Sprint, step.Crouch);
                        break;
                    case TourStepKind.Look:
                        yield return _actor.Look(step.Yaw, step.Pitch);
                        break;
                    case TourStepKind.LookAt:
                        yield return _actor.AimAt(step.Point, 2f);
                        break;
                    case TourStepKind.View:
                        _actor.Intents.View = step.ViewMode;
                        yield return new WaitForSecondsRealtime(ViewRules.BlendSeconds + 0.15f);
                        break;
                    case TourStepKind.Wait:
                        yield return new WaitForSecondsRealtime(step.Seconds);
                        break;
                    case TourStepKind.Shot:
                        yield return _capture.Shot(step.Name);
                        break;
                    case TourStepKind.Frames:
                        StartCoroutine(TrackFrames(_capture.Frames(step.Name, step.FrameCount, step.Interval)));
                        break;
                    case TourStepKind.Act:
                        yield return Act(step.Name);
                        break;
                    case TourStepKind.WaitObserver:
                        yield return WaitForObserver();
                        break;
                }
            }
            while (_framesRunning > 0) yield return null;
        }

        private IEnumerator TrackFrames(IEnumerator frames)
        {
            _framesRunning++;
            yield return frames;
            _framesRunning--;
        }

        private IEnumerator WaitForObserver()
        {
            float limit = Time.unscaledTime + TourRules.ObserverWait;
            Vector3 spot = _tour.ObserverSpot;
            Vector3 last = Vector3.zero;
            float stillSince = float.PositiveInfinity;
            while (true)
            {
                // At its spot and standing still for a second: the observer has arrived and set up its camera.
                PlayerController other = FindPlayer(owner: false);
                bool atSpot = other != null && FlatDistance(other.transform.position, spot) <= TourRules.ObserverSpotDistance;
                if (!atSpot || FlatDistance(other.transform.position, last) > 0.02f) stillSince = Time.unscaledTime;
                if (other != null) last = other.transform.position;
                if (atSpot && Time.unscaledTime - stillSince >= 1f) break;
                if (Time.unscaledTime > limit)
                {
                    _actor.CountTimeout("observer at " + spot);
                    yield break;
                }
                yield return null;
            }
            Debug.Log("[Tour] the observer stands at its spot");
        }

        private Vessel _heldPan;
        private PlaceSurface _panHome;

        private IEnumerator Act(string name)
        {
            switch (name)
            {
                case TourRules.ActCook:
                    yield return CookSequence.Run(_actor, shot => _capture.Shot(shot), 30f);
                    break;
                case TourRules.ActPickUpPan:
                    _heldPan = null;
                    _panHome = null;
                    foreach (Vessel v in FindObjectsByType<Vessel>(FindObjectsSortMode.None))
                        if (v.Kind == VesselKind.Pan) _heldPan = v;
                    if (_heldPan == null)
                    {
                        _actor.CountTimeout("no pan in the scene");
                        yield break;
                    }
                    foreach (PlaceSurface s in FindObjectsByType<PlaceSurface>(FindObjectsSortMode.None))
                        if (s.IsBurner && Vector3.Distance(s.SpotWorldPosition(0), _heldPan.transform.position) <= 0.2f) _panHome = s;
                    var pan = _heldPan;
                    yield return _actor.Use("pick up the pan", pan.transform.position + Vector3.up * 0.05f, InteractionAction.PickUp,
                        () => _actor.Interactor.DevSendPickup(pan.NetworkObject));
                    break;
                case TourRules.ActPutPanBack:
                    if (_panHome == null) yield break;
                    PlaceSurface home = _panHome;
                    yield return _actor.Use("put the pan back", home.SpotWorldPosition(0), InteractionAction.Place,
                        () => _actor.Interactor.DevSendPlace(home.Index, 0));
                    break;
                default:
                    _actor.CountTimeout("unknown act '" + name + "'");
                    break;
            }
        }

        // ---- Observer ----

        private IEnumerator RunObserver()
        {
            _actor.Intents.View = ViewMode.FirstPerson;
            foreach (Vector3 point in _tour.ObserverRoute)
                yield return _actor.WalkTo(point, false, false, 40f);
            if (_tour.HasObserverCamera) FixCamera();
            else yield return _actor.Look(_tour.ObserverYaw, 0f);
            Debug.Log("[Tour] observer ready at " + _actor.Position);

            List<TourStep> sequences = TourRules.FrameSteps(_tour);
            int segment = 0;
            bool armed = false, capturing = false, seen = false;
            float stillFor = 0f, goneSince = -1f;
            float nextShot = _tour.ObserverShotEvery > 0f ? Time.unscaledTime + _tour.ObserverShotEvery : float.PositiveInfinity;
            int shots = 0;
            Vector3 last = Vector3.zero;
            float lastSample = Time.unscaledTime, ready = Time.unscaledTime;
            while (true)
            {
                PlayerController other = FindPlayer(owner: false);
                if (other == null && !seen && Time.unscaledTime - ready > NeverSeenSeconds)
                {
                    _actor.CountTimeout("the tour player never showed up");
                    break;
                }
                if (other == null)
                {
                    if (seen && goneSince < 0f) goneSince = Time.unscaledTime;
                    if (seen && Time.unscaledTime - goneSince > ObserverGoneSeconds) break;
                    yield return null;
                    continue;
                }
                if (!seen) last = other.transform.position;
                seen = true;
                goneSince = -1f;

                float dt = Time.unscaledTime - lastSample;
                if (dt >= 0.1f)
                {
                    // Sampled every 0.1 s: the root moves on ticks, so per-frame speeds are noisy.
                    float speed = FlatDistance(other.transform.position, last) / dt;
                    last = other.transform.position;
                    lastSample = Time.unscaledTime;
                    if (segment < sequences.Count && !capturing)
                    {
                        stillFor = speed < 0.1f ? stillFor + dt : 0f;
                        if (stillFor >= 0.5f) armed = true;
                        if (armed && speed > 0.4f)
                        {
                            TourStep step = sequences[segment];
                            Debug.Log("[Tour] observer: the tour player moves, frames '" + step.Name + "'");
                            capturing = true;
                            StartCoroutine(Capture(step, () => { capturing = false; armed = false; stillFor = 0f; segment++; }));
                        }
                    }
                }
                if (!capturing && shots < MaxObserverShots && Time.unscaledTime >= nextShot)
                {
                    shots++;
                    nextShot += _tour.ObserverShotEvery;
                    yield return _capture.Shot("guests-view-" + shots.ToString("00"));
                }
                yield return null;
            }
            while (capturing) yield return null;
            if (segment < sequences.Count) _actor.CountTimeout("observer saw " + segment + " of " + sequences.Count + " sequences");
        }

        private IEnumerator Capture(TourStep step, Action done)
        {
            yield return _capture.Frames(step.Name, step.FrameCount, step.Interval);
            done();
        }

        /// <summary>The observer's fixed camera: the main camera leaves Cinemachine and stays at the tour's pose; the
        /// screen-space UI is hidden so nothing covers the walking player.</summary>
        private void FixCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[Tour] observer: no main camera");
                return;
            }
            if (cam.GetComponent("CinemachineBrain") is Behaviour brain) brain.enabled = false;
            cam.transform.SetPositionAndRotation(_tour.ObserverCameraPosition, Quaternion.Euler(-_tour.ObserverCameraPitch, _tour.ObserverCameraYaw, 0f));
            cam.fieldOfView = ViewRules.FieldOfView;
            foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (canvas.renderMode == RenderMode.ScreenSpaceOverlay) canvas.enabled = false;
            // The HUD is UI Toolkit: hide the documents' roots (the views keep updating them, unseen).
            foreach (UIDocument document in FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
                if (document.rootVisualElement != null) document.rootVisualElement.style.display = DisplayStyle.None;
        }

        // ---- End ----

        private IEnumerator End()
        {
            if (_finished) yield break;
            _finished = true;
            if (_actor != null) _actor.Intents.Stop();
            string sheet = null;
            if (_capture != null) yield return _capture.WriteSheet(TourRules.SheetColumns, path => sheet = path);
            int timeouts = _actor != null ? _actor.Timeouts : 1;
            string summary = _tour.Name + " " + _role.ToString().ToLowerInvariant() + ": " + (_capture?.Count ?? 0) + " images, sheet "
                + (sheet ?? "none") + ", timeouts " + timeouts + ", aim fallbacks " + (_actor?.Fallbacks ?? 0);
            Finish(timeouts > 0 ? 1 : 0, summary);
        }

        private void Finish(int code, string message)
        {
            _finished = true;
            if (code == 0) Debug.Log("[Tour] done: " + message);
            else Debug.LogError("[Tour] failed: " + message);
            Application.Quit(code);
        }

        private static float FlatDistance(Vector3 a, Vector3 b) => TourActor.FlatDistance(a, b);
    }
}
