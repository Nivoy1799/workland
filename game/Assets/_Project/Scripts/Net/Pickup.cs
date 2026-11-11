using System.Collections.Generic;
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
    /// model waits where it was until it has (at most <see cref="ItemLook.RestSettleMax"/>) and then settles onto it. The owner's
    /// <see cref="PickupInteractor"/> can predict the look of a click (<see cref="PredictHeld"/>, <see cref="PredictRest"/>,
    /// <see cref="PredictInto"/>); the synced state confirms or denies it there, and a denied look eases back.
    /// </summary>
    public sealed class Pickup : NetworkBehaviour
    {
        /// <summary>Client id of the holder, or <see cref="PickupRegistry.NoHolder"/>. Server writes, everyone reads.</summary>
        public readonly SyncVar<int> HolderClientId = new SyncVar<int>(PickupRegistry.NoHolder);
        /// <summary>Spec 044: where the item goes when the server takes it out of the world (into cookware, the trash, the bin, the sink),
        /// set just before its despawn (FishNet sends pending SyncVars before the despawn). Every viewer that did not predict it
        /// draws it flying there (<see cref="ExitFlightLook"/>).</summary>
        public readonly SyncVar<Vector3> ExitPoint = new SyncVar<Vector3>(Vector3.zero);
        /// <summary>Spec 044: 0 = no exit; else the landing sound + 1 (<see cref="Worktown.Game.Audio.SoundKey"/>).</summary>
        public readonly SyncVar<byte> ExitSound = new SyncVar<byte>(0);

        [Tooltip("Spec 039: a tool with a handle (knife, spatula, cloth): put down, its handle points to the cook (ToolRest).")]
        [SerializeField] private bool _handleTool;

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
        /// <summary>s: the longest frame a tween is started early by, so the first frame after a click already moves.</summary>
        private const float MaxFrameLead = 1f / 30f;

        /// <summary>Spec 027b review: every pickup this client has started (OnStartClient to OnStopClient), so the aim needs
        /// no FindObjectsByType. Client only; never read on a headless server.</summary>
        private static readonly List<Pickup> ClientPickups = new List<Pickup>();

        private CharacterVisual _handHolder;
        private int _handHolderId = PickupRegistry.NoHolder;
        private float _nextHolderLookup;
        private ItemTween _tween;
        private readonly ItemLook _look = new ItemLook();
        private HeldGoalSource _heldGoal;
        private bool _renderHooked;
        private bool _registered;
        private int _poseFrame = -1;
        private Pose _framePose;
        // Spec 031b: the size the model is drawn at (eases to the view's fit scale while held in first person, else 1).
        private float _drawScale = 1f;
        private float _viewScale = 1f;
        private const float DrawScaleSeconds = 0.04f;
        // The client's own copy of the resting tilt (the server's is server only), for a predicted place.
        private Quaternion _clientRestTilt = Quaternion.identity;

        public bool IsHeld => HolderClientId.Value != PickupRegistry.NoHolder;

        /// <summary>Spec 027b review: the pickups this client knows (started and not stopped yet).</summary>
        public static IReadOnlyList<Pickup> ClientList => ClientPickups;

        /// <summary>Spec 027b: frame number of the first render that drew the current prediction (-1 before).</summary>
        internal int PredictionDrawnFrame { get; private set; } = -1;

        // Spec 034: this pickup is a delivery box (its carry pose).
        private bool _isBox;
        // Spec 044: this pickup is the wipe cloth (it goes round on a spill while its holder wipes).
        private bool _isCloth;

        private void Awake()
        {
            _isBox = TryGetComponent(out Worktown.Game.Shop.SupplyBox _);
            _isCloth = TryGetComponent(out Worktown.Game.Cooking.WipeCloth _);
            _collider = GetComponent<Collider>();
            _tween = new ItemTween(transform);
            _heldGoal = HeldGoal;
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
            if (ExitSound.Value != 0) ExitSound.Value = 0; // a pooled item starts without the exit of its last life
        }

        /// <summary>Spec 044, server: the item is about to be despawned into <paramref name="point"/>; viewers see it fly there and hear
        /// <paramref name="sound"/> when it lands. Call right before the despawn.</summary>
        internal void ServerExitTo(Vector3 point, Worktown.Game.Audio.SoundKey sound)
        {
            ExitPoint.Value = point;
            ExitSound.Value = (byte)((int)sound + 1);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ResetVisual();
            if (HolderClientId.Value == PickupRegistry.NoHolder) _clientRestTilt = PlaceSpots.TiltOf(transform.rotation);
            if (!_registered)
            {
                _registered = true;
                ClientPickups.Add(this);
            }
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null && !_renderHooked)
            {
                // Spec 027b review: the networked root never moves; a mesh on the root gets a visual child to move instead.
                ItemTween.EnsureVisualChild(transform);
                _renderHooked = true;
                RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
                RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
            }
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            // Spec 044: taken out of the world into something: the viewers who did not predict it see it fly there.
            if (_renderHooked && ExitFlightRules.ShowsFlight(ExitSound.Value != 0, _tween.HasDrawn, _look.HasPrediction))
            {
                _tween.Restore();
                ExitFlightLook.Begin(transform, _tween.Drawn, ExitPoint.Value, (Worktown.Game.Audio.SoundKey)(ExitSound.Value - 1), Time.unscaledTime);
            }
            Unhook();
        }

        /// <summary>Spec 044: this client's id (-1 before it has a connection).</summary>
        private int LocalClientId => ClientManager != null && ClientManager.Connection != null ? ClientManager.Connection.ClientId : ItemLook.NoHolder;

        /// <summary>Spec 039: a tool with a handle; it rests with the handle towards the cook (<see cref="ToolRest"/>).</summary>
        public bool IsHandleTool => _handleTool;

        /// <summary>Spec 020, server: the item's resting tilt (rotation without yaw), captured when the server starts it.</summary>
        internal Quaternion RestTilt => _restTilt;

        /// <summary>Spec 027b, client: the resting tilt as this client last saw the item lying (for a predicted place).</summary>
        internal Quaternion ClientRestTilt => _clientRestTilt;

        public override void OnStopServer()
        {
            base.OnStopServer();
            // Spec 045: a scene item held when the server stops is nobody's in the next session (its SyncVars reset; this does not), so
            // it never follows an old hold point after a restart.
            _serverHoldPoint = null;
        }

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
            if (_registered)
            {
                _registered = false;
                ClientPickups.Remove(this);
            }
            if (!_renderHooked) return;
            _renderHooked = false;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
        }

        private void ResetVisual()
        {
            _tween?.Reset();
            _look.Reset(HolderClientId.Value);
            _poseFrame = -1;
            _handHolder = null;
            _handHolderId = PickupRegistry.NoHolder;
        }

        // ---- Spec 027b: predicted looks, set by the owner's PickupInteractor (logic in ItemLook) ----

        /// <summary>Draws the item in <paramref name="clientId"/>'s hands (a predicted pick-up).</summary>
        internal void PredictHeld(int clientId)
        {
            _look.PredictHeld(clientId);
            PredictionDrawnFrame = -1;
        }

        /// <summary>Draws the item lying at <paramref name="pose"/> (a predicted place or drop).</summary>
        internal void PredictRest(Pose pose)
        {
            _look.PredictRest(pose);
            PredictionDrawnFrame = -1;
        }

        /// <summary>Draws the item just above the contents of the cookware it goes into (a predicted put-into).</summary>
        internal void PredictInto(Pose pose)
        {
            _look.PredictInto(pose);
            PredictionDrawnFrame = -1;
        }

        /// <summary>True while a predicted look is drawn (pending, or confirmed and waiting for the state to catch up).</summary>
        internal bool HasPrediction => _look.HasPrediction;

        /// <summary>041 review: a predicted place or drop of this item is still waiting for the server.</summary>
        internal bool HasRestPrediction => _look.HasRestPrediction;

        /// <summary>The prediction is over (see <see cref="ItemLook.EndPrediction"/>).</summary>
        internal void EndPrediction(bool confirmed) => _look.EndPrediction(confirmed, Time.unscaledTime);

        /// <summary>Spec 024: raise the holder's arm while it carries this item (a predicted pick-up counts).</summary>
        private void LateUpdate()
        {
            if (!IsClientInitialized) return;
            int holder = _look.DrawnHolder(HolderClientId.Value);
            if (holder == PickupRegistry.NoHolder) return;
            CharacterVisual visual = HolderVisual(holder, Time.unscaledTime);
            if (visual == null) return;
            if (_isBox && visual.DrawsHeldInHand)
            {
                // Spec 044 (034 feels off): both hands on the box's sides, not the one-hand holding arm.
                ArmPoseRules.BoxHands(visual.transform.position, visual.Yaw, out Vector3 left, out Vector3 right);
                visual.ReachLeft(left);
                visual.ReachRight(right);
                return;
            }
            visual.MarkHolding();
            // Spec 044 (035 feels off): the cloth goes round on the spill being wiped, the arm reaches for it.
            if (_isCloth && visual.DrawsHeldInHand && Worktown.Game.Art.SpillWipeView.TryClothFor(visual.transform.position, out Pose cloth))
                visual.ReachRight(cloth.position);
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
            _tween.Apply(_framePose, _drawScale);
        }

        private void OnEndCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            _tween.Restore();
        }

        /// <summary>Where the model is drawn this frame: ItemLook's goal, eased by the tween it starts.</summary>
        private Pose ComputeFramePose(float now)
        {
            Pose root = ItemTween.RootPose(transform);
            ItemFrame frame = _look.Resolve(HolderClientId.Value, root, _tween.HasDrawn, _tween.Drawn, now, _heldGoal);
            // Spec 044: another player's put-down lands on its spot: its "tock" for this viewer (the one who put it down heard it at the click).
            if (frame.LandedBy != ItemLook.NoHolder && frame.LandedBy != LocalClientId)
                Worktown.Game.Audio.KitchenSounds.PlayOnce(Worktown.Game.Audio.SoundRules.PlaceSound(Worktown.Game.Audio.SoundRules.MaterialOf(this), false), root.position);
            if (frame.Begin != TweenStyle.None)
            {
                FeelSettings feel = FeelSettings.Current;
                // Started a frame early: the first frame after the change already shows movement (feel.md 1).
                float lead = Mathf.Min(Time.unscaledDeltaTime, MaxFrameLead);
                _tween.Begin(frame.Begin, FeelRules.TweenSeconds(frame.Begin, frame.Kind, feel), FeelRules.TweenAmount(frame.Begin, frame.Kind, feel), now - lead);
            }
            if (frame.Kind == ItemGoalKind.Rest && !_look.HasPrediction && !_look.IsSettling)
                _clientRestTilt = PlaceSpots.TiltOf(root.rotation);
            Pose drawn = _tween.Evaluate(frame.Goal, now);
            if (_look.HasPrediction && PredictionDrawnFrame < 0) PredictionDrawnFrame = Time.frameCount;
            // Spec 031b: a large item in the first-person view is drawn smaller; the size eases with the arc into and out of view.
            float wanted = frame.Kind == ItemGoalKind.View ? _viewScale : 1f;
            _drawScale = Mathf.Lerp(_drawScale, wanted, 1f - Mathf.Exp(-Time.unscaledDeltaTime / DrawScaleSeconds));
            if (Mathf.Abs(_drawScale - wanted) < 0.002f) _drawScale = wanted;
            return drawn;
        }

        /// <summary>Spec 024 / 018: in the holder's right hand or in your own first-person view; false: the hold point.</summary>
        private bool HeldGoal(int holder, out ItemGoalKind kind, out Pose pose)
        {
            CharacterVisual visual = HolderVisual(holder, Time.unscaledTime);
            if (visual != null)
            {
                Transform hand = visual.Hand;
                if (visual.DrawsHeldInHand && hand != null)
                {
                    kind = ItemGoalKind.Hand;
                    Quaternion frame = Quaternion.Euler(0f, visual.Yaw, 0f);
                    pose = new Pose(hand.position + frame * HandOffset, frame);
                    // Spec 034: a delivery box is carried in front of the chest with both hands (HoldViewRules.BoxCarry*), not in one hand.
                    if (_isBox) pose = new Pose(visual.transform.position + Vector3.up * HoldViewRules.BoxCarryHeight
                        + frame * (Vector3.forward * HoldViewRules.BoxCarryForward), frame);
                    // Spec 044: a wiping holder's cloth is drawn going round on the spill (the same pose the dispenser drew its own cloth at).
                    else if (_isCloth && Worktown.Game.Art.SpillWipeView.TryClothFor(visual.transform.position, out Pose cloth)) pose = cloth;
                    return true;
                }
                if (!visual.DrawsHeldInHand && visual.TryGetComponent(out HeldItemView view) && view.TryGetViewPose(this, out pose, out _viewScale))
                {
                    kind = ItemGoalKind.View;
                    return true;
                }
            }
            kind = ItemGoalKind.HoldPoint;
            pose = default;
            return false;
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
