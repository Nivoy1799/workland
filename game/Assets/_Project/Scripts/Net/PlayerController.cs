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
    /// crouch, jump) through [Replicate], simulates them at once, and the server runs the same code as the
    /// authority and corrects the owner through [Reconcile]. All numbers come from MovementRules.
    /// Spec 018: in first person the body faces the camera yaw (MoveData.FirstPerson, facing only).
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
            /// <summary>One-time input: true only on the tick the button was pressed.</summary>
            public bool Jump;
            /// <summary>Spec 018: view hint for facing only (body faces the camera yaw). Changes no speed or reach.</summary>
            public bool FirstPerson;
            private uint _tick;

            public MoveData(Vector2 move, float cameraYaw, bool sprint, bool crouch, bool jump, bool firstPerson)
            {
                Move = move;
                CameraYaw = cameraYaw;
                Sprint = sprint;
                Crouch = crouch;
                Jump = jump;
                FirstPerson = firstPerson;
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
        [Tooltip("Spec 018: first-person camera anchor inside the graphic; lowered while crouching.")]
        [SerializeField] private Transform _eyes;

        private CharacterController _controller;
        private Vector3 _velocity;
        private float _verticalVelocity;
        private float _yaw;
        private bool _crouched;
        private MoveData _lastTicked;
        private PlayerInputReader _input;
        private PlayerCameraRig _rig;

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
            _rig = rig;
            rig.Bind(_graphic != null ? _graphic : transform, _eyes, _input);
            // Spec 018: owner-only first-person visuals.
            if (TryGetComponent(out FirstPersonBody body)) body.Activate(rig);
            if (TryGetComponent(out HeldItemView held)) held.Activate(rig, GetComponent<PickupInteractor>());
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
            // SyncVar writes stay out of [Replicate] (review finding): server publishes after the tick.
            if (IsServerStarted && CrouchedForOthers.Value != _crouched)
                CrouchedForOthers.Value = _crouched;
            CreateReconcile();
        }

        private MoveData BuildMoveData()
        {
            if (!IsOwner || _input == null) return default;
            Camera cam = Camera.main;
            float cameraYaw = cam != null ? cam.transform.eulerAngles.y : _yaw;
            bool firstPerson = _rig != null && _rig.IsFirstPerson;
            return new MoveData(_input.Move, cameraYaw, _input.Sprint, _input.Crouch, _input.ConsumeJump(), firstPerson);
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
                    {
                        md = _lastTicked;
                        md.Jump = false; // a second jump in a row is unlikely; do not predict it (FishNet example)
                    }
                }
            }

            // The server never trusts the numbers: clamp everything the client sent.
            Vector2 move = MovementRules.SanitizeMove(md.Move);
            float cameraYaw = MovementRules.SanitizeYaw(md.CameraYaw);

            UpdateCrouch(md.Crouch);

            Vector3 direction = MovementRules.WorldDirection(move, cameraYaw);
            Vector3 target = direction * MovementRules.MaxSpeed(md.Sprint, _crouched);
            _velocity = MovementRules.Accelerate(_velocity, target, delta);
            // Spec 018: in first person the body faces the sanitized camera yaw; third person turns towards the move.
            _yaw = MovementRules.FacingYaw(md.FirstPerson, _yaw, cameraYaw, direction, delta);
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);

            // Own probe, not CharacterController.isGrounded: that flag resets after every reconcile
            // teleport, so client replays and the server disagreed about jumps (the 'invisible ceiling').
            bool grounded = IsGrounded();
            if (grounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f;
            else
                _verticalVelocity = MovementRules.ApplyGravity(_verticalVelocity, delta);
            if (MovementRules.CanJump(md.Jump, grounded, _crouched))
                _verticalVelocity = MovementRules.JumpSpeed();

            _controller.Move((_velocity + Vector3.up * _verticalVelocity) * delta);

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

        private bool IsGrounded()
        {
            float radius = _controller.radius * 0.95f;
            Vector3 center = transform.position + Vector3.up * (radius - 0.08f);
            _controller.enabled = false; // do not hit ourselves
            bool grounded = Physics.CheckSphere(center, radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            _controller.enabled = true;
            return grounded;
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
            if (_eyes != null)
            {
                // Spec 018: the first-person eyes follow the stance (visual only, like the squash below).
                Vector3 eyes = _eyes.localPosition;
                float eyeY = Mathf.MoveTowards(eyes.y, ViewRules.EyeHeightFor(IsCrouched), ViewRules.EyeHeightSpeed * Time.deltaTime);
                if (!Mathf.Approximately(eyes.y, eyeY)) _eyes.localPosition = new Vector3(eyes.x, eyeY, eyes.z);
            }
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
