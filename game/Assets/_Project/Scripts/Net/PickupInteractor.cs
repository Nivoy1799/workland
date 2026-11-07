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
    /// </summary>
    public sealed class PickupInteractor : NetworkBehaviour
    {
        private const float ClientSearchRadius = 4f;
        private const float MinRequestInterval = 0.2f;
        // Spec 019: the client waits a little longer than the server's limit, so no stroke is silently dropped.
        private const float ClientStrokeInterval = MinRequestInterval + 0.03f;
        private const string DemoCutArg = "-demo-cut";

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

        /// <summary>Owner side: increases with every result, so the HUD can toast repeats (spec 011).</summary>
        public int ResultVersion { get; private set; }

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
            // Spec 019 AC8, development builds only: walk to the kitchen and send the normal requests in order.
            if (Debug.isDebugBuild && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), DemoCutArg) >= 0)
                StartCoroutine(DemoCut());
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
            // Spec 018: first person aims with the camera centre; third person keeps the nearest station.
            Target = IsFirstPerson ? FindAimTarget() : FindNearestStation();

            if (_inputReader == null) _inputReader = FindFirstObjectByType<Worktown.Game.Player.PlayerInputReader>();
            Keyboard keyboard = Keyboard.current;
            Gamepad gamepad = Gamepad.current;
            // Spec 012: while quick chat (T) or the emote wheel (G / LB) is held, digits and E belong to them; the
            // wheel then owns the camera freeze, so the knife lets go of it without writing it.
            bool wheel = (keyboard != null && keyboard.gKey.isPressed) || (gamepad != null && gamepad.leftShoulder.isPressed);
            // Keys belong to the menu or banner while one is open (review finding, spec 011).
            bool blocked = keyboard == null || wheel || keyboard.tKey.isPressed
                || (_inputReader != null && _inputReader.IsUiOpen);

            Pickup held = FindHeldPickup();
            bool holdingKnife = held != null && held.TryGetComponent(out Knife _);
            if (blocked)
            {
                _gesture.Reset();
                if (wheel) _suppressingLook = false;
                else SetLookSuppressed(false);
                return;
            }

            if (IsFirstPerson)
            {
                UpdateKnife(holdingKnife, gamepad);
                HandleFirstPersonKeys(keyboard, held, holdingKnife);
            }
            else
            {
                _gesture.Reset();
                SetLookSuppressed(false);
                HandleThirdPersonKeys(keyboard, held != null);
            }
        }

        /// <summary>
        /// Spec 019: with the knife in the hands and a board with food under the dot, hold the left button and drag
        /// down for one stroke (the camera stays still meanwhile), right click turns the food. Gamepad: right
        /// trigger strokes, Y turns. Anywhere else the mouse turns the camera as usual.
        /// </summary>
        private void UpdateKnife(bool holdingKnife, Gamepad gamepad)
        {
            Station board = Target as Station;
            bool armed = holdingKnife && board != null && board.Kind == StationKind.Board && HasContents(board);
            Mouse mouse = Mouse.current;
            // A click while the cursor is free only locks it again (PlayerInputReader), it never cuts.
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            bool button = mouse != null && locked && mouse.leftButton.isPressed;
            float deltaY = mouse != null ? mouse.delta.ReadValue().y : 0f;

            bool stroke = _gesture.Update(armed, button, deltaY);
            SetLookSuppressed(armed && button);
            if (!armed) return;
            if (gamepad != null && gamepad.rightTrigger.wasPressedThisFrame) stroke = true;
            if (stroke) SendKnife(board, StationRules.KnifeStroke);

            bool turn = (mouse != null && locked && mouse.rightButton.wasPressedThisFrame)
                || (gamepad != null && gamepad.buttonNorth.wasPressedThisFrame);
            if (turn) SendKnife(board, StationRules.KnifeTurn);
        }

        private void SendKnife(Station board, int option)
        {
            // The server ignores requests closer than 0.2 s; skip it here too, so the chop never lies.
            if (Time.unscaledTime - _lastSendTime < ClientStrokeInterval) return;
            SendStation(board.Index, option);
            if (option == StationRules.KnifeStroke && _heldView != null) _heldView.PlayChop();
        }

        /// <summary>Spec 019: first person uses E only; the digits do nothing here (StationRules.DigitOption).</summary>
        private void HandleFirstPersonKeys(Keyboard keyboard, Pickup held, bool holdingKnife)
        {
            if (!keyboard.eKey.wasPressedThisFrame) return;
            bool holding = held != null;
            if (Target is StationButton button && button.Station != null)
            {
                SendStation(button.Station.Index, button.Option);
                return;
            }
            if (Target is Station station && TryUseStationWithE(station, holding)) return;
            if (Target is Pickup aimed)
            {
                if (!holding)
                {
                    // Spec 018: E picks up the food (or the knife) you look at.
                    SendPickup(aimed.NetworkObject);
                    return;
                }
                if (holdingKnife)
                {
                    SetLocalNote("Put the knife down first");
                    return;
                }
            }
            if (holding) SendDrop();
            else SetLocalNote("nothing to use here");
        }

        /// <summary>E on a station, first person: true when E goes to the station.</summary>
        private bool TryUseStationWithE(Station station, bool holding)
        {
            switch (station.Kind)
            {
                case StationKind.Crate:
                    if (holding) return false;
                    break;
                case StationKind.Counter:
                    if (!holding) return false;
                    break;
            }
            // Board (spec 019), pan, pot (spec 016), plating table and bowl (spec 013): put in or take.
            SendStation(station.Index, 0);
            return true;
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
            LastResult = note;
        }

        /// <summary>
        /// Owner side, spec 018: what E and the digits act on. First person: the <see cref="Station"/>, spec 019
        /// <see cref="StationButton"/> or free <see cref="Pickup"/> under the crosshair (within reach, nothing in
        /// between), else null. Third person: the nearest station within reach, as before.
        /// </summary>
        public Component Target { get; private set; }

        /// <summary>Owner side: the target station, for keys and the overlay prompt. Null if none. Kept as an
        /// alias of <see cref="Target"/> so older readers keep working.</summary>
        public Station NearestStation => Target as Station;

        /// <summary>Owner side: true while the local camera is in first person.</summary>
        public bool IsFirstPerson => _rig != null && _rig.IsFirstPerson;

        /// <summary>Spec 019: true when the station shows something on it (the board's food, the pan's).</summary>
        public static bool HasContents(Station station)
        {
            return station != null && station.TryGetComponent(out StationContents contents) && contents.Looks.Count > 0;
        }

        /// <summary>Spec 018: one ray from the camera centre, our own colliders and triggers ignored.</summary>
        private Component FindAimTarget()
        {
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
                Station station = hitCollider.GetComponentInParent<Station>();
                Pickup pickup = station == null ? hitCollider.GetComponentInParent<Pickup>() : null;
                if (station != null)
                    AddAimHit(distance, AimKind.Station, station);
                else if (pickup != null && !pickup.IsHeld)
                    AddAimHit(distance, AimKind.Pickup, pickup);
                else
                    AddAimHit(distance, AimKind.Other, null);
            }
            int picked = AimRules.Pick(_aimHits, transform.position);
            return picked == AimRules.None ? null : _aimSources[picked];
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
    }
}
