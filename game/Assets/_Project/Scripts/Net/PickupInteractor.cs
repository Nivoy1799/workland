using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;
using Worktown.Game.Audio;
using Worktown.Game.Cooking;
using Worktown.Game.Net.Rules;
using Worktown.Game.Player;

namespace Worktown.Game.Net
{
    /// <summary>
    /// On the player. The owner picks up, drops, places and uses things; the client only asks, the server decides
    /// (PickupService, KitchenService) and answers with the outcome. The client deliberately searches further than the
    /// allowed reach in third person, so requests from 2.5 to 4 m reach the server and get refused there (spec 003 AC4).
    /// Spec 018: in first person the target is what the camera centre looks at (AimRules), within the same reach the
    /// server checks. Spec 020: first person uses the mouse; one pure rule (InteractionRules) turns what you hold and
    /// what the dot points at into the click's action, and the prompt and the highlight show the same action. Q drops.
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
        private Worktown.Game.Art.SauceDispenserView _squeezeView;
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

        private enum QueuedKind : byte { None, Click, Drop, Key, Flip } // 030 review: the right-click flip queues too

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

        public override void OnStartServer()
        {
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
                if (look.Build != null) look.Build.EndPrediction(look.BuildToken, false); // spec 031
            }
            _ending.Clear();
            _squeeze.Reset(); // spec 031: the server stops a squeeze left running
            if (_squeezeView != null) _squeezeView.Predict(false);
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
            Target = firstPerson ? KeepShown(FindAimTarget(held)) : FindNearestStation();
            UpdateInteraction(firstPerson, held);

            if (_inputReader == null) _inputReader = FindFirstObjectByType<Worktown.Game.Player.PlayerInputReader>();
            Keyboard keyboard = Keyboard.current;
            Gamepad gamepad = Gamepad.current;
            Mouse mouse = Mouse.current;
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            // Spec 012: while quick chat (T) or the emote wheel (G / LB) is held, the keys belong to them; the
            // wheel then owns the camera freeze, so the knife lets go of it without writing it.
            bool wheel = (keyboard != null && keyboard.gKey.isPressed) || (gamepad != null && gamepad.leftShoulder.isPressed);
            // Keys belong to the menu or banner while one is open (review finding, spec 011).
            bool blocked = keyboard == null || wheel || keyboard.tKey.isPressed
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
                // 031b: holding E (or the right trigger) at the assembly board squeezes its nearest dispenser.
                bool squeezing = UpdateThirdPersonSqueeze(keyboard, gamepad);
                // Spec 020 AC7: E acts in third person only. Spec 027b review: through the click buffer as well.
                if (!squeezing && InteractionRules.KeysActInView(firstPerson) && keyboard.eKey.wasPressedThisFrame) Queue(QueuedKind.Key, false);
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
                return;
            }
            CurrentHeld = Hands(held);
            CurrentAim = TargetTooFar ? Describe(Target).TooFar() : Describe(Target);
            CurrentAction = InteractionRules.ActionFor(CurrentHeld, CurrentAim);
            // Spec 029b: while a take is on its way and nothing is really held, a click offers nothing (no Place, PutInto or
            // Sell of food that is not there yet), and the prompt stays empty.
            if (InteractionRules.WaitsForTake(_pendingTake != null, held != null))
            {
                CurrentAction = InteractionAction.None;
                CurrentAim = AimTarget.Nothing;
            }
        }

        /// <summary>
        /// Spec 020, first person: the left click does <see cref="CurrentAction"/> (on release, see ClickGesture); with
        /// the knife over a board with food the left button drags strokes instead and the right button turns the food
        /// (spec 019). Q drops. Gamepad: right trigger click (or stroke), Y turn, X drop. No E, no digits.
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

            if (armed)
            {
                if (stroke) _click.Cancel();
                if (gamepad != null && gamepad.rightTrigger.wasPressedThisFrame) stroke = true;
                Vessel board = Target as Vessel;
                if (stroke) SendKnife(board, VesselRules.ActionStroke);
                bool turn = (mouse != null && locked && mouse.rightButton.wasPressedThisFrame)
                    || (gamepad != null && gamepad.buttonNorth.wasPressedThisFrame);
                if (turn) SendKnife(board, VesselRules.ActionTurn);
            }
            else if (click || (!squeezing && gamepad != null && gamepad.rightTrigger.wasPressedThisFrame))
            {
                // Spec 027b: inside the request interval the click waits (never swallowed).
                Queue(QueuedKind.Click, true);
            }
            else if (((mouse != null && locked && mouse.rightButton.wasPressedThisFrame) || (gamepad != null && gamepad.buttonNorth.wasPressedThisFrame))
                     && InteractionRules.RightClickFlips(CurrentAim) && Target is Vessel)
            {
                // Spec 030: the right click on a griddle spot flips; without the spatula the server answers "Use the spatula".
                // 030 review: through the click buffer like a left click, so a right click inside the request interval waits and is
                // never swallowed.
                Queue(QueuedKind.Flip, true);
            }

            bool drop = keyboard.qKey.wasPressedThisFrame || (gamepad != null && gamepad.buttonWest.wasPressedThisFrame);
            if (drop && InteractionRules.DropKeyActs(true) && held != null) Queue(QueuedKind.Drop, true);
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
            _queuedTarget = TargetId(Target);
            _queuedSpot = TargetSpot;
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
                case QueuedKind.Flip:
                    // 030 review: a right-click flip, sent now or after the request interval (still on the same spot with food).
                    if (InteractionRules.RightClickFlips(CurrentAim) && Target is Vessel spot) FlipWithLook(spot);
                    break;
            }
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
            if (kind == QueuedKind.Flip && TargetId(Target) != _queuedTarget) return; // 030 review: aimed elsewhere since
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
                        if (button.Station.Kind == StationKind.Fryer && FryerRules.TryParseBasketOption(button.Option, out _))
                            PressBasketButton(button); // spec 029b: the intent (lower or raise), then the arc at the click
                        else
                            SendStation(button.Station.Index, button.Option);
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
                    {
                        Pickup held = FindHeldPickup();
                        SendPlace(surface.Index, TargetSpot);
                        if (held != null)
                        {
                            Predict(PredictionKind.Place, held, null);
                            held.PredictRest(PlacePose(held, surface, TargetSpot));
                            PlaySound(SoundRules.PlaceSound(SoundRules.MaterialOf(held), surface.IsBurner), TargetSpotPosition);
                        }
                    }
                    break;
                case InteractionAction.PickUp:
                    if (Target is Vessel cookware) PickUpWithLook(cookware.GetComponent<Pickup>());
                    else if (Target is Pickup pickup) PickUpWithLook(pickup);
                    break;
            }
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
        private void TakeWithLook(StockSlot slot)
        {
            PlaySound(SoundKey.Pick, slot.Anchor);
            SendStation(slot.Station.Index, slot.Option);
            if (_snapshotOf == null) _snapshotOf = Snapshot;
            float now = Time.unscaledTime;
            Worktown.Game.Art.StockTakeLook look = Worktown.Game.Art.StockTakeLook.Begin(slot.transform, HandPose, now, FeelSettings.Current, this);
            if (look == null) return;
            _pendingTake = look;
            string id = slot.IngredientId;
            _pendingTakeName = id != null ? DemoIngredients.All[slot.IngredientIndex].DisplayName : "food";
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

        /// <summary>Spec 027b: a drop request (Q, or E in third person) with its predicted fall in front of you.</summary>
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
        /// server places it (PickupService.TryPlace): its bottom on the spot, its resting tilt, the surface's yaw.</summary>
        private static Pose PlacePose(Pickup item, PlaceSurface surface, int spot)
        {
            Vector3 position = surface.SpotWorldPosition(spot) + surface.transform.up * item.BottomOffset;
            return new Pose(position, PlaceSpots.RestRotation(item.ClientRestTilt, surface.transform.rotation));
        }

        /// <summary>Spec 027b: just above the cookware's contents, where food is dropped in from.</summary>
        private static Pose IntoPose(Pickup item, Vessel vessel)
        {
            return new Pose(vessel.ContentPoint + Vector3.up * FeelSettings.Current.DropHeight,
                PlaceSpots.RestRotation(item.ClientRestTilt, vessel.transform.rotation));
        }

        /// <summary>
        /// Spec 027b, owner side: where the held item would land with a click now (a place spot, or into cookware), for
        /// the placement ghost. False when the click does not put it anywhere.
        /// </summary>
        public bool TryGetLandingPose(out Pickup held, out Pose pose)
        {
            pose = default;
            held = null;
            if (CurrentAction != InteractionAction.Place && CurrentAction != InteractionAction.PutInto) return false;
            held = FindHeldPickup();
            if (held == null || held.HasPrediction) return false;
            if (CurrentAction == InteractionAction.Place && Target is PlaceSurface surface && TargetSpot >= 0)
            {
                pose = PlacePose(held, surface, TargetSpot);
                return true;
            }
            if (CurrentAction == InteractionAction.PutInto && Target is Vessel vessel)
            {
                pose = new Pose(vessel.ContentPoint, PlaceSpots.RestRotation(held.ClientRestTilt, vessel.transform.rotation));
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
            return FeelRules.HandsSnapshot(OwnerId, held.NetworkObject.ObjectId, held.TryGetComponent(out Food _));
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
                    if (look.Build != null) look.Build.EndPrediction(look.BuildToken, true); // spec 031
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
                    look.Build.EndPrediction(look.BuildToken, false); // spec 031: it opens back up
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

        /// <summary>The server's next answer: it reaches only the prediction of that very request (PredictionBook).</summary>
        private int NoteAnswer(AnswerKind kind, bool ok)
        {
            return _looks.Answer(kind, ok, Time.unscaledTime);
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
            if (held.TryGetComponent(out Vessel vessel)) return Held.Cookware(vessel.Kind, vessel.ContentCount, vessel.Title.Value);
            if (held.TryGetComponent(out Food food))
                return Held.Food(food.DisplayName.Value, FoodLookRules.IsFinished(food.Look.Value), // spec 031: a finished build too
                    FoodLookRules.IsBuild(food.Look.Value)); // 031b: a build goes back only on an empty board
            return Held.Item("box");
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
                    return AimTarget.Spot(TargetSpot, surface.IsBurner);
                case Vessel vessel:
                    // Spec 031: the build as this client sees it (its own pending fold or packaging first), so a second click never repeats it.
                    if (vessel.Kind == VesselKind.Assembly && vessel.TryGetComponent(out Worktown.Game.Art.BuildStackView stackView))
                        return AimTarget.Cookware(vessel.Kind, vessel.ContentCount, vessel.Title.Value, stackView.SeenMode);
                    return AimTarget.Cookware(vessel.Kind, vessel.ContentCount, vessel.Title.Value, vessel.Mode.Value);
                case StockSlot slot:
                    if (slot.Station != null && slot.Station.Kind == StationKind.Assembly) return DescribeTool(slot); // spec 031
                    return DescribeStock(slot.IngredientIndex); // spec 028b: a slot takes like a crate
                case Station station:
                    return station.Kind == StationKind.Crate ? DescribeStock(station.IngredientIndex) : AimTarget.OnStation(station.Kind);
                case Pickup pickup:
                    if (pickup.TryGetComponent(out Knife _)) return AimTarget.Item("knife");
                    if (pickup.TryGetComponent(out Spatula _)) return AimTarget.Item("spatula"); // spec 030
                    if (pickup.TryGetComponent(out Food food))
                        return AimTarget.Item(food.DisplayName.Value.Length > 0 ? food.DisplayName.Value : "food");
                    return AimTarget.Item("box");
                default:
                    return AimTarget.Nothing;
            }
        }

        /// <summary>Spec 031: a ray over the assembly board means the tool it passes within this distance of (m); else the board.</summary>
        private const float ToolAimRadius = 0.12f;

        /// <summary>
        /// Spec 031: the tool of the assembly board <paramref name="board"/> the ray passes nearest (within <see cref="ToolAimRadius"/> of
        /// its anchor), or null. The tools have no colliders (they never block a ray), so the ray that aims at a bottle hits the
        /// board's top behind it; the distance to the ray, not to that hit point, says which tool is meant.
        /// </summary>
        private static StockSlot ToolOnRay(Station board, Ray ray)
        {
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
            return best;
        }

        /// <summary>
        /// Spec 031: a tool on the assembly board as the rules see it: the dispenser's sauce or the packaging and its price, the
        /// board's byte, and the build as this client sees it (its layers, and its mode with our own pending step first).
        /// </summary>
        private AimTarget DescribeTool(StockSlot slot)
        {
            Station board = slot.Station;
            int tool = slot.Option;
            byte flags = board.TryGetComponent(out StationContents contents) ? contents.StoveFlags.Value : (byte)0;
            // 031b: our own squeeze's bit is no "busy" for us (the server answers our own repeated press with Ok).
            if (BuildRules.IsDispenser(tool) && OwnSqueeze(board.Index, tool)) flags = BuildRules.WithSqueezing(flags, tool, false);
            Vessel spot = BuildSpotOf(board, out Worktown.Game.Art.BuildStackView view);
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
            bool onDispenser = CurrentAction == InteractionAction.Squeeze && tool != null && tool.Station != null
                && BuildRules.IsDispenser(tool.Option);
            bool mouseDown = mouse != null && locked && mouse.leftButton.isPressed;
            bool padDown = gamepad != null && gamepad.rightTrigger.isPressed;
            bool pressedNow = (mouse != null && locked && _lockedLastFrame && mouse.leftButton.wasPressedThisFrame)
                || (gamepad != null && gamepad.rightTrigger.wasPressedThisFrame);
            return UpdateSqueezeWith(tool, onDispenser, mouseDown || padDown, pressedNow);
        }

        /// <summary>
        /// 031b: third person, holding E (or the gamepad's right trigger) at an assembly board squeezes its nearest dispenser while
        /// the rules offer it. Returns true while that input belongs to the squeeze (no E action is made from it).
        /// </summary>
        private bool UpdateThirdPersonSqueeze(Keyboard keyboard, Gamepad gamepad)
        {
            StockSlot tool = Target is Station board && board.Kind == StationKind.Assembly ? NearestDispenser(board) : null;
            bool onDispenser = tool != null
                && InteractionRules.ActionFor(Hands(FindHeldPickup()), DescribeTool(tool)) == InteractionAction.Squeeze;
            bool down = (keyboard != null && keyboard.eKey.isPressed) || (gamepad != null && gamepad.rightTrigger.isPressed);
            bool pressedNow = (keyboard != null && keyboard.eKey.wasPressedThisFrame)
                || (gamepad != null && gamepad.rightTrigger.wasPressedThisFrame);
            return UpdateSqueezeWith(tool, onDispenser, down, pressedNow);
        }

        /// <summary>031b: the dispenser of <paramref name="board"/> nearest to the player, or null.</summary>
        private StockSlot NearestDispenser(Station board)
        {
            RefreshInteractables();
            StockSlot best = null;
            float bestSqr = float.MaxValue;
            foreach (StockSlot slot in _allSlots)
            {
                if (slot == null || slot.Station != board || !BuildRules.IsDispenser(slot.Option)) continue;
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
                }
            }
            else if (onDispenser && pressedNow && _squeeze.Press(tool.Station.Index, tool.Option, now))
            {
                _squeezeView = tool.GetComponent<Worktown.Game.Art.SauceDispenserView>();
                if (_squeezeView != null) _squeezeView.Predict(true); // the look in the press's frame
            }

            PollSqueeze();
            return _squeeze.Held || onDispenser;
        }

        private bool _devSqueeze;
        // 031b: the request number of the last squeeze start and its dispenser's view (a refusal ends the predicted ribbon at once).
        private int _squeezeStartRequest;
        private Worktown.Game.Art.SauceDispenserView _squeezeStartView;
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
                    SendStation(_squeeze.SendBoard, BuildRules.SqueezeStartOption(_squeeze.SendDispenser));
                    _squeezeStartRequest = _lastRequest;
                    _squeezeStartView = DispenserView(_squeeze.SendBoard, _squeeze.SendDispenser);
                    break;
                case SqueezeHold.Send.Stop:
                    SendStation(_squeeze.SendBoard, BuildRules.SqueezeStopOption(_squeeze.SendDispenser));
                    _ownStopBoard = _squeeze.SendBoard;
                    _ownStopDispenser = _squeeze.SendDispenser;
                    _ownStopUntil = Time.unscaledTime + Mathf.Max(1f, 2f * RoundTripSeconds());
                    if (_devSqueeze && _squeeze.Idle) _devSqueeze = false;
                    break;
            }
            return _squeeze.Held;
        }

        /// <summary>031b: the board's squeezing bit for <paramref name="dispenser"/> is our own squeeze (held, waiting, or its stop
        /// still on its way), so the busy rule never refuses it to us.</summary>
        private bool OwnSqueeze(int board, int dispenser)
        {
            if (_squeeze.Involves(board, dispenser)) return true;
            return board == _ownStopBoard && dispenser == _ownStopDispenser && Time.unscaledTime < _ownStopUntil;
        }

        /// <summary>031b: the view of dispenser <paramref name="dispenser"/> of board <paramref name="board"/>, or null.</summary>
        private Worktown.Game.Art.SauceDispenserView DispenserView(int board, int dispenser)
        {
            RefreshInteractables();
            foreach (StockSlot slot in _allSlots)
                if (slot != null && slot.Station != null && slot.Station.Index == board && slot.Option == dispenser)
                    return slot.GetComponent<Worktown.Game.Art.SauceDispenserView>();
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
            Component exact = FindExactTarget(holding, out bool direct, out Ray ray);
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

        private Component Stick(Component target)
        {
            _sticky = TargetTooFar ? null : target;
            _stickySpot = target is PlaceSurface ? TargetSpot : PlaceSpots.None;
            return target;
        }

        /// <summary>Spec 018–021: the exact camera-centre ray (see <see cref="FindAimTarget"/>); <paramref name="direct"/>
        /// when it landed on the target's body.</summary>
        private Component FindExactTarget(bool holding, out bool direct, out Ray ray)
        {
            direct = false;
            ray = default;
            TargetSpot = PlaceSpots.None;
            TargetTooFar = false;
            Camera cam = Camera.main;
            if (cam == null) return null;
            ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            int count = Physics.RaycastNonAlloc(ray, _rayHits, ViewRules.AimRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            _aimHits.Clear();
            _aimSources.Clear();
            _aimPoints.Clear();
            for (int i = 0; i < count; i++)
            {
                Collider hitCollider = _rayHits[i].collider;
                if (hitCollider == null || hitCollider.transform.IsChildOf(transform)) continue;
                float distance = _rayHits[i].distance;
                // Spec 019: a knob or button on a stove front; reach is measured to its station, like the server does.
                if (hitCollider.TryGetComponent(out StationButton button) && button.Station != null)
                {
                    _aimHits.Add(AimHit.Button(distance, button.Station.transform.position, button.Station.Index, button.Option));
                    _aimSources.Add(button);
                    _aimPoints.Add(_rayHits[i].point);
                    continue;
                }
                // Spec 020: a worktop top, the stove top or a burner (checked before its station).
                if (hitCollider.TryGetComponent(out PlaceSurface surface))
                {
                    Vector3 point = _rayHits[i].point;
                    int spot = NearestFreeSpot(surface, point, out Vector3 spotPosition);
                    _aimHits.Add(AimHit.Spot(distance, spot >= 0 ? spotPosition : point, surface.Index, spot));
                    _aimSources.Add(surface);
                    _aimPoints.Add(point);
                    continue;
                }
                // Spec 029: a fryer basket (a fixed vessel, no Pickup); its collider moves with its arc.
                Vessel fixedVessel = hitCollider.GetComponentInParent<Vessel>();
                if (fixedVessel != null && VesselRules.IsFixed(fixedVessel.Kind)) // spec 030: a griddle spot too
                {
                    if (fixedVessel.IsSpawned) AddAimHit(distance, AimKind.Vessel, fixedVessel);
                    else AddAimHit(distance, AimKind.Other, null);
                    _aimPoints.Add(_rayHits[i].point);
                    continue;
                }
                Station station = hitCollider.GetComponentInParent<Station>();
                Pickup pickup = station == null ? hitCollider.GetComponentInParent<Pickup>() : null;
                StockSlot slot = station != null && station.SlotCount > 0 ? station.NearestSlot(_rayHits[i].point) : null;
                // Spec 031: on the assembly board a ray means a tool only near it (elsewhere it is the board itself).
                if (station != null && station.Kind == StationKind.Assembly) slot = ToolOnRay(station, ray);
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
                    AddAimHit(distance, AimKind.Other, null);
                _aimPoints.Add(_rayHits[i].point);
            }
            int picked = AimRules.Pick(_aimHits, transform.position, holding, out int outOfReach);
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
                if (slot != null && slot.Station != null && (!hands.IsHolding || slot.Station.Kind == StationKind.Assembly)) // spec 031: tools with any hands
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
                        AddCone(ray, surface, spot, anchor, hands, AimTarget.Spot(spot, surface.IsBurner), range, feel.AimCone);
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
            int count = Physics.RaycastNonAlloc(new Ray(eye, to.normalized), _sightHits, distance, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider c = _sightHits[i].collider;
                if (c == null || c.transform.IsChildOf(transform)) continue;
                if (self != null && c.transform.IsChildOf(self)) continue;
                if (c.GetComponentInParent<Pickup>() != null || c.TryGetComponent(out PlaceSurface _) || c.TryGetComponent(out StationButton _)) continue;
                if (own != null && c.GetComponentInParent<Station>() == own) continue;
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

        /// <summary>Third person (spec 007/021): E at a crate or the shelf with empty hands, at the counter holding something.</summary>
        private bool TryHandleStationKey(Station station, bool holding)
        {
            switch (station.Kind)
            {
                case StationKind.Crate:
                case StationKind.PlateShelf:
                    if (holding) return false;
                    PlaySound(SoundKey.Pick, station.transform.position);
                    SendStation(station.Index, 0);
                    return true;
                case StationKind.Counter:
                    if (!holding) return false;
                    PlaySound(SoundKey.Button, station.transform.position);
                    SendStation(station.Index, 0);
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
            int count = Physics.OverlapSphereNonAlloc(transform.position, ClientSearchRadius, _hits);
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
            _lastRequest = _looks.Sent(_lastSendTime, RoundTripSeconds());
            RequestStation(stationIndex, option);
        }

        private void SendPickup(NetworkObject target)
        {
            _lastSendTime = Time.unscaledTime;
            _clicks.NoteSent(_lastSendTime);
            _lastRequest = _looks.Sent(_lastSendTime, RoundTripSeconds());
            RequestPickup(target);
        }

        private void SendDrop()
        {
            _lastSendTime = Time.unscaledTime;
            _clicks.NoteSent(_lastSendTime);
            _lastRequest = _looks.Sent(_lastSendTime, RoundTripSeconds());
            RequestDrop();
        }

        private void SendPlace(int surfaceIndex, int spotIndex)
        {
            _lastSendTime = Time.unscaledTime;
            _clicks.NoteSent(_lastSendTime);
            _lastRequest = _looks.Sent(_lastSendTime, RoundTripSeconds());
            RequestPlace(surfaceIndex, spotIndex);
        }

        private void SendUse(NetworkObject target, int action)
        {
            _lastSendTime = Time.unscaledTime;
            _clicks.NoteSent(_lastSendTime);
            _lastRequest = _looks.Sent(_lastSendTime, RoundTripSeconds());
            RequestUse(target, action);
        }

        [ServerRpc]
        private void RequestPickup(NetworkObject target)
        {
            PickupOutcome outcome = IsRateLimited() ? PickupOutcome.TooFast
                : _service != null ? _service.TryPickup(this, target) : PickupOutcome.NotFound;
            ReportResult(Owner, outcome, true);
        }

        [ServerRpc]
        private void RequestDrop()
        {
            PickupOutcome outcome = IsRateLimited() ? PickupOutcome.TooFast
                : _service != null ? _service.TryDrop(this) : PickupOutcome.NothingHeld;
            ReportResult(Owner, outcome, false);
        }

        /// <summary>
        /// Spec 020: put the held item on spot <paramref name="spotIndex"/> of place surface
        /// <paramref name="surfaceIndex"/> (spec 021: also a burner). Two ints, never a position: the server checks
        /// hands, surface, spot, reach and the rate, and places the item itself (PickupService.TryPlace).
        /// </summary>
        [ServerRpc]
        private void RequestPlace(int surfaceIndex, int spotIndex)
        {
            PickupOutcome outcome = IsRateLimited() ? PickupOutcome.TooFast
                : _service != null ? _service.TryPlace(this, surfaceIndex, spotIndex) : PickupOutcome.NotFound;
            ReportPlace(Owner, outcome);
        }

        [TargetRpc]
        private void ReportPlace(NetworkConnection target, PickupOutcome outcome)
        {
            // Review of spec 027b: a rate-limited request is answered too (numbering in step) but shows nothing.
            NoteAnswer(AnswerKind.Place, outcome == PickupOutcome.Ok);
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
        private void RequestStation(int stationIndex, int option)
        {
            CookOutcome outcome = IsRateLimited() ? CookOutcome.TooFast
                : _kitchen != null ? _kitchen.TryUseStation(this, stationIndex, option) : CookOutcome.UnknownStation;
            ReportCook(Owner, outcome);
        }

        /// <summary>
        /// Spec 021: use what you hold on the cookware <paramref name="target"/>: <paramref name="action"/> 0 use (put
        /// into, pour, take out), 1 one knife stroke, 2 turn the food. An object and an int only: the server reads what
        /// the player holds and the target's state, checks reach, hands, capacity and the sim, and decides.
        /// </summary>
        [ServerRpc]
        private void RequestUse(NetworkObject target, int action)
        {
            CookOutcome outcome = IsRateLimited() ? CookOutcome.TooFast
                : _kitchen != null ? _kitchen.TryUseVessel(this, target, action) : CookOutcome.NotAVessel;
            ReportCook(Owner, outcome);
        }

        [TargetRpc]
        private void ReportCook(NetworkConnection target, CookOutcome outcome)
        {
            int answered = NoteAnswer(AnswerKind.Cook, outcome == CookOutcome.Ok);
            if (answered != 0 && answered == _squeezeStartRequest)
            {
                // 031b (review of 031): a refused squeeze start ends the predicted ribbon at once (not after its 8 s hold).
                _squeezeStartRequest = 0;
                if (outcome != CookOutcome.Ok && _squeezeStartView != null) _squeezeStartView.Predict(false);
            }
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
        /// (one small report, the same RPCs) so the client's request numbering never shifts.
        /// </summary>
        private bool IsRateLimited()
        {
            return !_serverBucket.TryTake(Time.unscaledTimeAsDouble);
        }

        [TargetRpc]
        private void ReportResult(NetworkConnection target, PickupOutcome outcome, bool wasPickup)
        {
            NoteAnswer(wasPickup ? AnswerKind.Pickup : AnswerKind.Drop, outcome == PickupOutcome.Ok);
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
        /// <summary>Spec 031, tours: press a sauce dispenser like a held left click (the look now, the start when allowed).</summary>
        public void DevSqueezePress(StockSlot dispenser)
        {
            if (dispenser == null || dispenser.Station == null
                || !_squeeze.Press(dispenser.Station.Index, dispenser.Option, Time.unscaledTimeAsDouble)) return;
            _devSqueeze = true;
            _squeezeView = dispenser.GetComponent<Worktown.Game.Art.SauceDispenserView>();
            if (_squeezeView != null) _squeezeView.Predict(true);
        }
        /// <summary>Spec 031, tours: let go of the dispenser (the look ends now, the stop goes out when allowed).</summary>
        public void DevSqueezeRelease()
        {
            _squeeze.Release(Time.unscaledTimeAsDouble);
            if (_squeezeView != null) _squeezeView.Predict(false);
        }
        /// <summary>Spec 031, tours: the squeeze still has a request to send.</summary>
        public bool DevSqueezeBusy => !_squeeze.Idle;
#endif
    }
}
