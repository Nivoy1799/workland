using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Net
{
    /// <summary>
    /// A cube that can be carried. The server moves it to the holder's hold point every tick;
    /// NetworkTransform (server-authoritative) replicates the position.
    /// While held, the holder OWNS this object (PickupService gives ownership so the client can
    /// find its held cube). Do not add [ServerRpc] methods here without RequireOwnership = false
    /// plus explicit server checks: the holder could otherwise call them.
    /// </summary>
    public sealed class Pickup : NetworkBehaviour
    {
        /// <summary>Client id of the holder, or <see cref="PickupRegistry.NoHolder"/>. Server writes, everyone reads.</summary>
        public readonly SyncVar<int> HolderClientId = new SyncVar<int>(PickupRegistry.NoHolder);

        private const float DropDistance = 1.2f;
        private const float RestHeight = 0.25f;
        private static readonly Vector3 HalfExtents = Vector3.one * 0.24f;

        // Server only.
        private Transform _serverHoldPoint;
        private Collider _collider;

        public bool IsHeld => HolderClientId.Value != PickupRegistry.NoHolder;

        private void Awake()
        {
            _collider = GetComponent<Collider>();
            // A carried item never collides: on the holder's screen it trails a tick behind the
            // head, and the predicted jump would hit it from below (spec 010 play-test).
            HolderClientId.OnChange += (_, holder, _) =>
            {
                if (_collider != null) _collider.enabled = holder == PickupRegistry.NoHolder;
            };
        }

        public override void OnStartNetwork()
        {
            // Post tick: runs after every OnTick handler, so the cube follows the holder's
            // position of this tick instead of trailing it by one.
            TimeManager.OnPostTick += OnPostTick;
        }

        public override void OnStopNetwork()
        {
            if (TimeManager != null)
                TimeManager.OnPostTick -= OnPostTick;
        }

        /// <summary>Server: attach to a holder after the registry accepted the claim.</summary>
        internal void ServerAttach(Transform holdPoint, int clientId)
        {
            _serverHoldPoint = holdPoint;
            HolderClientId.Value = clientId;
            FollowHoldPoint();
        }

        /// <summary>
        /// Server: put the cube down in front of the holder. If that spot is inside geometry
        /// (a wall or box), fall back to the holder's feet so the cube stays reachable.
        /// </summary>
        internal void ServerDetach(Vector3 holderPosition, Vector3 dropForward)
        {
            Vector3 forward = Vector3.ProjectOnPlane(dropForward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;

            Vector3 inFront = holderPosition + forward.normalized * DropDistance;
            inFront.y = RestHeight;
            Vector3 atFeet = holderPosition;
            atFeet.y = RestHeight;

            transform.SetPositionAndRotation(IsFree(inFront) ? inFront : atFeet, Quaternion.identity);
            _serverHoldPoint = null;
            HolderClientId.Value = PickupRegistry.NoHolder;
        }

        private bool IsFree(Vector3 position)
        {
            // Ignore our own collider (it is still at the hold point) and other players' controllers.
            bool wasEnabled = _collider != null && _collider.enabled;
            if (_collider != null) _collider.enabled = false;
            bool blocked = false;
            foreach (Collider hit in Physics.OverlapBox(position, HalfExtents, Quaternion.identity))
            {
                if (hit is CharacterController) continue;
                blocked = true;
                break;
            }
            if (_collider != null) _collider.enabled = wasEnabled;
            return !blocked;
        }

        private void OnPostTick()
        {
            if (IsServerInitialized && _serverHoldPoint != null)
                FollowHoldPoint();
        }

        private void FollowHoldPoint()
        {
            transform.SetPositionAndRotation(_serverHoldPoint.position, _serverHoldPoint.rotation);
        }
    }
}
