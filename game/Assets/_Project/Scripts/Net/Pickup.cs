using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Net
{
    /// <summary>
    /// A cube that can be carried. The server moves it to the holder's hold point every tick;
    /// NetworkTransform (server-authoritative) replicates the position.
    /// </summary>
    public sealed class Pickup : NetworkBehaviour
    {
        /// <summary>Client id of the holder, or <see cref="PickupRegistry.NoHolder"/>. Server writes, everyone reads.</summary>
        public readonly SyncVar<int> HolderClientId = new SyncVar<int>(PickupRegistry.NoHolder);

        private const float DropDistance = 1.2f;
        private const float RestHeight = 0.25f;

        // Server only.
        private Transform _serverHoldPoint;

        public bool IsHeld => HolderClientId.Value != PickupRegistry.NoHolder;

        public override void OnStartNetwork()
        {
            TimeManager.OnTick += OnTick;
        }

        public override void OnStopNetwork()
        {
            if (TimeManager != null)
                TimeManager.OnTick -= OnTick;
        }

        /// <summary>Server: attach to a holder after the registry accepted the claim.</summary>
        internal void ServerAttach(Transform holdPoint, int clientId)
        {
            _serverHoldPoint = holdPoint;
            HolderClientId.Value = clientId;
            FollowHoldPoint();
        }

        /// <summary>Server: put the cube down in front of where it was held.</summary>
        internal void ServerDetach(Vector3 dropForward)
        {
            if (_serverHoldPoint != null)
            {
                Vector3 position = _serverHoldPoint.position + dropForward.normalized * DropDistance;
                position.y = RestHeight;
                transform.position = position;
            }
            _serverHoldPoint = null;
            HolderClientId.Value = PickupRegistry.NoHolder;
        }

        private void OnTick()
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
