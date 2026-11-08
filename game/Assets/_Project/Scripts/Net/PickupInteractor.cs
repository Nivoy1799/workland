using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;
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
            // Spec 021 AC8 -demo-cookware moved to Scripts/Dev (spec 027): it walks through the player's intents now.
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

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        // ---- Spec 027: hooks for the dev tours and -demo-cookware (Scripts/Dev), development builds only ----
        // The tour clicks like a player (DevClick: the action under the dot) or, when the aim fails, sends the same
        // request the click would have sent. All of them go through the normal ServerRpcs; the server decides as always.

        /// <summary>Spec 027: does what a left click does now (<see cref="CurrentAction"/> on <see cref="Target"/>).</summary>
        public void DevClick() => ExecuteAction();
        public void DevSendPickup(NetworkObject target) => SendPickup(target);
        public void DevSendPlace(int surfaceIndex, int spotIndex) => SendPlace(surfaceIndex, spotIndex);
        public void DevSendStation(int stationIndex, int option) => SendStation(stationIndex, option);
        public void DevSendUse(NetworkObject target, int action) => SendUse(target, action);
#endif
    }
}
