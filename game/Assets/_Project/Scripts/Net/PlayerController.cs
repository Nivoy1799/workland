using FishNet.Object.Prediction;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using FishNet.Utility.Template;
using UnityEngine;
using Worktown.Game.Player;

namespace Worktown.Game.Net
{
    /// <summary>
    /// Third-person movement with client-side prediction (spec 010), following FishNet's own
    /// CharacterControllerPrediction example: the owner sends intents (move, camera yaw, sprint,
    /// crouch) through [Replicate], simulates them at once, and the server runs the same code as the
    /// authority and corrects the owner through [Reconcile]. All numbers come from MovementRules.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : TickNetworkBehaviour
    {
        public struct MoveData : IReplicateData
        {
            public Vector2 Move;
            public float CameraYaw;
            public bool Sprint;
            public bool Crouch;
            private uint _tick;

            public MoveData(Vector2 move, float cameraYaw, bool sprint, bool crouch)
            {
                Move = move;
                CameraYaw = cameraYaw;
                Sprint = sprint;
                Crouch = crouch;
                _tick = 0;
            }

            public void Dispose() { }
            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
        }

        public struct StateData : IReconcileData
        {
            public Vector3 Position;
            public float Yaw;
            public Vector3 Velocity;
            public float VerticalVelocity;
            public bool Crouched;
            private uint _tick;

            public StateData(Vector3 position, float yaw, Vector3 velocity, float verticalVelocity, bool crouched)
            {
                Position = position;
                Yaw = yaw;
                Velocity = velocity;
                VerticalVelocity = verticalVelocity;
                Crouched = crouched;
                _tick = 0;
            }

            public void Dispose() { }
            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
        }

        /// <summary>Crouch state for players who do not simulate this object (spectators).</summary>
        public readonly SyncVar<bool> CrouchedForOthers = new SyncVar<bool>(false);

        [Tooltip("Smoothed visual child (NetworkTickSmoother). The camera follows it.")]
        [SerializeField] private Transform _graphic;
        [Tooltip("Visual body inside the graphic, squashed while crouching.")]
        [SerializeField] private Transform _body;

        private CharacterController _controller;
        private Vector3 _velocity;
        private float _verticalVelocity;
        private float _yaw;
        private bool _crouched;
        private MoveData _lastTicked;
        private PlayerInputReader _input;

        public bool IsCrouched => IsOwner || IsServerStarted ? _crouched : CrouchedForOthers.Value;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            SetTickCallbacks(TickCallback.Tick | TickCallback.PostTick);
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _yaw = transform.eulerAngles.y;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!IsOwner) return;
            _input = FindFirstObjectByType<PlayerInputReader>();
            PlayerCameraRig rig = FindFirstObjectByType<PlayerCameraRig>();
            if (_input == null || rig == null)
            {
                Debug.LogError("[Player] scene needs a PlayerInputReader and a PlayerCameraRig");
                return;
            }
            _input.Activate();
            rig.Bind(_graphic != null ? _graphic : transform, _input);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (IsOwner && _input != null) _input.Deactivate();
        }

        protected override void TimeManager_OnTick()
        {
            PerformReplicate(BuildMoveData());
        }

        protected override void TimeManager_OnPostTick()
        {
            CreateReconcile();
        }

        private MoveData BuildMoveData()
        {
            if (!IsOwner || _input == null) return default;
            Camera cam = Camera.main;
            float cameraYaw = cam != null ? cam.transform.eulerAngles.y : _yaw;
            return new MoveData(_input.Move, cameraYaw, _input.Sprint, _input.Crouch);
        }

        public override void CreateReconcile()
        {
            PerformReconcile(new StateData(transform.position, _yaw, _velocity, _verticalVelocity, _crouched));
        }

        [Replicate]
        private void PerformReplicate(MoveData md, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
        {
            float delta = (float)TimeManager.TickDelta;

            // Spectators predict at most one tick ahead with the last known input (FishNet example).
            if (!IsServerStarted && !IsOwner)
            {
                if (state.ContainsTicked())
                {
                    _lastTicked = md;
                }
                else if (state.IsFuture())
                {
                    if (md.GetTick() - _lastTicked.GetTick() > 1)
                        md = default;
                    else
                        md = _lastTicked;
                }
            }

            // The server never trusts the numbers: clamp everything the client sent.
            Vector2 move = MovementRules.SanitizeMove(md.Move);
            float cameraYaw = MovementRules.SanitizeYaw(md.CameraYaw);

            UpdateCrouch(md.Crouch);

            Vector3 direction = MovementRules.WorldDirection(move, cameraYaw);
            Vector3 target = direction * MovementRules.MaxSpeed(md.Sprint, _crouched);
            _velocity = MovementRules.Accelerate(_velocity, target, delta);
            _yaw = MovementRules.TurnTowards(_yaw, direction, delta);
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);

            if (_controller.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f;
            else
                _verticalVelocity = Mathf.Max(_verticalVelocity + Physics.gravity.y * delta, -40f);

            _controller.Move((_velocity + Vector3.up * _verticalVelocity) * delta);

            if (IsServerStarted && CrouchedForOthers.Value != _crouched)
                CrouchedForOthers.Value = _crouched;
        }

        [Reconcile]
        private void PerformReconcile(StateData sd, Channel channel = Channel.Unreliable)
        {
            // Disable the controller while teleporting, or its physics stays at the old spot (FishNet example).
            _controller.enabled = false;
            transform.SetPositionAndRotation(sd.Position, Quaternion.Euler(0f, sd.Yaw, 0f));
            _controller.enabled = true;
            _yaw = sd.Yaw;
            _velocity = sd.Velocity;
            _verticalVelocity = sd.VerticalVelocity;
            if (sd.Crouched != _crouched) SetCrouched(sd.Crouched);
        }

        private void UpdateCrouch(bool wantsCrouch)
        {
            if (wantsCrouch && !_crouched) SetCrouched(true);
            else if (!wantsCrouch && _crouched && CanStand()) SetCrouched(false);
        }

        private bool CanStand()
        {
            float radius = _controller.radius * 0.95f;
            Vector3 bottom = transform.position + Vector3.up * (radius + 0.05f);
            Vector3 top = transform.position + Vector3.up * (MovementRules.StandHeight - radius);
            _controller.enabled = false; // do not hit ourselves
            bool blocked = Physics.CheckCapsule(bottom, top, radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            _controller.enabled = true;
            return !blocked;
        }

        private void SetCrouched(bool crouched)
        {
            _crouched = crouched;
            float height = crouched ? MovementRules.CrouchHeight : MovementRules.StandHeight;
            _controller.height = height;
            _controller.center = new Vector3(0f, height * 0.5f, 0f);
        }

        private void Update()
        {
            if (_body == null) return;
            // Visual only: squash the capsule while crouched (others see it through the SyncVar).
            float scaleY = IsCrouched ? MovementRules.CrouchHeight / MovementRules.StandHeight : 1f;
            Vector3 scale = _body.localScale;
            if (!Mathf.Approximately(scale.y, scaleY))
            {
                _body.localScale = new Vector3(scale.x, scaleY, scale.z);
                _body.localPosition = new Vector3(0f, scaleY, 0f);
            }
        }
    }
}
