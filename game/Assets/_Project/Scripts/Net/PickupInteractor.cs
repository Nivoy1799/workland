using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
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
    /// </summary>
    public sealed class PickupInteractor : NetworkBehaviour
    {
        private const float ClientSearchRadius = 4f;
        private const float MinRequestInterval = 0.2f;
        // Spec 019: the client waits a little longer than the server's limit, so no stroke is silently dropped.
        private const float ClientStrokeInterval = MinRequestInterval + 0.03f;
        private const string DemoCookwareArg = "-demo-cookware";

        // Server only: time of the last accepted request from this player.
        private float _serverLastRequestTime = float.NegativeInfinity;

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
            _heldView = GetComponent<HeldItemView>();
            // Spec 020: owner-only highlight and world label fade.
            if (TryGetComponent(out InteractionHighlight highlight)) highlight.Activate(this);
            if (TryGetComponent(out WorldLabelFade labels)) labels.Activate();
            if (!Debug.isDebugBuild) return;
            // Spec 021 AC8, development builds only: move the pan, fill the pot, switch it on, with the normal requests.
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), DemoCookwareArg) >= 0)
                StartCoroutine(DemoCookware());
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            SetLookSuppressed(false);
        }

        private void Update()
        {
            if (!IsOwner) return;
            if (_rig == null) _rig = FindFirstObjectByType<PlayerCameraRig>();
            bool firstPerson = IsFirstPerson;
            Pickup held = FindHeldPickup();
            // Spec 018: first person aims with the camera centre; third person keeps the nearest station.
            if (!firstPerson) TargetTooFar = false;
            Target = firstPerson ? FindAimTarget(held != null) : FindNearestStation();
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
                _gesture.Reset();
                // A press that the UI or the wheel had never becomes a click.
                _click.Update(mouse != null && mouse.leftButton.isPressed, 0f, 0f, Time.unscaledTime);
                _click.Cancel();
                if (wheel) _suppressingLook = false;
                else SetLookSuppressed(false);
            }
            else if (firstPerson)
            {
                UpdateFirstPerson(keyboard, gamepad, mouse, locked, held);
            }
            else
            {
                _gesture.Reset();
                _click.Reset();
                SetLookSuppressed(false);
                // Spec 020 AC7: E acts in third person only.
                if (InteractionRules.KeysActInView(firstPerson)) HandleThirdPersonKeys(keyboard, held != null);
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
            CurrentHeld = Describe(held);
            CurrentAim = TargetTooFar ? Describe(Target).TooFar() : Describe(Target);
            CurrentAction = InteractionRules.ActionFor(CurrentHeld, CurrentAim);
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
            else if (click || (gamepad != null && gamepad.rightTrigger.wasPressedThisFrame))
            {
                ExecuteAction();
            }

            bool drop = keyboard.qKey.wasPressedThisFrame || (gamepad != null && gamepad.buttonWest.wasPressedThisFrame);
            if (drop && InteractionRules.DropKeyActs(true) && held != null) SendDrop();
        }

        /// <summary>Spec 020/021: sends the request for <see cref="CurrentAction"/> on <see cref="Target"/>. Nothing for None or Cut.</summary>
        private void ExecuteAction()
        {
            switch (CurrentAction)
            {
                case InteractionAction.Press:
                    if (Target is StationButton button && button.Station != null) SendStation(button.Station.Index, button.Option);
                    break;
                case InteractionAction.PutInto:
                case InteractionAction.Pour:
                case InteractionAction.Take:
                    if (Target is Vessel vessel) SendUse(vessel.NetworkObject, VesselRules.ActionUse);
                    else if (Target is Station shelf) SendStation(shelf.Index, 0); // the plate shelf
                    break;
                case InteractionAction.Sell:
                case InteractionAction.Buy:
                    if (Target is Station station) SendStation(station.Index, 0);
                    break;
                case InteractionAction.Place:
                    if (Target is PlaceSurface surface && TargetSpot >= 0) SendPlace(surface.Index, TargetSpot);
                    break;
                case InteractionAction.PickUp:
                    if (Target is Vessel cookware) SendPickup(cookware.NetworkObject);
                    else if (Target is Pickup pickup) SendPickup(pickup.NetworkObject);
                    break;
            }
        }

        private void SendKnife(Vessel board, int action)
        {
            if (board == null) return;
            // The server ignores requests closer than 0.2 s; skip it here too, so the chop never lies.
            if (Time.unscaledTime - _lastSendTime < ClientStrokeInterval) return;
            SendUse(board.NetworkObject, action);
            if (action == VesselRules.ActionStroke && _heldView != null) _heldView.PlayChop();
        }

        /// <summary>Spec 020/021: the held item as InteractionRules sees it (synced names and looks only).</summary>
        private static Held Describe(Pickup held)
        {
            if (held == null) return Held.Nothing;
            if (held.TryGetComponent(out Knife _)) return Held.Knife;
            if (held.TryGetComponent(out Vessel vessel)) return Held.Cookware(vessel.Kind, vessel.ContentCount, vessel.Title.Value);
            if (held.TryGetComponent(out Food food))
                return Held.Food(food.DisplayName.Value, food.Look.Value.Kind == FoodLook.KindPlated);
            return Held.Item("box");
        }

        /// <summary>Spec 020/021: the target as InteractionRules sees it (what every client can see of it).</summary>
        private AimTarget Describe(Component target)
        {
            switch (target)
            {
                case StationButton button:
                    byte flags = button.Station != null && button.Station.TryGetComponent(out StationContents stove) ? stove.StoveFlags.Value : (byte)0;
                    return AimTarget.Button(button.Option, flags);
                case PlaceSurface surface:
                    return AimTarget.Spot(TargetSpot, surface.IsBurner);
                case Vessel vessel:
                    return AimTarget.Cookware(vessel.Kind, vessel.ContentCount, vessel.Title.Value);
                case Station station:
                    return station.Kind == StationKind.Crate ? DescribeCrate(station) : AimTarget.OnStation(station.Kind);
                case Pickup pickup:
                    if (pickup.TryGetComponent(out Knife _)) return AimTarget.Item("knife");
                    if (pickup.TryGetComponent(out Food food))
                        return AimTarget.Item(food.DisplayName.Value.Length > 0 ? food.DisplayName.Value : "food");
                    return AimTarget.Item("box");
                default:
                    return AimTarget.Nothing;
            }
        }

        private AimTarget DescribeCrate(Station crate)
        {
            int i = crate.IngredientIndex;
            if (i < 0 || i >= DemoIngredients.All.Count) return AimTarget.Crate(null, -1);
            string id = DemoIngredients.All[i].Id;
            _prices ??= Worktown.Sim.Economy.EconomyCatalog.CreateDefault();
            return AimTarget.Crate(id, _prices.TryGetGrocerPrice(id, out long price) ? price : -1);
        }

        private void HandleThirdPersonKeys(Keyboard keyboard, bool holding)
        {
            if (!keyboard.eKey.wasPressedThisFrame) return;
            // Spec 007/021: near a crate, the counter or the plate shelf, E goes to the kitchen.
            Station station = Target as Station;
            if (station != null && TryHandleStationKey(station, holding)) return;

            if (holding)
            {
                SendDrop();
                return;
            }
            Pickup nearest = FindNearestFreePickup();
            if (nearest == null)
                SetLocalNote("nothing to pick up within " + ClientSearchRadius + " m");
            else
                SendPickup(nearest.NetworkObject);
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
        /// </summary>
        private Component FindAimTarget(bool holding)
        {
            TargetSpot = PlaceSpots.None;
            TargetTooFar = false;
            Camera cam = Camera.main;
            if (cam == null) return null;
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            int count = Physics.RaycastNonAlloc(ray, _rayHits, ViewRules.AimRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            _aimHits.Clear();
            _aimSources.Clear();
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
                    continue;
                }
                // Spec 020: a worktop top, the stove top or a burner (checked before its station).
                if (hitCollider.TryGetComponent(out PlaceSurface surface))
                {
                    Vector3 point = _rayHits[i].point;
                    int spot = NearestFreeSpot(surface, point, out Vector3 spotPosition);
                    _aimHits.Add(AimHit.Spot(distance, spot >= 0 ? spotPosition : point, surface.Index, spot));
                    _aimSources.Add(surface);
                    continue;
                }
                Station station = hitCollider.GetComponentInParent<Station>();
                Pickup pickup = station == null ? hitCollider.GetComponentInParent<Pickup>() : null;
                if (station != null)
                    AddAimHit(distance, AimKind.Station, station);
                else if (pickup != null && !pickup.IsHeld && pickup.TryGetComponent(out Vessel vessel))
                    AddAimHit(distance, AimKind.Vessel, vessel); // spec 021
                else if (pickup != null && !pickup.IsHeld)
                    AddAimHit(distance, AimKind.Pickup, pickup);
                else
                    AddAimHit(distance, AimKind.Other, null);
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
            return _aimSources[picked];
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
                _spotTaken[i] = i < count && PickupService.IsSpotOccupied(surface.SpotWorldPosition(i), _hits);
            int spot = PlaceSpots.NearestFree(surface.Size, surface.ToTopPlane(point), _spotTaken);
            if (spot >= 0) spotPosition = surface.SpotWorldPosition(spot);
            return spot;
        }

        private void AddAimHit(float distance, AimKind kind, Component source)
        {
            _aimHits.Add(new AimHit(distance, kind, source != null ? source.transform.position : Vector3.zero));
            _aimSources.Add(source);
        }

        /// <summary>Third person (spec 007/021): E at a crate or the shelf with empty hands, at the counter holding something.</summary>
        private bool TryHandleStationKey(Station station, bool holding)
        {
            switch (station.Kind)
            {
                case StationKind.Crate:
                case StationKind.PlateShelf:
                    if (holding) return false;
                    SendStation(station.Index, 0);
                    return true;
                case StationKind.Counter:
                    if (!holding) return false;
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
            RequestStation(stationIndex, option);
        }

        private void SendPickup(NetworkObject target)
        {
            _lastSendTime = Time.unscaledTime;
            RequestPickup(target);
        }

        private void SendDrop()
        {
            _lastSendTime = Time.unscaledTime;
            RequestDrop();
        }

        private void SendPlace(int surfaceIndex, int spotIndex)
        {
            _lastSendTime = Time.unscaledTime;
            RequestPlace(surfaceIndex, spotIndex);
        }

        private void SendUse(NetworkObject target, int action)
        {
            _lastSendTime = Time.unscaledTime;
            RequestUse(target, action);
        }

        [ServerRpc]
        private void RequestPickup(NetworkObject target)
        {
            if (IsRateLimited()) return;
            PickupOutcome outcome = _service != null ? _service.TryPickup(this, target) : PickupOutcome.NotFound;
            ReportResult(Owner, outcome, true);
        }

        [ServerRpc]
        private void RequestDrop()
        {
            if (IsRateLimited()) return;
            PickupOutcome outcome = _service != null ? _service.TryDrop(this) : PickupOutcome.NothingHeld;
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
            if (IsRateLimited()) return;
            PickupOutcome outcome = _service != null ? _service.TryPlace(this, surfaceIndex, spotIndex) : PickupOutcome.NotFound;
            ReportPlace(Owner, outcome);
        }

        [TargetRpc]
        private void ReportPlace(NetworkConnection target, PickupOutcome outcome)
        {
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
            if (IsRateLimited()) return;
            CookOutcome outcome = _kitchen != null ? _kitchen.TryUseStation(this, stationIndex, option) : CookOutcome.UnknownStation;
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
            if (IsRateLimited()) return;
            CookOutcome outcome = _kitchen != null ? _kitchen.TryUseVessel(this, target, action) : CookOutcome.NotAVessel;
            ReportCook(Owner, outcome);
        }

        [TargetRpc]
        private void ReportCook(NetworkConnection target, CookOutcome outcome)
        {
            LastPickupOutcome = null;
            LastCookOutcome = outcome;
            LastResult = "kitchen: " + outcome;
            if (outcome != CookOutcome.Ok)
                Debug.Log("[Kitchen] " + LastResult);
        }

        /// <summary>Server: drop requests that come faster than a person can click.
        /// Spam then costs neither validation work nor reply bandwidth. Spec 019: this also limits knife
        /// strokes to one per 0.2 s per player; extra strokes are ignored, not errors.</summary>
        private bool IsRateLimited()
        {
            float now = Time.unscaledTime;
            if (now - _serverLastRequestTime < MinRequestInterval) return true;
            _serverLastRequestTime = now;
            return false;
        }

        [TargetRpc]
        private void ReportResult(NetworkConnection target, PickupOutcome outcome, bool wasPickup)
        {
            LastCookOutcome = null;
            LastPickupOutcome = outcome;
            LastPickupWasPlace = false;
            LastResult = (wasPickup ? "pickup: " : "drop: ") + outcome;
            if (outcome != PickupOutcome.Ok)
                Debug.Log("[Pickup] " + LastResult);
        }

        // ---- Spec 021 AC8: dev-only intent script for the screenshot run (development builds only) ----

        // PlayerCameraRig's mouse sensitivity (degrees per count); the aim loop below only needs it roughly.
        private const float DemoDegreesPerCount = 0.12f;

        /// <summary>
        /// <c>-demo-cookware</c>: the owner walks with simulated W/A/S/D keys and turns the first-person view with
        /// simulated mouse movement (a screenshot run has no real input), and each step aims and does what a click
        /// would do (<see cref="ExecuteAction"/>, so the normal RequestPickup / RequestPlace / RequestStation /
        /// RequestUse go out; no server shortcut). Steps: pick up the pan from the first stove, put it on the second
        /// stove's free burner, take a potato and put it on that stove's counter next to the pan, take a potato and an
        /// onion and put both into the pot, switch the pot's burner on, then look at the stove for 40 s. If an aim
        /// does not give the expected action in 3 s it logs a warning and sends the same request directly. Expects the
        /// first spawn point; needs <c>-view first -skip-whatsnew</c> and window focus (one client only).
        /// </summary>
        private System.Collections.IEnumerator DemoCookware()
        {
            yield return new WaitForSeconds(6f);
            Station potatoCrate = null, onionCrate = null;
            var stoves = new List<Station>();
            foreach (Station s in FindObjectsByType<Station>(FindObjectsSortMode.None))
            {
                if (s.Kind == StationKind.Crate && s.IngredientIndex == 0) potatoCrate = s;
                else if (s.Kind == StationKind.Crate && s.IngredientIndex == 3) onionCrate = s;
                else if (s.Kind == StationKind.Stove) stoves.Add(s);
            }
            stoves.Sort((a, b) => a.transform.position.x.CompareTo(b.transform.position.x));
            Station stoveA = stoves.Count > 0 ? stoves[0] : null;
            Station stoveB = stoves.Count > 1 ? stoves[1] : null;
            Vessel pan = null, pot = null;
            foreach (Vessel v in FindObjectsByType<Vessel>(FindObjectsSortMode.None))
            {
                if (v.Kind == VesselKind.Pan) pan = v;
                else if (v.Kind == VesselKind.Pot) pot = v;
            }
            if (potatoCrate == null || onionCrate == null || stoveA == null || stoveB == null || pan == null || pot == null)
            {
                Debug.LogWarning("[Demo] crates, two stoves, the pan or the pot not found");
                yield break;
            }
            PlaceSurface freeBurner = null, stoveTop = null;
            foreach (PlaceSurface s in FindObjectsByType<PlaceSurface>(FindObjectsSortMode.None))
            {
                if (s.GetComponentInParent<Station>() != stoveB) continue;
                if (!s.IsBurner) stoveTop = s;
                else if (Vector3.Distance(s.SpotWorldPosition(0), pot.transform.position) > 0.2f) freeBurner = s;
            }
            PlaceSurface potBurner = null;
            foreach (PlaceSurface s in FindObjectsByType<PlaceSurface>(FindObjectsSortMode.None))
                if (s.IsBurner && Vector3.Distance(s.SpotWorldPosition(0), pot.transform.position) <= 0.2f) potBurner = s;
            StationButton potSwitch = null;
            foreach (StationButton b in FindObjectsByType<StationButton>(FindObjectsSortMode.None))
                if (potBurner != null && b.Station != null && b.Station.Index == potBurner.StoveIndex
                    && b.Option == StationRules.StoveOption(potBurner.Burner, StationRules.StoveSwitch)) potSwitch = b;
            if (freeBurner == null || stoveTop == null || potSwitch == null)
            {
                Debug.LogWarning("[Demo] the second stove has no free burner, no stove top or no switch for the pot");
                yield break;
            }

            float laneZ = stoveA.transform.position.z - 1.7f;
            float standZ = stoveA.transform.position.z - 1.3f;
            Vector3 atA = new Vector3(stoveA.transform.position.x, 0f, standZ);
            Vector3 atB = new Vector3(stoveB.transform.position.x, 0f, standZ);
            Debug.Log("[Demo] walking to the first stove (" + Time.time.ToString("0.0") + " s)");
            yield return DemoWalk(new Vector3(-1.75f, 0f, transform.position.z));
            yield return DemoWalk(new Vector3(-1.75f, 0f, laneZ));
            yield return DemoWalk(new Vector3(atA.x, 0f, laneZ));
            yield return DemoWalk(atA);
            NetworkObject panObject = pan.NetworkObject;
            yield return DemoUse("pick up the pan", ContentPoint(pan), InteractionAction.PickUp, () => SendPickup(panObject));

            yield return DemoLevelView();
            yield return DemoWalk(atB);
            Vector3 burnerSpot = freeBurner.SpotWorldPosition(0);
            yield return DemoUse("put the pan on the free burner", burnerSpot, InteractionAction.Place, () => SendPlace(freeBurner.Index, 0));
            Debug.Log("[Demo] the pan is on the burner (" + Time.time.ToString("0.0") + " s)");

            yield return DemoBuy("take a potato", potatoCrate, laneZ);
            yield return DemoWalk(new Vector3(atB.x, 0f, laneZ));
            yield return DemoWalk(atB);
            int nextToPan = PlaceSpots.NearestFree(stoveTop.Size, stoveTop.ToTopPlane(burnerSpot), null);
            yield return DemoUse("put the potato on the stove top next to the pan", stoveTop.SpotWorldPosition(nextToPan),
                InteractionAction.Place, () => SendPlace(stoveTop.Index, nextToPan));
            Debug.Log("[Demo] potato next to the pan (" + Time.time.ToString("0.0") + " s)");

            NetworkObject potObject = pot.NetworkObject;
            foreach (Station crate in new[] { potatoCrate, onionCrate })
            {
                yield return DemoBuy("take " + DemoIngredients.All[crate.IngredientIndex].Id, crate, laneZ);
                yield return DemoWalk(new Vector3(atB.x, 0f, laneZ));
                yield return DemoWalk(atB);
                yield return DemoUse("put it into the pot", ContentPoint(pot), InteractionAction.PutInto,
                    () => SendUse(potObject, VesselRules.ActionUse));
            }
            yield return DemoUse("switch the pot's burner on", potSwitch.transform.position, InteractionAction.Press,
                () => SendStation(potSwitch.Station.Index, potSwitch.Option));
            Debug.Log("[Demo] cookware demo done (" + Time.time.ToString("0.0") + " s): pot holds " + pot.ContentCount + " ingredients");
            yield return DemoAim((pan.transform.position + pot.transform.position) * 0.5f + Vector3.up * 0.1f, 40f, null);
        }

        /// <summary>Walks along the lane to the crate and takes one (aims at it, clicks; or sends the request directly).</summary>
        private System.Collections.IEnumerator DemoBuy(string what, Station crate, float laneZ)
        {
            yield return DemoLevelView();
            yield return DemoWalk(new Vector3(transform.position.x, 0f, laneZ));
            yield return DemoWalk(new Vector3(crate.transform.position.x + 1f, 0f, laneZ));
            yield return DemoUse(what, crate.transform.position, InteractionAction.Buy, () => SendStation(crate.Index, 0));
            yield return DemoLevelView();
        }

        /// <summary>Where a vessel draws its contents (inside the pot, on the board); its position if none.</summary>
        private static Vector3 ContentPoint(Component vessel)
        {
            Transform anchor = vessel.transform.Find("Contents");
            return anchor != null ? anchor.position : vessel.transform.position + Vector3.up * 0.05f;
        }

        /// <summary>Aims at <paramref name="point"/>, then does what a click would do (or sends the request directly).</summary>
        private System.Collections.IEnumerator DemoUse(string what, Vector3 point, InteractionAction expected, System.Action fallback)
        {
            Debug.Log("[Demo] " + what + " (" + Time.time.ToString("0.0") + " s)");
            yield return DemoAim(point, 3f, expected);
            if (CurrentAction == expected)
            {
                Debug.Log("[Demo] click: '" + InteractionRules.PromptFor(CurrentHeld, CurrentAim) + "'");
                ExecuteAction();
            }
            else
            {
                Debug.LogWarning("[Demo] aiming gave " + CurrentAction + " instead of " + expected + "; sending the request directly");
                fallback();
            }
            yield return new WaitForSeconds(1.5f);
        }

        /// <summary>Holds the movement keys towards <paramref name="target"/> (relative to the camera) until there.</summary>
        private System.Collections.IEnumerator DemoWalk(Vector3 target)
        {
            const float Arrived = 0.25f;
            float giveUp = Time.unscaledTime + 20f;
            Keyboard keyboard = Keyboard.current;
            while (keyboard != null && Time.unscaledTime < giveUp)
            {
                Vector3 delta = target - transform.position;
                delta.y = 0f;
                if (delta.magnitude < Arrived) break;
                Camera cam = Camera.main;
                Vector3 forward = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized : transform.forward;
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                float ahead = Vector3.Dot(delta, forward);
                float side = Vector3.Dot(delta, right);
                var keys = new List<Key>(2);
                if (ahead > Arrived * 0.5f) keys.Add(Key.W);
                else if (ahead < -Arrived * 0.5f) keys.Add(Key.S);
                if (side > Arrived * 0.5f) keys.Add(Key.D);
                else if (side < -Arrived * 0.5f) keys.Add(Key.A);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys.ToArray()));
                yield return null;
            }
            if (keyboard != null) InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return new WaitForSeconds(0.3f);
        }

        /// <summary>Looks straight ahead (towards the kitchen, +Z) again before walking (the walk steers by the camera's forward).</summary>
        private System.Collections.IEnumerator DemoLevelView()
        {
            Camera cam = Camera.main;
            if (cam == null) yield break;
            yield return DemoAim(cam.transform.position + Vector3.forward * 5f, 1.5f, null);
        }

        /// <summary>
        /// Turns the first-person view towards <paramref name="point"/> with simulated mouse movement, for up to
        /// <paramref name="seconds"/>; stops early once on target and (if given) the action is <paramref name="until"/>.
        /// </summary>
        private System.Collections.IEnumerator DemoAim(Vector3 point, float seconds, InteractionAction? until)
        {
            float end = Time.unscaledTime + seconds;
            Mouse mouse = Mouse.current;
            while (mouse != null && Time.unscaledTime < end)
            {
                Camera cam = Camera.main;
                if (cam == null) yield break;
                Vector3 dir = point - cam.transform.position;
                if (dir.sqrMagnitude < 0.0001f) yield break;
                dir.Normalize();
                Vector3 forward = cam.transform.forward;
                float yawError = Mathf.DeltaAngle(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg);
                float downError = (Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) - Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f))) * Mathf.Rad2Deg;
                bool onTarget = Mathf.Abs(yawError) < 0.4f && Mathf.Abs(downError) < 0.4f;
                if (until.HasValue && onTarget && CurrentAction == until.Value) yield break;
                if (!onTarget)
                {
                    // Half the error per frame: converges without overshoot even if the sensitivity is a bit off.
                    var counts = new Vector2(Mathf.Clamp(yawError * 0.5f / DemoDegreesPerCount, -60f, 60f),
                        Mathf.Clamp(-downError * 0.5f / DemoDegreesPerCount, -60f, 60f));
                    InputSystem.QueueStateEvent(mouse, new MouseState { delta = counts });
                }
                yield return null;
            }
        }
    }
}
