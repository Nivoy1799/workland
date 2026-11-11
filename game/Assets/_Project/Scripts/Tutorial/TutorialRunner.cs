using System;
using FishNet.Managing;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Worktown.Game.Cooking;
using Worktown.Game.Net;
using Worktown.Game.Npc;
using Worktown.Game.Player;
using Worktown.Game.Shop;
using Worktown.Game.UI;

namespace Worktown.Game.Tutorial
{
    /// <summary>
    /// Spec 038, owner client only: runs the first-shift tutorial. It adds itself to the HUD's object at start (no scene entry), waits for
    /// the local player, starts by itself until the tutorial was done once (PlayerPrefs <see cref="TutorialRules.DoneKey"/>), and every
    /// 0.2 s builds a <see cref="TutorialSnapshot"/> from what this client sees (the hands, the board, the basket, the build, the register,
    /// the sink, the spit) for the pure <see cref="TutorialProgress"/>. The card (<see cref="TutorialPanelView"/>) shows the step, the target
    /// pulses in Mustard (<see cref="TutorialHighlight"/>) and an edge arrow points to it when it is off the screen. It sends nothing:
    /// guidance only (AGENTS rule 6). On a host it tells the server half of the same process that the tutorial runs, so the guests it
    /// spawns are calm (<see cref="CustomerService.SetLocalTutorial"/>); a remote client's tutorial changes nothing on its server. 038b: it
    /// tells the host again whenever it finds a (new) player, and the host reads <see cref="Decided"/> and <see cref="Running"/> when its
    /// server starts, so the flag never goes stale; a scripted dev run (<c>-tour</c>, <c>-observe</c>) never stores the done flag.
    /// F4 skips the tutorial (or closes the done card), F5 skips an optional step; the buttons do the same with a free mouse (Esc).
    /// </summary>
    public sealed class TutorialRunner : MonoBehaviour
    {
        public const float SnapshotSeconds = 0.2f;
        public const float TargetSeconds = 0.25f;

        private readonly TutorialProgress _progress = new TutorialProgress(TutorialSteps.Default);
        private readonly TutorialWatch _watch = new TutorialWatch();
        private readonly TutorialTargets _targets = new TutorialTargets();
        private TutorialHighlight _highlight;
        private TutorialPanelView _panel;
        private NetworkManager _networkManager;
        private PickupInteractor _player;
        private PlayerInputReader _input;
        private Camera _camera;
        private bool _decided;
        private bool _disabled;
        private float _nextPlayerLookup;
        private float _nextInputLookup;
        private string _shownText;
        private float _nextSnapshot;
        private float _nextTarget;
        private float _doneAt = -1f;
        private Vector3 _lastPosition;
        private float _lastYaw;
        private float _moved;
        private float _turned;
        private int _basket;
        private TutorialEvents _events;
        private TutorialTarget _target;
        private int _preview = -1; // dev builds: a step's card shown for the tour (DevPreview); -1 otherwise

        /// <summary>The step index changed (index, or -1 when the tutorial ended). The tutorial tour shoots each card on it.</summary>
        public event Action<int> StepChanged;

        public bool Running => _progress.Running;
        /// <summary>038b: the runner chose on its own (the launch rule once it found its player) or by a replay; until then the host
        /// takes the launch rule (<see cref="StartsOnLaunchNow"/>).</summary>
        public bool Decided => _decided;
        public int StepIndex => _progress.Running ? _progress.Current : -1;
        public int StepCount => _progress.Count;
        public TutorialStepId StepId => _progress.Running ? _progress.CurrentStep.Id : TutorialStepId.Done;
        public TutorialTarget CurrentTarget => _target;
        public Component TargetObject => _highlight != null ? _highlight.Target : null;
        public bool ArrowShown => _panel != null && _panel.ArrowShown;
        public string CardText => _panel != null ? _panel.Text : string.Empty;

        /// <summary>The runner of this client, or null (a server build, batch mode).</summary>
        public static TutorialRunner Find() => FindFirstObjectByType<TutorialRunner>();

        /// <summary>The launch rule now (<see cref="TutorialRules.StartsOnLaunch"/> of this process's arguments and the done flag).</summary>
        public static bool StartsOnLaunchNow()
        {
            bool done = false;
            try { done = PlayerPrefs.GetInt(TutorialRules.DoneKey, 0) == 1; }
            catch (PlayerPrefsException) { }
            return TutorialRules.StartsOnLaunch(Environment.GetCommandLineArgs(), done, Debug.isDebugBuild);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
#if UNITY_SERVER
            return;
#else
            if (Application.isBatchMode) return;
            foreach (HudView hud in FindObjectsByType<HudView>(FindObjectsSortMode.None))
                if (hud.GetComponent<TutorialRunner>() == null && hud.GetComponent<UIDocument>() != null) hud.gameObject.AddComponent<TutorialRunner>();
#endif
        }

        private void Start()
        {
            // The scripted observer (spec 027, dev builds) films the tour player; it runs no tutorial of its own (its host still has the
            // calm guests through -tutorial, CustomerService reads that itself).
            string[] args = Environment.GetCommandLineArgs();
            if (Debug.isDebugBuild && Array.IndexOf(args, "-observe") >= 0) _disabled = true;
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            _panel = new TutorialPanelView(root, () => SkipStep(), () => SkipOrClose());
        }

        private void OnDestroy()
        {
            _highlight?.Destroy();
            _panel?.Remove();
        }

        // ---- the menu's buttons (What's new, F1) ----

        /// <summary>Starts the tutorial again from the first step (the What's-new and F1 buttons).</summary>
        public void Replay()
        {
            _decided = true;
            Begin();
        }

        /// <summary>"Skip tutorial" (F4): it ends now and does not start by itself again.</summary>
        public void Skip()
        {
            if (!_progress.Running) return;
            _progress.SkipAll();
            End("skipped at step " + (_progress.Current + 1));
        }

        /// <summary>"Skip step" (F5): an optional step is passed over.</summary>
        public void SkipStep()
        {
            if (_progress.SkipStep()) OnStepChanged();
        }

        private void SkipOrClose()
        {
            if (_progress.AtEnd) Finish();
            else Skip();
        }

        private void Begin()
        {
            _progress.Start();
            _watch.Reset();
            _moved = 0f;
            _turned = 0f;
            _events = TutorialEvents.None;
            _doneAt = -1f;
            if (_player != null) _lastPosition = _player.transform.position;
            _lastYaw = CameraYaw();
            _nextSnapshot = 0f;
            TellHost(true);
            Debug.Log("[Tutorial] started (" + _progress.Count + " steps)");
            OnStepChanged();
        }

        private void Finish()
        {
            _progress.Finish();
            End("finished");
        }

        private void End(string how)
        {
            SaveDone();
            TellHost(false);
            _target = TutorialTarget.None;
            _highlight?.Show(null);
            _panel.SetCard(false);
            _panel.SetArrow(false, default, 0f);
            Debug.Log("[Tutorial] " + how);
            StepChanged?.Invoke(-1);
        }

        private static void SaveDone()
        {
            // 038b: a scripted dev run (-tour, -observe: the visual check) never marks the tutorial done on this machine.
            if (!TutorialRules.SavesDone(Environment.GetCommandLineArgs()))
            {
                Debug.Log("[Tutorial] scripted run: the done flag is not stored");
                return;
            }
            try
            {
                PlayerPrefs.SetInt(TutorialRules.DoneKey, 1);
                PlayerPrefs.Save();
            }
            catch (PlayerPrefsException e)
            {
                Debug.LogWarning("[Tutorial] could not store the done flag: " + e.Message);
            }
        }

        /// <summary>On a host, the server half of this process gets the tutorial's state (no RPC: a remote server never hears of it).</summary>
        private void TellHost(bool running)
        {
            if (_networkManager == null || !_networkManager.IsServerStarted) return;
            CustomerService customers = _networkManager.GetComponent<CustomerService>();
            if (customers == null) customers = FindFirstObjectByType<CustomerService>();
            if (customers != null) customers.SetLocalTutorial(running);
        }

        // ---- every frame ----

        private void Update()
        {
            if (_disabled || _panel == null) return;
            float now = Time.unscaledTime;
            if (!FindPlayer(now)) return;
            if (!_decided)
            {
                _decided = true;
                if (StartsOnLaunchNow()) Begin();
                else TellHost(false);
            }
            if (!_progress.Running) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.f4Key.wasPressedThisFrame)
                {
                    SkipOrClose();
                    return;
                }
                if (keyboard.f5Key.wasPressedThisFrame) SkipStep();
            }

            Track();
            _watch.SawAction(_player.CurrentAction, _player.CurrentAim.Option, now);
            if (now >= _nextSnapshot)
            {
                _nextSnapshot = now + SnapshotSeconds;
                Observe(now);
                if (!_progress.Running) return;
            }
            if (_preview < 0 && _progress.AtEnd && _doneAt >= 0f && now - _doneAt >= TutorialRules.DoneCardSeconds)
            {
                Finish();
                return;
            }
            if (_input == null && now >= _nextInputLookup)
            {
                _nextInputLookup = now + 0.5f; // 038b: at most twice a second while there is none
                _input = FindFirstObjectByType<PlayerInputReader>();
            }
            _panel.SetCard(TutorialRules.ShowsCard(true, _input != null && _input.IsUiOpen));
            UpdateGuidance(now);
        }

        private bool FindPlayer(float now)
        {
            if (_player != null && _player.IsSpawned) return true;
            _player = null;
            if (now < _nextPlayerLookup) return false;
            _nextPlayerLookup = now + 0.5f;
            if (_networkManager == null) _networkManager = FindFirstObjectByType<NetworkManager>();
            _player = LocalPlayer.Find(_networkManager);
            if (_player == null) return false;
            _lastPosition = _player.transform.position;
            _lastYaw = CameraYaw();
            if (_highlight == null && _player.TryGetComponent(out InteractionHighlight interact))
                _highlight = new TutorialHighlight(interact.MarkerMaterial);
            // 038b: a new player is a new session (hosting after a remote game, a server restart): the host hears the state again.
            if (_decided) TellHost(_progress.Running);
            return true;
        }

        private float CameraYaw()
        {
            if (_camera == null) _camera = Camera.main;
            return _camera != null ? _camera.transform.eulerAngles.y : 0f;
        }

        /// <summary>Metres walked and degrees turned (a jump of more than 2 m in a frame is a respawn, not a walk).</summary>
        private void Track()
        {
            Vector3 p = _player.transform.position;
            Vector3 d = p - _lastPosition;
            d.y = 0f;
            float step = d.magnitude;
            if (step < 2f) _moved += step;
            _lastPosition = p;
            float yaw = CameraYaw();
            _turned += Mathf.Abs(Mathf.DeltaAngle(_lastYaw, yaw));
            _lastYaw = yaw;
        }

        private void Observe(float now)
        {
            _targets.Refresh(now);
            TutorialSnapshot s = Snapshot(now);
            if (_progress.Observe(in s)) OnStepChanged();
            _events = TutorialEvents.None;
            ShowTextFor(in s);
            if (now >= _nextTarget || _target == TutorialTarget.None)
            {
                _nextTarget = now + TargetSeconds;
                TutorialStep step = _preview >= 0 ? _progress.Steps[_preview] : _progress.CurrentStep;
                _target = step != null ? step.TargetFor(in s) : TutorialTarget.None;
                _highlight?.Show(_targets.Resolve(_target, _basket, _player.transform.position));
            }
        }

        /// <summary>038b: the step's instruction for what the player has now (step 4 with the knife in the hands); set only on a change.</summary>
        private void ShowTextFor(in TutorialSnapshot s)
        {
            if (_preview >= 0) return;
            TutorialStep step = _progress.CurrentStep;
            if (step == null) return;
            string text = step.TextFor(in s);
            if (ReferenceEquals(text, _shownText)) return;
            _shownText = text;
            _panel.SetText(Keys(text));
        }

        /// <summary>Spec 048 (review): the card names the bound keys ({Move} and other KeyPrompts tokens).</summary>
        private static string Keys(string text) => Settings.KeyPrompts.Fill(text, Settings.GameSettings.CurrentBindings);

        private TutorialSnapshot Snapshot(float now)
        {
            var s = new TutorialSnapshot
            {
                Moved = _moved,
                Turned = _turned,
                Held = _player.HandsNow,
                BoardTitle = _targets.Board != null ? _targets.Board.Title.Value : null,
            };
            // The basket in use: the first with something in it (else basket 0).
            _basket = 0;
            for (int i = 0; i < FryerRules.BasketsPerFryer; i++)
            {
                Vessel b = _targets.Basket(i);
                if (b != null && b.ContentCount > 0)
                {
                    _basket = i;
                    break;
                }
            }
            Vessel basket = _targets.Basket(_basket);
            if (basket != null)
            {
                s.BasketTitle = basket.Title.Value;
                s.BasketMode = basket.Mode.Value;
                s.BasketZone = basket.Zone;
            }
            s.FryerOn = FryerRules.IsOn(ByteOf(_targets.Fryer));
            if (_targets.Assembly != null)
            {
                s.BuildLayers = _targets.Assembly.Names;
                s.BuildCount = _targets.Assembly.ContentCount;
                s.BuildMode = _targets.Assembly.Mode.Value;
            }
            if (_targets.Register != null && _targets.Register.TryGetComponent(out Register register)) s.Payment = register.Payment.Value;
            s.SinkBasin = CleaningRules.BasinOf(ByteOf(_targets.Sink));
            s.DirtyWaiting = ByteOf(_targets.DishReturn);
            s.SpitFlags = ByteOf(_targets.Spit);
            s.LemonadeSpot = _targets.LemonadeSpotByte(); // spec 042
            int tray = SpitTray();
            _events |= _watch.Observe(_player.ResultVersion, _player.LastCookOutcome == CookOutcome.Ok, s.SinkBasin, tray, now);
            s.Events = _events;
            return s;
        }

        private Vessel _tray;

        private int SpitTray()
        {
            if (_tray == null && _targets.Spit != null)
                foreach (Vessel v in _targets.Spit.GetComponentsInChildren<Vessel>())
                    if (v.Kind == VesselKind.SpitTray) _tray = v;
            return _tray != null ? _tray.ContentCount : 0;
        }

        private static byte ByteOf(Station s) => s != null && s.TryGetComponent(out StationContents c) ? c.StoveFlags.Value : (byte)0;

        private void OnStepChanged()
        {
            TutorialStep step = _progress.CurrentStep;
            if (step == null) return;
            bool done = _progress.AtEnd;
            _panel.ShowStep(_progress.Current, _progress.Count, Keys(step.Text), step.Optional, done);
            _shownText = step.Text;
            if (done && _doneAt < 0f) _doneAt = Time.unscaledTime;
            _nextTarget = 0f;
            Debug.Log("[Tutorial] step " + TutorialRules.ProgressText(_progress.Current, _progress.Count) + " " + step.Id
                + " (hands '" + (_player != null ? _player.HandsNow.Name : "") + "')");
            StepChanged?.Invoke(_progress.Current);
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        /// <summary>
        /// Dev builds (the tutorial tour): shows step <paramref name="index"/>'s card and target without changing the progress, so the
        /// visual check can shoot the cards of steps the tour completed ahead of time (they pass at once). <see cref="DevEndPreview"/> ends it.
        /// </summary>
        public void DevPreview(int index)
        {
            if (!_progress.Running || index < 0 || index >= _progress.Count) return;
            _preview = index;
            TutorialStep step = _progress.Steps[index];
            _panel.ShowStep(index, _progress.Count, Keys(step.Text), step.Optional, index == _progress.Count - 1);
            _target = TutorialTarget.None;
            _nextTarget = 0f;
            _nextSnapshot = 0f;
        }

        public void DevEndPreview()
        {
            if (_preview < 0) return;
            _preview = -1;
            TutorialStep step = _progress.CurrentStep;
            if (step != null) _panel.ShowStep(_progress.Current, _progress.Count, Keys(step.Text), step.Optional, _progress.AtEnd);
            _shownText = step != null ? step.Text : null;
            if (_progress.AtEnd) _doneAt = Time.unscaledTime; // the done card's time starts again
            _target = TutorialTarget.None;
            _nextTarget = 0f;
            _nextSnapshot = 0f;
        }
#endif

        private void UpdateGuidance(float now)
        {
            if (_highlight == null) return;
            _highlight.Update(TutorialRules.Pulse01(now));
            if (_camera == null) _camera = Camera.main;
            bool arrow = false;
            Vector2 point = default;
            float angle = 0f;
            if (_camera != null && _panel.CardShown && _highlight.TryGetCentre(out Vector3 centre))
            {
                Vector3 viewport = _camera.WorldToViewportPoint(centre);
                if (!TutorialRules.IsOnScreen(viewport))
                {
                    arrow = true;
                    point = TutorialRules.EdgePoint(viewport, TutorialRules.ArrowMargin, out angle);
                }
            }
            _panel.SetArrow(arrow, point, angle);
        }
    }
}
