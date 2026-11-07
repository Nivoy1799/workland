using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.Rendering;
using Worktown.Game.Net.Rules;
using Worktown.Game.Player;

namespace Worktown.Game.Net
{
    /// <summary>
    /// A cube that can be carried (spec 021: also every piece of cookware, with a Vessel). The server moves it to the
    /// holder's hold point every tick;
    /// NetworkTransform (server-authoritative) replicates the position.
    /// While held, the holder OWNS this object (PickupService gives ownership so the client can
    /// find its held cube). Do not add [ServerRpc] methods here without RequireOwnership = false
    /// plus explicit server checks: the holder could otherwise call them.
    /// Spec 024, client side only: while held, every client draws the item's visual children at the holder's right hand
    /// (<see cref="CharacterVisual.Hand"/>) for the duration of each camera render and puts them back afterwards, so the
    /// networked transform, the server hold point above the head and every rule stay unchanged. Your own item in first
    /// person is left to <see cref="HeldItemView"/>.
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
        // Spec 020: how the item lies when it is put down, without its yaw (a knife lies flat on its side).
        private Quaternion _restTilt = Quaternion.identity;
        private Collider _collider;

        // Spec 024, client only: the visual in the holder's hand.
        /// <summary>m, in the holder's yaw frame: a little in front of the hand anchor, so the item sits in the fingers.</summary>
        private static readonly Vector3 HandOffset = new Vector3(0f, -0.02f, 0.06f);
        private const float HolderLookupSeconds = 0.5f;
        private CharacterVisual _handHolder;
        private int _handHolderId = PickupRegistry.NoHolder;
        private float _nextHolderLookup;
        private bool _handHooked;
        private bool _inHand;
        private readonly List<Transform> _handMoved = new List<Transform>();
        private readonly List<Vector3> _handPositions = new List<Vector3>();
        private readonly List<Quaternion> _handRotations = new List<Quaternion>();

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

        public override void OnStartServer()
        {
            base.OnStartServer();
            // Spec 020: scene objects and server-spawned items (a respawned knife, food made in the hands) start in
            // their resting pose; keep its tilt for every later drop or place.
            _restTilt = PlaceSpots.TiltOf(transform.rotation);
        }

        /// <summary>Spec 020, server: the item's resting tilt (rotation without yaw), captured when the server starts it.</summary>
        internal Quaternion RestTilt => _restTilt;

        public override void OnStopNetwork()
        {
            if (TimeManager != null)
                TimeManager.OnPostTick -= OnPostTick;
            UnhookHand();
        }

        private void OnDestroy()
        {
            UnhookHand();
        }

        /// <summary>Spec 024, client: find the holder's character and decide whether this frame draws the item in its hand.</summary>
        private void LateUpdate()
        {
            int holder = HolderClientId.Value;
            if (!IsClientInitialized || holder == PickupRegistry.NoHolder)
            {
                _handHolder = null;
                _handHolderId = PickupRegistry.NoHolder;
                UnhookHand();
                return;
            }
            if (holder != _handHolderId)
            {
                _handHolderId = holder;
                _handHolder = null;
                _nextHolderLookup = 0f;
            }
            if (_handHolder == null && Time.unscaledTime >= _nextHolderLookup)
            {
                // A late joiner may see the item before the holder's player object: try again shortly.
                _handHolder = CharacterVisual.FindPlayer(holder);
                _nextHolderLookup = Time.unscaledTime + HolderLookupSeconds;
            }
            if (_handHolder == null || _handHolder.OwnerId != holder)
            {
                _handHolder = null;
                UnhookHand();
                return;
            }
            _handHolder.MarkHolding();
            if (_handHolder.DrawsHeldInHand && _handHolder.Hand != null) HookHand();
            else UnhookHand();
        }

        private void HookHand()
        {
            if (_handHooked) return;
            _handHooked = true;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
        }

        private void UnhookHand()
        {
            RestoreFromHand();
            if (!_handHooked) return;
            _handHooked = false;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            RestoreFromHand();
            if (_handHolder == null) return;
            Transform hand = _handHolder.Hand;
            if (hand == null) return;

            // The visual children (model, food visual, fallback cube); labels stay where they are. An item without
            // children (the spec 003 cube) moves as a whole.
            foreach (Transform child in transform)
                if (!child.TryGetComponent(out TextMesh _)) _handMoved.Add(child);
            if (_handMoved.Count == 0) _handMoved.Add(transform);

            Quaternion frame = Quaternion.Euler(0f, _handHolder.Yaw, 0f);
            Vector3 origin = hand.position + frame * HandOffset;
            foreach (Transform moved in _handMoved)
            {
                _handPositions.Add(moved.localPosition);
                _handRotations.Add(moved.localRotation);
                if (moved == transform) moved.SetPositionAndRotation(origin, frame);
                else moved.SetPositionAndRotation(origin + frame * moved.localPosition, frame * moved.localRotation);
            }
            _inHand = true;
        }

        private void OnEndCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            RestoreFromHand();
        }

        private void RestoreFromHand()
        {
            if (!_inHand) return;
            for (int i = 0; i < _handMoved.Count; i++)
                if (_handMoved[i] != null) _handMoved[i].SetLocalPositionAndRotation(_handPositions[i], _handRotations[i]);
            _handMoved.Clear();
            _handPositions.Clear();
            _handRotations.Clear();
            _inHand = false;
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

            // Spec 020: lies the way it rests (a knife flat), yaw reset as before. Spec 021: the free-space check stays at
            // the old height; the item then rests with its collider bottom on the floor (cookware has its pivot at the
            // bottom, food and boxes at the centre).
            Vector3 rest = IsFree(inFront) ? inFront : atFeet;
            rest.y = BottomOffset;
            transform.SetPositionAndRotation(rest, PlaceSpots.RestRotation(_restTilt, Quaternion.identity));
            _serverHoldPoint = null;
            HolderClientId.Value = PickupRegistry.NoHolder;
        }

        /// <summary>
        /// Spec 020, server only: put the held item down exactly here (a place spot the server chose), e.g. its
        /// bottom on a worktop. No free-space fallback: PickupService checked the spot.
        /// </summary>
        internal void ServerPlaceAt(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            _serverHoldPoint = null;
            HolderClientId.Value = PickupRegistry.NoHolder;
        }

        /// <summary>Spec 020: height from the bottom of the item's collider to its pivot, so it rests on a surface.</summary>
        internal float BottomOffset
        {
            get
            {
                if (_collider is BoxCollider box)
                    return (box.size.y * 0.5f - box.center.y) * transform.lossyScale.y;
                return RestHeight;
            }
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
