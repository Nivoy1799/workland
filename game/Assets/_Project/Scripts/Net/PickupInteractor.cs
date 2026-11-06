using FishNet.Connection;
using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;

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

        [SerializeField] private Transform _holdPoint;

        private PickupService _service;
        private readonly Collider[] _hits = new Collider[16];

        public Transform HoldPoint => _holdPoint;

        /// <summary>Owner side: last answer from the server, for the debug overlay.</summary>
        public string LastResult { get; private set; } = string.Empty;

        public override void OnStartServer()
        {
            _service = NetworkManager.GetComponent<PickupService>();
            if (_service == null)
                Debug.LogError("[Pickup] PickupService missing on the NetworkManager object");
        }

        private void Update()
        {
            if (!IsOwner) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.eKey.wasPressedThisFrame) return;

            if (IsHoldingSomething())
            {
                RequestDrop();
                return;
            }

            Pickup nearest = FindNearestFreePickup();
            if (nearest == null)
                LastResult = "no cube within " + ClientSearchRadius + " m";
            else
                RequestPickup(nearest.NetworkObject);
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
            PickupOutcome outcome = _service != null ? _service.TryPickup(this, target) : PickupOutcome.NotFound;
            ReportResult(Owner, outcome, "pickup");
        }

        [ServerRpc]
        private void RequestDrop()
        {
            PickupOutcome outcome = _service != null ? _service.TryDrop(this) : PickupOutcome.NothingHeld;
            ReportResult(Owner, outcome, "drop");
        }

        [TargetRpc]
        private void ReportResult(NetworkConnection target, PickupOutcome outcome, string action)
        {
            LastResult = action + ": " + outcome;
            if (outcome != PickupOutcome.Ok)
                Debug.Log("[Pickup] " + LastResult);
        }
    }
}
