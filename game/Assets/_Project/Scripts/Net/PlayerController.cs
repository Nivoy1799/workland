using FishNet.Component.Transforming;
using FishNet.Object.Prediction;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using FishNet.Utility.Template;
using UnityEngine;
using Worktown.Game.Net.Rules;
using Worktown.Game.Player;

namespace Worktown.Game.Net
{
    /// <summary>
    /// Third-person movement with client-side prediction (spec 010), following FishNet's own
    /// CharacterControllerPrediction example: the owner sends intents (move, camera yaw, sprint,
    /// crouch, jump) through [Replicate], simulates them at once, and the server runs the same code as the
    /// authority and corrects the owner through [Reconcile]. All numbers come from MovementRules.
    /// Spec 018: in first person the body faces the camera yaw (MoveData.FirstPerson, facing only).
    /// Spec 054 (B1): inputs and states go only between the owner and the server (no state forwarding). The other players see this
    /// player through a server-authoritative NetworkTransform on the root at NetRateRules.RemotePlayerInterval (15 Hz), drawn two
    /// updates behind (about 133 ms), never sent to the owner; the graphic's tick smoother then stands aside for them (it still
    /// smooths the owner). Crouch for the others stays the CrouchedForOthers SyncVar.
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

        /// <summary>Spec 054: Player against Player in the physics layer matrix (global). 054 review: the host's rule lives on one session
        /// object (<see cref="SessionPhysics"/>), not on every player.</summary>
        public static void ApplyPlayerCollision(bool collide) =>
            Physics.IgnoreLayerCollision(PhysicsLayers.Player, PhysicsLayers.Player, !collide);

        /// <summary>Spec 054: the ground and headroom probes follow the matrix (SessionPhysics calls this when the rule changes).</summary>
        internal void RefreshBodyMask() => _bodyMask = PhysicsLayers.BodyMask(gameObject.layer);

        [Tooltip("Smoothed visual child (NetworkTickSmoother). The camera follows it.")]
        [SerializeField] private Transform _graphic;
        [Tooltip("Visual body inside the graphic, squashed while crouching.")]
        [SerializeField] private Transform _body;
        [Tooltip("Spec 018: first-person camera anchor inside the graphic; lowered while crouching.")]
        [SerializeField] private Transform _eyes;

        private CharacterController _controller;
        // Spec 039: the ground and headroom probes see what the controller collides with (the matrix row of the player layer).
        private int _bodyMask = Physics.DefaultRaycastLayers;
        private Vector3 _velocity;
        private float _verticalVelocity;
        private float _yaw;
        private bool _crouched;
        private PlayerInputReader _input;
        private PlayerCameraRig _rig;

        public bool IsCrouched => IsOwner || IsServerStarted ? _crouched : CrouchedForOthers.Value;

        /// <summary>Spec 054: the drawn (smoothed) body; the camera follows it. The root without one.</summary>
        public Transform Graphic => _graphic != null ? _graphic : transform;

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        /// <summary>
        /// Spec 027, development builds only: a scripted source of the same intents the devices give (move, look,
        /// sprint, crouch, jump, view). The owner's MoveData is built from it exactly as from the devices, so
        /// prediction and the server's validation stay the same; the camera rig reads look and view from it.
        /// </summary>
        public interface IIntentSource
        {
            /// <summary>Move input relative to the camera (x right, y forward), length 0..1, like the Move action.</summary>
            Vector2 Move { get; }
            bool Sprint { get; }
            /// <summary>Held crouch state (the device input is a toggle; this is its result).</summary>
            bool Crouch { get; }
            /// <summary>True once per requested jump.</summary>
            bool ConsumeJump();
            /// <summary>Look delta in degrees since the last call (x yaw right, y pitch up).</summary>
            Vector2 ConsumeLook();
            /// <summary>The wanted view; null leaves the view as it is.</summary>
            ViewMode? View { get; }
        }

        private IIntentSource _intents;

        /// <summary>Spec 054 (B4, dev builds, read only): the owner's predicted position after each of its own live ticks (not replays).</summary>
        public event System.Action<uint, Vector3> DevOwnerTicked;
        /// <summary>Spec 054 (B4, dev builds, read only): the state a reconcile puts the owner's player to, with its tick.</summary>
        public event System.Action<uint, Vector3> DevOwnerReconciled;

        /// <summary>Spec 027, owner only: replaces device input while set; null means the devices, as before.</summary>
        public IIntentSource IntentSource
        {
            get => _intents;
            set
            {
                _intents = value;
                if (_rig != null) _rig.IntentSource = value;
            }
        }
#endif

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _bodyMask = PhysicsLayers.BodyMask(gameObject.layer); // spec 039: what the body meets (not items)
            SetTickCallbacks(TickCallback.Tick | TickCallback.PostTick);
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _yaw = transform.eulerAngles.y;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            // Spec 054 (dev builds): -remote-hz 30 sends this player to the others every tick, for the human's A/B (docs/feel.md 9);
            // without it the prefab's 15 Hz. SetInterval tells the observers too (FishNet, buffered for late joiners).
            NetBootstrap boot = FindFirstObjectByType<NetBootstrap>();
            int interval = NetRateRules.RemotePlayerIntervalFor(boot != null ? boot.Args.RemoteHz : 0);
            if (interval != NetRateRules.RemotePlayerInterval && TryGetComponent(out NetworkTransform nt)) nt.SetInterval((byte)interval);
#endif
            RefreshBodyMask(); // 054 review: the collision rule comes from the session object (SessionPhysics)
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!IsServerStarted) RefreshBodyMask(); // spec 054: the host's rule (SessionPhysics arrived with the scene), before the first tick
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
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            rig.IntentSource = _intents;
#endif
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
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            // Spec 027: the same MoveData as from the devices, only the inputs come from the script.
            if (_intents != null)
                return new MoveData(_intents.Move, cameraYaw, _intents.Sprint, _intents.Crouch, _intents.ConsumeJump(), firstPerson);
#endif
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
            // Spec 054 (B1): no state forwarding, so only the owner and the server run this; spectators see the root through the
            // NetworkTransform (the spectator prediction that was here is gone).

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
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            // Spec 054 (B4): the feel guard's owner corrections compare this with the server's reconcile of the same tick. Read only.
            if (DevOwnerTicked != null && IsOwner && !IsServerStarted && state.IsTickedCreated()) DevOwnerTicked(md.GetTick(), transform.position);
#endif
        }

        [Reconcile]
        private void PerformReconcile(StateData sd, Channel channel = Channel.Unreliable)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (DevOwnerReconciled != null && IsOwner && !IsServerStarted) DevOwnerReconciled(sd.GetTick(), sd.Position); // spec 054 (B4), read only
#endif
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
            bool grounded = Physics.CheckSphere(center, radius, _bodyMask, QueryTriggerInteraction.Ignore);
            _controller.enabled = true;
            return grounded;
        }

        private bool CanStand()
        {
            float radius = _controller.radius * 0.95f;
            Vector3 bottom = transform.position + Vector3.up * (radius + 0.05f);
            Vector3 top = transform.position + Vector3.up * (MovementRules.StandHeight - radius);
            _controller.enabled = false; // do not hit ourselves
            bool blocked = Physics.CheckCapsule(bottom, top, radius, _bodyMask, QueryTriggerInteraction.Ignore);
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
            // Spec 054 (B1): a spectator runs no replicate any more, so its copy of the body takes the stance from the SyncVar
            // (the own controller bumps into a capsule of the height the server has; where it is, is drawn 133 ms late: spec 054 notes).
            if (IsSpawned && !IsOwner && !IsServerStarted && _crouched != CrouchedForOthers.Value) SetCrouched(CrouchedForOthers.Value);
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
