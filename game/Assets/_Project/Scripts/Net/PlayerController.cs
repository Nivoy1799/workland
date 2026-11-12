using System.Collections.Generic;
using FishNet.Component.Transforming;
using FishNet.Component.Transforming.Beta;
using FishNet.Managing.Timing;
using FishNet.Object;
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
    /// Spec 054b (D48): in the soft mode (SessionPhysics, the default) players do not collide as rigid capsules; every replicate adds
    /// the PlayerSeparation push from the other players (the server with their real positions, the owner's prediction with the ones it
    /// draws). The owner's drawn body blends small reconcile corrections over CorrectionBlend.BlendSeconds (the simulated root still
    /// snaps), and a remote player is drawn no deeper than PlayerSeparation.MaxDrawnOverlap into the own body (display only).
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
            /// <summary>Spec 054b option (a): the server tick the owner's drawn players stand for (LagCompensation.ClientViewTick; 0 =
            /// now). The server clamps it (LagCompensation.ServerViewTick) and uses it only for the separation push.</summary>
            public uint ViewTick;
            private uint _tick;

            public MoveData(Vector2 move, float cameraYaw, bool sprint, bool crouch, bool jump, bool firstPerson, uint viewTick = 0)
            {
                ViewTick = viewTick;
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

        /// <summary>Spec 054b: every spawned player in this process (the server's real ones, a client's drawn ones), for the separation.</summary>
        private static readonly List<PlayerController> Spawned = new List<PlayerController>();

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
        private Vector3 _graphicLocal;
        private Vector3 _drawnOffset; // spec 054b design 4: a client's copy of another player, drawn this far from its root

        // Spec 054b design 3 (the owner on a client): the drawn body's blend and what was drawn for each recent tick, so a replay leaves
        // the drawn path alone and the correction is blended instead (the tick smoother's tracker carries the offset).
        private const int DrawnRing = 128;
        private readonly Vector3[] _drawnAt = new Vector3[DrawnRing];
        private readonly uint[] _drawnTick = new uint[DrawnRing];
        private CorrectionBlend _blend;
        // Spec 054b option (a), the server: where this player was at the end of each recent server tick (LagCompensation).
        private readonly PositionHistory _history = new PositionHistory();
        private Vector3 _lastLive;
        private bool _hasLastLive;
        private UniversalTickSmoother _tracker;
        private TransformProperties _trackerBase;

        // Spec 054b design 2 (the owner on a client): where the other players were drawn at each recent live tick, so a replay of that
        // tick pushes against the same positions as its live run did (not against where they are drawn now, a few ticks later).
        private const int MaxOthers = 63;
        private Vector3[] _seenAt;
        private int[] _seenId;
        private readonly int[] _seenCount = new int[DrawnRing];
        private readonly uint[] _seenTick = new uint[DrawnRing];

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
        /// <summary>Spec 054b (dev builds, read only): a reconcile moved the owner's current simulated position by the first value (m);
        /// the drawn body moves by the second (m) within a tick because of it (CorrectionBlend.Correct).</summary>
        public event System.Action<float, float> DevOwnerSmoothed;

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
            if (_graphic != null) _graphicLocal = _graphic.localPosition;
            SetTickCallbacks(TickCallback.Tick | TickCallback.PostTick);
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _yaw = transform.eulerAngles.y;
            if (!Spawned.Contains(this)) Spawned.Add(this);
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            Spawned.Remove(this);
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
            _history.Clear();
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
            _tracker = null; // FishNet hands the smoother's controller back to its pool
            _blend = CorrectionBlend.None;
            _hasLastLive = false;
        }

        protected override void TimeManager_OnTick()
        {
            if (SmoothsOwnCorrections) BlendCorrection();
            PerformReplicate(BuildMoveData());
        }

        /// <summary>Spec 054b design 3: only the owner on a client is corrected by reconciles (the host simulates its own player).</summary>
        private bool SmoothsOwnCorrections => IsOwner && !IsServerStarted;

        /// <summary>
        /// Spec 054b design 3, before the own live tick: if this tick's reconcile and replay moved the simulated position away from where
        /// the last live tick left it, the drawn body keeps its place and blends (CorrectionBlend); a long correction snaps.
        /// </summary>
        private void BlendCorrection()
        {
            if (!_hasLastLive) return;
            Vector3 correction = transform.position - _lastLive;
            if (correction.sqrMagnitude < 1e-8f) return;
            _blend = _blend.Correct(correction, (float)TimeManager.TickDelta, out float visible);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            DevOwnerSmoothed?.Invoke(correction.magnitude, visible); // read only
#endif
        }

        /// <summary>Spec 054b design 3, after each own tick: the tick smoother follows its tracker, so the tracker carries the offset.
        /// Live tick: the blend advances one tick and the drawn position is kept for this tick. Replayed tick: the tracker goes back to
        /// what was drawn for that tick, so the replay does not move the drawn path (the correction is blended from the next tick).</summary>
        private void PlaceTracker(uint tick, bool replayed, float delta)
        {
            if (!TryTracker()) return;
            int slot = (int)(tick % DrawnRing);
            Vector3 drawn;
            if (replayed)
            {
                if (!_hasLastLive || _drawnTick[slot] != tick) return; // nothing drawn for that tick yet
                drawn = _drawnAt[slot];
            }
            else
            {
                _blend = _blend.Advance(delta);
                _lastLive = transform.position;
                _hasLastLive = true;
                drawn = _blend.Drawn(_lastLive);
                _drawnAt[slot] = drawn;
                _drawnTick[slot] = tick;
            }
            Vector3 local = transform.InverseTransformPoint(drawn);
            _tracker.TrySetGraphicalTrackerLocalProperties(new TransformProperties(local, _trackerBase.Rotation, _trackerBase.Scale));
        }

        /// <summary>The graphic's tick smoother (FishNet's public tracker API), once it is set up for this session.</summary>
        private bool TryTracker()
        {
            if (_tracker != null) return true;
            if (_graphic == null || !_graphic.TryGetComponent(out NetworkTickSmoother smoother) || smoother.SmootherController == null) return false;
            UniversalTickSmoother universal = smoother.SmootherController.UniversalSmoother;
            if (universal == null || !universal.IsInitialized) return false;
            _trackerBase = universal.GetGraphicalTrackerLocalProperties();
            _tracker = universal;
            return true;
        }

        protected override void TimeManager_OnPostTick()
        {
            // SyncVar writes stay out of [Replicate] (review finding): server publishes after the tick.
            if (IsServerStarted && CrouchedForOthers.Value != _crouched)
                CrouchedForOthers.Value = _crouched;
            if (IsServerStarted) _history.Record(TimeManager.LocalTick, transform.position); // spec 054b option (a)
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
                return new MoveData(_intents.Move, cameraYaw, _intents.Sprint, _intents.Crouch, _intents.ConsumeJump(), firstPerson, ViewTick());
#endif
            return new MoveData(_input.Move, cameraYaw, _input.Sprint, _input.Crouch, _input.ConsumeJump(), firstPerson, ViewTick());
        }

        /// <summary>Spec 054b option (a): the server tick this client's drawn players stand for (the host draws them where they are).</summary>
        private uint ViewTick()
        {
            if (IsServerStarted) return LagCompensation.Now;
            EstimatedTick newest = TimeManager.LastPacketTick;
            return newest == null || newest.IsUnset ? LagCompensation.Now : LagCompensation.ClientViewTick(newest.RemoteTick);
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
            uint tick = md.GetTick();
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

            Vector3 step = _velocity * delta;
            if (SessionPhysics.ModeNow == PlayerCollisionMode.Soft) step = Separate(step, delta, tick, state.IsReplayed(), md.ViewTick); // spec 054b (D48)
            _controller.Move(step + Vector3.up * (_verticalVelocity * delta));
            if (SmoothsOwnCorrections) PlaceTracker(tick, state.IsReplayed(), delta);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            // Spec 054 (B4): the feel guard's owner corrections compare this with the server's reconcile of the same tick. Read only.
            if (DevOwnerTicked != null && IsOwner && !IsServerStarted && state.IsTickedCreated()) DevOwnerTicked(md.GetTick(), transform.position);
#endif
        }

        /// <summary>
        /// Spec 054b (D48): this tick's horizontal step with the soft separation from every other player: all pushes first, then the
        /// no-pass-through floor against each (PlayerSeparation). The server sees the others where they are, a client where it draws them:
        /// at a live tick where they are drawn now (kept per tick), at a replayed tick where they were drawn when that tick ran live.
        /// Option (a): the server pushes against where this player's client drew the others (their history at the input's view tick);
        /// its floor stays on the real positions.
        /// </summary>
        private Vector3 Separate(Vector3 step, float delta, uint tick, bool replayed, uint viewTick)
        {
            if (IsServerStarted) return SeparateAsServer(step, delta, viewTick);
            int slot = (int)(tick % DrawnRing);
            int count = SmoothsOwnCorrections ? SeenOthers(slot, tick, replayed) : -1;
            if (count < 0) return SeparateFromSpawned(step, delta);
            Vector3 self = transform.position;
            int selfId = ObjectId;
            int first = slot * MaxOthers;
            Vector3 push = Vector3.zero;
            for (int i = 0; i < count; i++) push += PlayerSeparation.ContactPush(self, _seenAt[first + i], _seenAt[first + i], selfId, _seenId[first + i]);
            step += push * delta;
            for (int i = 0; i < count; i++) step = PlayerSeparation.Block(self, step, _seenAt[first + i], selfId, _seenId[first + i]);
            return step;
        }

        /// <summary>The owner's other players for this tick in the ring: recorded now at a live tick, read back at a replayed one.
        /// -1 when a replayed tick was never recorded (then the drawn positions of now are all there is).</summary>
        private int SeenOthers(int slot, uint tick, bool replayed)
        {
            if (_seenAt == null)
            {
                _seenAt = new Vector3[DrawnRing * MaxOthers];
                _seenId = new int[DrawnRing * MaxOthers];
            }
            if (replayed) return _seenTick[slot] == tick && _seenCount[slot] >= 0 ? _seenCount[slot] : -1;
            int first = slot * MaxOthers, count = 0;
            for (int i = 0; i < Spawned.Count && count < MaxOthers; i++)
            {
                PlayerController other = Spawned[i];
                if (other == this || other == null || !other.IsSpawned) continue;
                _seenAt[first + count] = other.transform.position;
                _seenId[first + count] = other.ObjectId;
                count++;
            }
            _seenCount[slot] = count;
            _seenTick[slot] = tick;
            return count;
        }

        /// <summary>Spec 054b option (a): the server's separation for one input: pushes from the others where its sender drew them,
        /// clamped to LagCompensation.MaxViewTicks back; the floor against where they really are.</summary>
        private Vector3 SeparateAsServer(Vector3 step, float delta, uint viewTick)
        {
            uint now = TimeManager.LocalTick;
            uint at = LagCompensation.ServerViewTick(now, viewTick);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            DevCountViewDelay(now, viewTick, at);
#endif
            Vector3 self = transform.position;
            int selfId = ObjectId;
            Vector3 push = Vector3.zero;
            for (int i = 0; i < Spawned.Count; i++)
            {
                PlayerController other = Spawned[i];
                if (other == this || other == null || !other.IsSpawned) continue;
                Vector3 seen = at < now && other._history.TryGet(at, out Vector3 past) ? past : other.transform.position;
                push += PlayerSeparation.ContactPush(self, seen, other.transform.position, selfId, other.ObjectId);
            }
            step += push * delta;
            for (int i = 0; i < Spawned.Count; i++)
            {
                PlayerController other = Spawned[i];
                if (other == this || other == null || !other.IsSpawned) continue;
                step = PlayerSeparation.Block(self, step, other.transform.position, selfId, other.ObjectId);
            }
            return step;
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        // Spec 054b option (a), dev builds, read only: how far back the clients' view ticks reach on the server, logged every 30 s
        // (host.log "[Net] view delay"), so a load test shows whether the 300 ms clamp is hit.
        private static readonly int[] DevDelays = new int[LagCompensation.HistoryTicks];
        private static int _devClamped, _devNow;
        private static uint _devNextLog;

        private static void DevCountViewDelay(uint now, uint claimed, uint used)
        {
            if (claimed == LagCompensation.Now) _devNow++;
            else DevDelays[Mathf.Min((int)(now > claimed ? now - claimed : 0), DevDelays.Length - 1)]++;
            if (claimed != LagCompensation.Now && used != claimed) _devClamped++;
            if (now < _devNextLog) return;
            _devNextLog = now + 30 * NetRateRules.TickRate;
            int total = 0;
            for (int i = 0; i < DevDelays.Length; i++) total += DevDelays[i];
            if (total == 0) return;
            var line = new System.Text.StringBuilder("[Net] view delay ticks (inputs):");
            for (int i = 0; i < DevDelays.Length; i++)
                if (DevDelays[i] > 0) line.Append(' ').Append(i).Append('=').Append(DevDelays[i]);
            line.Append(" clamped=").Append(_devClamped).Append(" now=").Append(_devNow);
            Debug.Log(line.ToString());
            System.Array.Clear(DevDelays, 0, DevDelays.Length);
            _devClamped = 0;
            _devNow = 0;
        }
#endif

        private Vector3 SeparateFromSpawned(Vector3 step, float delta)
        {
            Vector3 self = transform.position;
            int selfId = ObjectId;
            Vector3 push = Vector3.zero;
            for (int i = 0; i < Spawned.Count; i++)
            {
                PlayerController other = Spawned[i];
                if (other == this || other == null || !other.IsSpawned) continue;
                push += PlayerSeparation.ContactPush(self, other.transform.position, other.transform.position, selfId, other.ObjectId);
            }
            step += push * delta;
            for (int i = 0; i < Spawned.Count; i++)
            {
                PlayerController other = Spawned[i];
                if (other == this || other == null || !other.IsSpawned) continue;
                step = PlayerSeparation.Block(self, step, other.transform.position, selfId, other.ObjectId);
            }
            return step;
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

        /// <summary>
        /// Spec 054b design 4 (a client's copy of another player, display only): in the soft mode the drawn body never overlaps the own
        /// drawn body by more than PlayerSeparation.MaxDrawnOverlap. Nothing else moves this graphic on a client (the root's
        /// NetworkTransform smooths, the tick smoother stands aside), so it is placed every frame, after the NetworkTransform's Update.
        /// The push-out itself moves at most PlayerSeparation.DrawnOffsetSpeed, so a remote player passing the own centre slides
        /// round instead of jumping to the other side.
        /// </summary>
        private void LateUpdate()
        {
            if (_graphic == null || !IsSpawned || IsOwner || IsServerStarted) return;
            Vector3 root = transform.TransformPoint(_graphicLocal);
            Vector3 wanted = Vector3.zero;
            if (SessionPhysics.ModeNow == PlayerCollisionMode.Soft)
            {
                PlayerController own = OwnOnThisClient();
                Vector3 ownDrawn = own != null ? own.Graphic.position : Vector3.zero;
                if (own != null && PlayerSeparation.DrawnTooClose(root, ownDrawn))
                    wanted = PlayerSeparation.ClampDrawn(root + _drawnOffset, ownDrawn) - root;
            }
            _drawnOffset = PlayerSeparation.FollowOffset(_drawnOffset, wanted, Time.deltaTime);
            Vector3 drawn = root + _drawnOffset;
            if (_graphic.position != drawn) _graphic.position = drawn;
        }

        private static PlayerController OwnOnThisClient()
        {
            for (int i = 0; i < Spawned.Count; i++)
                if (Spawned[i] != null && Spawned[i].IsOwner) return Spawned[i];
            return null;
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
