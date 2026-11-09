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
                    yield return CookSequence.Run(_actor, new CookCapture(shot => _capture.Shot(shot), StartFrames, SetTweenScale, () => _framesRunning > 0), 30f);
                    break;
                case TourRules.ActSlowMotion:
                    SetTweenScale(TourRules.SlowMotionScale);
                    break;
                case TourRules.ActNormalMotion:
                    SetTweenScale(1f);
                    break;
                case TourRules.ActPickUpPan:
                case TourRules.ActPickUpPanFrames:
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
                    // Spec 027b: with frames, a shot of the highlight and prompt, then frames from the click's own frame on.
                    Func<IEnumerator> pickFrames = name == TourRules.ActPickUpPanFrames
                        ? BeforeClick("pan-highlight" + SlowSuffix, "pan-pickup" + SlowSuffix) : null;
                    yield return _actor.Use("pick up the pan", pan.transform.position + Vector3.up * 0.05f, InteractionAction.PickUp,
                        () => _actor.Interactor.DevSendPickup(pan.NetworkObject), pickFrames);
                    break;
                case TourRules.ActPutPanBack:
                case TourRules.ActPutPanBackFrames:
                    if (_panHome == null) yield break;
                    PlaceSurface home = _panHome;
                    Func<IEnumerator> placeFrames = name == TourRules.ActPutPanBackFrames
                        ? BeforeClick("pan-ghost" + SlowSuffix, "pan-place" + SlowSuffix) : null;
                    yield return _actor.Use("put the pan back", home.SpotWorldPosition(0), InteractionAction.Place,
                        () => _actor.Interactor.DevSendPlace(home.Index, 0), placeFrames);
                    break;
                default:
                    _actor.CountTimeout("unknown act '" + name + "'");
                    break;
            }
        }

        // ---- Spec 027b: frames around a click, slow motion ----

        private float _tweenScale = 1f;

        private string SlowSuffix => _tweenScale > 1f ? "-x" + _tweenScale.ToString("0") : string.Empty;

        /// <summary>For TourActor.Use: a shot of the aimed state, then frames every 0.1 s that start before the click.</summary>
        private Func<IEnumerator> BeforeClick(string shot, string frames) =>
            new CookCapture(s => _capture.Shot(s), StartFrames, SetTweenScale).Before(shot, frames, TourRules.ClickFramesSeconds);

        /// <summary>Starts a frame sequence alongside the following steps (the tour waits for it before it ends).</summary>
        private IEnumerator StartFrames(string name, float seconds)
        {
            int count = Mathf.Max(1, Mathf.RoundToInt(seconds / TourRules.FrameInterval));
            StartCoroutine(TrackFrames(_capture.Frames(name, count, TourRules.FrameInterval)));
            yield break;
        }

        /// <summary>Every tween <paramref name="scale"/> times longer (FeelSettings), so the arc shows at 0.1 s frames.</summary>
        private void SetTweenScale(float scale)
        {
            _tweenScale = scale;
            FeelSettings feel = FeelSettings.PresetA();
            feel.PickupSeconds *= scale;
            feel.PlaceSeconds *= scale;
            feel.DropSeconds *= scale;
            feel.ConfirmTimeout = Mathf.Max(feel.ConfirmTimeout, feel.PickupSeconds);
            feel.Preset = scale > 1f ? "slow-motion x" + scale.ToString("0") : FeelSettings.PresetAName;
            FeelSettings.Use(feel);
            Debug.Log("[Tour] tweens " + scale.ToString("0") + "x (" + feel.Preset + ")");
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
            // Spec 029: the fryer close-up (the basket arcs and bubbles), between the other captures.
            StartCoroutine(WatchFryer(() => capturing));
            StartCoroutine(WatchGriddle(() => capturing)); // spec 030: the griddle close-up and the remote flip
            StartCoroutine(WatchAssembly(() => capturing)); // spec 031: the assembly board close-up and the remote stack
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
                WatchAnimation(other);

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
                        if (armed && speed > 0.4f && !_fryerCapturing)
                        {
                            TourStep step = sequences[segment];
                            Debug.Log("[Tour] observer: the tour player moves, frames '" + step.Name + "'");
                            capturing = true;
                            StartCoroutine(Capture(step, () => { capturing = false; armed = false; stillFor = 0f; segment++; }));
                        }
                    }
                }
                if (!capturing && !_fryerCapturing && shots < MaxObserverShots && Time.unscaledTime >= nextShot)
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

        // Spec 027b: what the observer's copy of the tour player animates, against the old frame-to-frame speed, so the
        // log shows whether a standing (or only turning) remote body still strides.
        private CharacterVisual _watched;
        private Vector3 _watchedLast;
        private float _legacySpeed;
        private float _nextWatchLog;
        private int _strideWhileStill;
        private int _stillFrames;
        private Vector3 _anchor;
        private float _anchorTime;

        private void WatchAnimation(PlayerController other)
        {
            if (_watched == null || _watched.gameObject != other.gameObject)
            {
                _watched = other.GetComponent<CharacterVisual>();
                if (_watched == null) return;
                _watchedLast = _watched.TrackedPosition;
            }
            float dt = Time.deltaTime;
            Vector3 p = _watched.TrackedPosition;
            Vector3 d = p - _watchedLast;
            d.y = 0f;
            _watchedLast = p;
            if (dt > 0f && d.magnitude / dt < 20f) _legacySpeed = CharacterAnimRules.SmoothSpeed(_legacySpeed, d.magnitude / dt, dt);
            // Standing still: the networked root has not moved 5 mm for 0.3 s (turning in place counts as standing).
            Vector3 root = other.transform.position;
            if ((root - _anchor).sqrMagnitude > 0.005f * 0.005f)
            {
                _anchor = root;
                _anchorTime = Time.unscaledTime;
            }
            bool still = Time.unscaledTime - _anchorTime > 0.3f;
            if (still)
            {
                _stillFrames++;
                if (_watched.State != CharacterAnimState.Idle && _watched.State != CharacterAnimState.Crouch) _strideWhileStill++;
            }
            if (Time.unscaledTime < _nextWatchLog) return;
            _nextWatchLog = Time.unscaledTime + 0.5f;
            Debug.Log("[Tour] observer anim: state " + _watched.State + ", speed " + _watched.Speed.ToString("0.00") + " m/s (frame-to-frame "
                      + _legacySpeed.ToString("0.00") + "), yaw " + _watched.Yaw.ToString("0") + ", stride while still " + _strideWhileStill
                      + " of " + _stillFrames + " frames");
        }

        private bool _fryerCapturing;

        /// <summary>
        /// Spec 029, observer of a tour with a fryer: when basket 0 first goes into the oil the fixed camera moves to a close-up of
        /// the fryer from the aisle's far side (pose from the scene: the fryer's position), takes 1.2 s of frames of every lower
        /// and raise (the remote arc), keeps the close-up for the regular shots while the basket holds food (bubbles, ring), and
        /// goes back to the tour's camera once it is empty and up again.
        /// </summary>
        private IEnumerator WatchFryer(Func<bool> busy)
        {
            Worktown.Game.Cooking.FryerBasket first = null;
            foreach (Worktown.Game.Cooking.FryerBasket b in FindObjectsByType<Worktown.Game.Cooking.FryerBasket>(FindObjectsSortMode.None))
                if (b.Basket == 0 && b.Fryer != null) first = b;
            if (first == null || !_tour.HasObserverCamera) yield break;
            var basket = first.GetComponent<Worktown.Game.Cooking.Vessel>();
            Vector3 fryer = first.Fryer.transform.position;
            Worktown.Game.Cooking.BasketState last = Worktown.Game.Cooking.FryerRules.StateOf(basket.Mode.Value);
            bool close = false;
            while (!_finished && basket != null)
            {
                Worktown.Game.Cooking.BasketState now = Worktown.Game.Cooking.FryerRules.StateOf(basket.Mode.Value);
                bool moved = now != last && (now == Worktown.Game.Cooking.BasketState.Lowered || now == Worktown.Game.Cooking.BasketState.Draining);
                last = now;
                if (moved)
                {
                    if (!close)
                    {
                        close = true;
                        Camera cam = Camera.main;
                        // From the prep side, above the worktop: the cook at the fryer stands outside the view.
                        Vector3 eye = new Vector3(fryer.x - 0.9f, 1.75f, fryer.z - 0.95f);
                        Vector3 look = new Vector3(fryer.x, 0.82f, fryer.z - 0.15f);
                        if (cam != null) cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
                        Debug.Log("[Tour] observer: fryer close-up");
                    }
                    while (busy()) yield return null;
                    _fryerCapturing = true;
                    string name = now == Worktown.Game.Cooking.BasketState.Lowered ? "fryer-lower" : "fryer-raise";
                    Debug.Log("[Tour] observer: frames '" + name + "'");
                    yield return _capture.Frames(name, 12, TourRules.FrameInterval);
                    _fryerCapturing = false;
                }
                if (close && now == Worktown.Game.Cooking.BasketState.Raised && basket.ContentCount == 0)
                {
                    close = false;
                    FixCamera();
                    Debug.Log("[Tour] observer: back to the tour camera");
                }
                yield return null;
            }
        }

        /// <summary>
        /// Spec 030, observer of a tour with a griddle: when the griddle is switched on with food on spot 0 the fixed camera moves to
        /// a close-up of the griddle from the aisle's far side; every flip of spot 0 (its synced flip counter) is filmed in
        /// frames 0.04 s apart (the remote 0.25 s arc and turn), the regular shots show the rings and the grill marks, and the
        /// camera goes back once spot 0 is empty again.
        /// </summary>
        private IEnumerator WatchGriddle(Func<bool> busy)
        {
            Worktown.Game.Cooking.GriddleSpot first = null;
            foreach (Worktown.Game.Cooking.GriddleSpot s in FindObjectsByType<Worktown.Game.Cooking.GriddleSpot>(FindObjectsSortMode.None))
                if (s.Spot == 0 && s.Griddle != null) first = s;
            if (first == null || !_tour.HasObserverCamera) yield break;
            var spot = first.GetComponent<Worktown.Game.Cooking.Vessel>();
            var contents = first.Griddle.GetComponent<Worktown.Game.Cooking.StationContents>();
            Vector3 griddle = first.Griddle.transform.position;
            int lastFlips = Worktown.Game.Cooking.GriddleRules.FlipsOf(spot.Mode.Value);
            bool close = false;
            while (!_finished && spot != null)
            {
                bool on = contents != null && Worktown.Game.Cooking.GriddleRules.IsOn(contents.StoveFlags.Value);
                int flips = Worktown.Game.Cooking.GriddleRules.FlipsOf(spot.Mode.Value);
                if (!close && on && spot.ContentCount > 0)
                {
                    close = true;
                    while (busy() || _fryerCapturing) yield return null;
                    Camera cam = Camera.main;
                    // From the fryer's side, above the plate: the cook at the griddle stands outside the view.
                    Vector3 eye = new Vector3(griddle.x + 0.85f, 1.7f, griddle.z - 1.05f);
                    Vector3 look = new Vector3(griddle.x - 0.05f, 0.9f, griddle.z - 0.3f);
                    if (cam != null) cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
                    Debug.Log("[Tour] observer: griddle close-up");
                    lastFlips = flips;
                }
                if (close && flips != lastFlips)
                {
                    lastFlips = flips;
                    while (busy()) yield return null;
                    _fryerCapturing = true; // the regular shots wait, as for the fryer
                    Debug.Log("[Tour] observer: frames 'griddle-flip'");
                    yield return _capture.Frames("griddle-flip", 10, 0.04f);
                    _fryerCapturing = false;
                }
                if (close && spot.ContentCount == 0)
                {
                    close = false;
                    FixCamera();
                    Debug.Log("[Tour] observer: back to the tour camera");
                }
                yield return null;
            }
        }

        /// <summary>
        /// Spec 031, observer of a tour with an assembly board: when the build spot gets its first layer the fixed camera moves to a
        /// close-up of the board from the aisle's far side; every new layer, the squeeze (the board's byte), the fold, the paper are
        /// filmed in frames 0.04 s apart (the remote drop, ribbon and fold), and the camera goes back once the board is empty.
        /// </summary>
        private IEnumerator WatchAssembly(Func<bool> busy)
        {
            Worktown.Game.Cooking.AssemblySpot first = FindFirstObjectByType<Worktown.Game.Cooking.AssemblySpot>();
            if (first == null || first.Board == null || !_tour.HasObserverCamera) yield break;
            var spot = first.GetComponent<Worktown.Game.Cooking.Vessel>();
            var contents = first.Board.GetComponent<Worktown.Game.Cooking.StationContents>();
            Vector3 board = first.transform.position;
            int lastLayers = spot.ContentCount;
            byte lastMode = spot.Mode.Value, lastFlags = contents != null ? contents.StoveFlags.Value : (byte)0;
            bool close = false;
            while (!_finished && spot != null)
            {
                int layers = spot.ContentCount;
                byte mode = spot.Mode.Value, flags = contents != null ? contents.StoveFlags.Value : (byte)0;
                if (!close && layers > 0)
                {
                    close = true;
                    while (busy() || _fryerCapturing) yield return null;
                    AssemblyCloseUp(board);
                    Debug.Log("[Tour] observer: assembly close-up");
                    lastLayers = layers;
                    lastMode = mode;
                    lastFlags = flags;
                    _fryerCapturing = true;
                    yield return _capture.Frames("assembly-first-layer", 8, 0.04f);
                    _fryerCapturing = false;
                }
                if (close && (layers != lastLayers || mode != lastMode || flags != lastFlags))
                {
                    string name = flags != lastFlags ? (flags != 0 ? "assembly-squeeze" : "assembly-squeeze-end")
                        : mode != lastMode ? "assembly-close" : "assembly-layer-" + layers;
                    lastLayers = layers;
                    lastMode = mode;
                    lastFlags = flags;
                    while (busy()) yield return null;
                    AssemblyCloseUp(board); // the fryer's watch may have put the camera back meanwhile
                    _fryerCapturing = true;
                    Debug.Log("[Tour] observer: frames '" + name + "'");
                    yield return _capture.Frames(name, 10, 0.04f);
                    _fryerCapturing = false;
                }
                if (close && layers == 0)
                {
                    close = false;
                    FixCamera();
                    Debug.Log("[Tour] observer: back to the tour camera");
                }
                yield return null;
            }
        }

        /// <summary>Spec 031: the observer's close-up of the assembly board, from the plate shelf's side above the board.</summary>
        private static void AssemblyCloseUp(Vector3 board)
        {
            Camera cam = Camera.main;
            Vector3 eye = new Vector3(board.x + 0.75f, 1.65f, board.z - 0.95f);
            Vector3 look = new Vector3(board.x - 0.05f, board.y + 0.04f, board.z + 0.02f);
            if (cam != null) cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
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
