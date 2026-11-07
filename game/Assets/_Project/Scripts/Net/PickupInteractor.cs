using FishNet.Connection;
using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;
using Worktown.Game.Cooking;

namespace Worktown.Game.Net
{
    /// <summary>
    /// On the player. The owner presses E to pick up the nearest cube or drop the held one.
    /// The client only asks; the server decides (PickupService) and answers with the outcome.
    /// The client deliberately searches further than the allowed reach, so requests from
    /// 2.5 to 4 m reach the server and get refused there (spec 003 AC4, manual check).
    /// </summary>
    public sealed class PickupInteractor : NetworkBehaviour
    {
        private const float ClientSearchRadius = 4f;
        private const float MinRequestInterval = 0.2f;

        // Server only: time of the last accepted pickup or drop request from this player.
        private float _serverLastRequestTime = float.NegativeInfinity;

        [SerializeField] private Transform _holdPoint;

        private PickupService _service;
        private KitchenService _kitchen;
        private Worktown.Game.Player.PlayerInputReader _inputReader;
        private Station[] _stations;
        private readonly Collider[] _hits = new Collider[16];

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

        private void Update()
        {
            if (!IsOwner) return;
            // Keys belong to the menu or banner while one is open (review finding, spec 011).
            if (_inputReader == null) _inputReader = FindFirstObjectByType<Worktown.Game.Player.PlayerInputReader>();
            if (_inputReader != null && _inputReader.IsUiOpen) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            // Spec 012: while quick chat (T) or the emote wheel (G) is held, digits and E belong to them.
            if (keyboard.tKey.isPressed || keyboard.gKey.isPressed) return;

            // Spec 007: near a station, E and 1-4 go to the kitchen.
            Station station = FindNearestStation();
            NearestStation = station;
            if (station != null && TryHandleStationKeys(keyboard, station)) return;

            if (!keyboard.eKey.wasPressedThisFrame) return;
            if (IsHoldingSomething())
            {
                RequestDrop();
                return;
            }

            Pickup nearest = FindNearestFreePickup();
            if (nearest == null)
                SetLocalNote("no cube within " + ClientSearchRadius + " m");
            else
                RequestPickup(nearest.NetworkObject);
        }

        private void SetLocalNote(string note)
        {
            LastCookOutcome = null;
            LastPickupOutcome = null;
            LastResult = note;
        }

        /// <summary>Owner side: the station within reach, for keys and the overlay prompt. Null if none.</summary>
        public Station NearestStation { get; private set; }

        private bool TryHandleStationKeys(Keyboard keyboard, Station station)
        {
            bool holding = IsHoldingSomething();
            switch (station.Kind)
            {
                case StationKind.Crate:
                    if (!keyboard.eKey.wasPressedThisFrame || holding) return false;
                    RequestStation(station.Index, 0);
                    return true;
                case StationKind.Counter:
                    if (!keyboard.eKey.wasPressedThisFrame || !holding) return false;
                    RequestStation(station.Index, 0);
                    return true;
                case StationKind.PlatingTable:
                case StationKind.Bowl:
                    // Spec 013: E puts the held food on/in it, or takes the result with empty hands.
                    if (!keyboard.eKey.wasPressedThisFrame) return false;
                    RequestStation(station.Index, 0);
                    return true;
                default:
                    int option = PressedOption(keyboard);
                    if (option == 0) return false;
                    if (option > StationRules.OptionCount(station.Kind))
                    {
                        SetLocalNote(station.DisplayName + " has no option " + option);
                        return true;
                    }
                    RequestStation(station.Index, option);
                    return true;
            }
        }

        private static int PressedOption(Keyboard keyboard)
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

        /// <summary>Spec 007: take from a crate (option 0), cook at a station (1-4) or serve (0).</summary>
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
        /// Spam then costs neither validation work nor reply bandwidth.</summary>
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
    }
}
