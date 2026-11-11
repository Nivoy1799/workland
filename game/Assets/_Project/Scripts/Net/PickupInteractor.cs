using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;
using Worktown.Game.Audio;
using Worktown.Game.Cooking;
using Worktown.Game.Net.Rules;
using Worktown.Game.Player;
using Worktown.Game.Settings;

namespace Worktown.Game.Net
{
    /// <summary>
    /// On the player. The owner picks up, drops, places and uses things; the client only asks, the server decides
    /// (PickupService, KitchenService) and answers with the outcome. The client deliberately searches further than the
    /// allowed reach in third person, so requests from 2.5 to 4 m reach the server and get refused there (spec 003 AC4).
    /// Spec 018: in first person the target is what the camera centre looks at (AimRules), within the same reach the
    /// server checks. Spec 020: first person uses the mouse; one pure rule (InteractionRules) turns what you hold and
    /// what the dot points at into the click's action, and the prompt and the highlight show the same action. Spec 041: one meaning
    /// per input (ControlRules): left click does, hold left holds, the right click puts down (Q its hidden alias), the wheel turns.
    /// Spec 021: the board, pan, pot, plates and bowl are cookware you carry. Using what you hold on a piece of
    /// cookware (put food in, pour, take out, a knife stroke or turn) is the one new request, RequestUse(target,
    /// action): an object and an int, never a value. Crates, the counter, the plate shelf and the stove knobs keep
    /// RequestStation(station, option). Third person keeps E for pickups, crates, the counter and the shelf.
    /// Spec 027b (docs/feel.md 1–4, client look only; the server checks every request exactly as before): the aim takes
    /// the best interactable within a small cone around the dot (FeelRules.PickTarget: an exact hit on an object's body
    /// always wins, cookware that takes what you hold gets a bonus, the current target is sticky, a target shows at least
    /// FeelSettings.HighlightMinSeconds). A click is never swallowed: inside the request interval it is queued (one at
    /// most) and sent when allowed (ClickBuffer). A click plays its sound and starts the expected look at once (the item
    /// flies to the hand, onto the spot or into the pot: Pickup.Predict*); the synced state (holder, position, the pot's
    /// contents) confirms it (FeelPrediction), and without a confirming change within FeelSettings.ConfirmTimeout of the
    /// last relevant change the look eases back and "nope" plays. No new RPCs or values.
    /// Spec 028b: the slots of the fridge and the dry shelf (<see cref="StockSlot"/>) are interactables like the stove
    /// buttons: a ray hit on the station's body means its slot nearest to the hit point, the cone aims at their anchors
    /// (reach is measured to the anchor, as the server does), and a click sends RequestStation(station, slot) with the pick
    /// sound and a copy of the item flying into the hand (<see cref="Worktown.Game.Art.StockTakeLook"/>) until the server's
    /// food arrives there; while that is pending the hands count as full.
    /// </summary>
    public sealed class PickupInteractor : NetworkBehaviour
    {
        private const float ClientSearchRadius = 4f;
        /// <summary>Spec 027b: m (horizontal) from an item's centre within which a ray hit counts as on its body (a pan's
        /// handle or a wide collider edge does not; then the cone decides).</summary>
        public const float DirectHitRadius = 0.11f;
        /// <summary>Spec 027b review: s a spot's occupancy is reused before the physics query runs again.</summary>
        private const float SpotCacheSeconds = 0.1f;

        // Server only: this player's request rate limit (review of spec 027b: a token bucket).
        private readonly RequestBucket _serverBucket = new RequestBucket();
        private readonly SpamGuard _spam = new SpamGuard(); // spec 045: kick after many TooFast replies within 10 s

        [SerializeField] private Transform _holdPoint;

        private PickupService _service;
        private KitchenService _kitchen;
        private Worktown.Game.Player.PlayerInputReader _inputReader;
        private Station[] _stations;
        private readonly Collider[] _hits = new Collider[16];
        // Spec 018: first-person aim buffers (owner only).
        private PlayerCameraRig _rig;
        private readonly RaycastHit[] _rayHits = new RaycastHit[16];
        private readonly List<AimHit> _aimHits = new List<AimHit>(16);
        private readonly List<Component> _aimSources = new List<Component>(16);
        private readonly List<Vector3> _aimPoints = new List<Vector3>(16);
        // 041 review: what stops the right click's ray put-down on this frame's ray (a wall: no station).
        private readonly List<float> _rayBlockDistances = new List<float>(8);
        private readonly List<Station> _rayBlockStations = new List<Station>(8);
        // Spec 019: the knife gesture (owner only).
        private readonly CutGesture _gesture = new CutGesture();
        private HeldItemView _heldView;
        private bool _suppressingLook;
        private float _lastSendTime = float.NegativeInfinity;
        // Spec 020: the click, the free spots of the aimed surface and the crate prices (owner only).
        private readonly ClickGesture _click = new ClickGesture();
        private bool _lockedLastFrame;
        private bool[] _spotTaken = new bool[32];
        private Worktown.Sim.Economy.EconomyCatalog _prices;
        // Spec 027b: cone aim, stickiness, the shown target, the click buffer, sounds and the predicted look (owner only).
        // Spec 019 / 027b review: the client keeps its requests one tick plus a margin further apart than the server's
        // limit (PickupRules.ClientRequestInterval), for clicks, keys and knife strokes alike. Set with the tick on start.
        private float _clientInterval = PickupRules.ClientRequestInterval(1f / 30f);
        private ClickBuffer _clicks = new ClickBuffer(PickupRules.ClientRequestInterval(1f / 30f));
        private QueuedKind _queuedKind;
        private InteractionAction _queuedAction;
        private int _queuedTarget;
        private int _queuedSpot = PlaceSpots.None;
        private bool _queuedFirstPerson;
        private StationButton[] _allButtons;
        private PlaceSurface[] _allSurfaces;
        private StockSlot[] _allSlots;
        // Spec 029: the fryer baskets (scene objects; fixed vessels without a Pickup) and their looks.
        private FryerBasket[] _allBaskets;
        private Vessel[] _basketVessels = new Vessel[0];
        // Spec 030: the griddle spots (fixed vessels without a Pickup, like the baskets).
        private GriddleSpot[] _allGriddleSpots;
        private Vessel[] _griddleVessels = new Vessel[0];
        // Spec 031: the assembly boards' build spots (fixed vessels) and the held sauce squeeze (press and release, owner only).
        private AssemblySpot[] _allAssemblySpots;
        private float _nextAssemblyLookup;
        private float _nextBasketLookup; // 031b
        private float _nextGriddleLookup;
        private Vessel[] _assemblyVessels = new Vessel[0];
        private readonly SqueezeHold _squeeze = new SqueezeHold();
        private Worktown.Game.Art.IPourView _squeezeView; // spec 033: a sauce dispenser or a drink tap
        // Spec 033: our own pour at a drink tap: its view, the cup in the hands and its predicted ml (owner only), the lid click's answer.
        private Worktown.Game.Art.DrinkTapView _pourTap;
        private Food _pourCup;
        private int _lidRequest;
        private Food _lidCup;
        // The cup of our last pour and the request number of its stop: a refused start or stop takes the predicted level back out.
        private Food _lastPourCup;
        private int _pourStopRequest;
        // Spec 028b: the copy of a taken ingredient flying into the hand until the server's food is there (owner only).
        private Worktown.Game.Art.StockTakeLook _pendingTake;
        private string _pendingTakeName;
        // Spec 029b: an item whose predicted pick-up has not been confirmed yet (it counts as held for the rules).
        private Pickup _pendingPickUp;
        // Spec 028b: how far a food's model stands below its pivot (the food prefab's collider half height, 0.2 m); learnt from
        // the first food held.
        private float _foodBottom = 0.2f;
        private readonly Dictionary<long, SpotState> _spotCache = new Dictionary<long, SpotState>();
        private readonly List<AimCandidate> _cone = new List<AimCandidate>(32);
        private readonly List<Component> _coneSources = new List<Component>(32);
        private readonly List<int> _coneSpots = new List<int>(32);
        private readonly List<Vector3> _coneAnchors = new List<Vector3>(32);
        private readonly RaycastHit[] _sightHits = new RaycastHit[16];
        private Component _sticky;
        private int _stickySpot = PlaceSpots.None;
        private Component _shown;
        private int _shownSpot = PlaceSpots.None;
        private Vector3 _shownSpotPosition;
        private bool _shownTooFar;
        private float _shownLastSeen;
        private KitchenSounds _sounds;
        private bool _soundsLooked;
        private int _lastRequest;
        // Spec 027b: the predicted looks waiting for the server, and the answers matched to their requests.
        private readonly PredictionBook<PendingLook> _looks = new PredictionBook<PendingLook>();
        private readonly List<PredictionBook<PendingLook>.Resolved> _resolved = new List<PredictionBook<PendingLook>.Resolved>();
        private readonly List<PendingLook> _ending = new List<PendingLook>();
        private System.Func<PendingLook, ItemSnapshot> _snapshotOf;

        // Spec 041: the right click (or Q) puts down (on the aimed free spot, else at the feet); the right-click flip is gone.
        private enum QueuedKind : byte { None, Click, Drop, Key, PutDown }

        // Spec 041: the wheel's turn of the held item's rest rotation (PlaceTurn steps; back to 0 when the hands change), the wheel
        // itself, and where a right click puts the held item down (first person; null: it drops at the feet).
        private WheelSteps _wheel; // spec 048: made on first use, with the Input System's notch size (PlayerInputReader.WheelCountsPerNotch)
        private WheelSteps Wheel => _wheel ??= new WheelSteps(Worktown.Game.Player.PlayerInputReader.WheelCountsPerNotch());
        private int _restTurn;
        private Pickup _restTurnItem;
        private PlaceSurface _rayPutDownSurface;
        private int _rayPutDownSpot = PlaceSpots.None;
        private Vector3 _rayPutDownPosition;
        private bool _coneBuilt;

        private struct SpotState
        {
            public bool Taken;
            public float Until;
        }

        /// <summary>One predicted look: the item, the cookware it goes into, when it was clicked.</summary>
        private sealed class PendingLook
        {
            public Pickup Item;
            public Vessel Vessel;
            /// <summary>Spec 028b: a take from a stock slot (no item yet; the snapshot is the hands).</summary>
            public Worktown.Game.Art.StockTakeLook Take;
            /// <summary>Spec 029: a fryer basket's predicted arc into or out of the oil.</summary>
            public Worktown.Game.Art.BasketView Basket;
            /// <summary>Spec 030: a griddle spot's predicted flip.</summary>
            public Worktown.Game.Art.GriddleSpotView Griddle;
            /// <summary>Spec 031: an assembly board's predicted fold or packaging.</summary>
            public Worktown.Game.Art.BuildStackView Build;
            /// <summary>031b: the build view's token of this prediction (each fold or packaging click has its own).</summary>
            public int BuildToken;
            public int ClickFrame;
            public bool VisualLogged;
        }

        public Transform HoldPoint => _holdPoint;

        /// <summary>Owner side: last answer from the server, for the debug overlay.</summary>
        public string LastResult
        {
            get => _lastResult;
            private set
            {
                _lastResult = value;
                ResultVersion++;
            }
        }
        private string _lastResult = string.Empty;

        /// <summary>Owner side: last kitchen answer (null if the last result was not a kitchen one).</summary>
        public CookOutcome? LastCookOutcome { get; private set; }
        /// <summary>Owner side: last pickup/drop answer (null if the last result was not a pickup one).</summary>
        public PickupOutcome? LastPickupOutcome { get; private set; }
        /// <summary>Spec 020, owner side: the last pickup answer was for a place request (for the toast text).</summary>
        public bool LastPickupWasPlace { get; private set; }

        /// <summary>Owner side: increases with every result, so the HUD can toast repeats (spec 011).</summary>
        public int ResultVersion { get; private set; }

        /// <summary>Spec 020, owner side, first person: what the hands hold, as InteractionRules sees it.</summary>
        public Held CurrentHeld { get; private set; }
        /// <summary>Spec 020, owner side, first person: what the dot points at, as InteractionRules sees it.</summary>
        public AimTarget CurrentAim { get; private set; }
        /// <summary>Spec 020, owner side: what a click does now (None in third person). Prompt, highlight and click use it.</summary>
        public InteractionAction CurrentAction { get; private set; }
        /// <summary>Spec 020, owner side: the free spot chosen on the aimed surface (PlaceSpots.None if none).</summary>
        public int TargetSpot { get; private set; } = PlaceSpots.None;
        /// <summary>Spec 020, owner side: world position of <see cref="TargetSpot"/> (where the item would rest).</summary>
        public Vector3 TargetSpotPosition { get; private set; }
        /// <summary>Spec 020, owner side, first person: <see cref="Target"/> is seen but beyond reach (no action, "Too far").</summary>
        public bool TargetTooFar { get; private set; }

        /// <summary>
        /// Spec 041, owner side, first person: the surface a right click puts the held item on (the aimed free spot: the click's own
        /// spot when the click places, else the free spot nearest where the ray meets a surface behind what it aims at, e.g. next to
        /// the pot, else the best free spot in the aim cone), or null: a right click drops it at your feet.
        /// </summary>
        public PlaceSurface PutDownSurface { get; private set; }
        /// <summary>Spec 041: the spot of <see cref="PutDownSurface"/> (PlaceSpots.None without one).</summary>
        public int PutDownSpot { get; private set; } = PlaceSpots.None;
        /// <summary>Spec 041: the world position of <see cref="PutDownSpot"/>.</summary>
        public Vector3 PutDownPosition { get; private set; }
        /// <summary>Spec 041, owner side: the held item's rest rotation from the wheel, in PlaceTurn steps (0 = the server's own rest).</summary>
        public int RestTurn => _restTurn;

        public override void OnStartServer()
        {
            _spam.Reset(); // spec 045: a pooled player object starts a new life
            _service = NetworkManager.GetComponent<PickupService>();
            if (_service == null)
                Debug.LogError("[Pickup] PickupService missing on the NetworkManager object");
            _kitchen = NetworkManager.GetComponent<KitchenService>();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!IsOwner) return;
            // Spec 027b review: requests one tick plus a margin further apart than the server's limit.
            _clientInterval = PickupRules.ClientRequestInterval((float)TimeManager.TickDelta);
            _clicks = new ClickBuffer(_clientInterval);
            _snapshotOf = Snapshot;
            _heldView = GetComponent<HeldItemView>();
            // Spec 020: owner-only highlight and world label fade.
            if (TryGetComponent(out InteractionHighlight highlight)) highlight.Activate(this);
            if (TryGetComponent(out WorldLabelFade labels)) labels.Activate();
            // Spec 021 AC8 -demo-cookware moved to Scripts/Dev (spec 027): it walks through the player's intents now.
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            SetLookSuppressed(false);
            // Spec 027b review: no predicted look outlives its player.
            _ending.Clear();
            _looks.Clear(_ending);
            foreach (PendingLook look in _ending)
            {
                if (look.Item != null) look.Item.EndPrediction(false);
                if (look.Take != null) look.Take.Confirm(); // spec 028b: the copy just goes
                if (look.Basket != null) look.Basket.EndPrediction(); // spec 029: back to the synced pose
                if (look.Griddle != null) look.Griddle.EndPrediction(false); // spec 030
                if (look.Build != null) look.Build.EndPrediction(look.BuildToken); // spec 031
            }
            _ending.Clear();
            _squeeze.Reset(); // spec 031: the server stops a squeeze left running
            if (_squeezeView != null) _squeezeView.Predict(false);
            StopPourLook(false); // spec 033
            _pendingTake = null;
            _pendingPickUp = null;
            ClearQueuedClick();
        }

        private void Update()
        {
            if (!IsOwner) return;
            if (_rig == null) _rig = FindFirstObjectByType<PlayerCameraRig>();
            bool firstPerson = IsFirstPerson;
            Pickup held = FindHeldPickup();
            UpdatePrediction();
            // Spec 018: first person aims with the camera centre; third person keeps the nearest station.
            if (!firstPerson) TargetTooFar = false;
            _coneBuilt = false;
            Target = firstPerson ? KeepShown(FindAimTarget(held)) : FindNearestStation();
            UpdateInteraction(firstPerson, held);
            UpdatePutDown(firstPerson, held); // spec 041: where a right click puts it
            if (held != _restTurnItem)
            {
                _restTurnItem = held; // spec 041: a new item in the hands starts at the server's own rest rotation
                _restTurn = 0;
            }

            if (_inputReader == null) _inputReader = FindFirstObjectByType<Worktown.Game.Player.PlayerInputReader>();
            Keyboard keyboard = Keyboard.current;
            Gamepad gamepad = Gamepad.current;
            Mouse mouse = Mouse.current;
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            ConsumeScripted(out _scriptedLeft, out _scriptedRight, out _scriptedTurn); // spec 041: the tours' inputs, this frame only
            // Spec 012: while quick chat (T) or the emote wheel (G / LB) is held, the keys belong to them; the
            // wheel then owns the camera freeze, so the knife lets go of it without writing it.
            bool wheel = GameInput.Held(ControlAction.EmoteWheel); // spec 048: the bound keys and buttons
            // Keys belong to the menu or banner while one is open (review finding, spec 011).
            bool blocked = keyboard == null || wheel || GameInput.Held(ControlAction.QuickChat)
                || (_inputReader != null && _inputReader.IsUiOpen);

            if (blocked)
            {
                if (!_squeeze.Idle) UpdateSqueezeWith(null, false, false, false); // spec 031: a menu lets go of the dispenser
            }
            if (blocked)
            {
                _gesture.Reset();
                // A press that the UI or the wheel had never becomes a click.
                _click.Update(mouse != null && mouse.leftButton.isPressed, 0f, 0f, Time.unscaledTime);
                _click.Cancel();
                ClearQueuedClick();
                if (wheel) _suppressingLook = false;
                else SetLookSuppressed(false);
            }
            else if (firstPerson)
            {
                if (_queuedKind != QueuedKind.None && !_queuedFirstPerson) ClearQueuedClick(); // the view changed
                UpdateFirstPerson(keyboard, gamepad, mouse, locked, held);
                SendQueuedClick(true);
            }
            else
            {
                _gesture.Reset();
                _click.Reset();
                if (_queuedKind != QueuedKind.None && _queuedFirstPerson) ClearQueuedClick(); // the view changed
                SetLookSuppressed(false);
                Wheel.Reset(); // the wheel zooms the camera in third person
                // Spec 041: the left button is E in third person (the mouse orbits the camera, so it acts on the nearest target);
                // the first press on a free cursor only locks it.
                bool leftDown = mouse != null && locked && mouse.leftButton.isPressed;
                bool leftPressed = mouse != null && locked && _lockedLastFrame && mouse.leftButton.wasPressedThisFrame;
                // 031b: holding E (or the right trigger) at the assembly board squeezes its nearest dispenser. 031b review: only after
                // a 0.2 s hold (SqueezeHoldGate); a tap keeps its normal E meaning and is queued when it is let go.
                bool squeezing = UpdateThirdPersonSqueeze(gamepad, leftDown, leftPressed, out bool tapped);
                bool act = GameInput.Pressed(ControlAction.Use) || leftPressed || _scriptedLeft
                    || (gamepad != null && gamepad.rightTrigger.wasPressedThisFrame);
                // Spec 020 AC7: E acts in third person only. Spec 027b review: through the click buffer as well.
                // 041 review: E (and the left click) always act in third person (ControlRules), so no router check here.
                if (InteractionRules.KeysActInView(firstPerson) && (tapped || (!squeezing && act)))
                    Queue(QueuedKind.Key, false);
                // Spec 041: the right click (left trigger, Q, X) drops what you hold; it never does anything else.
                bool right = RightPressed(mouse, gamepad, locked) || _scriptedRight;
                bool q = GameInput.Pressed(ControlAction.PutDown);
                if ((right || q) && ControlRules.IntentFor(right ? HandInput.RightClick : HandInput.KeyQ, false, InteractionAction.None,
                        held != null, false) == HandIntent.Drop)
                    Queue(QueuedKind.Drop, false);
                SendQueuedClick(false);
            }
            _lockedLastFrame = locked;
        }

        /// <summary>Spec 020: the rule's inputs and its action for this frame (first person only).</summary>
        private void UpdateInteraction(bool firstPerson, Pickup held)
        {
            if (!firstPerson)
            {
                CurrentHeld = Held.Nothing;
                CurrentAim = AimTarget.Nothing;
                CurrentAction = InteractionAction.None;
                AimClosed = TargetClosed(); // spec 053: the third-person prompt of the nearest station
                return;
            }
            CurrentHeld = Hands(held);
            CurrentAim = TargetTooFar ? Describe(Target).TooFar() : Describe(Target);
            CurrentAction = InteractionRules.ActionFor(CurrentHeld, CurrentAim);
            // Spec 053: a closed station, slot or cookware offers nothing; the prompt says why (the server refuses it the same way).
            AimClosed = TargetClosed();
            if (AimClosed)
            {
                CurrentAction = InteractionAction.None;
                CurrentAim = AimTarget.Nothing;
            }
            // Spec 029b: while a take is on its way and nothing is really held, a click offers nothing (no Place, PutInto or
            // Sell of food that is not there yet), and the prompt stays empty.
            if (InteractionRules.WaitsForTake(_pendingTake != null, held != null))
            {
                CurrentAction = InteractionAction.None;
                CurrentAim = AimTarget.Nothing;
            }
        }

        // Spec 041: the tours' scripted presses of this frame (development builds; always false / 0 in a release build).
        private bool _scriptedLeft;
        private bool _scriptedRight;
        private int _scriptedTurn;

        private static bool RightPressed(Mouse mouse, Gamepad gamepad, bool locked) =>
            (mouse != null && locked && mouse.rightButton.wasPressedThisFrame) || (gamepad != null && gamepad.leftTrigger.wasPressedThisFrame);

        /// <summary>
        /// Spec 020, first person: the left click does <see cref="CurrentAction"/> (on release, see ClickGesture); with
        /// the knife over a board with food the left button drags strokes instead (spec 019). Spec 041 (ControlRules): the right
        /// click only puts down what you hold (on <see cref="PutDownSurface"/>, else at your feet; Q and X are hidden aliases); the
        /// wheel turns the food on the board with the knife, else the held item's rest rotation. Gamepad: right trigger click (or
        /// stroke), left trigger put down, Y one turn step. No E, no digits.
        /// </summary>
        private void UpdateFirstPerson(Keyboard keyboard, Gamepad gamepad, Mouse mouse, bool locked, Pickup held)
        {
            bool pressed = mouse != null && mouse.leftButton.isPressed;
            Vector2 delta = mouse != null ? mouse.delta.ReadValue() : Vector2.zero;
            bool armed = CurrentAction == InteractionAction.Cut;

            // Spec 019: a press while the cursor is free only locks it again (PlayerInputReader), it never cuts.
            bool stroke = _gesture.Update(armed, pressed && locked, delta.y);
            SetLookSuppressed(armed && pressed && locked);
            bool click = _click.Update(pressed, delta.x, delta.y, Time.unscaledTime);
            // The first click on a free cursor only locks it (spec 010): that press is no click.
            if (pressed && !(locked && _lockedLastFrame)) _click.Cancel();
            // Spec 031: holding on a sauce dispenser squeezes; that press and its release make no click (031b: nor the trigger's).
            bool squeezing = UpdateSqueeze(mouse, gamepad, locked);
            if (squeezing)
            {
                _click.Cancel();
                click = false;
            }

            bool holding = held != null;
            bool spot = PutDownSurface != null;
            if (armed)
            {
                if (stroke) _click.Cancel();
                if (gamepad != null && gamepad.rightTrigger.wasPressedThisFrame) stroke = true;
                Vessel board = Target as Vessel;
                if (stroke && ControlRules.IntentFor(HandInput.HoldLeft, true, CurrentAction, holding, spot) == HandIntent.Stroke)
                    SendKnife(board, VesselRules.ActionStroke);
            }
            else if (click || _scriptedLeft || (!squeezing && gamepad != null && gamepad.rightTrigger.wasPressedThisFrame))
            {
                // Spec 027b: inside the request interval the click waits (never swallowed). Spec 030's right-click flip is gone
                // (spec 041): a flip is this left click with the spatula, like every tool use.
                if (ControlRules.IntentFor(HandInput.LeftClick, true, CurrentAction, holding, spot) == HandIntent.Act)
                    Queue(QueuedKind.Click, true);
            }

            // Spec 041: the right click (left trigger; Q and X as hidden aliases) puts down what you hold, nothing else.
            bool right = RightPressed(mouse, gamepad, locked) || _scriptedRight;
            bool q = GameInput.Pressed(ControlAction.PutDown);
            if (right || q)
            {
                HandIntent put = ControlRules.IntentFor(right ? HandInput.RightClick : HandInput.KeyQ, true, CurrentAction, holding, spot);
                if (put == HandIntent.PutDown || put == HandIntent.Drop) Queue(QueuedKind.PutDown, true);
            }

            // Spec 041: the wheel (gamepad Y: one step) turns the food on the board with the knife, else the held item's rest rotation.
            int steps = _scriptedTurn + (GameInput.Pressed(ControlAction.Turn) ? 1 : 0);
            if (armed || holding) steps += Wheel.Update(locked && _inputReader != null ? _inputReader.Zoom : 0f);
            else Wheel.Reset();
            if (steps == 0) return;
            switch (ControlRules.IntentFor(HandInput.Wheel, true, CurrentAction, holding, spot))
            {
                case HandIntent.TurnFood:
                    SendKnife(Target as Vessel, VesselRules.ActionTurn); // one turn request, however far the wheel rolled
                    break;
                case HandIntent.TurnRest:
                    _restTurn = PlaceTurn.Add(_restTurn, steps);
                    break;
            }
        }

        /// <summary>
        /// Spec 027b: a click, Q or E. Outside the request interval it acts at once; inside it waits (one at most, the newest
        /// wins). A click that does nothing now is not queued. What it was aimed at is kept, so a queued click is dropped
        /// when the target changed before it could go out (FeelRules.KeepQueuedClick).
        /// </summary>
        private void Queue(QueuedKind kind, bool firstPerson)
        {
            if (kind == QueuedKind.Click && CurrentAction == InteractionAction.None) return;
            if (_clicks.Click(Time.unscaledTime))
            {
                Act(kind);
                return;
            }
            _queuedKind = kind;
            _queuedFirstPerson = firstPerson;
            _queuedAction = CurrentAction;
            // Spec 041: a queued put-down remembers where it was aimed (a surface and spot, or the feet).
            _queuedTarget = kind == QueuedKind.PutDown ? TargetId(PutDownSurface) : TargetId(Target);
            _queuedSpot = kind == QueuedKind.PutDown ? PutDownSpot : TargetSpot;
        }

        private static int TargetId(Component target) => target != null ? target.GetInstanceID() : 0;

        private void Act(QueuedKind kind)
        {
            switch (kind)
            {
                case QueuedKind.Click:
                    ExecuteAction();
                    break;
                case QueuedKind.Drop:
                    Pickup held = FindHeldPickup();
                    if (held != null) DropWithLook(held);
                    break;
                case QueuedKind.Key:
                    HandleThirdPersonKey(FindHeldPickup() != null);
                    break;
                case QueuedKind.PutDown:
                    PutDownWithLook(FindHeldPickup()); // spec 041: the right click
                    break;
            }
        }

        /// <summary>
        /// Spec 041: the right click. On <see cref="PutDownSurface"/>'s spot (the place request, with the wheel's turn and the
        /// predicted rest), else the drop at the feet. Nothing without something in the hands.
        /// </summary>
        private void PutDownWithLook(Pickup held)
        {
            // 041 review: an item already flying to a predicted place sends no second place request (no pose jump).
            if (held == null || held.HasRestPrediction) return;
            if (PutDownSurface != null && PutDownSpot >= 0) PlaceWithLook(held, PutDownSurface, PutDownSpot, PutDownPosition);
            else DropWithLook(held);
        }

        /// <summary>Spec 020/027b: the place request for <paramref name="spot"/> with its predicted rest and sound; spec 041: turned by
        /// the wheel's <see cref="RestTurn"/> (packed into the spot int, PlaceTurn).</summary>
        private void PlaceWithLook(Pickup held, PlaceSurface surface, int spot, Vector3 spotPosition)
        {
            SendPlace(surface.Index, PlaceTurn.Pack(spot, _restTurn));
            if (held == null) return;
            if (surface.IsCupSpot) // spec 042: it stands under the tap for us now (a press right after it pours into it)
            {
                _placedCup = held;
                _placedSpot = surface;
                _placedUntil = Time.unscaledTime + PlacedCupSeconds;
            }
            Predict(PredictionKind.Place, held, null);
            held.PredictRest(PlacePose(held, surface, spot, transform.position, _restTurn));
            PlaySound(SoundRules.PlaceSound(SoundRules.MaterialOf(held), surface.IsBurner), spotPosition);
        }

        /// <summary>Spec 027b: sends the queued click, Q or E once the request interval has passed, if still aimed alike.</summary>
        private void SendQueuedClick(bool firstPerson)
        {
            if (_queuedKind == QueuedKind.None || !_clicks.Poll(Time.unscaledTime)) return;
            QueuedKind kind = _queuedKind;
            _queuedKind = QueuedKind.None;
            if (kind == QueuedKind.Click
                && !FeelRules.KeepQueuedClick(_queuedAction, _queuedTarget, _queuedSpot, CurrentAction, TargetId(Target), TargetSpot))
                return; // review of spec 027b: the target, the spot or the action changed since the click

            if (kind == QueuedKind.Key && !firstPerson && TargetId(Target) != _queuedTarget) return;
            // Spec 041: a queued put-down goes only where it was aimed (the same spot, or still the feet).
            if (kind == QueuedKind.PutDown && (TargetId(PutDownSurface) != _queuedTarget || PutDownSpot != _queuedSpot)) return;
            Act(kind);
        }

        private void ClearQueuedClick()
        {
            _clicks.Clear();
            _queuedKind = QueuedKind.None;
        }

        /// <summary>
        /// Spec 020/021: sends the request for <see cref="CurrentAction"/> on <see cref="Target"/>. Nothing for None or Cut.
        /// Spec 027b: plays the action's sound and starts its expected look in the same frame.
        /// </summary>
        private void ExecuteAction()
        {
            switch (CurrentAction)
            {
                case InteractionAction.Press:
                    if (Target is StationButton button && button.Station != null)
                    {
                        PlaySound(SoundKey.Button, button.transform.position);
                        button.ShowPress(); // spec 044: the button sinks at the click
                        if (button.Station.Kind == StationKind.Fryer && FryerRules.TryParseBasketOption(button.Option, out _))
                            PressBasketButton(button); // spec 029b: the intent (lower or raise), then the arc at the click
                        else
                            SendStation(button.Station.Index, button.Option);
                    }
                    else if (Target is StockSlot boardButton && boardButton.Station != null && boardButton.Station.Kind == StationKind.MenuBoard)
                    {
                        PlaySound(SoundKey.Button, boardButton.Anchor); // spec 037: a menu board button (the server moves the price)
                        SendStation(boardButton.Station.Index, boardButton.Option);
                    }
                    break;
                case InteractionAction.Shake:
                    if (Target is Vessel shaken)
                    {
                        // Spec 029: the rattle and the jolt in the click's frame; the server counts the shake.
                        PlaySound(SoundKey.Rattle, shaken.transform.position);
                        SendUse(shaken.NetworkObject, VesselRules.ActionShake);
                        if (shaken.TryGetComponent(out Worktown.Game.Art.BasketView view))
                            view.PredictShake(FeelRules.ConfirmTimeout(FeelSettings.Current.ConfirmTimeout, RoundTripSeconds(),
                                TimeManager != null ? (float)TimeManager.TickDelta : 0f));
                    }
                    break;
                case InteractionAction.Flip:
                    if (Target is Vessel spot) FlipWithLook(spot); // spec 030: the arc and the tssh at the click
                    break;
                case InteractionAction.Fold:
                    // Spec 031: the wrap closes over the fillings at the click (its swish comes with the look).
                    if (Target is Vessel foldSpot)
                        BuildStepWithLook(foldSpot, BuildRules.Mode(BuildRules.CloseFolded, BuildRules.PackNone, true), 0, false, null);
                    break;
                case InteractionAction.Pay when Target is StockSlot till:
                    PayWithLook(till); // spec 032: the clink or the beep and the coin in the tray at the click
                    break;
                case InteractionAction.Unpack when Target is StockSlot into:
                    UnpackWithLook(into); // spec 034: the box tips into the slot at the click
                    break;
                case InteractionAction.Order when Target is StockSlot row:
                    TerminalClick(row); // spec 034: the beep and the lit row
                    break;
                case InteractionAction.Page when Target is StockSlot pageButton:
                    TerminalClick(pageButton); // spec 034: the page or the pack count at once
                    break;
                case InteractionAction.Take when Target is StockSlot dirty && dirty.Station != null
                    && (dirty.Station.Kind == StationKind.DishReturn || dirty.Station.Kind == StationKind.TrashCan):
                    CleaningTakeWithLook(dirty); // spec 035: the top dirty plate (or the bag) flies into the hands at the click
                    break;
                case InteractionAction.Wash when Target is StockSlot basin && basin.Station != null:
                    WashWithLook(basin); // spec 035: the plate drops into the sink at the click
                    break;
                case InteractionAction.Discard:
                    DiscardWithLook(Target); // spec 035: into the trash (or the bag into the bin) at the click
                    break;
                case InteractionAction.Take when Target is StockSlot cups && cups.Station != null && cups.Station.Kind == StationKind.DrinkDispenser:
                    TakeWithLook(cups, DrinkRules.CupOption, "empty cup"); // spec 033: the top cup flies into the hand at the click
                    break;
                case InteractionAction.Load when Target is StockSlot coneSlot && coneSlot.Station != null:
                    LoadConeWithLook(coneSlot); // spec 036: the full cone stands on the spit at the click
                    break;
                case InteractionAction.Lid when Target is StockSlot lids && lids.Station != null:
                    LidWithLook(lids.Station); // spec 033: the lid snaps on at the click
                    break;
                case InteractionAction.Pack when Target is StockSlot rack:
                {
                    // Spec 031: wrap paper, a box or a bag from the rack onto the build, its look at the click.
                    Vessel rackSpot = BuildSpotOf(rack.Station, out Worktown.Game.Art.BuildStackView rackStack);
                    int packaging = BuildRules.PackagingOf(rack.Option);
                    byte seen = rackStack != null ? rackStack.SeenMode : rackSpot != null ? rackSpot.Mode.Value : (byte)0;
                    byte next = packaging == BuildRules.Paper
                        ? BuildRules.Mode(BuildRules.CloseWrapped, BuildRules.PackNone, BuildRules.OnBread(seen))
                        : BuildRules.Mode(BuildRules.CloseOf(seen), packaging == BuildRules.Box ? BuildRules.PackBox : BuildRules.PackBag, BuildRules.OnBread(seen));
                    PlaySound(SoundKey.Pick, rack.Anchor);
                    BuildStepWithLook(rackSpot, next, BuildRules.PackagingOption(packaging), true, rack.Station);
                    break;
                }
                case InteractionAction.PutInto:
                case InteractionAction.Pour:
                case InteractionAction.Take:
                    if (Target is Vessel vessel)
                    {
                        Pickup held = FindHeldPickup();
                        // Spec 029b: holding a plate, a Take tips the basket's food onto it (the food sound, not the pick).
                        bool pick = CurrentAction == InteractionAction.Take && held == null;
                        PlaySound(pick ? SoundKey.Pick : SoundKey.PlaceFood, vessel.transform.position);
                        SendUse(vessel.NetworkObject, VesselRules.ActionUse);
                        if (CurrentAction == InteractionAction.Take) PredictTipOrScoop(vessel, held); // spec 044
                        if (CurrentAction == InteractionAction.PutInto && held != null)
                        {
                            Predict(PredictionKind.PutInto, held, vessel);
                            held.PredictInto(IntoPose(held, vessel));
                        }
                    }
                    else if (Target is Station shelf)
                    {
                        PlaySound(SoundKey.Pick, shelf.transform.position);
                        SendStation(shelf.Index, 0); // the plate shelf
                    }
                    break;
                case InteractionAction.Buy when Target is StockSlot slot:
                    if (slot.Station != null) TakeWithLook(slot);
                    break;
                case InteractionAction.Sell:
                case InteractionAction.Buy:
                    if (Target is Station station)
                    {
                        PlaySound(CurrentAction == InteractionAction.Buy ? SoundKey.Pick : SoundKey.Button, station.transform.position);
                        SendStation(station.Index, 0);
                    }
                    break;
                case InteractionAction.Place:
                    if (Target is PlaceSurface surface && TargetSpot >= 0)
                        PlaceWithLook(FindHeldPickup(), surface, TargetSpot, TargetSpotPosition);
                    break;
                case InteractionAction.PickUp:
                    if (Target is Vessel cookware) PickUpWithLook(cookware.GetComponent<Pickup>());
                    else if (Target is Pickup pickup) PickUpWithLook(pickup);
                    break;
            }
        }

        /// <summary>
        /// Spec 044: the look of a tip or a scoop at the click (the server decides; the look goes back after the confirm timeout if
        /// nothing changes). Holding a plate or bowl at a raised basket or a griddle spot: its food is on the plate at once and the source
        /// is empty. At the spit's tray (empty hands or an open build): the last portion leaves the tray and a copy of it flies into the hands.
        /// </summary>
        private void PredictTipOrScoop(Vessel target, Pickup held)
        {
            float timeout = RequestTimeout();
            if (target.Kind == VesselKind.SpitTray)
            {
                if (target.ContentCount == 0) return;
                Transform part = target.LastPartModel;
                target.PredictContents(ContentsPrediction.Scooped(target.SeenLooks), timeout);
                NoteContentsRequest(target, null);
                if (part != null)
                    Worktown.Game.Art.ExitFlightLook.Begin(part, new Pose(part.position, part.rotation), HandPose().position, null, Time.unscaledTime);
                return;
            }
            if (held == null || !held.TryGetComponent(out Vessel plate)) return;
            bool tips = (target.Kind == VesselKind.Basket && VesselRules.TipsFromBasket(plate.Kind))
                        || (target.Kind == VesselKind.Griddle && VesselRules.TipsFromFixed(target.Kind, plate.Kind));
            if (!tips || target.ContentCount == 0) return;
            plate.PredictContents(ContentsPrediction.Tipped(plate.SeenLooks, target.SeenLooks), timeout);
            target.PredictContents(System.Array.Empty<FoodLook>(), timeout);
            NoteContentsRequest(target, plate);
        }

        // 044 follow-up: the request of the last predicted tip or scoop and its vessels, so a refusal ends their look at once.
        private int _contentsRequest;
        private Vessel _contentsSource;
        private Vessel _contentsPlate;

        private void NoteContentsRequest(Vessel source, Vessel plate)
        {
            _contentsRequest = _lastRequest;
            _contentsSource = source;
            _contentsPlate = plate;
        }

        /// <summary>
        /// 044 follow-up: the answer to request <paramref name="answered"/>: a refused tip or scoop shows the real contents now. One check
        /// (ContentsPrediction.RefusalEnds; 054 round, review nit): a yes leaves the note as it is (the synced contents end the look, and
        /// request ids never repeat, so no later answer matches it); the next tip or scoop overwrites it.
        /// </summary>
        private void AnswerContents(int answered, bool ok)
        {
            if (!ContentsPrediction.RefusalEnds(answered, _contentsRequest, ok)) return;
            if (_contentsSource != null) _contentsSource.EndPredictedContents();
            if (_contentsPlate != null) _contentsPlate.EndPredictedContents();
            _contentsRequest = 0;
            _contentsSource = null;
            _contentsPlate = null;
        }

        /// <summary>Spec 027b: a pick-up request with its predicted arc into the hands and the pick sound.</summary>
        private void PickUpWithLook(Pickup pickup)
        {
            if (pickup == null) return;
            PlaySound(SoundKey.Pick, pickup.transform.position);
            SendPickup(pickup.NetworkObject);
            if (!pickup.IsHeld)
            {
                Predict(PredictionKind.PickUp, pickup, null);
                pickup.PredictHeld(OwnerId);
                _pendingPickUp = pickup; // spec 029b: counts as held until the server answers
            }
        }

        /// <summary>
        /// Spec 028b: take from a stock slot. The pick sound, RequestStation(station, slot), and a copy of the slot's item on the
        /// pick-up arc into the hand, judged like any predicted look (confirmed when food is in the hands; a refusal or the
        /// timeout sends it back with "nope").
        /// </summary>
        private void TakeWithLook(StockSlot slot, int option = -1, string name = null)
        {
            PlaySound(SoundKey.Pick, slot.Anchor);
            SendStation(slot.Station.Index, option >= 0 ? option : slot.Option); // spec 033: the cup stack's option is its own
            if (slot.TryGetComponent(out Worktown.Game.Art.StockSlotLook stockLook)) // spec 034: one fewer at once
            {
                stockLook.PredictTake(_lastRequest);
                _pendingStockLook = stockLook;
            }
            if (_snapshotOf == null) _snapshotOf = Snapshot;
            float now = Time.unscaledTime;
            Worktown.Game.Art.StockTakeLook look = Worktown.Game.Art.StockTakeLook.Begin(slot.transform, HandPose, now, FeelSettings.Current, this);
            if (look == null) return;
            _pendingTake = look;
            string id = slot.IngredientId;
            _pendingTakeName = name ?? (id != null ? DemoIngredients.All[slot.IngredientIndex].DisplayName : "food");
            var pending = new PendingLook { Take = look, ClickFrame = Time.frameCount };
            float timeout = FeelRules.ConfirmTimeout(FeelSettings.Current.ConfirmTimeout, RoundTripSeconds(),
                TimeManager != null ? (float)TimeManager.TickDelta : 0f);
            _looks.Add(pending, look, new FeelPrediction(PredictionKind.Take, OwnerId, Snapshot(pending), now, timeout), _lastRequest);
        }

        /// <summary>
        /// Spec 028b: where the copy of a taken item lands: where the food's model will be drawn in the hands, i.e. the
        /// first-person view anchor (else the hold point) with the model standing on the bottom of the food's collider
        /// (<see cref="_foodBottom"/> below the pivot), so the real food takes over without a jump.
        /// </summary>
        private Pose HandPose()
        {
            Transform anchor = _rig != null && _rig.IsFirstPerson && _rig.ViewHold != null ? _rig.ViewHold : _holdPoint;
            if (anchor == null) return new Pose(transform.position + Vector3.up * 1.2f, transform.rotation);
            return new Pose(anchor.position - anchor.up * _foodBottom, anchor.rotation);
        }

        /// <summary>
        /// The hands as InteractionRules sees them; spec 028b: while a take from stock is on its way they count as holding
        /// that food (a second take would be refused anyway, and the prompt already shows what the hands will hold).
        /// </summary>
        private Held Hands(Pickup held)
        {
            // Spec 029b/030: the pure rule (InteractionRules.EffectiveHands): a take on its way counts as that food; an item on its
            // predicted way into the hands counts as held for refusals only (no take or buy, and never a tip or pour with it).
            bool flying = _pendingPickUp != null && _pendingPickUp.IsSpawned && _pendingPickUp.HasPrediction;
            return InteractionRules.EffectiveHands(Describe(held), _pendingTake != null ? _pendingTakeName ?? "food" : null,
                flying ? Describe(_pendingPickUp) : (Held?)null);
        }

        /// <summary>Spec 027b: a drop request (the right click with no spot aimed at, Q, or E in third person) with its predicted fall.</summary>
        private void DropWithLook(Pickup held)
        {
            SendDrop();
            if (held != null)
            {
                Predict(PredictionKind.Drop, held, null);
                Pose pose = held.PredictedDropPose(transform.position, transform.forward);
                held.PredictRest(pose);
                PlaySound(SoundRules.PlaceSound(SoundRules.MaterialOf(held), false), pose.position);
            }
        }

        /// <summary>Spec 027b: where an item lands on spot <paramref name="spot"/> of <paramref name="surface"/>, as the
        /// server places it (PickupService.TryPlace): its bottom on the spot, its resting tilt, the surface's yaw; spec 039: a
        /// tool's handle towards <paramref name="placer"/> (the same ToolRest rule the server applies to its own copy of
        /// the player's position, so the predicted look does not jump). Spec 041: turned by <paramref name="turn"/> wheel steps, as the
        /// server turns it (PlaceTurn.Apply).</summary>
        private static Pose PlacePose(Pickup item, PlaceSurface surface, int spot, Vector3 placer, int turn)
        {
            Vector3 spotPosition = surface.SpotWorldPosition(spot);
            Vector3 position = spotPosition + surface.transform.up * item.BottomOffset;
            return new Pose(position, PlaceTurn.Apply(ToolRest.RestRotation(item.IsHandleTool, item.ClientRestTilt, surface.transform.rotation,
                placer, spotPosition), turn));
        }

        /// <summary>Spec 027b: just above the cookware's contents, where food is dropped in from.</summary>
        private static Pose IntoPose(Pickup item, Vessel vessel)
        {
            return new Pose(vessel.ContentPoint + Vector3.up * FeelSettings.Current.DropHeight,
                PlaceSpots.RestRotation(item.ClientRestTilt, vessel.transform.rotation));
        }

        /// <summary>
        /// Spec 027b, owner side: where the held item would land now, for the placement ghost. False when nothing would put it anywhere.
        /// 041 review: the left click's own spot when it places (turned by the wheel); whenever the left click would not place (nothing,
        /// or into the pot), the right click's put-down spot, so the ghost never shows a landing the right click would not give.
        /// </summary>
        public bool TryGetLandingPose(out Pickup held, out Pose pose)
        {
            pose = default;
            held = FindHeldPickup();
            if (held == null || held.HasPrediction) return false;
            if (CurrentAction == InteractionAction.Place && Target is PlaceSurface surface && TargetSpot >= 0)
            {
                pose = PlacePose(held, surface, TargetSpot, transform.position, _restTurn);
                return true;
            }
            if (PutDownSurface != null && PutDownSpot >= 0)
            {
                pose = PlacePose(held, PutDownSurface, PutDownSpot, transform.position, _restTurn);
                return true;
            }
            return false;
        }

        // ---- Spec 027b: the predicted look (state machine in PredictionBook / FeelPrediction / ItemLook) ----

        /// <summary>
        /// Tracks the look just started for <paramref name="item"/> against the request sent right before it
        /// (<see cref="_lastRequest"/>). Earlier looks of other items keep going until they resolve (no bounce).
        /// </summary>
        private void Predict(PredictionKind kind, Pickup item, Vessel vessel)
        {
            float now = Time.unscaledTime;
            float rtt = RoundTripSeconds();
            float tick = TimeManager != null ? (float)TimeManager.TickDelta : 0f;
            float timeout = FeelRules.ConfirmTimeout(FeelSettings.Current.ConfirmTimeout, rtt, tick);
            var look = new PendingLook { Item = item, Vessel = vessel, ClickFrame = Time.frameCount };
            _looks.Add(look, item, new FeelPrediction(kind, OwnerId, Snapshot(look), now, timeout), _lastRequest);
        }

        /// <summary>
        /// Spec 029: a fryer basket button was clicked: the basket swings into (or out of) the oil now, judged like any predicted
        /// look (confirmed when its synced state crosses the oil line; a refusal or the timeout sends it back with "nope").
        /// Raising always works; lowering is only predicted when the client sees the fryer on and food in the basket (the server
        /// would refuse otherwise, so nothing moves). Spec 029b: the request is the intent (FryerRules.IntentOption) from where
        /// this client sees the basket (its own pending prediction first), so two players never cancel each other.
        /// </summary>
        private void PressBasketButton(StationButton button)
        {
            FryerRules.TryParseBasketOption(button.Option, out int index);
            Vessel basket = BasketOf(button.Station, index, out Worktown.Game.Art.BasketView view);
            byte flags = button.Station.TryGetComponent(out StationContents fryer) ? fryer.StoveFlags.Value : (byte)0;
            bool seenLowered = SeenLowered(button.Station, index, flags); // spec 030: the same state the prompt shows
            SendStation(button.Station.Index, FryerRules.IntentOption(index, seenLowered));
            if (view == null) return;
            bool lowering = !seenLowered;
            if (lowering && (!FryerRules.IsOn(flags) || basket.ContentCount == 0)) return;
            view.Predict(lowering);
            if (_snapshotOf == null) _snapshotOf = Snapshot;
            var pending = new PendingLook { Basket = view, ClickFrame = Time.frameCount };
            float timeout = FeelRules.ConfirmTimeout(FeelSettings.Current.ConfirmTimeout, RoundTripSeconds(),
                TimeManager != null ? (float)TimeManager.TickDelta : 0f);
            _looks.Add(pending, view, new FeelPrediction(PredictionKind.Basket, OwnerId, Snapshot(pending), Time.unscaledTime, timeout),
                _lastRequest);
        }

        /// <summary>
        /// Spec 030: a flip of the food on a griddle spot: RequestUse(spot, ActionFlip). With the spatula in the hands the food
        /// turns over now on its arc with the "tssh" (judged like any predicted look: confirmed when the spot's synced flip
        /// counter moves on; a refusal or the timeout turns it back with "nope"). Without the spatula nothing moves: the
        /// server says "Use the spatula".
        /// </summary>
        private void FlipWithLook(Vessel spot)
        {
            if (spot == null) return;
            SendUse(spot.NetworkObject, VesselRules.ActionFlip);
            if (!InteractionRules.PredictsFlip(CurrentHeld, CurrentAim) || !spot.TryGetComponent(out Worktown.Game.Art.GriddleSpotView view)) return;
            PlaySound(SoundKey.Flip, spot.transform.position);
            if (_heldView != null) _heldView.PlayFlip(); // spec 031b: the spatula's motion in the hand
            float timeout = FeelRules.ConfirmTimeout(FeelSettings.Current.ConfirmTimeout, RoundTripSeconds(),
                TimeManager != null ? (float)TimeManager.TickDelta : 0f);
            int ahead = view.OwedFlips; // 030 review: our earlier flips still on their way count before this one
            view.PredictFlip(timeout);
            if (_snapshotOf == null) _snapshotOf = Snapshot;
            var pending = new PendingLook { Griddle = view, ClickFrame = Time.frameCount };
            _looks.Add(pending, view, new FeelPrediction(PredictionKind.Flip, OwnerId, FeelRules.FlipStart(Snapshot(pending), ahead),
                Time.unscaledTime, timeout),
                _lastRequest);
        }

        /// <summary>Spec 029/030: basket <paramref name="index"/> of <paramref name="fryer"/> and its view, or null.</summary>
        private Vessel BasketOf(Station fryer, int index, out Worktown.Game.Art.BasketView view)
        {
            RefreshInteractables();
            Vessel basket = null;
            for (int i = 0; i < _allBaskets.Length; i++)
                if (_allBaskets[i] != null && _allBaskets[i].Fryer == fryer && _allBaskets[i].Basket == index) basket = _basketVessels[i];
            view = basket != null && basket.TryGetComponent(out Worktown.Game.Art.BasketView v) ? v : null;
            return basket;
        }

        /// <summary>
        /// Spec 029b/030: where this client sees basket <paramref name="index"/>: its own pending arc first, else the synced state
        /// (the fryer's <paramref name="flags"/> when the basket has no view). The button's click and its prompt both use it.
        /// </summary>
        private bool SeenLowered(Station fryer, int index, byte flags)
        {
            BasketOf(fryer, index, out Worktown.Game.Art.BasketView view);
            return view != null ? view.SeenLowered : FryerRules.IsLowered(flags, index);
        }

        /// <summary>The measured round trip in s, or FeelRules.AssumedRoundTrip before the first ping.</summary>
        private float RoundTripSeconds()
        {
            return FeelRules.RoundTrip(TimeManager != null ? TimeManager.RoundTripTime / 1000f : 0f);
        }

        private ItemSnapshot TakeSnapshot(Pickup held)
        {
            if (held == null) return ItemSnapshot.Gone;
            // Spec 035: a dirty dish from the return or the bag from the trash can confirms its take like food does.
            bool taken = held.TryGetComponent(out Food _) || held.TryGetComponent(out Worktown.Game.Shop.DirtyDish _)
                || held.TryGetComponent(out Worktown.Game.Shop.TrashBag _);
            return FeelRules.HandsSnapshot(OwnerId, held.NetworkObject.ObjectId, taken);
        }

        private ItemSnapshot Snapshot(PendingLook look)
        {
            // Spec 028b: a take from stock is about the hands: does anything stand in them now?
            // Spec 029b: with the held object's id, so only a new held food confirms (a knife arriving from a pending pick-up
            // never does).
            if (look.Take != null) return TakeSnapshot(FindHeldPickup());
            // Spec 029: a basket is about its synced state: 1 while it is in the oil.
            if (look.Basket != null) return new ItemSnapshot(true, -1, Vector3.zero, look.Basket.SyncedLowered ? 1 : 0);
            // Spec 030: a flip is about the spot's synced flip counter (gone once the spot is empty).
            if (look.Griddle != null)
            {
                Vessel spotVessel = look.Griddle.GetComponent<Vessel>();
                return new ItemSnapshot(spotVessel != null && spotVessel.ContentCount > 0, -1, Vector3.zero, look.Griddle.SyncedFlips);
            }
            // Spec 031: a fold or packaging is about the build spot's synced close and pack bits (gone once the board is empty).
            if (look.Build != null)
            {
                Vessel buildVessel = look.Build.GetComponent<Vessel>();
                return new ItemSnapshot(buildVessel != null && buildVessel.ContentCount > 0, -1, Vector3.zero,
                    buildVessel != null ? buildVessel.Mode.Value & 0x0F : 0);
            }
            Pickup item = look.Item;
            if (item == null || !item.IsSpawned) return ItemSnapshot.Gone;
            Vessel vessel = look.Vessel;
            return new ItemSnapshot(true, item.HolderClientId.Value, item.transform.position, vessel != null ? vessel.ContentCount : 0);
        }

        /// <summary>Every frame: reads the synced state; a confirmed look stays, a denied one eases back with "nope".</summary>
        private void UpdatePrediction()
        {
            if (_looks.Count == 0) return;
            if (_snapshotOf == null) _snapshotOf = Snapshot;
            _resolved.Clear();
            _looks.Update(Time.unscaledTime, _snapshotOf, _resolved);
            foreach (PredictionBook<PendingLook>.Resolved r in _resolved)
            {
                PendingLook look = r.Value;
                FeelPrediction p = r.Prediction;
                LogVisual(look, p.Kind);
                float ms = (p.ResolvedTime - p.StartTime) * 1000f;
                if (look.Take != null && look.Take == _pendingTake) _pendingTake = null; // spec 028b: the hands are free again
                if (look.Item != null && look.Item == _pendingPickUp && p.Kind == PredictionKind.PickUp) _pendingPickUp = null;
                if (p.Phase == PredictionPhase.Confirmed)
                {
                    Debug.Log("[Feel] click to confirm: " + ms.ToString("0") + " ms (" + p.Kind + ")");
                    if (look.Item != null) look.Item.EndPrediction(true);
                    if (look.Griddle != null) look.Griddle.EndPrediction(true); // spec 030
                    if (look.Build != null) look.Build.EndPrediction(look.BuildToken); // spec 031
                    if (look.Basket != null) look.Basket.EndPrediction();
                    if (look.Take != null)
                    {
                        Pickup taken = FindHeldPickup();
                        if (taken != null && taken.TryGetComponent(out Food _)) _foodBottom = taken.BottomOffset;
                        look.Take.Confirm(taken != null ? taken.gameObject : null, Time.unscaledTime);
                    }
                    continue;
                }
                Debug.Log("[Feel] " + p.Kind + " " + (p.TimedOut ? "timed out" : "refused") + " after " + ms.ToString("0")
                          + " ms: the look goes back, nope");
                if (look.Item != null) look.Item.EndPrediction(false);
                Vector3 nopeAt = look.Item != null ? look.Item.transform.position : transform.position;
                if (look.Basket != null)
                {
                    look.Basket.EndPrediction(); // spec 029: it swings back to where the server keeps it
                    nopeAt = look.Basket.transform.position;
                }
                if (look.Griddle != null)
                {
                    look.Griddle.EndPrediction(false); // spec 030: it turns back over
                    nopeAt = look.Griddle.transform.position;
                }
                if (look.Build != null)
                {
                    look.Build.EndPrediction(look.BuildToken); // spec 031: it opens back up
                    nopeAt = look.Build.transform.position;
                }
                if (look.Take != null)
                {
                    nopeAt = look.Take.transform.position;
                    look.Take.Deny(Time.unscaledTime);
                }
                PlaySound(SoundKey.Nope, nopeAt);
            }
            _resolved.Clear();
        }

        private static void LogVisual(PendingLook look, PredictionKind kind)
        {
            if (look.VisualLogged) return;
            // Spec 029b: measured for every look: the frame its prediction was first drawn (an item, a take's copy, a basket).
            int drawn = -1;
            if (look.Item != null) drawn = look.Item.PredictionDrawnFrame;
            else if ((object)look.Take != null) drawn = look.Take.DrawnFrame; // the copy may be gone already; its number stays
            else if (look.Basket != null) drawn = look.Basket.PredictionDrawnFrame;
            else if (look.Griddle != null) drawn = look.Griddle.PredictionDrawnFrame; // spec 030
            else if (look.Build != null) drawn = look.Build.PredictionDrawnFrame; // spec 031
            if (drawn < 0) return;
            look.VisualLogged = true;
            int frames = drawn - look.ClickFrame;
            Debug.Log("[Feel] click to visual: " + (frames <= 0 ? "same frame" : frames + " frame(s) later") + " (" + kind + ")");
        }

        /// <summary>The server's answer to request <paramref name="request"/> (its echo): it reaches only that request's prediction (PredictionBook).</summary>
        private int NoteAnswer(int request, AnswerKind kind, bool ok)
        {
            return _looks.Answer(request, kind, ok, Time.unscaledTime);
        }

        private void PlaySound(SoundKey key, Vector3 position)
        {
            if (!_soundsLooked)
            {
                _soundsLooked = true;
                _sounds = FindFirstObjectByType<KitchenSounds>();
            }
            if (_sounds != null) _sounds.Play(key, position);
        }

        private void SendKnife(Vessel board, int action)
        {
            if (board == null) return;
            // The server ignores requests closer than 0.2 s; the client keeps one tick more (spec 027b review), so the chop
            // never lies.
            if (Time.unscaledTime - _lastSendTime < _clientInterval) return;
            SendUse(board.NetworkObject, action);
            if (action == VesselRules.ActionStroke && _heldView != null) _heldView.PlayChop();
        }

        /// <summary>Spec 020/021: the held item as InteractionRules sees it (synced names and looks only).</summary>
        private static Held Describe(Pickup held)
        {
            if (held == null) return Held.Nothing;
            if (held.TryGetComponent(out Knife _)) return Held.Knife;
            if (held.TryGetComponent(out Spatula _)) return Held.Spatula; // spec 030
            if (held.TryGetComponent(out WipeCloth _)) return Held.Cloth; // spec 035
            if (held.TryGetComponent(out Worktown.Game.Shop.DirtyDish _)) return Held.DirtyDish;
            if (held.TryGetComponent(out Worktown.Game.Shop.TrashBag _)) return Held.TrashBag;
            if (held.TryGetComponent(out Vessel vessel)) return Held.Cookware(vessel.Kind, vessel.ContentCount, vessel.Title.Value);
            if (held.TryGetComponent(out Food food))
            {
                FoodLook look = food.Look.Value;
                if (FoodLookRules.IsCup(look)) return DescribeCup(food, look); // spec 033
                return Held.Food(food.DisplayName.Value, FoodLookRules.IsFinished(look), // spec 031: a finished build too
                    FoodLookRules.IsBuild(look)); // 031b: a build goes back only on an empty board
            }
            // Spec 034: a delivery box: its good and units (the box's synced contents), so it unpacks only into its own slot.
            if (held.TryGetComponent(out Worktown.Game.Shop.SupplyBox supply) && supply.TryGetContents(out Worktown.Game.Shop.BoxContents box))
                return Held.Box("box of " + Worktown.Game.Shop.StockCatalog.NameOf(box.Line), box.Line, box.Units);
            return Held.Item("box");
        }

        /// <summary>
        /// Spec 033: a held cup as the rules see it: its synced drink and level, or this player's own pour and lid first (the level from
        /// the release on, the lid from the click on), so the next click never waits for the server's look.
        /// </summary>
        private static Held DescribeCup(Food food, FoodLook look)
        {
            Worktown.Game.Art.FoodVisual visual = food.Visual;
            int tap = DrinkRules.TapOfDrinkByte(look.Ingredient);
            byte level = look.Shape;
            bool lidded = look.Kind == FoodLook.KindLidded || (visual != null && visual.LidShown);
            if (visual != null && visual.ShowsCup && level == DrinkRules.LevelEmpty && visual.PredictedLevel != DrinkRules.LevelEmpty)
            {
                level = visual.PredictedLevel;
                tap = visual.PredictedTap;
            }
            return Held.Cup(food.DisplayName.Value, tap, level, lidded);
        }

        /// <summary>Spec 020/021: the target as InteractionRules sees it (what every client can see of it).</summary>
        private AimTarget Describe(Component target)
        {
            switch (target)
            {
                case StationButton button:
                    byte flags = button.Station != null && button.Station.TryGetComponent(out StationContents stove) ? stove.StoveFlags.Value : (byte)0;
                    // Spec 029: the fryer's knob and basket buttons say what they do from the fryer's flags; spec 030 (029b review): a
                    // basket button from where this client sees the basket (its pending arc first), as its click asks.
                    if (button.Station != null && button.Station.Kind == StationKind.Fryer && FryerRules.TryParseBasketOption(button.Option, out int seenBasket))
                        flags = FryerRules.WithLowered(flags, seenBasket, SeenLowered(button.Station, seenBasket, flags));
                    return AimTarget.Button(button.Station != null ? button.Station.Kind : StationKind.Stove, button.Option, flags);
                case PlaceSurface surface:
                    return SpotTarget(surface, TargetSpot); // spec 042: a tap's cup spot says which tap
                case Vessel vessel:
                    // Spec 031: the build as this client sees it (its own pending fold or packaging first), so a second click never repeats it.
                    if (vessel.Kind == VesselKind.Assembly && vessel.TryGetComponent(out Worktown.Game.Art.BuildStackView stackView))
                        return AimTarget.Cookware(vessel.Kind, vessel.ContentCount, vessel.Title.Value, stackView.SeenMode);
                    return AimTarget.Cookware(vessel.Kind, vessel.ContentCount, vessel.Title.Value, vessel.Mode.Value);
                case StockSlot slot:
                    if (slot.Station != null && IsCleaningKind(slot.Station.Kind)) return DescribeCleaning(slot.Station, slot.Option); // spec 035
                    if (slot.Station != null && slot.Station.Kind == StationKind.Assembly) return WithStock(DescribeTool(slot), slot); // spec 031
                    if (slot.Station != null && slot.Station.Kind == StationKind.Register) return DescribeRegister(slot); // spec 032
                    if (slot.Station != null && slot.Station.Kind == StationKind.DrinkDispenser) return WithStock(DescribeDrinkTool(slot), slot); // spec 033
                    if (slot.Station != null && slot.Station.Kind == StationKind.Terminal) return DescribeTerminal(slot); // spec 034
                    if (slot.Station != null && slot.Station.Kind == StationKind.Spit) return WithStock(DescribeSpit(slot), slot); // spec 036
                    if (slot.Station != null && slot.Station.Kind == StationKind.MenuBoard) return AimTarget.Button(StationKind.MenuBoard, slot.Option, 0); // spec 037
                    return WithStock(DescribeStock(slot.IngredientIndex), slot); // spec 028b: a slot takes like a crate (034: from its count)
                case Station station:
                    // Spec 032: the register's body (between its slots) is no slot: nothing to click, no prompt.
                    if (station.Kind == StationKind.Register) return AimTarget.RegisterSlot(-1, 0, 0);
                    if (station.Kind == StationKind.DrinkDispenser) return AimTarget.DrinkTool(-1, 0, null, -1); // spec 033: the body itself
                    if (station.Kind == StationKind.Terminal) return AimTarget.TerminalSlot(-1, null, -1); // spec 034: the frame itself
                    if (IsCleaningKind(station.Kind) || station.Kind == StationKind.PlateShelf) return DescribeCleaning(station, 0); // spec 035
                    return station.Kind == StationKind.Crate ? DescribeStock(station.IngredientIndex) : AimTarget.OnStation(station.Kind);
                case Pickup pickup:
                    if (pickup.TryGetComponent(out Knife _)) return AimTarget.Item("knife");
                    if (pickup.TryGetComponent(out Spatula _)) return AimTarget.Item("spatula"); // spec 030
                    if (pickup.TryGetComponent(out WipeCloth _)) return AimTarget.Item("the cloth"); // spec 035
                    if (pickup.TryGetComponent(out Worktown.Game.Shop.DirtyDish _)) return AimTarget.Item("the dirty plate");
                    if (pickup.TryGetComponent(out Worktown.Game.Shop.TrashBag _)) return AimTarget.Item("the trash bag");
                    // Spec 034: a delivery box says what is in it ("Click · pick up the box of 6 tomato").
                    if (pickup.TryGetComponent(out Worktown.Game.Shop.SupplyBox delivery) && delivery.TryGetContents(out Worktown.Game.Shop.BoxContents inBox))
                        return AimTarget.Item(delivery.PickupName(inBox));
                    if (pickup.TryGetComponent(out Food food))
                    {
                        string foodName = food.DisplayName.Value.Length > 0 ? food.DisplayName.Value : "food";
                        // Spec 042: a cup on a tap's spot that another player pours into cannot be taken now.
                        return IsBusySpotCup(pickup, food) ? AimTarget.BusyCup(foodName) : AimTarget.Item(foodName);
                    }
                    return AimTarget.Item("box");
                default:
                    return AimTarget.Nothing;
            }
        }

        // ---- Spec 035: cleaning (owner side) ----

        private static bool IsCleaningKind(StationKind kind) => Worktown.Game.Shop.CleaningRules.IsCleaningStation(kind);

        /// <summary>
        /// Spec 035: a cleaning station (or the plate shelf) as the rules see it: its synced byte with this player's own pending step first
        /// (the views' Seen values), so a second click never waits for the server; our own scrub's bit is cleared.
        /// </summary>
        private AimTarget DescribeCleaning(Station station, int option)
        {
            bool known = station.TryGetComponent(out StationContents contents) && contents.IsSpawned;
            // 035 review (F8): the plate shelf's byte means "clean plates" only where a cleaning service counts them (else it is endless).
            if (station.Kind == StationKind.PlateShelf) known = Worktown.Game.Shop.CleaningRules.ShelfKnown(known, HasCleaningService());
            byte b = known ? contents.StoveFlags.Value : (byte)0;
            switch (station.Kind)
            {
                case StationKind.DishReturn:
                    if (station.TryGetComponent(out Worktown.Game.Art.DishReturnView ret)) b = ret.SeenCount;
                    break;
                case StationKind.Sink:
                    if (station.TryGetSlot(Worktown.Game.Shop.CleaningRules.BasinTool, out StockSlot basin)
                        && basin.TryGetComponent(out Worktown.Game.Art.SinkView sink)) b = sink.SeenByte;
                    if (OwnSqueeze(station.Index, Worktown.Game.Shop.CleaningRules.BasinTool))
                        b = (byte)(b & ~Worktown.Game.Shop.CleaningRules.ScrubbingBit);
                    break;
                case StationKind.TrashCan:
                    if (station.TryGetComponent(out Worktown.Game.Art.TrashCanView can)) b = can.SeenFill;
                    break;
            }
            return AimTarget.CleaningStation(station.Kind, option, b, known);
        }

        private Worktown.Game.Shop.CleaningService _cleaningService;
        private bool _cleaningLooked;

        /// <summary>035 review: whether the scene has a cleaning service (looked up once).</summary>
        private bool HasCleaningService()
        {
            if (!_cleaningLooked)
            {
                _cleaningLooked = true;
                _cleaningService = FindFirstObjectByType<Worktown.Game.Shop.CleaningService>();
            }
            return _cleaningService != null;
        }

        private float RequestTimeout() => FeelRules.ConfirmTimeout(FeelSettings.Current.ConfirmTimeout, RoundTripSeconds(),
            TimeManager != null ? (float)TimeManager.TickDelta : 0f);

        /// <summary>Spec 035: the dish return's top plate (or the trash can's bag) on the pick-up arc into the hands, the station's look one fewer.</summary>
        private void CleaningTakeWithLook(StockSlot slot)
        {
            Station station = slot.Station;
            bool bag = station.Kind == StationKind.TrashCan;
            if (bag) PlaySound(SoundRules.BagSound, slot.Anchor);
            TakeWithLook(slot, 0, bag ? "trash bag" : "dirty plate");
            if (!bag && station.TryGetComponent(out Worktown.Game.Art.DishReturnView ret)) ret.PredictTake(RequestTimeout());
            if (bag && station.TryGetComponent(out Worktown.Game.Art.TrashCanView can)) can.PredictTakeBag(RequestTimeout());
        }

        /// <summary>Spec 035: the dirty plate in the hands drops into the sink at the click (the splash), judged like any put-in.</summary>
        private void WashWithLook(StockSlot basin)
        {
            Pickup held = FindHeldPickup();
            SendStation(basin.Station.Index, Worktown.Game.Shop.CleaningRules.SinkPutOption);
            PlaySound(SoundRules.IntoTheSinkSound, basin.Anchor);
            if (basin.TryGetComponent(out Worktown.Game.Art.SinkView sink)) sink.PredictAdd(RequestTimeout());
            if (held == null) return;
            Predict(PredictionKind.PutInto, held, null);
            held.PredictInto(new Pose(basin.Anchor + Vector3.up * 0.05f, held.transform.rotation));
        }

        /// <summary>Spec 035: what the hands hold goes into the trash can (the lid thump), or the bag into the bin, at the click.</summary>
        private void DiscardWithLook(Component target)
        {
            Station station = target is StockSlot s ? s.Station : target as Station;
            if (station == null) return;
            Pickup held = FindHeldPickup();
            bool bin = station.Kind == StationKind.Bin;
            Vector3 opening = station.transform.position + Vector3.up * 0.5f;
            if (station.TryGetComponent(out Worktown.Game.Art.TrashCanView can))
            {
                opening = can.Opening;
                if (held == null || !held.TryGetComponent(out Vessel _)) can.PredictDiscard(RequestTimeout());
            }
            else if (station.TryGetComponent(out Worktown.Game.Art.BinView binView))
            {
                opening = binView.Opening;
                binView.PredictBag();
            }
            PlaySound(SoundRules.DiscardSound(bin), opening);
            SendStation(station.Index, bin ? Worktown.Game.Shop.CleaningRules.BinOption : Worktown.Game.Shop.CleaningRules.TrashOption);
            // Food and the bag fly in and are gone; cookware stays in the hands (only its food goes).
            if (held == null || held.TryGetComponent(out Vessel _)) return;
            Predict(PredictionKind.PutInto, held, null);
            held.PredictInto(new Pose(opening, held.transform.rotation));
        }

        // ---- Spec 036: the seitan spit (owner side) ----

        /// <summary>Spec 036: the spit's cone is a held tool for the squeeze-like hold (the knife on it shaves).</summary>
        private static bool IsSpitHold(StockSlot tool) =>
            tool != null && tool.Station != null && tool.Station.Kind == StationKind.Spit && tool.Option == SpitRules.ConeTool;

        /// <summary>
        /// Spec 036: the spit's cone as the rules see it: the spit's byte (our own loaded cone first, our own shave's bit cleared), the
        /// cone's portions and take name, the tray's portions.
        /// </summary>
        private AimTarget DescribeSpit(StockSlot slot)
        {
            Station spit = slot.Station;
            slot.TryGetComponent(out Worktown.Game.Art.SpitView view);
            byte flags = view != null ? view.SeenFlags : spit.TryGetComponent(out StationContents contents) ? contents.StoveFlags.Value : (byte)0;
            if (OwnSqueeze(spit.Index, SpitRules.ConeTool)) flags = SpitRules.WithShaving(flags, false);
            Vessel cone = view != null ? view.Cone : null;
            Vessel tray = view != null ? view.Tray : null;
            return AimTarget.SpitTool(slot.Option, flags, cone != null ? cone.Mode.Value : (byte)0, tray != null ? tray.ContentCount : 0,
                cone != null ? cone.Title.Value : null);
        }

        /// <summary>Spec 036: the load at the click: the pick sound, the full cone stands on the spit at once (SpitView), RequestStation goes out.</summary>
        private void LoadConeWithLook(StockSlot slot)
        {
            PlaySound(SoundKey.Pick, slot.Anchor);
            if (slot.TryGetComponent(out Worktown.Game.Art.SpitView view)) view.PredictLoad(RequestTimeout());
            SendStation(slot.Station.Index, SpitRules.LoadOption);
        }

        private Worktown.Game.Art.SpitView _shaveView;

        /// <summary>Spec 036: our own shave's look starts at the press: the knife saws in view, the spit's curls fly (its IPourView).</summary>
        private void StartShaveLook(StockSlot cone)
        {
            _shaveView = cone.GetComponent<Worktown.Game.Art.SpitView>();
            if (_shaveView != null) _shaveView.OwnKnifeInHand = IsFirstPerson;
            if (_heldView != null) _heldView.SetShaving(true);
        }

        private void StopShaveLook()
        {
            if (_shaveView != null) _shaveView.OwnKnifeInHand = false;
            _shaveView = null;
            if (_heldView != null && _heldView.IsShaving) _heldView.SetShaving(false);
        }

        /// <summary>Spec 035: the sink's basin or the dispenser's spill is a held tool for the squeeze-like hold.</summary>
        private static bool IsCleaningHold(StockSlot tool) =>
            tool != null && tool.Station != null && ((tool.Station.Kind == StationKind.Sink && tool.Option == Worktown.Game.Shop.CleaningRules.BasinTool)
                || (tool.Station.Kind == StationKind.DrinkDispenser && tool.Option == Worktown.Game.Shop.CleaningRules.SpillTool));

        private Worktown.Game.Art.SpillWipeView _wipeView;

        /// <summary>Every frame of our own wipe: the held cloth goes round on the puddle (the view's pose), and back when it ends.</summary>
        private void UpdateWipeLook()
        {
            if (_heldView == null) return;
            if (_wipeView != null && _squeeze.Held)
            {
                _wipeView.OwnClothInHand = IsFirstPerson;
                if (IsFirstPerson) _heldView.SetPourPose(true, _wipeView.ClothPose(Time.unscaledTimeAsDouble));
            }
            else if (_wipeView != null)
            {
                _heldView.SetPourPose(false, default);
                _wipeView = null;
            }
        }

        // ---- Spec 034: stock counts, delivery boxes and the ordering terminal ----

        private Worktown.Game.Shop.StockCounts _stockCounts;
        private float _nextStockCountsLookup;

        /// <summary>Spec 034: the synced stock counts (found once, looked for again at most once a second while missing).</summary>
        private Worktown.Game.Shop.StockCounts StockCountsNow()
        {
            if (_stockCounts == null && Time.unscaledTime >= _nextStockCountsLookup)
            {
                _nextStockCountsLookup = Time.unscaledTime + 1f;
                _stockCounts = FindFirstObjectByType<Worktown.Game.Shop.StockCounts>();
            }
            return _stockCounts;
        }

        /// <summary>
        /// Spec 034: the target with the stock of its slot as this client sees it: the synced count, our own pending take or unpack first
        /// (Art.StockSlotLook), so a second click never acts on a count that already went.
        /// </summary>
        private AimTarget WithStock(AimTarget target, StockSlot slot)
        {
            if (slot == null || slot.Station == null) return target;
            int line = Worktown.Game.Shop.StockCatalog.LineOfSlot(slot.Station.Kind, slot.Option);
            if (line < 0) return target;
            int count = -1;
            Worktown.Game.Shop.StockCounts counts = StockCountsNow();
            if (counts != null && counts.TryGet(line, out byte synced)) count = synced;
            if (count >= 0 && slot.TryGetComponent(out Worktown.Game.Art.StockSlotLook look)) count = look.Shown(count);
            return target.WithStock(line, count);
        }

        /// <summary>Spec 034: a slot of the terminal: a row's order words and price, a button's words (the terminal's own page and packs).</summary>
        private AimTarget DescribeTerminal(StockSlot slot)
        {
            if (!slot.Station.TryGetComponent(out Worktown.Game.Shop.TerminalView view)) return AimTarget.TerminalSlot(slot.Option, null, -1);
            string words = view.WordsFor(slot.Option, out long price); // cached per page and pack count
            return AimTarget.TerminalSlot(slot.Option, words, price);
        }

        /// <summary>
        /// Spec 034: a terminal click: a row orders (RequestStation(terminal, TerminalRules.OrderOption(line, packs)), the beep and the
        /// row lit at the click, the server checks and charges); a button turns the page or the pack count at once (the client's view).
        /// </summary>
        private void TerminalClick(StockSlot slot)
        {
            if (slot == null || slot.Station == null || !slot.Station.TryGetComponent(out Worktown.Game.Shop.TerminalView view)) return;
            if (Worktown.Game.Shop.TerminalRules.IsRow(slot.Option))
            {
                int line = Worktown.Game.Shop.TerminalRules.LineAt(view.Page, slot.Option);
                if (line < 0) return;
                PlaySound(SoundKey.Beep, slot.Anchor);
                SendStation(slot.Station.Index, Worktown.Game.Shop.TerminalRules.OrderOption(line, view.Packs));
                view.PredictOrder(slot.Option, _lastRequest);
                _pendingTerminal = view;
                return;
            }
            PlaySound(SoundKey.Button, slot.Anchor);
            view.Press(slot.Option);
        }

        /// <summary>
        /// Spec 034: the carried box into its slot: RequestStation(station, the slot's unpack option) and the unpack look at the click (the
        /// box tips and shakes, the slot's samples pop in up to what fits); a refusal takes the slot's predicted count back with "nope".
        /// </summary>
        private void UnpackWithLook(StockSlot slot)
        {
            if (slot == null || slot.Station == null) return;
            int option = Worktown.Game.Shop.StockCatalog.UnpackOptionOf(slot.Station.Kind, slot.Option);
            if (option < 0) return;
            Pickup held = FindHeldPickup();
            Held hands = Describe(held);
            PlaySound(SoundKey.Unpack, slot.Anchor);
            SendStation(slot.Station.Index, option);
            AimTarget seen = WithStock(AimTarget.Nothing, slot);
            int room = Worktown.Game.Shop.StockCatalog.RoomFor(seen.StockLine, seen.StockCount);
            if (held != null && held.TryGetComponent(out Worktown.Game.Shop.SupplyBox box))
            {
                if (room >= hands.BoxUnits && box.Model != null)
                {
                    // Everything fits: the box's copy tips into the slot and shrinks away; the real box hides until the server empties it.
                    Pose from = new Pose(box.Model.transform.position, box.Model.transform.rotation);
                    float scale = 1f;
                    if (_heldView != null && _heldView.TryGetViewPose(held, out Pose root, out scale))
                    {
                        Vector3 local = held.transform.InverseTransformPoint(box.Model.transform.position);
                        from = new Pose(root.position + root.rotation * (local * scale), root.rotation);
                    }
                    Worktown.Game.Art.UnpackLook.Begin(box.Model, from, scale, slot.Anchor);
                    box.HideForUnpack(Mathf.Max(1f, FeelRules.ConfirmTimeout(FeelSettings.Current.ConfirmTimeout, RoundTripSeconds(),
                        TimeManager != null ? (float)TimeManager.TickDelta : 0f)));
                    _pendingUnpackBox = box;
                }
                else box.PlayUnpack(); // part of it fits: the box tips and shakes in the hands, the rest stays in it
            }
            if (slot.TryGetComponent(out Worktown.Game.Art.StockSlotLook look))
            {
                look.PredictUnpack(hands.BoxUnits, _lastRequest);
                _pendingStockLook = look;
            }
        }

        /// <summary>Spec 034, third person: the slot of <paramref name="station"/> that takes the carried box's good, or null.</summary>
        private static StockSlot SlotForBox(Station station, Held hands)
        {
            if (station == null || !hands.IsBox) return null;
            for (int s = 0; s < station.SlotCount; s++)
                if (Worktown.Game.Shop.StockCatalog.LineOfSlot(station.Kind, s) == hands.BoxLine && station.TryGetSlot(s, out StockSlot slot))
                    return slot;
            return null;
        }

        /// <summary>Spec 034, third person: the terminal's row nearest to the player (on its current page).</summary>
        private StockSlot NearestTerminalRow(Station terminal)
        {
            StockSlot best = null;
            float bestSqr = float.MaxValue;
            for (int r = 0; r < Worktown.Game.Shop.TerminalRules.Rows; r++)
            {
                if (!terminal.TryGetSlot(r, out StockSlot row) || row == null) continue;
                if (terminal.TryGetComponent(out Worktown.Game.Shop.TerminalView view) && Worktown.Game.Shop.TerminalRules.LineAt(view.Page, r) < 0) continue;
                float sqr = (row.Anchor - transform.position).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                best = row;
            }
            return best;
        }

        // Spec 034: the stock looks waiting for the server's answer to our request (one each: a take or unpack, an order).
        private Worktown.Game.Art.StockSlotLook _pendingStockLook;
        private Worktown.Game.Shop.TerminalView _pendingTerminal;
        private Worktown.Game.Shop.SupplyBox _pendingUnpackBox;

        /// <summary>Spec 034: the server answered request <paramref name="answered"/>: a refused take, unpack or order takes its look back, "nope".</summary>
        private void AnswerStockLooks(int answered, CookOutcome outcome)
        {
            bool ok = outcome == CookOutcome.Ok;
            if (_pendingStockLook != null && _pendingStockLook.Answer(answered, ok))
            {
                if (!ok && outcome != CookOutcome.TooFast) PlaySound(SoundKey.Nope, _pendingStockLook.transform.position);
                if (!ok && _pendingUnpackBox != null) _pendingUnpackBox.ShowAgain(); // a refused unpack: the box is still in the hands
                _pendingStockLook = null;
                _pendingUnpackBox = null;
            }
            if (_pendingTerminal != null && _pendingTerminal.Answer(answered, ok))
            {
                if (!ok && outcome != CookOutcome.TooFast) PlaySound(SoundKey.Nope, _pendingTerminal.transform.position);
                _pendingTerminal = null;
            }
        }

        /// <summary>Spec 034, the tours: a terminal row or button clicked through the normal path.</summary>
        public void DevTerminal(StockSlot slot) => TerminalClick(slot);

        /// <summary>Spec 034, the tours: the carried box unpacked into <paramref name="slot"/> through the normal path.</summary>
        public void DevUnpack(StockSlot slot) => UnpackWithLook(slot);

        // ---- Spec 032: the register ----

        // The request numbers of our register clicks still unanswered (fixed storage), and the view each predicted on.
        private readonly int[] _registerRequests = new int[8];
        private int _registerRequestCount;
        private Worktown.Game.Shop.RegisterView _registerView;

        /// <summary>Spec 032: a register slot as the rules see it: the head payer's packed short and the tray as this player sees it.</summary>
        private AimTarget DescribeRegister(StockSlot slot)
        {
            Station register = slot.Station;
            ushort payment = 0;
            int tray = 0;
            if (register.TryGetComponent(out Worktown.Game.Shop.Register till))
            {
                payment = till.Payment.Value;
                tray = till.Tray.Value;
            }
            if (register.TryGetComponent(out Worktown.Game.Shop.RegisterView view)) tray = view.ShownTray;
            return AimTarget.RegisterSlot(slot.Option, payment, tray);
        }

        /// <summary>
        /// Spec 032: a register click with its look now (feel.md 1): a coin or note clinks and lies in the tray at once, the card
        /// terminal beeps and shows "approved", take back and hand over sound; RequestStation(register, slot) goes out and the server
        /// decides. A refusal takes the predicted coin back out with "nope".
        /// </summary>
        private void PayWithLook(StockSlot slot)
        {
            Station register = slot.Station;
            if (register == null) return;
            register.TryGetComponent(out Worktown.Game.Shop.RegisterView view);
            float timeout = FeelRules.ConfirmTimeout(FeelSettings.Current.ConfirmTimeout, RoundTripSeconds(),
                TimeManager != null ? (float)TimeManager.TickDelta : 0f);
            if (Worktown.Game.Shop.RegisterRules.TryParseTray(slot.Option, out int value))
            {
                PlaySound(SoundKey.Clink, slot.Anchor);
                if (view != null) view.PredictAdd(value, Time.unscaledTime + timeout);
            }
            else if (slot.Option == Worktown.Game.Shop.RegisterRules.CardOption)
            {
                PlaySound(SoundKey.Beep, slot.Anchor);
                if (view != null) view.PredictCard(Time.unscaledTime + timeout);
            }
            else
            {
                PlaySound(slot.Option == Worktown.Game.Shop.RegisterRules.TakeBackOption ? SoundKey.Pick : SoundKey.Clink, slot.Anchor);
                if (view != null && slot.Option == Worktown.Game.Shop.RegisterRules.TakeBackOption) view.PredictTakeBack(Time.unscaledTime + timeout);
            }
            SendStation(register.Index, slot.Option);
            if (_registerRequestCount == _registerRequests.Length)
            {
                for (int i = 1; i < _registerRequests.Length; i++) _registerRequests[i - 1] = _registerRequests[i];
                _registerRequestCount--;
            }
            _registerRequests[_registerRequestCount++] = _lastRequest;
            _registerView = view;
        }

        /// <summary>Spec 032: the answer to request <paramref name="answered"/>; a refused register click undoes its predicted look.</summary>
        private void RegisterAnswered(int answered, bool ok)
        {
            if (answered == 0 || _registerRequestCount == 0) return;
            for (int i = 0; i < _registerRequestCount; i++)
            {
                if (_registerRequests[i] != answered) continue;
                for (int j = i + 1; j < _registerRequestCount; j++) _registerRequests[j - 1] = _registerRequests[j];
                _registerRequestCount--;
                if (!ok && _registerView != null)
                {
                    _registerView.Refused();
                    PlaySound(SoundKey.Nope, _registerView.transform.position);
                }
                return;
            }
        }

        /// <summary>Spec 031: a ray over the assembly board means the tool it passes within this distance of (m); else the board.</summary>
        private const float ToolAimRadius = 0.12f;

        /// <summary>
        /// Spec 039: the tool of the assembly board a place surface sits on (its back strip) that the ray passes, or null (any other
        /// surface, or no tool near the ray). 039 review: only the assembly board has a surface on it (the register and the drink
        /// dispenser have their tops beside them, not on them).
        /// </summary>
        private static StockSlot ToolNearRay(PlaceSurface surface, Ray ray, out float distance)
        {
            distance = float.PositiveInfinity;
            Station station = surface.GetComponentInParent<Station>();
            if (station == null || station.Kind != StationKind.Assembly) return null;
            return ToolOnRay(station, ray, out distance);
        }

        /// <summary>
        /// Spec 031: the tool of the assembly board <paramref name="board"/> the ray passes nearest (within <see cref="ToolAimRadius"/> of
        /// its anchor), or null. The tools have no colliders (they never block a ray), so the ray that aims at a bottle hits the
        /// board's top behind it; the distance to the ray, not to that hit point, says which tool is meant.
        /// </summary>
        private static StockSlot ToolOnRay(Station board, Ray ray) => ToolOnRay(board, ray, out _);

        /// <summary>Spec 039: <see cref="ToolOnRay(Station, Ray)"/> with how far the ray passes the tool's anchor (m).</summary>
        private static StockSlot ToolOnRay(Station board, Ray ray, out float distance)
        {
            distance = float.PositiveInfinity;
            if (board == null) return null;
            StockSlot best = null;
            float bestSqr = ToolAimRadius * ToolAimRadius;
            for (int option = 0; option < board.SlotCount; option++)
            {
                if (!board.TryGetSlot(option, out StockSlot slot)) continue;
                float sqr = Vector3.Cross(ray.direction, slot.Anchor - ray.origin).sqrMagnitude; // the direction is unit length
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                best = slot;
            }
            if (best != null) distance = Mathf.Sqrt(bestSqr);
            return best;
        }

        /// <summary>
        /// Spec 031: a tool on the assembly board as the rules see it: the dispenser's sauce or the packaging and its price, the
        /// board's byte, and the build as this client sees it (its layers, and its mode with our own pending step first).
        /// </summary>
        private AimTarget DescribeTool(StockSlot slot)
        {
            Vessel spot = BuildSpotOf(slot.Station, out Worktown.Game.Art.BuildStackView view);
            return DescribeTool(slot, spot, view);
        }

        /// <summary>031b review: <see cref="DescribeTool(StockSlot)"/> with the board's build spot and view already known.</summary>
        private AimTarget DescribeTool(StockSlot slot, Vessel spot, Worktown.Game.Art.BuildStackView view)
        {
            Station board = slot.Station;
            int tool = slot.Option;
            byte flags = board.TryGetComponent(out StationContents contents) ? contents.StoveFlags.Value : (byte)0;
            // 031b: our own squeeze's bit is no "busy" for us (the server answers our own repeated press with Ok).
            if (BuildRules.IsDispenser(tool) && OwnSqueeze(board.Index, tool)) flags = BuildRules.WithSqueezing(flags, tool, false);
            int layers = spot != null ? spot.ContentCount : 0;
            byte mode = view != null ? view.SeenMode : spot != null ? spot.Mode.Value : (byte)0;
            _prices ??= Worktown.Sim.Economy.EconomyCatalog.CreateDefault();
            string name;
            long price = -1;
            if (BuildRules.IsDispenser(tool))
            {
                string sauce = BuildRules.SauceOf(tool);
                int index = StockLayout.IngredientIndexOf(sauce);
                name = index >= 0 ? DemoIngredients.All[index].DisplayName : "sauce";
                if (_prices.TryGetGrocerPrice(sauce, out long p)) price = p;
            }
            else
            {
                int packaging = BuildRules.PackagingOf(tool);
                name = BuildRules.PackagingName(packaging);
                if (_prices.TryGetGrocerPrice(BuildRules.PackagingId(packaging), out long p)) price = p;
            }
            return AimTarget.AssemblyTool(tool, flags, name, price, layers, mode);
        }

        /// <summary>Spec 031: the build spot of an assembly board and its view, or null.</summary>
        private Vessel BuildSpotOf(Station board, out Worktown.Game.Art.BuildStackView view)
        {
            RefreshInteractables();
            view = null;
            for (int i = 0; i < _allAssemblySpots.Length; i++)
            {
                if (_allAssemblySpots[i] == null || _allAssemblySpots[i].Board != board) continue;
                Vessel spot = _assemblyVessels[i];
                if (spot != null) spot.TryGetComponent(out view);
                return spot;
            }
            return null;
        }

        /// <summary>
        /// Spec 031: holding left click on a dispenser squeezes (first person). The look starts at the press and ends at the
        /// release, or when the aim leaves the dispenser; the press and the release go out as RequestStation start and stop,
        /// each when the request interval allows (<see cref="SqueezeHold"/>: never swallowed). Returns true while the squeeze
        /// owns the left button (no click is made from it). 031b: the gamepad's right trigger holds it too.
        /// </summary>
        private bool UpdateSqueeze(Mouse mouse, Gamepad gamepad, bool locked)
        {
            StockSlot tool = Target as StockSlot;
            bool onDispenser = tool != null && tool.Station != null
                && ((CurrentAction == InteractionAction.Squeeze && BuildRules.IsDispenser(tool.Option))
                    || (CurrentAction == InteractionAction.Fill && DrinkRules.IsTap(tool.Option)) // spec 033: a drink tap
                    || ((CurrentAction == InteractionAction.Scrub || CurrentAction == InteractionAction.Wipe) && IsCleaningHold(tool)) // spec 035
                    || (CurrentAction == InteractionAction.Shave && IsSpitHold(tool))); // spec 036
            bool mouseDown = mouse != null && locked && mouse.leftButton.isPressed;
            bool padDown = gamepad != null && gamepad.rightTrigger.isPressed;
            bool pressedNow = (mouse != null && locked && _lockedLastFrame && mouse.leftButton.wasPressedThisFrame)
                || (gamepad != null && gamepad.rightTrigger.wasPressedThisFrame);
            return UpdateSqueezeWith(tool, onDispenser, mouseDown || padDown, pressedNow);
        }

        /// <summary>
        /// 031b: third person, holding E (or the gamepad's right trigger) at an assembly board squeezes its nearest dispenser while
        /// the rules offer it. 031b review: the squeeze starts only after <see cref="SqueezeHoldGate.HoldSeconds"/> of holding (the
        /// press, and so the measured time, from then on); a shorter press is a tap (<paramref name="tapped"/>: do the normal E action
        /// now), so E still picks up and drops at the board. Returns true while the gate owns the input (no E action from it).
        /// The board's nearest dispenser and its build spot are cached per target (no slot scan per frame).
        /// </summary>
        private bool UpdateThirdPersonSqueeze(Gamepad gamepad, bool leftDown, bool leftPressed, out bool tapped)
        {
            tapped = false;
            Station board = Target is Station s && (s.Kind == StationKind.Assembly || s.Kind == StationKind.DrinkDispenser
                || s.Kind == StationKind.Sink || s.Kind == StationKind.Spit) ? s : null; // spec 033; spec 035: the sink; spec 036: the spit
            bool needsSpot = board != null && board.Kind == StationKind.Assembly; // spec 033: the drink dispenser has no build spot
            // Spec 035: with the cloth in the hands at a dispenser with a spill, E is held on the spill (not on a tap).
            Held hands = Hands(FindHeldPickup());
            bool wipe = board != null && board.Kind == StationKind.DrinkDispenser && hands.IsCloth;
            if (board != _thirdPersonBoard || wipe != _thirdPersonWipe
                || (board != null && ((needsSpot && _thirdPersonSpot == null) || _thirdPersonTool == null)))
            {
                _thirdPersonBoard = board;
                _thirdPersonWipe = wipe;
                StockSlot spill = null;
                if (wipe) board.TryGetSlot(Worktown.Game.Shop.CleaningRules.SpillTool, out spill);
                StockSlot basin = null;
                if (board != null && board.Kind == StationKind.Sink) board.TryGetSlot(Worktown.Game.Shop.CleaningRules.BasinTool, out basin);
                if (board != null && board.Kind == StationKind.Spit) board.TryGetSlot(SpitRules.ConeTool, out basin); // spec 036: the cone
                _thirdPersonTool = board == null ? null : wipe ? spill : basin != null ? basin : NearestDispenser(board);
                _thirdPersonView = null;
                _thirdPersonSpot = needsSpot ? BuildSpotOf(board, out _thirdPersonView) : null;
            }
            bool drinks = board != null && board.Kind == StationKind.DrinkDispenser;
            // Spec 042: the tap held is the nearest one with a cup on its spot (kept while it pours).
            if (drinks && !wipe && !_squeeze.Held) _thirdPersonTool = BestTap(board);
            StockSlot tool = _thirdPersonTool;
            InteractionAction offeredAction = tool == null ? InteractionAction.None : InteractionRules.ActionFor(hands,
                IsCleaningHold(tool) || IsSpitHold(tool) ? Describe(tool) : drinks ? DescribeDrinkTool(tool) : DescribeTool(tool, _thirdPersonSpot, _thirdPersonView));
            bool offered = offeredAction == InteractionAction.Squeeze || offeredAction == InteractionAction.Fill // spec 033: a tap too
                || offeredAction == InteractionAction.Scrub || offeredAction == InteractionAction.Wipe // spec 035
                || offeredAction == InteractionAction.Shave; // spec 036
            // Spec 041: the left button holds like E in third person.
            bool down = GameInput.Held(ControlAction.Use) || (gamepad != null && gamepad.rightTrigger.isPressed) || leftDown;
            bool pressedNow = GameInput.Pressed(ControlAction.Use)
                || (gamepad != null && gamepad.rightTrigger.wasPressedThisFrame) || leftPressed;
            if (_devSqueeze) return PollSqueeze();
            SqueezeHoldGate.Step step = _thirdPersonGate.Update(offered, down, pressedNow, Time.unscaledTimeAsDouble);
            tapped = step == SqueezeHoldGate.Step.Tap;
            UpdateSqueezeWith(tool, _thirdPersonGate.Squeezing, down, step == SqueezeHoldGate.Step.Start);
            return _thirdPersonGate.OwnsInput || _squeeze.Held;
        }

        // 031b review: third person's cached board, its nearest dispenser and build spot (refreshed when the target changes).
        private readonly SqueezeHoldGate _thirdPersonGate = new SqueezeHoldGate();
        private Station _thirdPersonBoard;
        private bool _thirdPersonWipe; // spec 035: the cloth was in the hands when the tool was picked
        private StockSlot _thirdPersonTool;
        private Vessel _thirdPersonSpot;
        private Worktown.Game.Art.BuildStackView _thirdPersonView;

        /// <summary>031b: the dispenser of <paramref name="board"/> nearest to the player, or null.</summary>
        private StockSlot NearestDispenser(Station board)
        {
            RefreshInteractables();
            StockSlot best = null;
            float bestSqr = float.MaxValue;
            bool drinks = board != null && board.Kind == StationKind.DrinkDispenser; // spec 033: its nearest tap
            foreach (StockSlot slot in _allSlots)
            {
                if (slot == null || slot.Station != board || !(drinks ? DrinkRules.IsTap(slot.Option) : BuildRules.IsDispenser(slot.Option))) continue;
                float sqr = (slot.transform.position - transform.position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = slot;
                }
            }
            return best;
        }

        /// <summary>The held squeeze for this frame: <paramref name="down"/> is the use input held, <paramref name="pressedNow"/>
        /// its press this frame, <paramref name="onDispenser"/> the rules offer a squeeze on <paramref name="tool"/>.</summary>
        private bool UpdateSqueezeWith(StockSlot tool, bool onDispenser, bool down, bool pressedNow)
        {
            if (_devSqueeze) return PollSqueeze(); // spec 031: a tour holds the dispenser (DevSqueezePress / DevSqueezeRelease)
            double now = Time.unscaledTimeAsDouble;
            if (_squeeze.Held)
            {
                bool same = onDispenser && tool.Station.Index == _squeeze.Board && tool.Option == _squeeze.Dispenser;
                if (!down || !same)
                {
                    _squeeze.Release(now);
                    if (_squeezeView != null) _squeezeView.Predict(false);
                    StopPourLook(true); // spec 033: the cup comes back from under the tap, its level stays
                    StopShaveLook(); // spec 036: the knife comes back
                }
            }
            else if (onDispenser && pressedNow && _squeeze.Press(tool.Station.Index, tool.Option, now))
            {
                _squeezeView = tool.GetComponent<Worktown.Game.Art.IPourView>();
                if (_squeezeView != null) _squeezeView.Predict(true); // the look in the press's frame
                if (tool.Station.Kind == StationKind.DrinkDispenser && DrinkRules.IsTap(tool.Option)) StartPourLook(tool, now); // spec 033
                if (tool.Station.Kind == StationKind.DrinkDispenser && tool.Option == Worktown.Game.Shop.CleaningRules.SpillTool)
                    _wipeView = tool.GetComponent<Worktown.Game.Art.SpillWipeView>(); // spec 035: the held cloth goes to the puddle
                if (tool.Station.Kind == StationKind.Sink) PlaySound(SoundKey.Splash, tool.Anchor); // spec 035: the hands into the water
                if (IsSpitHold(tool)) StartShaveLook(tool); // spec 036: the knife to the cone
            }

            PollSqueeze();
            return _squeeze.Held || onDispenser;
        }

        // ---- Spec 033: the drink dispenser (owner side) ----

        /// <summary>Spec 033: a tool of the drink dispenser as the rules see it: the dispenser's byte with our own pour's bit cleared.</summary>
        private AimTarget DescribeDrinkTool(StockSlot slot)
        {
            Station dispenser = slot.Station;
            int tool = slot.Option;
            byte flags = dispenser.TryGetComponent(out StationContents contents) ? contents.StoveFlags.Value : (byte)0;
            if (DrinkRules.IsTap(tool) && OwnSqueeze(dispenser.Index, tool)) flags = DrinkRules.WithPouring(flags, tool, false);
            // Spec 035: our own wipe is not "someone wiping"; a spill we just wiped is gone for us already.
            if (tool == Worktown.Game.Shop.CleaningRules.SpillTool)
            {
                if (OwnSqueeze(dispenser.Index, tool)) flags = (byte)(flags & ~Worktown.Game.Shop.CleaningRules.WipingBit);
                if (slot.TryGetComponent(out Worktown.Game.Art.SpillWipeView wipe) && wipe.SeenWiped) flags = DrinkRules.WithSpill(flags, false);
            }
            // Spec 042: a tap pours into the cup on its spot; the lid stack lids the first filled open cup on a spot (or the one in the hands).
            byte spot = DrinkRules.IsTap(tool) ? SpotByteOf(SpotCup(dispenser, tool))
                : tool == DrinkRules.LidTool ? SpotByteOf(LiddableSpotCup(dispenser, flags)) : DrinkRules.NoSpotCup;
            return AimTarget.DrinkTool(tool, flags, DrinkRules.IsTap(tool) ? DrinkRules.DrinkName(tool) : tool == DrinkRules.CupTool ? "a cup" : "a lid", -1,
                spot);
        }

        /// <summary>
        /// Spec 033: our press on a drink tap: the liquid starts rising in this frame, from what we see in the cup (docs/feel.md 1); the
        /// server measures the real pour. Spec 042: the cup is the one standing on that tap's spot (the tap's view draws its level and the
        /// stream into it every frame), the hands stay free.
        /// </summary>
        private void StartPourLook(StockSlot tap, double now)
        {
            StopPourLook(true);
            _pourTap = tap.GetComponent<Worktown.Game.Art.DrinkTapView>();
            _pourCup = SpotCup(tap.Station, tap.Option);
            _lastPourCup = _pourCup;
            float start = 0f;
            if (_pourCup != null)
            {
                Held seen = DescribeCup(_pourCup, _pourCup.Look.Value);
                start = _pourCup.Visual != null && _pourCup.Visual.PredictedMl >= 0f ? _pourCup.Visual.PredictedMl : DrinkRules.SeenMl(seen.CupLevel);
                if (_pourCup.Visual != null) _pourCup.Visual.SetPredictedTap(tap.Option);
            }
            if (_pourTap != null) _pourTap.PredictFrom(start, now, _pourCup);
        }

        /// <summary>Our pour's look ends; <paramref name="keepLevel"/> keeps the cup's predicted level (a release), else (a refusal) it goes
        /// back to the synced one.</summary>
        private void StopPourLook(bool keepLevel)
        {
            if (_pourTap != null && _pourCup != null && _pourCup.Visual != null)
            {
                if (keepLevel) _pourCup.Visual.PredictMl(_pourTap.PredictedMl(Time.unscaledTimeAsDouble), false);
                else _pourCup.Visual.EndPrediction();
            }
            _pourTap = null;
            _pourCup = null;
        }

        /// <summary>
        /// Spec 033: the lid stack: the lid snaps onto the cup at the click (sound and look), RequestStation goes out. Spec 042: the cup in
        /// the hands, else the first filled open cup on a spot (the server's order).
        /// </summary>
        private void LidWithLook(Station dispenser)
        {
            Pickup held = FindHeldPickup();
            Food cup = held != null && held.TryGetComponent(out Food inHands) && FoodLookRules.IsCup(inHands.Look.Value) ? inHands : null;
            if (cup == null) cup = LiddableSpotCup(dispenser, DispenserFlags(dispenser));
            _lidCup = cup;
            PlaySound(SoundKey.LidClick, cup != null ? cup.transform.position : dispenser.transform.position);
            if (_lidCup != null && _lidCup.Visual != null) _lidCup.Visual.PredictLid(true);
            SendStation(dispenser.Index, DrinkRules.LidOption);
            _lidRequest = _lastRequest;
        }

        // ---- Spec 042: the cup spots on the drip tray (owner side) ----

        /// <summary>A ray over the dispenser means the cup on a spot (or the free spot) it passes within this distance of (m).</summary>
        private const float CupAimRadius = 0.08f;
        /// <summary>How long our own place on a cup spot counts as standing there before the server's answer (s).</summary>
        private const float PlacedCupSeconds = 2f;

        private readonly List<PlaceSurface> _cupSpotList = new List<PlaceSurface>();
        private PlaceSurface[] _cupSpotsFrom;
        private readonly Collider[] _cupProbe = new Collider[8];
        private readonly Food[] _spotCupSeen = new Food[DrinkRules.TapCount];
        private readonly bool[] _spotCupKnown = new bool[DrinkRules.TapCount];
        private int _spotCupFrame = -1;
        private int _spotCupStation = -1;
        private Pickup _placedCup;
        private PlaceSurface _placedSpot;
        private float _placedUntil;

        /// <summary>The cup spot of tap <paramref name="tap"/> of <paramref name="dispenser"/>, or null.</summary>
        private PlaceSurface CupSpotOf(Station dispenser, int tap)
        {
            if (dispenser == null || !DrinkRules.IsTap(tap)) return null;
            EnsureCupSpots();
            for (int i = 0; i < _cupSpotList.Count; i++)
            {
                PlaceSurface spot = _cupSpotList[i];
                if (spot != null && spot.DispenserIndex == dispenser.Index && spot.CupTap == tap) return spot;
            }
            return null;
        }

        /// <summary>The scene's cup spots, found again only when the place surfaces were looked up again.</summary>
        private void EnsureCupSpots()
        {
            // 042 review: the cache first; the full refresh only when it was never built or one of its cup spots is gone.
            if (_cupSpotsFrom != null && _cupSpotsFrom == _allSurfaces && !CupSpotMissing()) return;
            RefreshInteractables();
            if (_cupSpotsFrom == _allSurfaces && !CupSpotMissing()) return;
            _cupSpotsFrom = _allSurfaces;
            _cupSpotList.Clear();
            foreach (PlaceSurface surface in _allSurfaces)
                if (surface != null && surface.IsCupSpot) _cupSpotList.Add(surface);
        }

        private bool CupSpotMissing()
        {
            for (int i = 0; i < _cupSpotList.Count; i++)
                if (_cupSpotList[i] == null) return true;
            return false;
        }

        /// <summary>
        /// The cup standing on tap <paramref name="tap"/>'s spot as this client sees it (the item lying there, or our own place on its way
        /// for <see cref="PlacedCupSeconds"/>; an item flying away is gone), or null. One probe per spot and frame.
        /// </summary>
        private Food SpotCup(Station dispenser, int tap)
        {
            PlaceSurface spot = CupSpotOf(dispenser, tap);
            if (spot == null) return null;
            if (_spotCupFrame != Time.frameCount || _spotCupStation != dispenser.Index)
            {
                _spotCupFrame = Time.frameCount;
                _spotCupStation = dispenser.Index;
                for (int t = 0; t < _spotCupKnown.Length; t++) _spotCupKnown[t] = false;
            }
            if (_spotCupKnown[tap]) return _spotCupSeen[tap];
            _spotCupKnown[tap] = true;
            Pickup item = PickupService.OccupantAt(spot.SpotWorldPosition(0), _cupProbe);
            if (item != null && item != _placedCup && item.HasPrediction) item = null; // our own take: on its way to the hands
            if (item == null && _placedCup != null && _placedSpot == spot && Time.unscaledTime < _placedUntil && _placedCup.IsSpawned
                && _placedCup.HasRestPrediction) item = _placedCup;
            Food cup = item != null && item.TryGetComponent(out Food food) && FoodLookRules.IsCup(food.Look.Value) ? food : null;
            _spotCupSeen[tap] = cup;
            return cup;
        }

        /// <summary>A cup as the rules see it in one byte (DrinkRules.SpotByte; our own pour's level and lid first), or no cup.</summary>
        private static byte SpotByteOf(Food cup)
        {
            if (cup == null) return DrinkRules.NoSpotCup;
            Held seen = DescribeCup(cup, cup.Look.Value);
            return DrinkRules.SpotByte(true, seen.CupLidded, seen.CupTap, seen.CupLevel);
        }

        /// <summary>The dispenser's synced byte as this client sees it.</summary>
        private static byte DispenserFlags(Station dispenser) =>
            dispenser != null && dispenser.TryGetComponent(out StationContents contents) ? contents.StoveFlags.Value : (byte)0;

        /// <summary>
        /// The cup a click on the lid stack lids when the hands hold no cup, as the server picks it (KitchenState.LiddableSpotCup): the
        /// first tap's filled open cup nobody pours into; else the first cup on a spot (for its reason), else null.
        /// </summary>
        private Food LiddableSpotCup(Station dispenser, byte flags)
        {
            // 042 review: the one pure rule (DrinkRules.LiddableTap), fed with the cups this client sees.
            int tap = DrinkRules.LiddableTap(t => SpotByteOf(SpotCup(dispenser, t)),
                t => DrinkRules.IsPouring(flags, t) || _squeeze.Involves(dispenser.Index, t));
            if (tap >= 0) return SpotCup(dispenser, tap);
            for (int t = 0; t < DrinkRules.TapCount; t++)
            {
                Food cup = SpotCup(dispenser, t);
                if (cup != null) return cup;
            }
            return null;
        }

        /// <summary>A place surface as the rules see it: a tap's cup spot says which tap (spec 042).</summary>
        private static AimTarget SpotTarget(PlaceSurface surface, int spot) =>
            surface.IsCupSpot ? AimTarget.CupSpot(spot, surface.CupTap) : AimTarget.Spot(spot, surface.IsBurner);

        /// <summary>Whether <paramref name="hands"/> may go on <paramref name="surface"/> (only a fitting cup on a tap's cup spot).</summary>
        private static bool SpotFits(PlaceSurface surface, Held hands) =>
            !surface.IsCupSpot || DrinkRules.CanStand(hands.IsCup, hands.CupLidded, hands.CupTap, hands.CupLevel, surface.CupTap) == CookOutcome.Ok;

        /// <summary>Whether <paramref name="pickup"/> is a cup on a tap's spot that someone else pours into (the tap's bit, not our own pour).</summary>
        private bool IsBusySpotCup(Pickup pickup, Food food)
        {
            if (!FoodLookRules.IsCup(food.Look.Value)) return false;
            EnsureCupSpots();
            for (int i = 0; i < _cupSpotList.Count; i++)
            {
                PlaceSurface spot = _cupSpotList[i];
                if (spot == null) continue;
                Vector3 offset = pickup.transform.position - spot.SpotWorldPosition(0);
                offset.y = 0f;
                if (offset.sqrMagnitude > 0.05f * 0.05f) continue;
                Station dispenser = StationAt(spot.DispenserIndex);
                return dispenser != null && DrinkRules.IsPouring(DispenserFlags(dispenser), spot.CupTap) && !OwnSqueeze(dispenser.Index, spot.CupTap);
            }
            return false;
        }

        /// <summary>The station with index <paramref name="index"/> as this client knows it, or null.</summary>
        private Station StationAt(int index)
        {
            if (_stations == null) _stations = FindObjectsByType<Station>(FindObjectsSortMode.None);
            foreach (Station s in _stations)
                if (s != null && s.Index == index) return s;
            return null;
        }

        /// <summary>
        /// The exact ray hit the drink dispenser's body (its taps and tray lie inside its aim box): with empty hands a cup standing on a
        /// spot the ray passes (closer than any tool) is the target; holding a cup, the free spot under the tap it aims at (the tap, or
        /// the spot itself). True when it added the hit; false leaves the tools' rule (a tap pours into its spot's cup).
        /// </summary>
        private bool AddDrinkSpotHit(Station dispenser, Ray ray, Pickup held, float distance, Vector3 point)
        {
            StockSlot tool = ToolOnRay(dispenser, ray, out float toolDistance);
            int nearest = -1;
            float best = CupAimRadius * CupAimRadius;
            for (int t = 0; t < DrinkRules.TapCount; t++)
            {
                PlaceSurface spot = CupSpotOf(dispenser, t);
                if (spot == null) continue;
                Vector3 anchor = spot.SpotWorldPosition(0) + Vector3.up * (Worktown.Game.Art.DrinkArtRules.CupHeight * 0.5f);
                float sqr = Vector3.Cross(ray.direction, anchor - ray.origin).sqrMagnitude;
                if (sqr >= best) continue;
                best = sqr;
                nearest = t;
            }
            bool nearCup = nearest >= 0 && Mathf.Sqrt(best) < toolDistance;
            if (held == null)
            {
                Food cup = nearCup ? SpotCup(dispenser, nearest) : null;
                if (cup == null || !cup.TryGetComponent(out Pickup item)) return false;
                _aimHits.Add(new AimHit(distance, AimKind.Pickup, item.transform.position));
                _aimSources.Add(item);
                _aimPoints.Add(AimAnchor(item)); // a hit on the body in front of the cup means the cup itself
                return true;
            }
            if (!Hands(held).IsCup) return false;
            int tap = nearCup ? nearest : tool != null && DrinkRules.IsTap(tool.Option) ? tool.Option : -1;
            PlaceSurface free = tap >= 0 && SpotCup(dispenser, tap) == null ? CupSpotOf(dispenser, tap) : null;
            if (free == null) return false;
            _aimHits.Add(AimHit.Spot(distance, free.SpotWorldPosition(0), free.Index, 0));
            _aimSources.Add(free);
            _aimPoints.Add(point);
            return true;
        }

        /// <summary>Third person: the free cup spot nearest the player that the held cup fits (DrinkRules.CanStand), or null.</summary>
        private PlaceSurface NearestFreeCupSpot(Station dispenser, Held hands)
        {
            PlaceSurface best = null;
            float bestSqr = float.MaxValue;
            for (int t = 0; t < DrinkRules.TapCount; t++)
            {
                PlaceSurface spot = CupSpotOf(dispenser, t);
                if (spot == null || SpotCup(dispenser, t) != null || !SpotFits(spot, hands)) continue;
                float sqr = (spot.SpotWorldPosition(0) - transform.position).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                best = spot;
            }
            return best;
        }

        /// <summary>Third person: the tap to hold, the nearest one with a cup it can pour into on its spot, else the nearest tap.</summary>
        private StockSlot BestTap(Station dispenser)
        {
            byte flags = DispenserFlags(dispenser);
            StockSlot best = null, nearest = null;
            float bestSqr = float.MaxValue, nearestSqr = float.MaxValue;
            for (int t = 0; t < DrinkRules.TapCount; t++)
            {
                if (!dispenser.TryGetSlot(t, out StockSlot slot) || slot == null) continue;
                float sqr = (slot.transform.position - transform.position).sqrMagnitude;
                if (sqr < nearestSqr)
                {
                    nearestSqr = sqr;
                    nearest = slot;
                }
                byte seen = OwnSqueeze(dispenser.Index, t) ? DrinkRules.WithPouring(flags, t, false) : flags;
                if (DrinkRules.CanPourOnSpot(SpotByteOf(SpotCup(dispenser, t)), t, seen) != CookOutcome.Ok || sqr >= bestSqr) continue;
                bestSqr = sqr;
                best = slot;
            }
            return best ?? nearest;
        }

        /// <summary>The station kind of station index <paramref name="index"/> as this client knows it (Crate when unknown).</summary>
        private StationKind StationKindOf(int index)
        {
            if (_stations == null) _stations = FindObjectsByType<Station>(FindObjectsSortMode.None);
            foreach (Station s in _stations)
                if (s != null && s.Index == index) return s.Kind;
            return StationKind.Crate;
        }

        private bool _devSqueeze;
        // 031b: the request number of the last squeeze start and its dispenser's view (a refusal ends the predicted ribbon at once).
        private int _squeezeStartRequest;
        private Worktown.Game.Art.IPourView _squeezeStartView;
        private bool _squeezeStartIsPour; // 033 review: the start was a drink tap's (only then does a refusal undo the cup's level)
        // 031b: our own stop went out for this dispenser; until the board's byte catches up its bit is still ours.
        private int _ownStopBoard = -1;
        private int _ownStopDispenser = -1;
        private float _ownStopUntil;

        /// <summary>Spec 031: sends the squeeze's start or stop when the request interval allows (031b: the stop as long after the
        /// release as the start went out after the press, so the server measures the hold).</summary>
        private bool PollSqueeze()
        {
            bool canSend = Time.unscaledTime - _lastSendTime >= _clientInterval;
            switch (_squeeze.Poll(canSend, Time.unscaledTimeAsDouble))
            {
                case SqueezeHold.Send.Start:
                    // Spec 033: a drink tap's press goes out as its own option; spec 035: the sink's scrub and the spill's wipe too.
                    _squeezeStartIsPour = StationKindOf(_squeeze.SendBoard) == StationKind.DrinkDispenser && DrinkRules.IsTap(_squeeze.SendDispenser);
                    SendStation(_squeeze.SendBoard, HoldOption(_squeeze.SendBoard, _squeeze.SendDispenser, true));
                    _squeezeStartRequest = _lastRequest;
                    _squeezeStartView = DispenserView(_squeeze.SendBoard, _squeeze.SendDispenser);
                    break;
                case SqueezeHold.Send.Stop:
                    bool drinkStop = StationKindOf(_squeeze.SendBoard) == StationKind.DrinkDispenser && DrinkRules.IsTap(_squeeze.SendDispenser);
                    SendStation(_squeeze.SendBoard, HoldOption(_squeeze.SendBoard, _squeeze.SendDispenser, false));
                    if (drinkStop) _pourStopRequest = _lastRequest; // spec 033: a refused stop (no money, cup put down) undoes the level
                    _ownStopBoard = _squeeze.SendBoard;
                    _ownStopDispenser = _squeeze.SendDispenser;
                    _ownStopUntil = Time.unscaledTime + Mathf.Max(1f, 2f * RoundTripSeconds());
                    if (_devSqueeze && _squeeze.Idle) _devSqueeze = false;
                    break;
            }
            UpdateWipeLook(); // spec 035: and of our own wipe
            return _squeeze.Held;
        }

        /// <summary>
        /// The RequestStation option of a held tool's press (<paramref name="start"/>) or release: a sauce dispenser (031), a drink tap (033),
        /// the sink's scrub or the spill's wipe (spec 035).
        /// </summary>
        private int HoldOption(int station, int tool, bool start)
        {
            StationKind kind = StationKindOf(station);
            if (kind == StationKind.Sink)
                return start ? Worktown.Game.Shop.CleaningRules.ScrubStartOption : Worktown.Game.Shop.CleaningRules.ScrubStopOption;
            if (kind == StationKind.DrinkDispenser && tool == Worktown.Game.Shop.CleaningRules.SpillTool)
                return start ? Worktown.Game.Shop.CleaningRules.WipeStartOption : Worktown.Game.Shop.CleaningRules.WipeStopOption;
            if (kind == StationKind.DrinkDispenser) return start ? DrinkRules.PourStartOption(tool) : DrinkRules.PourStopOption(tool);
            if (kind == StationKind.Spit) return start ? SpitRules.ShaveStartOption : SpitRules.ShaveStopOption; // spec 036
            return start ? BuildRules.SqueezeStartOption(tool) : BuildRules.SqueezeStopOption(tool);
        }

        /// <summary>031b: the board's squeezing bit for <paramref name="dispenser"/> is our own squeeze (held, waiting, or its stop
        /// still on its way), so the busy rule never refuses it to us.</summary>
        private bool OwnSqueeze(int board, int dispenser)
        {
            if (_squeeze.Involves(board, dispenser)) return true;
            return board == _ownStopBoard && dispenser == _ownStopDispenser && Time.unscaledTime < _ownStopUntil;
        }

        /// <summary>031b: the view of dispenser <paramref name="dispenser"/> of board <paramref name="board"/>, or null (spec 033: a tap too).</summary>
        private Worktown.Game.Art.IPourView DispenserView(int board, int dispenser)
        {
            RefreshInteractables();
            foreach (StockSlot slot in _allSlots)
                if (slot != null && slot.Station != null && slot.Station.Index == board && slot.Option == dispenser)
                    return slot.GetComponent<Worktown.Game.Art.IPourView>();
            return null;
        }

        /// <summary>
        /// Spec 031: a fold (the spot) or a packaging (a rack tool) with its look now: the stack shows the new mode at the click
        /// (the fold closes, the paper goes round, the box comes down), judged like any predicted look: confirmed when the
        /// spot's synced mode moves on, a refusal or the timeout turns it back with "nope".
        /// </summary>
        private void BuildStepWithLook(Vessel spot, byte mode, int option, bool viaStation, Station board)
        {
            if (spot == null) return;
            if (viaStation) SendStation(board.Index, option);
            else SendUse(spot.NetworkObject, VesselRules.ActionFold);
            if (!spot.TryGetComponent(out Worktown.Game.Art.BuildStackView view)) return;
            float timeout = FeelRules.ConfirmTimeout(FeelSettings.Current.ConfirmTimeout, RoundTripSeconds(),
                TimeManager != null ? (float)TimeManager.TickDelta : 0f);
            int token = view.PredictStep(mode, timeout);
            if (_snapshotOf == null) _snapshotOf = Snapshot;
            var pending = new PendingLook { Build = view, BuildToken = token, ClickFrame = Time.frameCount };
            // 031b (review of 031): the target bits in the start, so only this step's bits confirm it; each prediction is its own
            // entry (keyed by itself), so a fold and the paper right after it are judged apart.
            _looks.Add(pending, pending, new FeelPrediction(PredictionKind.BuildStep, OwnerId,
                FeelRules.BuildStepStart(Snapshot(pending), mode & 0x0F), Time.unscaledTime, timeout), _lastRequest);
        }

        /// <summary>A crate or (spec 028b) a stock slot of ingredient <paramref name="i"/>: its name ("red cabbage") and grocer price.</summary>
        private AimTarget DescribeStock(int i)
        {
            if (i < 0 || i >= DemoIngredients.All.Count) return AimTarget.Crate(null, -1);
            string id = DemoIngredients.All[i].Id;
            _prices ??= Worktown.Sim.Economy.EconomyCatalog.CreateDefault();
            return AimTarget.Crate(DemoIngredients.All[i].DisplayName, _prices.TryGetGrocerPrice(id, out long price) ? price : -1);
        }

        /// <summary>Third person: what E does (spec 007/021), sent through the click buffer (spec 027b review).</summary>
        private void HandleThirdPersonKey(bool holding)
        {
            // Spec 007/021: near a crate, the counter or the plate shelf, E goes to the kitchen.
            Station station = Target as Station;
            if (station != null && TryHandleStationKey(station, holding)) return;

            if (holding)
            {
                DropWithLook(FindHeldPickup());
                return;
            }
            Pickup nearest = FindNearestFreePickup();
            if (nearest == null)
            {
                SetLocalNote("nothing to pick up within " + ClientSearchRadius + " m");
                PlaySound(SoundKey.Nope, transform.position);
            }
            else
                PickUpWithLook(nearest);
        }

        private void SetLocalNote(string note)
        {
            LastCookOutcome = null;
            LastPickupOutcome = null;
            LastPickupWasPlace = false;
            LastResult = note;
        }

        /// <summary>
        /// Owner side, spec 018: what the click (first person) or E (third person) acts on. First person: the
        /// <see cref="Station"/>, spec 019 <see cref="StationButton"/>, spec 021 <see cref="Vessel"/>, free
        /// <see cref="Pickup"/> or spec 020 <see cref="PlaceSurface"/> under the crosshair (nothing in between; beyond
        /// reach only with <see cref="TargetTooFar"/>, then nothing can be done with it), else null. Third person: the
        /// nearest station within reach, as before.
        /// </summary>
        public Component Target { get; private set; }

        /// <summary>Owner side: the target station, for keys and the overlay prompt. Null if none. Kept as an
        /// alias of <see cref="Target"/> so older readers keep working.</summary>
        public Station NearestStation => Target as Station;

        /// <summary>
        /// Spec 053, owner side: what the dot (first person) or the nearest station (third person) is on is closed at this stage of the
        /// climb (ShopStage.IsClosed, the server's own test), so nothing is offered and the prompt shows ShopStageRules.NotOpenText.
        /// </summary>
        public bool AimClosed { get; private set; }

        private Worktown.Game.Climb.ShopStage _shopStage;
        private float _nextStageLookup;

        /// <summary>Spec 053: the stage this client sees (the synced ShopStage, looked up once a second until found).</summary>
        private byte StageSeen()
        {
            if (_shopStage == null && Time.unscaledTime >= _nextStageLookup)
            {
                _nextStageLookup = Time.unscaledTime + 1f;
                _shopStage = Worktown.Game.Climb.ShopStage.Find();
            }
            return _shopStage != null ? _shopStage.StageNow : Worktown.Game.Climb.ShopStageRules.OpenKitchen;
        }

        private bool TargetClosed()
        {
            byte stage = StageSeen();
            return stage != Worktown.Game.Climb.ShopStageRules.OpenKitchen && Target != null
                && Worktown.Game.Climb.ClosedTargets.IsClosed(stage, Target);
        }

        /// <summary>Owner side: true while the local camera is in first person.</summary>
        public bool IsFirstPerson => _rig != null && _rig.IsFirstPerson;

        /// <summary>
        /// Spec 018: one ray from the camera centre, our own colliders and triggers ignored. Spec 020: the top of a
        /// place surface is a target too (with its nearest free spot to the hit point); with full hands pickups do not
        /// block the ray (nothing could be picked up), so the spot or station behind them is found. Spec 021: cookware
        /// lying somewhere is always a target (food goes into it, a pan pours into it).
        /// Spec 027b (docs/feel.md 3): the exact ray hit wins when it lands on an object's body (a station, button or place
        /// surface always; an item within <see cref="DirectHitRadius"/> of its centre). Otherwise every interactable whose
        /// centre (or free spot) lies within FeelSettings.AimCone of the ray, within reach and in sight, competes on
        /// FeelRules.PickTarget (angle, a bonus for cookware that takes what you hold, the current target sticky); only
        /// targets a click can act on count there. Without any, the exact ray result stays (with its "Too far" or reason).
        /// </summary>
        private Component FindAimTarget(Pickup held)
        {
            bool holding = held != null;
            Component exact = FindExactTarget(held, out bool direct, out Ray ray);
            int exactSpot = TargetSpot;
            Vector3 exactSpotPosition = TargetSpotPosition;
            bool exactTooFar = TargetTooFar;

            // A direct exact hit goes through FeelRules.PickTarget as a Direct candidate (it always wins there).
            Component coned = FindConeTarget(ray, held, direct ? exact : null, exactSpot, exactSpotPosition);
            if (coned != null) return Stick(coned);
            TargetSpot = exactSpot;
            TargetSpotPosition = exactSpotPosition;
            TargetTooFar = exactTooFar;
            return Stick(exact);
        }

        // ---- Spec 041: where a right click puts the held item down ----

        private void ClearRayPutDown()
        {
            _rayPutDownSurface = null;
            _rayPutDownSpot = PlaceSpots.None;
            _rayPutDownPosition = default;
        }

        /// <summary>041 review: a wall (<paramref name="station"/> null) or a station's body or button on this frame's ray.</summary>
        private void AddRayBlocker(float distance, Station station)
        {
            _rayBlockDistances.Add(distance);
            _rayBlockStations.Add(station);
        }

        /// <summary>
        /// 041 review: something solid lies on the ray in front of the ray put-down's surface (AimRules.BlocksRayPutDown): a wall, or a
        /// station the surface does not belong to. The right click then does not put down through it, as the left click does not.
        /// </summary>
        private bool RayPutDownBlocked(float putDownDistance)
        {
            for (int i = 0; i < _rayBlockDistances.Count; i++)
            {
                Station station = _rayBlockStations[i];
                bool owns = station != null && _rayPutDownSurface.transform.IsChildOf(station.transform);
                if (AimRules.BlocksRayPutDown(station != null ? AimKind.Station : AimKind.Other, _rayBlockDistances[i], putDownDistance, owns))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Spec 041, first person with something in the hands: the click's own spot when the click places; else the free spot nearest
        /// where the aim ray meets a surface (behind cookware or a station on it, never through a wall); else the free spot nearest
        /// the ray in the aim cone (in sight and reach). None of them: the right click drops at the feet.
        /// </summary>
        private void UpdatePutDown(bool firstPerson, Pickup held)
        {
            PutDownSurface = null;
            PutDownSpot = PlaceSpots.None;
            PutDownPosition = default;
            if (!firstPerson || held == null) return;
            if (CurrentAction == InteractionAction.Place && Target is PlaceSurface aimed && TargetSpot >= 0)
                SetPutDown(aimed, TargetSpot, TargetSpotPosition);
            else if (_rayPutDownSurface != null)
                SetPutDown(_rayPutDownSurface, _rayPutDownSpot, _rayPutDownPosition);
            else if (BestConeSpot(out PlaceSurface surface, out int spot, out Vector3 position))
                SetPutDown(surface, spot, position);
        }

        private void SetPutDown(PlaceSurface surface, int spot, Vector3 position)
        {
            PutDownSurface = surface;
            PutDownSpot = spot;
            PutDownPosition = position;
        }

        /// <summary>Spec 041: the free spot of this frame's aim cone nearest the ray that is in sight (the cone checks reach), if any.</summary>
        private bool BestConeSpot(out PlaceSurface surface, out int spot, out Vector3 position)
        {
            surface = null;
            spot = PlaceSpots.None;
            position = default;
            if (!_coneBuilt) return false;
            Camera cam = Camera.main;
            if (cam == null) return false;
            float best = float.PositiveInfinity;
            for (int i = 0; i < _coneSources.Count; i++)
            {
                if (!(_coneSources[i] is PlaceSurface s) || _coneSpots[i] < 0 || _cone[i].Angle >= best) continue;
                if (!InSight(cam.transform.position, _coneAnchors[i], s)) continue;
                best = _cone[i].Angle;
                surface = s;
                spot = _coneSpots[i];
                position = _coneAnchors[i];
            }
            return surface != null;
        }

        /// <summary>Spec 041: the tours' scripted left click, right click and wheel steps of this frame (none in a release build).</summary>
        private void ConsumeScripted(out bool left, out bool right, out int turn)
        {
            left = false;
            right = false;
            turn = 0;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (_handIntents == null) return;
            left = _handIntents.ConsumeLeftClick();
            right = _handIntents.ConsumeRightClick();
            turn = _handIntents.ConsumeTurn();
#endif
        }

        private Component Stick(Component target)
        {
            _sticky = TargetTooFar ? null : target;
            _stickySpot = target is PlaceSurface ? TargetSpot : PlaceSpots.None;
            return target;
        }

        /// <summary>Spec 018–021: the exact camera-centre ray (see <see cref="FindAimTarget"/>); <paramref name="direct"/>
        /// when it landed on the target's body.</summary>
        private Component FindExactTarget(Pickup held, out bool direct, out Ray ray)
        {
            bool holding = held != null;
            direct = false;
            ray = default;
            TargetSpot = PlaceSpots.None;
            TargetTooFar = false;
            ClearRayPutDown();
            Camera cam = Camera.main;
            if (cam == null) return null;
            ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            int count = Physics.RaycastNonAlloc(ray, _rayHits, ViewRules.AimRange, PhysicsLayers.Interact, QueryTriggerInteraction.Ignore);
            _aimHits.Clear();
            _aimSources.Clear();
            _aimPoints.Clear();
            _rayBlockDistances.Clear();
            _rayBlockStations.Clear();
            float putDownDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                Collider hitCollider = _rayHits[i].collider;
                if (hitCollider == null || hitCollider.transform.IsChildOf(transform)) continue;
                float distance = _rayHits[i].distance;
                // 041 review: sorted by the shared AimHitSort (the built-scene aim tests use the same).
                AimHitClass hitClass = AimHitSort.Classify(hitCollider, out Component hitSource);
                // Spec 019: a knob or button on a stove front; reach is measured to its station, like the server does.
                if (hitClass == AimHitClass.Button)
                {
                    var button = (StationButton)hitSource;
                    AddRayBlocker(distance, button.Station); // 041 review: the right click does not put down through a station
                    _aimHits.Add(AimHit.Button(distance, button.Station.transform.position, button.Station.Index, button.Option));
                    _aimSources.Add(button);
                    _aimPoints.Add(_rayHits[i].point);
                    continue;
                }
                // Spec 020: a worktop top, the stove top or a burner (checked before its station).
                if (hitClass == AimHitClass.Surface)
                {
                    var surface = (PlaceSurface)hitSource;
                    Vector3 point = _rayHits[i].point;
                    int spot = NearestFreeSpot(surface, point, out Vector3 spotPosition);
                    // Spec 041: the nearest surface on the ray with a free spot in reach is where a right click puts things down,
                    // even behind what the ray aims at (the pot: next to it on the counter).
                    if (holding && spot >= 0 && distance < putDownDistance && PickupRules.IsInReach(transform.position, spotPosition)
                        && SpotFits(surface, Hands(held))) // spec 042: only a fitting cup goes on a tap's cup spot
                    {
                        putDownDistance = distance;
                        _rayPutDownSurface = surface;
                        _rayPutDownSpot = spot;
                        _rayPutDownPosition = spotPosition;
                    }
                    // Spec 039: a top on a station with tools (the assembly board's back) lies behind them. The ray means the tool
                    // when it passes the tool closer than the hit lies to the nearest free spot (as when it reached the body).
                    StockSlot tool = ToolNearRay(surface, ray, out float toolDistance);
                    if (tool != null && (spot < 0 || toolDistance < Vector3.Distance(point, spotPosition)))
                    {
                        AddAimHit(distance, AimKind.Station, tool);
                        _aimPoints.Add(point);
                        continue;
                    }
                    _aimHits.Add(AimHit.Spot(distance, spot >= 0 ? spotPosition : point, surface.Index, spot));
                    _aimSources.Add(surface);
                    _aimPoints.Add(point);
                    continue;
                }
                // Spec 029: a fryer basket (a fixed vessel, no Pickup); its collider moves with its arc.
                if (hitClass == AimHitClass.FixedVessel) // spec 030: a griddle spot too
                {
                    var fixedVessel = (Vessel)hitSource;
                    if (fixedVessel.IsSpawned) AddAimHit(distance, AimKind.Vessel, fixedVessel);
                    else AddAimHit(distance, AimKind.Other, null);
                    _aimPoints.Add(_rayHits[i].point);
                    continue;
                }
                Station station = hitClass == AimHitClass.Station ? (Station)hitSource : null;
                // Spec 042: the drink dispenser's aim box holds its drip tray: a cup on a spot, or the free spot for the cup in the hands.
                if (station != null && station.Kind == StationKind.DrinkDispenser && AddDrinkSpotHit(station, ray, held, distance, _rayHits[i].point))
                    continue;
                if (station != null) AddRayBlocker(distance, station); // 041 review: the right click does not put down through a station
                Pickup pickup = hitClass == AimHitClass.Item ? (Pickup)hitSource : null;
                StockSlot slot = AimHitSort.SlotAt(station, _rayHits[i].point);
                // Spec 031: on the assembly board a ray means a tool only near it (elsewhere it is the board itself).
                if (station != null && AimHitSort.SlotFollowsTheRay(station.Kind)) slot = ToolOnRay(station, ray); // spec 032: the register; spec 033: the drinks
                if (slot != null)
                    AddAimHit(distance, AimKind.Station, slot); // spec 028b: the fridge or shelf means its slot nearest the hit; reach to its anchor
                else if (station != null)
                    AddAimHit(distance, AimKind.Station, station);
                else if (pickup != null && pickup.HasPrediction)
                    continue; // spec 027b: flying to its predicted place, not where its collider still is
                else if (pickup != null && !pickup.IsHeld && pickup.TryGetComponent(out Vessel vessel))
                    AddAimHit(distance, AimKind.Vessel, vessel); // spec 021
                else if (pickup != null && !pickup.IsHeld)
                    AddAimHit(distance, AimKind.Pickup, pickup);
                else
                {
                    AddAimHit(distance, AimKind.Other, null);
                    if (pickup == null) AddRayBlocker(distance, null); // spec 041: no putting down through a wall
                }
                _aimPoints.Add(_rayHits[i].point);
            }
            if (_rayPutDownSurface != null && RayPutDownBlocked(putDownDistance)) ClearRayPutDown();
            // 039 review (must-fix): with empty hands the place surfaces' thin aim colliders are transparent (nothing to put down).
            int picked = AimRules.Pick(_aimHits, transform.position, holding, !holding, out int outOfReach);
            // Spec 020: a target the ray sees but the hands cannot reach is shown grey with "Too far".
            TargetTooFar = picked == AimRules.None && outOfReach != AimRules.None;
            if (TargetTooFar) picked = outOfReach;
            if (picked == AimRules.None) return null;
            if (_aimHits[picked].Kind == AimKind.Spot)
            {
                TargetSpot = _aimHits[picked].SpotIndex;
                TargetSpotPosition = _aimHits[picked].ObjectPosition;
            }
            Component source = _aimSources[picked];
            direct = !TargetTooFar && IsDirectHit(source, _aimPoints[picked]);
            return source;
        }

        /// <summary>
        /// Spec 020: the free spot of <paramref name="surface"/> nearest to the hit point, or PlaceSpots.None. A spot is
        /// taken when an item lies on it (what this client sees; the server checks its own table again).
        /// </summary>
        private int NearestFreeSpot(PlaceSurface surface, Vector3 point, out Vector3 spotPosition)
        {
            spotPosition = point;
            int count = surface.SpotCount;
            if (count == 0) return PlaceSpots.None;
            if (_spotTaken.Length < count) _spotTaken = new bool[count];
            for (int i = 0; i < _spotTaken.Length; i++)
                _spotTaken[i] = i < count && SpotTaken(surface, i);
            int spot = PlaceSpots.NearestFree(surface.Size, surface.ToTopPlane(point), _spotTaken);
            if (spot >= 0) spotPosition = surface.SpotWorldPosition(spot);
            return spot;
        }

        private void AddAimHit(float distance, AimKind kind, Component source)
        {
            _aimHits.Add(new AimHit(distance, kind, source != null ? source.transform.position : Vector3.zero));
            _aimSources.Add(source);
        }

        // ---- Spec 027b: cone aim (docs/feel.md 3) ----

        /// <summary>A station, button or place surface the ray hits is always on target; an item only when the hit lies
        /// within <see cref="DirectHitRadius"/> of its centre (not on a pan's handle next to a free spot).</summary>
        private static bool IsDirectHit(Component source, Vector3 point)
        {
            if (source == null) return false;
            if (!(source is Pickup) && !(source is Vessel)) return true;
            Vector3 offset = point - AimAnchor(source);
            offset.y = 0f;
            return offset.sqrMagnitude <= DirectHitRadius * DirectHitRadius;
        }

        /// <summary>The point an item is aimed at: a vessel's contents, else the centre of its collider.</summary>
        private static Vector3 AimAnchor(Component source)
        {
            if (source is Vessel vessel) return vessel.ContentPoint;
            if (source.TryGetComponent(out Collider collider) && collider.enabled) return collider.bounds.center;
            return source.transform.position;
        }

        /// <summary>
        /// Spec 027b review: the stove buttons and place surfaces are scene objects that never spawn, so they are looked up
        /// once (again only if one went away); the pickups come from Pickup.ClientList, kept by the pickups themselves.
        /// </summary>
        private void RefreshInteractables()
        {
            if (_allButtons == null || HasMissing(_allButtons)) _allButtons = FindObjectsByType<StationButton>(FindObjectsSortMode.None);
            if (_allSurfaces == null || HasMissing(_allSurfaces)) _allSurfaces = FindObjectsByType<PlaceSurface>(FindObjectsSortMode.None);
            if (_allSlots == null || HasMissing(_allSlots)) _allSlots = FindObjectsByType<StockSlot>(FindObjectsSortMode.None); // spec 028b
            float now = Time.unscaledTime;
            // 031b: the baskets, griddle spots and build spots refill while empty or when a member is gone (CacheRules): a scene
            // NetworkObject stays inactive on a client until the server spawns it, so an empty first look must not stick.
            if (CacheRules.ShouldRefill(_allBaskets == null, _allBaskets != null && HasMissing(_allBaskets),
                    _allBaskets != null ? _allBaskets.Length : 0, now, _nextBasketLookup))
            {
                // Spec 029: their vessels cached alongside.
                _nextBasketLookup = now + CacheRules.EmptyRetrySeconds;
                _allBaskets = FindObjectsByType<FryerBasket>(FindObjectsSortMode.None);
                _basketVessels = new Vessel[_allBaskets.Length];
                for (int i = 0; i < _allBaskets.Length; i++) _basketVessels[i] = _allBaskets[i].GetComponent<Vessel>();
            }
            if (CacheRules.ShouldRefill(_allGriddleSpots == null, _allGriddleSpots != null && HasMissing(_allGriddleSpots),
                    _allGriddleSpots != null ? _allGriddleSpots.Length : 0, now, _nextGriddleLookup))
            {
                // Spec 030: like the baskets.
                _nextGriddleLookup = now + CacheRules.EmptyRetrySeconds;
                _allGriddleSpots = FindObjectsByType<GriddleSpot>(FindObjectsSortMode.None);
                _griddleVessels = new Vessel[_allGriddleSpots.Length];
                for (int i = 0; i < _allGriddleSpots.Length; i++) _griddleVessels[i] = _allGriddleSpots[i].GetComponent<Vessel>();
            }
            if (CacheRules.ShouldRefill(_allAssemblySpots == null, _allAssemblySpots != null && HasMissing(_allAssemblySpots),
                    _allAssemblySpots != null ? _allAssemblySpots.Length : 0, now, _nextAssemblyLookup))
            {
                // Spec 031: like the griddle spots.
                _nextAssemblyLookup = now + CacheRules.EmptyRetrySeconds;
                _allAssemblySpots = FindObjectsByType<AssemblySpot>(FindObjectsSortMode.None);
                _assemblyVessels = new Vessel[_allAssemblySpots.Length];
                for (int i = 0; i < _allAssemblySpots.Length; i++) _assemblyVessels[i] = _allAssemblySpots[i].GetComponent<Vessel>();
            }
        }

        private static bool HasMissing<TItem>(TItem[] items) where TItem : UnityEngine.Object
        {
            foreach (TItem item in items)
                if (item == null) return true;
            return false;
        }

        /// <summary>Spec 027b review: a spot's occupancy (a physics query), reused for <see cref="SpotCacheSeconds"/>.</summary>
        private bool SpotTaken(PlaceSurface surface, int spot)
        {
            long key = ((long)surface.Index << 16) | (uint)spot;
            float now = Time.unscaledTime;
            if (_spotCache.TryGetValue(key, out SpotState state) && now < state.Until) return state.Taken;
            bool taken = PickupService.IsSpotOccupied(surface.SpotWorldPosition(spot), _hits);
            _spotCache[key] = new SpotState { Taken = taken, Until = now + SpotCacheSeconds };
            return taken;
        }

        /// <summary>
        /// The best actionable interactable within the cone around <paramref name="ray"/>, or null. A <paramref name="direct"/>
        /// exact hit (with its spot) joins as a Direct candidate and wins.
        /// </summary>
        private Component FindConeTarget(Ray ray, Pickup held, Component direct, int directSpot, Vector3 directSpotPosition)
        {
            if (ray.direction.sqrMagnitude < 0.5f) return null;
            RefreshInteractables();
            FeelSettings feel = FeelSettings.Current;
            Held hands = Hands(held);
            _cone.Clear();
            _coneSources.Clear();
            _coneSpots.Clear();
            _coneAnchors.Clear();
            _coneBuilt = true; // spec 041: the put-down looks at this frame's cone
            float range = ViewRules.AimRange;
            foreach (Pickup pickup in Pickup.ClientList)
            {
                if (pickup == null || !pickup.IsSpawned || pickup.IsHeld || pickup.HasPrediction || pickup == held) continue;
                Component source = pickup.TryGetComponent(out Vessel vessel) ? vessel : (Component)pickup;
                if (vessel == null && held != null) continue; // full hands: only cookware takes things
                AddCone(ray, source, PlaceSpots.None, AimAnchor(source), hands, Describe(source), range, feel.AimCone);
            }
            foreach (StationButton button in _allButtons)
            {
                if (button == null || button.Station == null) continue;
                AddCone(ray, button, PlaceSpots.None, button.transform.position, hands, Describe(button), range, feel.AimCone,
                    button.Station.transform.position);
            }
            for (int i = 0; i < _basketVessels.Length; i++)
            {
                // Spec 029: a basket competes at its contents, reach measured to its pivot (as the server does).
                Vessel basket = _basketVessels[i];
                if (basket == null || !basket.IsSpawned) continue;
                AddCone(ray, basket, PlaceSpots.None, basket.ContentPoint, hands, Describe(basket), range, feel.AimCone, basket.transform.position);
            }
            for (int i = 0; i < _griddleVessels.Length; i++)
            {
                // Spec 030: a griddle spot competes at its food, reach measured to its pivot (as the server does).
                Vessel spot = _griddleVessels[i];
                if (spot == null || !spot.IsSpawned) continue;
                AddCone(ray, spot, PlaceSpots.None, spot.ContentPoint, hands, Describe(spot), range, feel.AimCone, spot.transform.position);
            }
            for (int i = 0; i < _assemblyVessels.Length; i++)
            {
                // Spec 031: the build spot competes at its contents, reach to its pivot (as the server does).
                Vessel build = _assemblyVessels[i];
                if (build == null || !build.IsSpawned) continue;
                AddCone(ray, build, PlaceSpots.None, build.ContentPoint, hands, Describe(build), range, feel.AimCone, build.transform.position);
            }
            foreach (StockSlot slot in _allSlots) // spec 028b: reach to the slot's anchor, as the server measures it
                if (slot != null && slot.Station != null && (!hands.IsHolding || slot.Station.Kind == StationKind.Assembly
                    || slot.Station.Kind == StationKind.Register || slot.Station.Kind == StationKind.DrinkDispenser // spec 031-033: tools with any hands
                    || slot.Station.Kind == StationKind.Sink || slot.Station.Kind == StationKind.TrashCan)) // spec 035: a dish in, a discard
                    AddCone(ray, slot, PlaceSpots.None, slot.Anchor, hands, Describe(slot), range, feel.AimCone);
            if (held != null)
            {
                foreach (PlaceSurface surface in _allSurfaces)
                {
                    if (surface == null || surface.SpotCount == 0) continue;
                    if ((surface.transform.position - transform.position).sqrMagnitude > (PickupRules.MaxReach + 1.5f) * (PickupRules.MaxReach + 1.5f)) continue;
                    for (int spot = 0; spot < surface.SpotCount; spot++)
                    {
                        Vector3 anchor = surface.SpotWorldPosition(spot);
                        Vector3 to = anchor - ray.origin;
                        if (FeelRules.AngleTo(ray.direction, to) > feel.AimCone || to.magnitude > range) continue;
                        if (SpotTaken(surface, spot)) continue;
                        AddCone(ray, surface, spot, anchor, hands, SpotTarget(surface, spot), range, feel.AimCone); // spec 042
                    }
                }
            }

            if (direct != null)
            {
                bool isSpot = direct is PlaceSurface;
                Vector3 anchor = isSpot ? directSpotPosition : AimAnchor(direct);
                _cone.Add(new AimCandidate(FeelRules.AngleTo(ray.direction, anchor - ray.origin), false, true));
                _coneSources.Add(direct);
                _coneSpots.Add(isSpot ? directSpot : PlaceSpots.None);
                _coneAnchors.Add(anchor);
            }

            int current = PlaceSpots.None;
            for (int i = 0; i < _coneSources.Count; i++)
                if (_coneSources[i] == _sticky && _coneSpots[i] == _stickySpot) current = i;
            // The best one in sight (small items never block the view, the candidate's own station neither).
            for (int attempt = 0; attempt < 4; attempt++)
            {
                int pick = FeelRules.PickTarget(_cone, current, feel.AimCone, feel.StickMargin, feel.HeldBonus);
                if (pick == FeelRules.None) return null;
                if (_cone[pick].Direct || InSight(ray.origin, _coneAnchors[pick], _coneSources[pick]))
                {
                    if (_coneSources[pick] is PlaceSurface)
                    {
                        TargetSpot = _coneSpots[pick];
                        TargetSpotPosition = _coneAnchors[pick];
                    }
                    else TargetSpot = PlaceSpots.None;
                    TargetTooFar = false;
                    return _coneSources[pick];
                }
                // Out of sight: push it out of the cone and try the next.
                _cone[pick] = new AimCandidate(float.PositiveInfinity, false, false);
                if (pick == current) current = PlaceSpots.None;
            }
            return null;
        }

        private void AddCone(Ray ray, Component source, int spot, Vector3 anchor, Held hands, AimTarget target, float range, float cone,
            Vector3? reachPoint = null)
        {
            Vector3 to = anchor - ray.origin;
            float angle = FeelRules.AngleTo(ray.direction, to);
            if (angle > cone || to.magnitude > range) return;
            if (!PickupRules.IsInReach(transform.position, reachPoint ?? anchor)) return;
            InteractionAction action = InteractionRules.ActionFor(hands, target);
            if (action == InteractionAction.None || action == InteractionAction.Cut) return;
            bool accepts = action == InteractionAction.PutInto || action == InteractionAction.Pour;
            _cone.Add(new AimCandidate(angle, accepts, false));
            _coneSources.Add(source);
            _coneSpots.Add(spot);
            _coneAnchors.Add(anchor);
        }

        /// <summary>Nothing solid between the eye and the anchor, except items (they never block), place surfaces, buttons,
        /// our own body and the candidate's own station.</summary>
        private bool InSight(Vector3 eye, Vector3 anchor, Component candidate)
        {
            Vector3 to = anchor + Vector3.up * 0.02f - eye;
            float distance = to.magnitude - 0.03f;
            if (distance <= 0f) return true;
            Station own = candidate != null ? candidate.GetComponentInParent<Station>() : null;
            if (candidate is StationButton button) own = button.Station;
            // Spec 029: a basket hangs in its fryer; neither the fryer nor the basket's own collider hides it.
            Transform self = candidate is Vessel fixedVessel && VesselRules.IsFixed(fixedVessel.Kind) ? fixedVessel.transform : null;
            if (self != null && self.TryGetComponent(out FryerBasket fb)) own = fb.Fryer;
            if (self != null && self.TryGetComponent(out GriddleSpot gs)) own = gs.Griddle; // spec 030: on its griddle
            if (self != null && self.TryGetComponent(out AssemblySpot asp)) own = asp.Board; // spec 031: on its board
            int count = Physics.RaycastNonAlloc(new Ray(eye, to.normalized), _sightHits, distance, PhysicsLayers.Interact,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider c = _sightHits[i].collider;
                if (c == null || c.transform.IsChildOf(transform)) continue;
                if (self != null && c.transform.IsChildOf(self)) continue;
                if (c.GetComponentInParent<Pickup>() != null || c.TryGetComponent(out PlaceSurface _) || c.TryGetComponent(out StationButton _)) continue;
                if (own != null && c.GetComponentInParent<Station>() == own) continue;
                // Spec 042: an item standing inside the drink dispenser's aim box (a cup on its tray) is not hidden by it. 042 review: only
                // the dispenser (the world AABB of a rotated station is too big to trust for the others).
                if (candidate is Pickup && c.bounds.Contains(anchor) && c.GetComponentInParent<Station>() is Station box
                    && box.Kind == StationKind.DrinkDispenser) continue;
                return false;
            }
            return true;
        }

        /// <summary>An item that is held or flying (predicted) is no target any more.</summary>
        private static bool StillTargetable(Component target)
        {
            Pickup item = target as Pickup;
            if (item == null && target is Vessel vessel) item = vessel.GetComponent<Pickup>();
            return item == null || (item.IsSpawned && !item.IsHeld && !item.HasPrediction);
        }

        /// <summary>
        /// docs/feel.md 3: no single-frame flicker. A target lost this frame stays until it has been unseen for
        /// FeelSettings.HighlightMinSeconds (a one-frame gap never blinks the highlight and prompt off).
        /// </summary>
        private Component KeepShown(Component fresh)
        {
            float now = Time.unscaledTime;
            float min = FeelSettings.Current.HighlightMinSeconds;
            bool shownAlive = _shown != null && StillTargetable(_shown);
            bool same = fresh == _shown && TargetSpot == _shownSpot;
            if (same && fresh != null)
            {
                _shownLastSeen = now;
                _shownTooFar = TargetTooFar;
                _shownSpotPosition = TargetSpotPosition;
                return fresh;
            }
            // Only a lost target is held on: a new one takes over at once, so a click always acts on what the dot is on now
            // (between two candidates the cone's stickiness prevents flicker).
            bool keep = shownAlive && fresh == null && FeelRules.KeepShown(_shownLastSeen, now, min);
            if (keep)
            {
                TargetSpot = _shownSpot;
                TargetSpotPosition = _shownSpotPosition;
                TargetTooFar = _shownTooFar;
                return _shown;
            }
            _shown = fresh;
            _shownSpot = TargetSpot;
            _shownSpotPosition = TargetSpotPosition;
            _shownTooFar = TargetTooFar;
            _shownLastSeen = now;
            return fresh;
        }

        /// <summary>
        /// Spec 036, third person: a tap of E at the spit loads a cone (empty hands, bare spit), else scoops from the tray (empty hands, or
        /// onto the build in them), else turns the knob; with the knife it does nothing (holding E shaves).
        /// </summary>
        private bool HandleSpitKey(Station spit, bool holding)
        {
            Held hands = Hands(FindHeldPickup());
            if (!spit.TryGetSlot(SpitRules.ConeTool, out StockSlot cone)) return false;
            if (hands.IsKnife) return true;
            AimTarget seen = WithStock(DescribeSpit(cone), cone);
            if (InteractionRules.ActionFor(hands, seen) == InteractionAction.Load)
            {
                LoadConeWithLook(cone);
                return true;
            }
            if (cone.TryGetComponent(out Worktown.Game.Art.SpitView view) && view.Tray != null)
            {
                AimTarget tray = AimTarget.Cookware(VesselKind.SpitTray, view.Tray.ContentCount, view.Tray.Title.Value);
                if (InteractionRules.ActionFor(hands, tray) == InteractionAction.Take)
                {
                    PlaySound(holding ? SoundKey.PlaceFood : SoundKey.Pick, view.Tray.transform.position);
                    SendUse(view.Tray.NetworkObject, VesselRules.ActionUse);
                    PredictTipOrScoop(view.Tray, FindHeldPickup()); // spec 044: the scoop's look at the key
                    return true;
                }
            }
            if (holding) return false;
            PlaySound(SoundKey.Button, spit.transform.position);
            SendStation(spit.Index, SpitRules.KnobOption);
            return true;
        }

        /// <summary>Third person (spec 007/021): E at a crate or the shelf with empty hands, at the counter holding something.</summary>
        private bool TryHandleStationKey(Station station, bool holding)
        {
            // Spec 034, third person: a carried delivery box goes into this station's slot for its good (E).
            if (holding)
            {
                StockSlot boxSlot = SlotForBox(station, Hands(FindHeldPickup()));
                if (boxSlot != null)
                {
                    UnpackWithLook(boxSlot);
                    return true;
                }
            }
            switch (station.Kind)
            {
                case StationKind.Terminal:
                {
                    // Spec 034, third person: E orders the nearest row of the terminal's page (paging is a first-person job).
                    StockSlot row = NearestTerminalRow(station);
                    if (row == null) return false;
                    TerminalClick(row);
                    return true;
                }
                case StationKind.Crate:
                case StationKind.PlateShelf:
                    if (holding) return false;
                    PlaySound(SoundKey.Pick, station.transform.position);
                    SendStation(station.Index, 0);
                    return true;
                // Spec 035, third person: E at the cleaning stations does their click (holding E at the sink scrubs).
                case StationKind.DishReturn:
                case StationKind.Sink:
                case StationKind.TrashCan:
                case StationKind.Bin:
                {
                    StockSlot slot = station.SlotCount > 0 && station.TryGetSlot(0, out StockSlot first) ? first : null;
                    Component target = slot != null ? slot : station;
                    Held hands = Hands(FindHeldPickup());
                    switch (InteractionRules.ActionFor(hands, Describe(target)))
                    {
                        case InteractionAction.Take when slot != null:
                            CleaningTakeWithLook(slot);
                            return true;
                        case InteractionAction.Wash when slot != null:
                            WashWithLook(slot);
                            return true;
                        case InteractionAction.Discard:
                            DiscardWithLook(target);
                            return true;
                        default:
                            return false;
                    }
                }
                case StationKind.Counter:
                    if (!holding) return false;
                    PlaySound(SoundKey.Button, station.transform.position);
                    SendStation(station.Index, 0);
                    return true;
                case StationKind.Spit:
                    return HandleSpitKey(station, holding); // spec 036
                case StationKind.DrinkDispenser:
                {
                    // Spec 033/042, third person (holding E pours into the cup on the nearest tap's spot): a tap of E with a cup puts it
                    // under the nearest free tap it fits, else lids it; with empty hands it lids a filled cup on a spot, else takes a cup off
                    // a spot (DrinkRules.TakeableTap: a lidded one first, then any other, such as an empty cup put back), else takes a cup
                    // from the stack.
                    Pickup heldItem = FindHeldPickup();
                    Held hands = Hands(heldItem);
                    if (holding)
                    {
                        PlaceSurface free = hands.IsCup ? NearestFreeCupSpot(station, hands) : null;
                        if (free != null && hands.CupLevel == DrinkRules.LevelEmpty)
                        {
                            PlaceWithLook(heldItem, free, 0, free.SpotWorldPosition(0));
                            return true;
                        }
                        if (DrinkRules.CanLid(hands.IsCup, hands.CupLidded, hands.CupLevel) == CookOutcome.Ok)
                        {
                            LidWithLook(station);
                            return true;
                        }
                        if (free == null) return false;
                        PlaceWithLook(heldItem, free, 0, free.SpotWorldPosition(0));
                        return true;
                    }
                    byte flags = DispenserFlags(station);
                    Food liddable = LiddableSpotCup(station, flags);
                    if (liddable != null && DrinkRules.CanLidOnSpot(SpotByteOf(liddable)) == CookOutcome.Ok)
                    {
                        LidWithLook(station);
                        return true;
                    }
                    // Follow-up 2026-11-11: the same cups a first-person aim takes (not one that another player pours into).
                    int take = DrinkRules.TakeableTap(t => SpotByteOf(SpotCup(station, t)),
                        t => DrinkRules.IsPouring(flags, t) && !OwnSqueeze(station.Index, t),
                        t => CupSpotOf(station, t) is PlaceSurface spot ? (spot.SpotWorldPosition(0) - transform.position).sqrMagnitude : float.MaxValue);
                    if (take >= 0 && SpotCup(station, take) is Food onSpot && onSpot.TryGetComponent(out Pickup cupOnSpot))
                    {
                        PickUpWithLook(cupOnSpot);
                        return true;
                    }
                    if (station.TryGetSlot(DrinkRules.CupTool, out StockSlot cups)) TakeWithLook(cups, DrinkRules.CupOption, "empty cup");
                    else SendStation(station.Index, DrinkRules.CupOption);
                    return true;
                }
                case StationKind.Register:
                    // Spec 032, third person: E takes a card payment (counting change out is a first-person job, by the coin).
                    if (!station.TryGetComponent(out Worktown.Game.Shop.Register till) || !Worktown.Game.Shop.RegisterRules.IsCard(till.Payment.Value))
                        return false;
                    if (station.TryGetSlot(Worktown.Game.Shop.RegisterRules.CardOption, out StockSlot terminal)) PayWithLook(terminal);
                    return true;
                default:
                    return false;
            }
        }

        private Station FindNearestStation()
        {
            if (_stations == null) _stations = FindObjectsByType<Station>(FindObjectsSortMode.None);
            Station best = null;
            float bestSqr = float.MaxValue;
            foreach (Station s in _stations)
            {
                if (s == null || !StationRules.IsInReach(transform.position, s.transform.position)) continue;
                float sqr = (s.transform.position - transform.position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = s;
                }
            }
            return best;
        }

        /// <summary>Owner side: true if a cube we own says we hold it.</summary>
        public bool IsHoldingSomething() => FindHeldPickup() != null;

        /// <summary>Spec 038, owner side: what the hands hold now in either view (CurrentHeld is first person only), for the tutorial.</summary>
        public Held HandsNow => Hands(FindHeldPickup());

        /// <summary>Owner side: the cube this client holds, or null.</summary>
        public Pickup FindHeldPickup()
        {
            foreach (NetworkObject obj in Owner.Objects)
                if (obj != null && obj.TryGetComponent(out Pickup pickup) && pickup.HolderClientId.Value == OwnerId)
                    return pickup;
            return null;
        }

        private Pickup FindNearestFreePickup()
        {
            int count = Physics.OverlapSphereNonAlloc(transform.position, ClientSearchRadius, _hits, PhysicsLayers.Interact,
                QueryTriggerInteraction.Ignore); // spec 039: items lie on their own layer
            Pickup best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (!_hits[i].TryGetComponent(out Pickup pickup) || pickup.IsHeld) continue;
                float sqr = (pickup.transform.position - transform.position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = pickup;
                }
            }
            return best;
        }

        /// <summary>Spec 019: the camera stays still while the knife drag is armed (same flag as the emote wheel).</summary>
        private void SetLookSuppressed(bool suppressed)
        {
            if (suppressed == _suppressingLook) return;
            _suppressingLook = suppressed;
            if (_inputReader != null) _inputReader.SuppressLook = suppressed;
        }

        // Owner side: every request goes through these, so the knife can respect the server's rate limit.
        private void SendStation(int stationIndex, int option)
        {
            _lastSendTime = Time.unscaledTime;
            _clicks.NoteSent(_lastSendTime);
            _lastRequest = _looks.Sent();
            RequestStation(stationIndex, option, _lastRequest);
        }

        private void SendPickup(NetworkObject target)
        {
            _lastSendTime = Time.unscaledTime;
            _clicks.NoteSent(_lastSendTime);
            _lastRequest = _looks.Sent();
            RequestPickup(target, _lastRequest);
        }

        private void SendDrop()
        {
            _lastSendTime = Time.unscaledTime;
            _clicks.NoteSent(_lastSendTime);
            _lastRequest = _looks.Sent();
            RequestDrop(_lastRequest);
        }

        private void SendPlace(int surfaceIndex, int spotIndex)
        {
            _lastSendTime = Time.unscaledTime;
            _clicks.NoteSent(_lastSendTime);
            _lastRequest = _looks.Sent();
            RequestPlace(surfaceIndex, spotIndex, _lastRequest);
        }

        private void SendUse(NetworkObject target, int action)
        {
            _lastSendTime = Time.unscaledTime;
            _clicks.NoteSent(_lastSendTime);
            _lastRequest = _looks.Sent();
            RequestUse(target, action, _lastRequest);
        }

        [ServerRpc]
        private void RequestPickup(NetworkObject target, int request)
        {
            bool limited = IsRateLimited();
            if (_spam.Tripped) return; // spec 045: disconnected for spam
            PickupOutcome outcome = limited ? PickupOutcome.TooFast
                : _service != null ? _service.TryPickup(this, target) : PickupOutcome.NotFound;
            ReportResult(Owner, outcome, true, request);
        }

        [ServerRpc]
        private void RequestDrop(int request)
        {
            bool limited = IsRateLimited();
            if (_spam.Tripped) return;
            PickupOutcome outcome = limited ? PickupOutcome.TooFast
                : _service != null ? _service.TryDrop(this) : PickupOutcome.NothingHeld;
            ReportResult(Owner, outcome, false, request);
        }

        /// <summary>
        /// Spec 020: put the held item on spot <paramref name="spotIndex"/> of place surface
        /// <paramref name="surfaceIndex"/> (spec 021: also a burner). Two ints, never a position: the server checks
        /// hands, surface, spot, reach and the rate, and places the item itself (PickupService.TryPlace).
        /// </summary>
        [ServerRpc]
        private void RequestPlace(int surfaceIndex, int spotIndex, int request)
        {
            bool limited = IsRateLimited();
            if (_spam.Tripped) return;
            PickupOutcome outcome = limited ? PickupOutcome.TooFast
                : _service != null ? _service.TryPlace(this, surfaceIndex, spotIndex) : PickupOutcome.NotFound;
            ReportPlace(Owner, outcome, request);
        }

        [TargetRpc]
        private void ReportPlace(NetworkConnection target, PickupOutcome outcome, int request)
        {
            // Review of spec 027b: a rate-limited request is answered too (numbering in step) but shows nothing.
            NoteAnswer(request, AnswerKind.Place, outcome == PickupOutcome.Ok);
            if (outcome == PickupOutcome.TooFast) return;
            LastCookOutcome = null;
            LastPickupOutcome = outcome;
            LastPickupWasPlace = true;
            LastResult = "place: " + outcome;
            if (outcome != PickupOutcome.Ok)
                Debug.Log("[Pickup] " + LastResult);
        }

        /// <summary>
        /// Spec 007: take from a crate or sell at the counter (option 0). Spec 021: take a plate from the shelf (0), and
        /// a stove knob or button (StationRules.StoveOption: burner and switch or mode).
        /// </summary>
        [ServerRpc]
        private void RequestStation(int stationIndex, int option, int request)
        {
            bool limited = IsRateLimited();
            if (_spam.Tripped) return;
            CookOutcome outcome = limited ? CookOutcome.TooFast
                : _kitchen != null ? _kitchen.TryUseStation(this, stationIndex, option) : CookOutcome.UnknownStation;
            ReportCook(Owner, outcome, request);
        }

        /// <summary>
        /// Spec 021: use what you hold on the cookware <paramref name="target"/>: <paramref name="action"/> 0 use (put
        /// into, pour, take out), 1 one knife stroke, 2 turn the food. An object and an int only: the server reads what
        /// the player holds and the target's state, checks reach, hands, capacity and the sim, and decides.
        /// </summary>
        [ServerRpc]
        private void RequestUse(NetworkObject target, int action, int request)
        {
            bool limited = IsRateLimited();
            if (_spam.Tripped) return;
            CookOutcome outcome = limited ? CookOutcome.TooFast
                : _kitchen != null ? _kitchen.TryUseVessel(this, target, action) : CookOutcome.NotAVessel;
            ReportCook(Owner, outcome, request);
        }

        [TargetRpc]
        private void ReportCook(NetworkConnection target, CookOutcome outcome, int request)
        {
            int answered = NoteAnswer(request, AnswerKind.Cook, outcome == CookOutcome.Ok);
            if (answered != 0) AnswerStockLooks(answered, outcome); // spec 034
            AnswerContents(answered, outcome == CookOutcome.Ok); // 044 follow-up: a refused tip or scoop goes back at once
            if (answered != 0 && answered == _squeezeStartRequest)
            {
                // 031b (review of 031): a refused squeeze start ends the predicted ribbon at once (not after its 8 s hold).
                _squeezeStartRequest = 0;
                if (outcome != CookOutcome.Ok && _squeezeStartView != null) _squeezeStartView.Predict(false);
                if (outcome != CookOutcome.Ok && outcome != CookOutcome.TooFast && _squeezeStartIsPour) // 033 review: only a tap's start
                {
                    StopPourLook(false); // spec 033: no pour: back to the hand
                    if (_lastPourCup != null && _lastPourCup.Visual != null) _lastPourCup.Visual.EndPrediction();
                }
            }
            if (answered != 0 && answered == _pourStopRequest)
            {
                // Spec 033: the server did not fill the cup (its wallet said no, or the cup was put down): the level goes back, "nope".
                _pourStopRequest = 0;
                if (outcome != CookOutcome.Ok && outcome != CookOutcome.TooFast && _lastPourCup != null)
                {
                    if (_lastPourCup.Visual != null) _lastPourCup.Visual.EndPrediction();
                    PlaySound(SoundKey.Nope, _lastPourCup.transform.position);
                }
            }
            if (answered != 0 && answered == _lidRequest)
            {
                // Spec 033: a refused lid comes off again with "nope".
                _lidRequest = 0;
                if (outcome != CookOutcome.Ok && _lidCup != null && _lidCup.Visual != null)
                {
                    _lidCup.Visual.PredictLid(false);
                    PlaySound(SoundKey.Nope, _lidCup.transform.position);
                }
                _lidCup = null;
            }
            RegisterAnswered(answered, outcome == CookOutcome.Ok); // spec 032
            if (outcome == CookOutcome.TooFast) return; // review of spec 027b: answered only to keep the numbering
            LastPickupOutcome = null;
            LastCookOutcome = outcome;
            LastResult = "kitchen: " + outcome;
            if (outcome != CookOutcome.Ok)
                Debug.Log("[Kitchen] " + LastResult);
        }

        /// <summary>
        /// Server: refuse requests that come faster than a person can click (spam costs no validation work). Spec 019: this
        /// also limits knife strokes. Review of spec 027b: a token bucket per player (RequestBucket: burst 2, one token per
        /// 0.2 s), so requests sent far enough apart but arriving bunched all pass; a refused one is answered with TooFast
        /// (one small report, the same RPCs; spec 045: with the request's echoed number). Spec 045: after
        /// SpamGuard.MaxTooFastInWindow refusals within SpamGuard.WindowSeconds the client is disconnected (045 review: a sliding window) (a flood, not a person); the host's own player is
        /// never kicked from its own server.
        /// </summary>
        private bool IsRateLimited()
        {
            double now = Time.unscaledTimeAsDouble;
            bool limited = !_serverBucket.TryTake(now);
            if (_spam.Note(limited, now))
            {
                NetworkConnection owner = Owner;
                if (!SpamGuard.ShouldKick(_spam.Tripped, owner == null || !owner.IsValid || owner.IsLocalClient))
                {
                    _spam.Reset(); // the host's own player (or nobody): keep answering
                }
                else
                {
                    Debug.LogWarning("[Net] player " + owner.ClientId + " disconnected: " + SpamGuard.MaxTooFastInWindow
                        + " requests in " + SpamGuard.WindowSeconds + " s came too fast (spam)");
                    NetworkManager.ServerManager.Kick(owner, FishNet.Managing.Server.KickReason.ExcessiveData,
                        FishNet.Managing.Logging.LoggingType.Off);
                }
            }
            return limited;
        }

        [TargetRpc]
        private void ReportResult(NetworkConnection target, PickupOutcome outcome, bool wasPickup, int request)
        {
            NoteAnswer(request, wasPickup ? AnswerKind.Pickup : AnswerKind.Drop, outcome == PickupOutcome.Ok);
            if (outcome == PickupOutcome.TooFast) return; // review of spec 027b: answered only to keep the numbering
            LastCookOutcome = null;
            LastPickupOutcome = outcome;
            LastPickupWasPlace = false;
            LastResult = (wasPickup ? "pickup: " : "drop: ") + outcome;
            if (outcome != PickupOutcome.Ok)
                Debug.Log("[Pickup] " + LastResult);
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        // ---- Spec 027: hooks for the dev tours and -demo-cookware (Scripts/Dev), development builds only ----
        // The tour clicks like a player (DevClick: the action under the dot) or, when the aim fails, sends the same
        // request the click would have sent. All of them go through the normal ServerRpcs; the server decides as always.

        /// <summary>
        /// Spec 041, development builds only: the hand inputs a tour gives instead of the mouse (a whole left click, a right click,
        /// wheel steps), each read once in the next frame and routed exactly like the device's (ControlRules, the click buffer).
        /// </summary>
        public interface IHandIntentSource
        {
            bool ConsumeLeftClick();
            bool ConsumeRightClick();
            int ConsumeTurn();
        }

        private IHandIntentSource _handIntents;

        /// <summary>Spec 041, owner only: the tour's hand inputs (added to the devices'); null for none.</summary>
        public IHandIntentSource HandIntentSource
        {
            get => _handIntents;
            set => _handIntents = value;
        }

        /// <summary>Spec 027: does what a left click does now (<see cref="CurrentAction"/> on <see cref="Target"/>).</summary>
        public void DevClick() => ExecuteAction();
        /// <summary>Spec 029b, tours: a stock take with its predicted look, sent whatever the rules offer now.</summary>
        public void DevTake(StockSlot slot)
        {
            if (slot != null && slot.Station != null) TakeWithLook(slot);
        }
        /// <summary>Spec 029b, tours: the hands as the rules see them now (a pending pick-up counts as held).</summary>
        public Held DevHands() => Hands(FindHeldPickup());
        public void DevSendPickup(NetworkObject target) => SendPickup(target);
        public void DevSendPlace(int surfaceIndex, int spotIndex) => SendPlace(surfaceIndex, spotIndex);
        public void DevSendStation(int stationIndex, int option) => SendStation(stationIndex, option);
        public void DevSendUse(NetworkObject target, int action) => SendUse(target, action);
        /// <summary>Spec 039, tours: drop what the hands hold in front of the player (Q), with its predicted fall.</summary>
        public void DevDrop()
        {
            Pickup held = FindHeldPickup();
            if (held != null) DropWithLook(held);
        }
        /// <summary>Spec 031, tours: press a sauce dispenser like a held left click (the look now, the start when allowed).</summary>
        public void DevSqueezePress(StockSlot dispenser)
        {
            if (dispenser == null || dispenser.Station == null
                || !_squeeze.Press(dispenser.Station.Index, dispenser.Option, Time.unscaledTimeAsDouble)) return;
            _devSqueeze = true;
            _squeezeView = dispenser.GetComponent<Worktown.Game.Art.IPourView>();
            if (_squeezeView != null) _squeezeView.Predict(true);
            if (dispenser.Station.Kind == StationKind.DrinkDispenser && DrinkRules.IsTap(dispenser.Option))
                StartPourLook(dispenser, Time.unscaledTimeAsDouble); // spec 033: a tap too
            if (dispenser.Station.Kind == StationKind.DrinkDispenser && dispenser.Option == Worktown.Game.Shop.CleaningRules.SpillTool)
                _wipeView = dispenser.GetComponent<Worktown.Game.Art.SpillWipeView>(); // spec 035: the cloth to the puddle
            if (IsSpitHold(dispenser)) StartShaveLook(dispenser); // spec 036: the knife to the cone
        }
        /// <summary>Spec 031, tours: let go of the dispenser (the look ends now, the stop goes out when allowed).</summary>
        public void DevSqueezeRelease()
        {
            _squeeze.Release(Time.unscaledTimeAsDouble);
            if (_squeezeView != null) _squeezeView.Predict(false);
            StopPourLook(true); // spec 033
            StopShaveLook(); // spec 036
        }
        /// <summary>Spec 033, tours: a lid from the lid stack with its look, like a click on it.</summary>
        public void DevLid(Station dispenser)
        {
            if (dispenser != null) LidWithLook(dispenser);
        }
        /// <summary>Spec 033, tours: a cup from the cup stack with its look, like a click on it.</summary>
        public void DevTakeCup(Station dispenser)
        {
            if (dispenser != null && dispenser.TryGetSlot(DrinkRules.CupTool, out StockSlot cups)) TakeWithLook(cups, DrinkRules.CupOption, "empty cup");
        }
        /// <summary>Follow-up 2026-11-11 (spec 042), tours: a third-person tap of E at <paramref name="station"/>; true when it acted.</summary>
        public bool DevStationKey(Station station) => station != null && TryHandleStationKey(station, FindHeldPickup() != null);
        /// <summary>Spec 031, tours: the squeeze still has a request to send.</summary>
        public bool DevSqueezeBusy => !_squeeze.Idle;
#endif
    }
}
