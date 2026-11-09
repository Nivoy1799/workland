using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.Rendering;
using Worktown.Game.Art;
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
    /// person is drawn at the view anchor (<see cref="HeldItemView.TryGetViewPose"/>).
    /// Spec 027b, client side only: nothing teleports. Every viewer draws the model children through an
    /// <see cref="ItemTween"/>: when the holder or the resting place changes, they ease from where they were drawn last
    /// (pick up: an arc, put down: a settle). After a put-down the networked position takes a few ticks to arrive, so the
    /// model waits where it was until it has (at most <see cref="RestSettleMax"/>) and then settles onto it. The owner's
    /// <see cref="PickupInteractor"/> can predict the look of a click (<see cref="PredictHeld"/>, <see cref="PredictRest"/>,
    /// <see cref="PredictInto"/>); the synced state confirms or denies it there, and a denied look eases back.
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

        // ---- Client visuals (spec 024 hand, spec 027b tweens and predicted looks) ----
        /// <summary>m, in the holder's yaw frame: a little in front of the hand anchor, so the item sits in the fingers.</summary>
        private static readonly Vector3 HandOffset = new Vector3(0f, -0.02f, 0.06f);
        private const float HolderLookupSeconds = 0.5f;
        /// <summary>s: the longest the model waits for the networked position after a put-down before settling anyway.</summary>
        public const float RestSettleMax = 0.3f;
        /// <summary>s: the networked position counts as arrived after this long without moving.</summary>
        private const float RestQuiet = 0.05f;
        /// <summary>s: a confirmed put-into keeps its look at most this long while the despawn is on its way.</summary>
        private const float ConfirmedIntoMax = 1f;
        /// <summary>s: the longest frame a tween is started early by, so the first frame after a click already moves.</summary>
        private const float MaxFrameLead = 1f / 30f;

        private CharacterVisual _handHolder;
        private int _handHolderId = PickupRegistry.NoHolder;
        private float _nextHolderLookup;
        private ItemTween _tween;
        private bool _renderHooked;
        private int _poseFrame = -1;
        private Pose _framePose;
        private bool _hasGoal;
        private ItemGoalKind _goalKind;
        private int _goalHolder = PickupRegistry.NoHolder;
        private int _seenHolder = PickupRegistry.NoHolder;
        // The resting place: the client's own copy of the tilt (the server's is server only), and the settle wait.
        private Quaternion _clientRestTilt = Quaternion.identity;
        private bool _settling;
        private Pose _settleLatch;
        private float _settleDeadline;
        private bool _settleSawMove;
        private float _lastRootMove = float.NegativeInfinity;
        private Pose _lastRoot;
        private bool _hasLastRoot;
        // A predicted look (owner of the click only).
        private bool _hasPrediction;
        private ItemGoalKind _predictedKind;
        private Pose _predictedPose;
        private int _predictedHolder = PickupRegistry.NoHolder;
        private bool _predictionConfirmed;
        private float _predictionKeepUntil;

        public bool IsHeld => HolderClientId.Value != PickupRegistry.NoHolder;

        /// <summary>Spec 027b: frame number of the first render that drew the current prediction (-1 before).</summary>
        internal int PredictionDrawnFrame { get; private set; } = -1;

        private void Awake()
        {
            _collider = GetComponent<Collider>();
            _tween = new ItemTween(transform);
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

        public override void OnStartClient()
        {
            base.OnStartClient();
            ResetVisual();
            _seenHolder = HolderClientId.Value;
            if (_seenHolder == PickupRegistry.NoHolder) _clientRestTilt = PlaceSpots.TiltOf(transform.rotation);
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null && !_renderHooked)
            {
                _renderHooked = true;
                RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
                RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
            }
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            Unhook();
        }

        /// <summary>Spec 020, server: the item's resting tilt (rotation without yaw), captured when the server starts it.</summary>
        internal Quaternion RestTilt => _restTilt;

        /// <summary>Spec 027b, client: the resting tilt as this client last saw the item lying (for a predicted place).</summary>
        internal Quaternion ClientRestTilt => _clientRestTilt;

        public override void OnStopNetwork()
        {
            if (TimeManager != null)
                TimeManager.OnPostTick -= OnPostTick;
            Unhook();
        }

        private void OnDestroy()
        {
            Unhook();
        }

        private void Unhook()
        {
            _tween?.Restore();
            ResetVisual();
            if (!_renderHooked) return;
            _renderHooked = false;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
        }

        private void ResetVisual()
        {
            _tween?.Reset();
            _hasGoal = false;
            _settling = false;
            _hasLastRoot = false;
            _hasPrediction = false;
            _poseFrame = -1;
            _handHolder = null;
            _handHolderId = PickupRegistry.NoHolder;
        }

        // ---- Spec 027b: predicted looks, set by the owner's PickupInteractor ----

        /// <summary>Draws the item in <paramref name="clientId"/>'s hands (a predicted pick-up).</summary>
        internal void PredictHeld(int clientId) => StartPrediction(ItemGoalKind.Hand, default, clientId);

        /// <summary>Draws the item lying at <paramref name="pose"/> (a predicted place or drop).</summary>
        internal void PredictRest(Pose pose) => StartPrediction(ItemGoalKind.Rest, pose, PickupRegistry.NoHolder);

        /// <summary>Draws the item just above the contents of the cookware it goes into (a predicted put-into).</summary>
        internal void PredictInto(Pose pose) => StartPrediction(ItemGoalKind.Into, pose, PickupRegistry.NoHolder);

        /// <summary>True while a predicted look is drawn (pending, or confirmed and waiting for the state to catch up).</summary>
        internal bool HasPrediction => _hasPrediction;

        private void StartPrediction(ItemGoalKind kind, Pose pose, int holder)
        {
            _hasPrediction = true;
            _predictedKind = kind;
            _predictedPose = pose;
            _predictedHolder = holder;
            _predictionConfirmed = false;
            PredictionDrawnFrame = -1;
        }

        /// <summary>
        /// The prediction is over. Denied: the look follows the real state again (it eases back). Confirmed: a pick-up
        /// simply continues (you hold it now); a place keeps its spot until the networked position has arrived there; a
        /// put-into stays above the cookware until the food is despawned.
        /// </summary>
        internal void EndPrediction(bool confirmed)
        {
            if (!_hasPrediction) return;
            float now = Time.unscaledTime;
            if (!confirmed || _predictedKind == ItemGoalKind.Hand)
            {
                _hasPrediction = false;
                return;
            }
            if (_predictedKind == ItemGoalKind.Rest)
            {
                _hasPrediction = false;
                BeginSettling(_predictedPose, now);
                return;
            }
            _predictionConfirmed = true;
            _predictionKeepUntil = now + ConfirmedIntoMax;
        }

        /// <summary>Client: who holds it as this client draws it (a predicted pick-up counts).</summary>
        private int EffectiveHolder()
        {
            if (_hasPrediction && _predictedKind == ItemGoalKind.Hand) return _predictedHolder;
            if (_hasPrediction) return PickupRegistry.NoHolder;
            return HolderClientId.Value;
        }

        /// <summary>Spec 024: raise the holder's arm while it carries this item.</summary>
        private void LateUpdate()
        {
            if (!IsClientInitialized) return;
            int holder = EffectiveHolder();
            if (holder == PickupRegistry.NoHolder) return;
            CharacterVisual visual = HolderVisual(holder, Time.unscaledTime);
            if (visual != null) visual.MarkHolding();
        }

        /// <summary>The character of <paramref name="holder"/>, looked up at most every <see cref="HolderLookupSeconds"/>.</summary>
        private CharacterVisual HolderVisual(int holder, float now)
        {
            if (holder != _handHolderId)
            {
                _handHolderId = holder;
                _handHolder = null;
                _nextHolderLookup = 0f;
            }
            if (_handHolder == null && now >= _nextHolderLookup)
            {
                // A late joiner may see the item before the holder's player object: try again shortly.
                _handHolder = CharacterVisual.FindPlayer(holder);
                _nextHolderLookup = now + HolderLookupSeconds;
            }
            if (_handHolder != null && _handHolder.OwnerId != holder) _handHolder = null;
            return _handHolder;
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            if (!IsClientInitialized) return;
            // One pose per frame for every camera (the goal does not depend on the camera).
            if (_poseFrame != Time.frameCount)
            {
                _poseFrame = Time.frameCount;
                _framePose = ComputeFramePose(Time.unscaledTime);
            }
            _tween.Apply(_framePose);
        }

        private void OnEndCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            _tween.Restore();
        }

        /// <summary>Where the model is drawn this frame: the goal, eased from the last drawn pose when the goal changed.</summary>
        private Pose ComputeFramePose(float now)
        {
            Pose goal = ComputeGoal(now, out ItemGoalKind kind, out int holder);
            if (_hasGoal && (kind != _goalKind || holder != _goalHolder))
            {
                TweenStyle style = FeelRules.StyleFor(_goalKind, _goalHolder, kind, holder);
                FeelSettings feel = FeelSettings.Current;
                // Started a frame early: the first frame after the change already shows movement (feel.md 1).
                float lead = Mathf.Min(Time.unscaledDeltaTime, MaxFrameLead);
                _tween.Begin(style, FeelRules.TweenSeconds(style, kind, feel), FeelRules.TweenAmount(style, kind, feel), now - lead);
            }
            _hasGoal = true;
            _goalKind = kind;
            _goalHolder = holder;
            Pose drawn = _tween.Evaluate(goal, now);
            if (_hasPrediction && PredictionDrawnFrame < 0) PredictionDrawnFrame = Time.frameCount;
            return drawn;
        }

        private Pose ComputeGoal(float now, out ItemGoalKind kind, out int holder)
        {
            int synced = HolderClientId.Value;
            bool rootMoved = TrackRoot(now);
            if (synced != _seenHolder)
            {
                // Put down (or taken from someone): wait for the networked position before settling on it.
                if (synced == PickupRegistry.NoHolder && !_settling)
                    BeginSettling(_tween.HasDrawn ? _tween.Drawn : ItemTween.RootPose(transform), now);
                else if (synced != PickupRegistry.NoHolder) _settling = false;
                _seenHolder = synced;
            }

            if (_hasPrediction)
            {
                if (_predictedKind == ItemGoalKind.Into)
                {
                    if (_predictionConfirmed && now > _predictionKeepUntil) _hasPrediction = false;
                    else
                    {
                        kind = ItemGoalKind.Into;
                        holder = PickupRegistry.NoHolder;
                        return _predictedPose;
                    }
                }
                else if (_predictedKind == ItemGoalKind.Rest)
                {
                    kind = ItemGoalKind.Rest;
                    holder = PickupRegistry.NoHolder;
                    return _predictedPose;
                }
            }

            holder = EffectiveHolder();
            if (holder != PickupRegistry.NoHolder) return HeldGoal(holder, now, out kind);

            kind = ItemGoalKind.Rest;
            Pose root = ItemTween.RootPose(transform);
            // The server moved a lying item: wait until it stopped, then settle onto the new place.
            if (!_settling && rootMoved && _tween.HasDrawn) BeginSettling(_tween.Drawn, now);
            if (_settling)
            {
                bool arrived = _settleSawMove && now - _lastRootMove >= RestQuiet;
                if (!arrived && now < _settleDeadline) return _settleLatch;
                _settling = false;
                bool apart = (root.position - _settleLatch.position).sqrMagnitude > FeelRules.MoveEpsilon * FeelRules.MoveEpsilon
                             || Quaternion.Angle(root.rotation, _settleLatch.rotation) > 2f;
                if (apart)
                {
                    FeelSettings feel = FeelSettings.Current;
                    _tween.Begin(TweenStyle.Settle, feel.PlaceSeconds, feel.PlaceSettle, now);
                }
            }
            _clientRestTilt = PlaceSpots.TiltOf(root.rotation);
            return root;
        }

        private void BeginSettling(Pose latch, float now)
        {
            _settling = true;
            _settleLatch = latch;
            _settleDeadline = now + RestSettleMax;
            _settleSawMove = false;
        }

        /// <summary>Notes whether the networked transform moved since the last frame.</summary>
        private bool TrackRoot(float now)
        {
            Pose root = ItemTween.RootPose(transform);
            bool moved = _hasLastRoot && ((root.position - _lastRoot.position).sqrMagnitude > 1e-6f
                                          || Quaternion.Angle(root.rotation, _lastRoot.rotation) > 0.2f);
            _lastRoot = root;
            _hasLastRoot = true;
            if (moved)
            {
                _lastRootMove = now;
                _settleSawMove = true;
            }
            return moved;
        }

        /// <summary>Spec 024 / 018: in the holder's right hand, in your own first-person view, or at the hold point.</summary>
        private Pose HeldGoal(int holder, float now, out ItemGoalKind kind)
        {
            CharacterVisual visual = HolderVisual(holder, now);
            if (visual != null)
            {
                Transform hand = visual.Hand;
                if (visual.DrawsHeldInHand && hand != null)
                {
                    kind = ItemGoalKind.Hand;
                    Quaternion frame = Quaternion.Euler(0f, visual.Yaw, 0f);
                    return new Pose(hand.position + frame * HandOffset, frame);
                }
                if (!visual.DrawsHeldInHand && visual.TryGetComponent(out HeldItemView view) && view.TryGetViewPose(this, out Pose inView))
                {
                    kind = ItemGoalKind.View;
                    return inView;
                }
            }
            kind = ItemGoalKind.HoldPoint;
            return ItemTween.RootPose(transform);
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
        /// Spec 027b, client: where <see cref="ServerDetach"/> would most likely put the item (in front of the holder; the
        /// server may still choose the feet when that spot is blocked, then the look settles there).
        /// </summary>
        internal Pose PredictedDropPose(Vector3 holderPosition, Vector3 dropForward)
        {
            Vector3 forward = Vector3.ProjectOnPlane(dropForward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            Vector3 rest = holderPosition + forward.normalized * DropDistance;
            rest.y = BottomOffset;
            return new Pose(rest, PlaceSpots.RestRotation(_clientRestTilt, Quaternion.identity));
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
