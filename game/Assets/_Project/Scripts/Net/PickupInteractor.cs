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
    /// On the player. The owner presses E to pick up the nearest cube or drop the held one.
    /// The client only asks; the server decides (PickupService) and answers with the outcome.
    /// The client deliberately searches further than the allowed reach, so requests from
    /// 2.5 to 4 m reach the server and get refused there (spec 003 AC4, manual check).
    /// Spec 018: in first person the target is what the camera centre looks at (AimRules), within the same
    /// reach the server checks; the requests and their arguments are unchanged.
    /// Spec 019: first person has no digit keys. E presses the stove button you look at, puts food on the board
    /// or takes it; holding a knife over a board with food, a downward mouse drag is one cut stroke and the right
    /// button turns the food. All of it goes through the same RequestStation(stationIndex, option).
    /// Spec 020: first person uses the mouse instead of E. One pure rule (InteractionRules) turns what you hold and
    /// what the dot points at into the click's action; the prompt and the highlight show the same action. Putting
    /// an item on a worktop spot is the only new request, RequestPlace(surface, spot): two ints, never a position.
    /// Q drops. Third person keeps E, the digits and the nearest station.
    /// </summary>
    public sealed class PickupInteractor : NetworkBehaviour
    {
        private const float ClientSearchRadius = 4f;
        private const float MinRequestInterval = 0.2f;
        // Spec 019: the client waits a little longer than the server's limit, so no stroke is silently dropped.
        private const float ClientStrokeInterval = MinRequestInterval + 0.03f;
        private const string DemoCutArg = "-demo-cut";
        private const string DemoPlaceArg = "-demo-place";

        // Server only: time of the last accepted pickup or drop request from this player.
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
            string[] args = System.Environment.GetCommandLineArgs();
            // Spec 019 AC8, development builds only: walk to the kitchen and send the normal requests in order.
            if (System.Array.IndexOf(args, DemoCutArg) >= 0)
                StartCoroutine(DemoCut());
            // Spec 020 AC8, development builds only: aim, highlight and place with the normal requests.
            else if (System.Array.IndexOf(args, DemoPlaceArg) >= 0)
                StartCoroutine(DemoPlace());
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
            // Spec 012: while quick chat (T) or the emote wheel (G / LB) is held, digits and E belong to them; the
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
                // Spec 020 AC7: E and the digits act in third person only.
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
        /// Spec 020, first person: the left click does <see cref="CurrentAction"/> (on release, see ClickGesture);
        /// with the knife over a board with food the left button drags strokes instead and the right button turns
        /// the food (spec 019). Q drops. Gamepad: right trigger click (or stroke), Y turn, X drop. No E, no digits.
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
                Station board = Target as Station;
                if (stroke) SendKnife(board, StationRules.KnifeStroke);
                bool turn = (mouse != null && locked && mouse.rightButton.wasPressedThisFrame)
                    || (gamepad != null && gamepad.buttonNorth.wasPressedThisFrame);
                if (turn) SendKnife(board, StationRules.KnifeTurn);
            }
            else if (click || (gamepad != null && gamepad.rightTrigger.wasPressedThisFrame))
            {
                ExecuteAction();
            }

            bool drop = keyboard.qKey.wasPressedThisFrame || (gamepad != null && gamepad.buttonWest.wasPressedThisFrame);
            if (drop && InteractionRules.DropKeyActs(true) && held != null) SendDrop();
        }

        /// <summary>Spec 020: sends the request for <see cref="CurrentAction"/> on <see cref="Target"/>. Nothing for None or Cut.</summary>
        private void ExecuteAction()
        {
            switch (CurrentAction)
            {
                case InteractionAction.Press:
                    if (Target is StationButton button && button.Station != null) SendStation(button.Station.Index, button.Option);
                    break;
                case InteractionAction.PutOnBoard:
                case InteractionAction.PutInto:
                case InteractionAction.Sell:
                case InteractionAction.Buy:
                case InteractionAction.Take:
                    if (Target is Station station) SendStation(station.Index, 0);
                    break;
                case InteractionAction.Place:
                    if (Target is PlaceSurface surface && TargetSpot >= 0) SendPlace(surface.Index, TargetSpot);
                    break;
                case InteractionAction.PickUp:
                    if (Target is Pickup pickup) SendPickup(pickup.NetworkObject);
                    break;
            }
        }

        private void SendKnife(Station board, int option)
        {
            if (board == null) return;
            // The server ignores requests closer than 0.2 s; skip it here too, so the chop never lies.
            if (Time.unscaledTime - _lastSendTime < ClientStrokeInterval) return;
            SendStation(board.Index, option);
            if (option == StationRules.KnifeStroke && _heldView != null) _heldView.PlayChop();
        }

        /// <summary>Spec 020: the held item as InteractionRules sees it (synced name and coarse look only).</summary>
        private static Held Describe(Pickup held)
        {
            if (held == null) return Held.Nothing;
            if (held.TryGetComponent(out Knife _)) return Held.Knife;
            if (held.TryGetComponent(out Food food))
                return Held.Food(food.DisplayName.Value, food.Look.Value.Kind == FoodLook.KindPlated);
            return Held.Item("box");
        }

        /// <summary>Spec 020: the target as InteractionRules sees it (what every client can see of it).</summary>
        private AimTarget Describe(Component target)
        {
            switch (target)
            {
                case StationButton button:
                    byte flags = button.Station != null && button.Station.TryGetComponent(out StationContents stove) ? stove.StoveFlags.Value : (byte)0;
                    return AimTarget.Button(button.Option, flags);
                case PlaceSurface _:
                    return AimTarget.Spot(TargetSpot);
                case Station station:
                    if (station.Kind == StationKind.Crate) return DescribeCrate(station);
                    if (!station.TryGetComponent(out StationContents contents)) return AimTarget.OnStation(station.Kind, 0, null);
                    int count = Mathf.Max(contents.Names.Count, contents.Looks.Count);
                    return AimTarget.OnStation(station.Kind, count, contents.Names.Count > 0 ? contents.Names[0] : null);
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
            // Spec 007: near a station, E and 1-4 go to the kitchen.
            Station station = Target as Station;
            if (station != null && TryHandleStationKeys(keyboard, station, holding)) return;

            if (!keyboard.eKey.wasPressedThisFrame) return;
            if (holding)
            {
                SendDrop();
                return;
            }
            Pickup nearest = FindNearestFreePickup();
            if (nearest == null)
                SetLocalNote("no cube within " + ClientSearchRadius + " m");
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
        /// Owner side, spec 018: what the click (first person) or E and the digits (third person) act on. First
        /// person: the <see cref="Station"/>, spec 019 <see cref="StationButton"/>, free <see cref="Pickup"/> or spec 020
        /// <see cref="PlaceSurface"/> under the crosshair (nothing in between; beyond reach only with
        /// <see cref="TargetTooFar"/>, then nothing can be done with it), else null. Third
        /// person: the nearest station within reach, as before.
        /// </summary>
        public Component Target { get; private set; }

        /// <summary>Owner side: the target station, for keys and the overlay prompt. Null if none. Kept as an
        /// alias of <see cref="Target"/> so older readers keep working.</summary>
        public Station NearestStation => Target as Station;

        /// <summary>Owner side: true while the local camera is in first person.</summary>
        public bool IsFirstPerson => _rig != null && _rig.IsFirstPerson;

        /// <summary>
        /// Spec 018: one ray from the camera centre, our own colliders and triggers ignored. Spec 020: the top of a
        /// place surface is a target too (with its nearest free spot to the hit point); with full hands pickups
        /// do not block the ray (nothing could be picked up), so the spot or station behind them is found.
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
                // Spec 019: a button on a station front; reach is measured to its station, like the server does.
                if (hitCollider.TryGetComponent(out StationButton button) && button.Station != null)
                {
                    _aimHits.Add(AimHit.Button(distance, button.Station.transform.position, button.Station.Index, button.Option));
                    _aimSources.Add(button);
                    continue;
                }
                // Spec 020: a worktop top or the board station's free half (checked before its station).
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
        /// Spec 020: the free spot of <paramref name="surface"/> nearest to the hit point, or PlaceSpots.None. A spot
        /// is taken when an item lies on it (what this client sees; the server checks its own table again).
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

        /// <summary>Third person (spec 007/016): E and the digits on the nearest station.</summary>
        private bool TryHandleStationKeys(Keyboard keyboard, Station station, bool holding)
        {
            switch (station.Kind)
            {
                case StationKind.Crate:
                    if (!keyboard.eKey.wasPressedThisFrame || holding) return false;
                    SendStation(station.Index, 0);
                    return true;
                case StationKind.Counter:
                    if (!keyboard.eKey.wasPressedThisFrame || !holding) return false;
                    SendStation(station.Index, 0);
                    return true;
                case StationKind.PlatingTable:
                case StationKind.Bowl:
                    // Spec 013: E puts the held food on/in it, or takes the result with empty hands.
                    if (!keyboard.eKey.wasPressedThisFrame) return false;
                    SendStation(station.Index, 0);
                    return true;
                case StationKind.Pan:
                case StationKind.Pot:
                    // Spec 016: E puts food in or takes it out; 1 switches on/off, 2 the pan fry/saute.
                    if (keyboard.eKey.wasPressedThisFrame)
                    {
                        SendStation(station.Index, 0);
                        return true;
                    }
                    goto default;
                default:
                    int option = StationRules.DigitOption(station.Kind, PressedDigit(keyboard), firstPerson: false);
                    if (option == 0) return false;
                    if (option > StationRules.OptionCount(station.Kind))
                    {
                        SetLocalNote(station.DisplayName + " has no option " + option);
                        return true;
                    }
                    SendStation(station.Index, option);
                    return true;
            }
        }

        private static int PressedDigit(Keyboard keyboard)
        {
            if (keyboard.digit1Key.wasPressedThisFrame) return 1;
            if (keyboard.digit2Key.wasPressedThisFrame) return 2;
            if (keyboard.digit3Key.wasPressedThisFrame) return 3;
            if (keyboard.digit4Key.wasPressedThisFrame) return 4;
            return 0;
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
        /// <paramref name="surfaceIndex"/>. Two ints, never a position: the server checks hands, surface, spot,
        /// reach and the rate, and places the item itself (PickupService.TryPlace).
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

        /// <summary>Spec 007: take from a crate (option 0), cook at a station (1-4) or serve (0).
        /// Spec 019: on the board also a knife stroke or turn (StationRules.KnifeStroke / KnifeTurn).</summary>
        [ServerRpc]
        private void RequestStation(int stationIndex, int option)
        {
            if (IsRateLimited()) return;
            CookOutcome outcome = _kitchen != null ? _kitchen.TryUseStation(this, stationIndex, option) : CookOutcome.UnknownStation;
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

        /// <summary>Server: drop requests that come faster than a person can press E.
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

        // ---- Spec 019 AC8: dev-only intent script for the screenshot run (development builds only) ----

        /// <summary>
        /// <c>-demo-cut</c>: the owner walks to the potato crate with simulated W/A/S/D keys (batch clients get no
        /// real input), then sends the normal requests in order: take a potato, put it on the board, take the
        /// knife, two strokes (slices), and press the pan's switch. No server shortcut: the server checks reach,
        /// rate and hands for every request as for a human. Expects the first spawn point (facing the kitchen).
        /// </summary>
        private System.Collections.IEnumerator DemoCut()
        {
            yield return new WaitForSeconds(6f);
            Station crate = null, board = null, pan = null;
            foreach (Station s in FindObjectsByType<Station>(FindObjectsSortMode.None))
            {
                if (s.Kind == StationKind.Crate && s.IngredientIndex == 0) crate = s;
                else if (s.Kind == StationKind.Board) board = s;
                else if (s.Kind == StationKind.Pan) pan = s;
            }
            if (crate == null || board == null || pan == null)
            {
                Debug.LogWarning("[Demo] crate, board or pan not found");
                yield break;
            }
            float laneZ = board.transform.position.z - 1.7f;
            float standX = (board.transform.position.x + pan.transform.position.x) * 0.5f;
            Debug.Log("[Demo] walking to the potato crate");
            yield return DemoWalk(new Vector3(-1.75f, 0f, transform.position.z));
            yield return DemoWalk(new Vector3(-1.75f, 0f, laneZ));
            yield return DemoWalk(new Vector3(crate.transform.position.x + 1f, 0f, laneZ));
            yield return DemoStep("take a potato", crate.Index, 0, 1.5f);
            Debug.Log("[Demo] walking to the board");
            yield return DemoWalk(new Vector3(standX, 0f, laneZ));
            yield return new WaitForSeconds(1f);
            yield return DemoStep("put it on the board", board.Index, 0, 2.5f);

            Knife knife = null;
            float best = float.MaxValue;
            foreach (Knife k in FindObjectsByType<Knife>(FindObjectsSortMode.None))
            {
                float d = (k.transform.position - board.transform.position).sqrMagnitude;
                if (d < best && k.TryGetComponent(out Pickup p) && !p.IsHeld)
                {
                    best = d;
                    knife = k;
                }
            }
            if (knife == null)
            {
                Debug.LogWarning("[Demo] no free knife near the board");
                yield break;
            }
            Debug.Log("[Demo] take the knife");
            SendPickup(knife.GetComponent<NetworkObject>());
            yield return new WaitForSeconds(1.5f);
            for (int i = 0; i < 2; i++)
            {
                Debug.Log("[Demo] cut stroke " + (i + 1));
                SendKnife(board, StationRules.KnifeStroke);
                yield return new WaitForSeconds(i == 0 ? 0.8f : 2f);
            }
            yield return DemoStep("press the pan's switch", pan.Index, StationRules.StoveSwitch, 1f);
            Debug.Log("[Demo] cut demo done");
        }

        private System.Collections.IEnumerator DemoStep(string what, int stationIndex, int option, float wait)
        {
            Debug.Log("[Demo] " + what);
            SendStation(stationIndex, option);
            yield return new WaitForSeconds(wait);
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

        // ---- Spec 020 AC8: dev-only intent script for the screenshot run (development builds only) ----

        // PlayerCameraRig's mouse sensitivity (degrees per count); the aim loop below only needs it roughly.
        private const float DemoDegreesPerCount = 0.12f;

        /// <summary>
        /// <c>-demo-place</c>: like <c>-demo-cut</c> the owner walks with simulated keys and turns the first-person
        /// view with simulated mouse movement (no real input in a screenshot run). It takes a potato, aims at the
        /// pot for 4 s holding it (green glow, "Click · put potato into the pot"), then aims at the board, the knife
        /// and the worktop between board and pan and each time does what a click would do (the same
        /// <see cref="ExecuteAction"/>, so the normal RequestStation / RequestPickup / RequestPlace go out). No
        /// server shortcut. If an aim does not give the expected action in 3 s it logs a warning and sends the
        /// same request directly. Expects the first spawn point; needs <c>-view first -skip-whatsnew</c>.
        /// </summary>
        private System.Collections.IEnumerator DemoPlace()
        {
            yield return new WaitForSeconds(6f);
            Station crate = null, board = null, pan = null, pot = null;
            foreach (Station s in FindObjectsByType<Station>(FindObjectsSortMode.None))
            {
                if (s.Kind == StationKind.Crate && s.IngredientIndex == 0) crate = s;
                else if (s.Kind == StationKind.Board) board = s;
                else if (s.Kind == StationKind.Pan) pan = s;
                else if (s.Kind == StationKind.Pot) pot = s;
            }
            if (crate == null || board == null || pan == null || pot == null)
            {
                Debug.LogWarning("[Demo] crate, board, pan or pot not found");
                yield break;
            }
            Vector3 between = (board.transform.position + pan.transform.position) * 0.5f;
            PlaceSurface worktop = null;
            float bestWorktop = float.MaxValue;
            foreach (PlaceSurface s in FindObjectsByType<PlaceSurface>(FindObjectsSortMode.None))
            {
                float d = (s.transform.position - between).sqrMagnitude;
                if (s.GetComponentInParent<Station>() != null || d >= bestWorktop) continue;
                bestWorktop = d;
                worktop = s;
            }
            if (worktop == null)
            {
                Debug.LogWarning("[Demo] no worktop between the board and the pan");
                yield break;
            }

            float laneZ = board.transform.position.z - 1.7f;
            float standZ = board.transform.position.z - 1.3f;
            Debug.Log("[Demo] walking to the potato crate");
            yield return DemoWalk(new Vector3(-1.75f, 0f, transform.position.z));
            yield return DemoWalk(new Vector3(-1.75f, 0f, laneZ));
            yield return DemoWalk(new Vector3(crate.transform.position.x + 1f, 0f, laneZ));
            yield return DemoStep("take a potato", crate.Index, 0, 1.5f);

            // Right in front of the pot, about 1.3 m from it (well inside the 2.5 m reach).
            Debug.Log("[Demo] walking to the pot");
            Vector3 potStand = new Vector3(pot.transform.position.x, 0f, pot.transform.position.z - 1.2f);
            yield return DemoWalk(new Vector3(potStand.x, 0f, laneZ));
            yield return DemoWalk(potStand);
            Debug.Log("[Demo] in front of the pot, " + Vector3.Distance(transform.position, pot.transform.position).ToString("0.00") + " m");
            yield return DemoAim(ContentPoint(pot), 3f, InteractionAction.PutInto);
            Debug.Log("[Demo] holding the potato over the pot (" + Time.time.ToString("0.0") + " s): " + CurrentAction
                + " '" + InteractionRules.PromptFor(CurrentHeld, CurrentAim) + "'");
            yield return DemoAim(ContentPoint(pot), 4f, null); // stay on it for the screenshot

            Debug.Log("[Demo] walking to the board");
            yield return DemoLevelView();
            yield return DemoWalk(new Vector3(potStand.x, 0f, standZ));
            yield return DemoWalk(new Vector3(between.x, 0f, standZ));
            yield return DemoUse("put it on the board", ContentPoint(board), InteractionAction.PutOnBoard,
                () => SendStation(board.Index, 0));

            Knife knife = null;
            float bestKnife = float.MaxValue;
            foreach (Knife k in FindObjectsByType<Knife>(FindObjectsSortMode.None))
            {
                float d = (k.transform.position - board.transform.position).sqrMagnitude;
                if (d < bestKnife && k.TryGetComponent(out Pickup p) && !p.IsHeld)
                {
                    bestKnife = d;
                    knife = k;
                }
            }
            if (knife == null)
            {
                Debug.LogWarning("[Demo] no free knife near the board");
                yield break;
            }
            NetworkObject knifeObject = knife.GetComponent<NetworkObject>();
            yield return DemoUse("take the knife", knife.transform.position, InteractionAction.PickUp,
                () => SendPickup(knifeObject));

            // The front middle spot of the worktop: closest to the player.
            int frontSpot = PlaceSpots.NearestFree(worktop.Size, new Vector2(0f, -worktop.Size.y * 0.5f), null);
            Vector3 spotPoint = worktop.SpotWorldPosition(frontSpot);
            yield return DemoUse("put the knife on the worktop", spotPoint, InteractionAction.Place,
                () => SendPlace(worktop.Index, frontSpot));
            Debug.Log("[Demo] place demo done (" + Time.time.ToString("0.0") + " s)");
            yield return DemoAim(spotPoint, 30f, null); // keep looking at the knife on the worktop
        }

        /// <summary>Where a station draws its contents (the cutting board, inside the pot); its centre if none.</summary>
        private static Vector3 ContentPoint(Station station)
        {
            Transform anchor = station.transform.Find("Contents");
            return anchor != null ? anchor.position : station.transform.position + Vector3.up * 0.5f;
        }

        /// <summary>Aims at <paramref name="point"/>, then does what a click would do (or sends the request directly).</summary>
        private System.Collections.IEnumerator DemoUse(string what, Vector3 point, InteractionAction expected, System.Action fallback)
        {
            Debug.Log("[Demo] " + what);
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

        /// <summary>Looks straight ahead again before walking (the walk steers by the camera's forward).</summary>
        private System.Collections.IEnumerator DemoLevelView()
        {
            Camera cam = Camera.main;
            if (cam == null) yield break;
            Vector3 ahead = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            if (ahead.sqrMagnitude < 0.01f) ahead = transform.forward;
            yield return DemoAim(cam.transform.position + ahead.normalized * 5f, 1f, null);
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
