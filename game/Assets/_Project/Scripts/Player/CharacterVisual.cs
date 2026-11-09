using FishNet.Object;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using Worktown.Game.Art;
using Worktown.Game.Net;

namespace Worktown.Game.Player
{
    /// <summary>
    /// Spec 024: the low-poly person of a player or customer. Visual only and local to every client: the networked
    /// CharacterController (players) or NetworkTransform (customers) and every rule stay as they are.
    /// Picks one of the prebuilt character variants from the owner client id (players) or the object id (customers),
    /// then animates it from data every client already has: the horizontal speed of the smoothed transform and the
    /// crouch flag (<see cref="CharacterAnimRules"/>). Carried items raise the right arm (holding pose on the arm only).
    /// Clips are played through a small PlayableGraph (no AnimatorController asset). A server without graphics turns
    /// all of it off.
    /// Spec 025: the body is a Humanoid rig with retargeted Humanoid clips; root motion is never applied (the server
    /// moves the body). Walking and running play the walk and jog clips as one blend on a shared phase, weighted and
    /// timed by <see cref="CharacterAnimRules.Locomotion"/> from the clip speeds the art builder measured, so the cadence
    /// fits the speed and the feet stay planted. Crouching plays the crouch idle standing and the crouch walk moving.
    /// The hand anchor sits on the rig's real right hand bone, the holding pose is masked to the right arm.
    /// </summary>
    public sealed class CharacterVisual : MonoBehaviour
    {
        /// <summary>Name of the hand anchor the art builder adds under the rig's right hand bone.</summary>
        public const string HandName = "HandR";
        /// <summary>m/s: anything faster is a teleport (spawn, reconcile snap), not walking.</summary>
        private const float TeleportSpeed = 20f;
        // Spec 027b: layers over the base clips: the legs of the standing pose, then the holding arm.
        private const int BaseLayer = 0, LegsLayer = 1, HoldLayer = 2, LayerCount = 3;

        // Mixer inputs of the base layer.
        private const int IdleInput = 0, WalkInput = 1, JogInput = 2, CrouchInput = 3, CrouchWalkInput = 4, InputCount = 5;
        // Fade groups: idle, moving (walk + jog), crouch, crouch walk.
        private const int IdleGroup = 0, MoveGroup = 1, CrouchGroup = 2, CrouchWalkGroup = 3, GroupCount = 4;

        [Tooltip("Prebuilt character wrappers, in ArtCatalogRules variant order. One is active.")]
        [SerializeField] private GameObject[] _variants = new GameObject[0];
        [Tooltip("The HandR anchor of each variant (same order).")]
        [SerializeField] private Transform[] _hands = new Transform[0];
        [Tooltip("Parent of the variants.")]
        [SerializeField] private Transform _container;
        [Tooltip("Transform whose movement drives the animation (the smoothed Graphic of a player, the customer root).")]
        [SerializeField] private Transform _tracked;
        [Tooltip("Players: the variant follows the owner client id. Customers: the object id.")]
        [SerializeField] private bool _variantByOwner;
        [SerializeField] private PlayerController _player;
        [SerializeField] private FirstPersonBody _firstPerson;
        [SerializeField] private AnimationClip _idle;
        [SerializeField] private AnimationClip _walk;
        [SerializeField] private AnimationClip _jog;
        [SerializeField] private AnimationClip _crouch;
        [SerializeField] private AnimationClip _crouchWalk;
        [SerializeField] private AnimationClip _hold;
        [Tooltip("Spec 027b: a pose with the feet under the hips; its legs are blended over the idle (CharacterAnimRules.StandLegsWeight).")]
        [SerializeField] private AnimationClip _stand;
        [Header("Spec 025: measured by the art builder on this body (m/s at rate 1, loop seconds)")]
        [SerializeField] private float _walkClipSpeed;
        [SerializeField] private float _walkLoopLength;
        [SerializeField] private float _jogClipSpeed;
        [SerializeField] private float _jogLoopLength;
        [Tooltip("The library's sprint, measured for reference: it is not played (see spec 025, amendment notes).")]
        [SerializeField] private float _sprintClipSpeed;
        [SerializeField] private float _sprintLoopLength;
        [SerializeField] private float _crouchWalkClipSpeed;
        [SerializeField] private float _crouchWalkLoopLength;
        [Tooltip("Normalized phase of the jog at which its left foot lands, minus the walk's (keeps the blend in step).")]
        [SerializeField] private float _jogPhaseOffset;

        private NetworkObject _nob;
        private int _variant = -1;
        private int _variantId = int.MinValue;
        private PlayableGraph _graph;
        private AnimationMixerPlayable _base;
        private AnimationLayerMixerPlayable _layers;
        private AnimationClipPlayable[] _clips = new AnimationClipPlayable[0];
        private AnimationClipPlayable _holdClip;
        private bool _hasHoldLayer;
        private AnimationClipPlayable _standClip;
        private bool _hasLegsLayer;
        private readonly float[] _groupWeights = { 1f, 0f, 0f, 0f };
        private readonly double[] _groupPhase = new double[GroupCount]; // normalized loops
        private double _holdPhase;
        private float _holdWeight;
        private int _holdFrame = -100;
        private CharacterAnimState _state = CharacterAnimState.Idle;
        private float _speed;
        private Vector3 _lastPosition;
        private bool _hasLastPosition;

        /// <summary>The right hand anchor of the shown variant (null before a variant is chosen).</summary>
        public Transform Hand => _variant >= 0 && _variant < _hands.Length ? _hands[_variant] : null;

        /// <summary>Owner client id of the player (or -1).</summary>
        public int OwnerId => _nob != null ? _nob.OwnerId : -1;

        /// <summary>
        /// False only for your own character in first person: there <see cref="HeldItemView"/> draws the held item in
        /// view instead of the hand.
        /// </summary>
        public bool DrawsHeldInHand => _firstPerson == null || !_firstPerson.IsFirstPerson;

        /// <summary>Yaw of the shown body in degrees (the smoothed transform).</summary>
        public float Yaw => (_tracked != null ? _tracked : transform).eulerAngles.y;

        public CharacterAnimState State => _state;

        /// <summary>Spec 027b: the smoothed horizontal speed the animation uses (m/s).</summary>
        public float Speed => _speed;

        /// <summary>Spec 027b: the position the animation measures (the smoothed body).</summary>
        public Vector3 TrackedPosition => (_tracked != null ? _tracked : transform).position;

        /// <summary>Spec 025: the measured clips (m/s at rate 1 on this body, loop seconds).</summary>
        public GaitClip WalkGait => new GaitClip(_walkClipSpeed, _walkLoopLength);
        public GaitClip JogGait => new GaitClip(_jogClipSpeed, _jogLoopLength);
        public GaitClip SprintGait => new GaitClip(_sprintClipSpeed, _sprintLoopLength);
        public GaitClip CrouchWalkGait => new GaitClip(_crouchWalkClipSpeed, _crouchWalkLoopLength);

        /// <summary>Called every frame by an item this character carries: raise the right arm.</summary>
        public void MarkHolding() => _holdFrame = Time.frameCount;

        /// <summary>The character of the player with this owner client id, or null (not spawned here yet).</summary>
        public static CharacterVisual FindPlayer(int ownerClientId)
        {
            foreach (CharacterVisual visual in FindObjectsByType<CharacterVisual>(FindObjectsSortMode.None))
                if (visual._variantByOwner && visual.OwnerId == ownerClientId) return visual;
            return null;
        }

        private void Awake()
        {
            _nob = GetComponent<NetworkObject>();
            if (_tracked == null) _tracked = transform;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                // Headless server: nobody looks, so no animation work at all.
                foreach (GameObject variant in _variants)
                    if (variant != null)
                        foreach (Animator animator in variant.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
                enabled = false;
            }
        }

        private void Update()
        {
            SelectVariant();
            float dt = Time.deltaTime;

            Vector3 position = _tracked.position;
            if (_hasLastPosition && dt > 0f)
            {
                Vector3 delta = position - _lastPosition;
                delta.y = 0f;
                float measured = delta.magnitude / dt;
                if (measured < TeleportSpeed) _speed = CharacterAnimRules.SmoothSpeed(_speed, measured, dt);
            }
            _lastPosition = position;
            _hasLastPosition = true;

            bool crouched = _player != null && _player.IsCrouched;
            _state = CharacterAnimRules.StateFor(_speed, crouched, _state);
            bool holding = Time.frameCount - _holdFrame <= 2;
            _holdWeight = CharacterAnimRules.FadeWeight(_holdWeight, holding, dt);

            if (!_graph.IsValid()) return;
            int shown = GroupOf(_state);
            for (int g = 0; g < GroupCount; g++)
                _groupWeights[g] = CharacterAnimRules.FadeWeight(_groupWeights[g], g == shown, dt);

            // Idle and crouch idle at rate 1.
            Advance(IdleGroup, dt, 1f, _idle.length);
            Advance(CrouchGroup, dt, 1f, _crouch.length);
            SetClip(IdleInput, _groupWeights[IdleGroup], _groupPhase[IdleGroup]);
            SetClip(CrouchInput, _groupWeights[CrouchGroup], _groupPhase[CrouchGroup]);

            // Walk and run: one phase for both clips, the jog on the same step (phase offset).
            LocomotionBlend blend = CharacterAnimRules.Locomotion(_speed, WalkGait, JogGait);
            Advance(MoveGroup, dt, blend.Rate, blend.LoopLength);
            float move = _groupWeights[MoveGroup];
            SetClip(WalkInput, move * (1f - blend.Weight), _groupPhase[MoveGroup]);
            SetClip(JogInput, move * blend.Weight, _groupPhase[MoveGroup] + _jogPhaseOffset);

            Advance(CrouchWalkGroup, dt, CharacterAnimRules.CrouchRate(_speed, _crouchWalkClipSpeed), _crouchWalk.length);
            SetClip(CrouchWalkInput, _groupWeights[CrouchWalkGroup], _groupPhase[CrouchWalkGroup]);

            // Spec 027b: the idle clip stands with one foot 0.29 m ahead (a frozen stride from the side); its legs are pulled
            // towards the feet-together pose while (and as much as) the idle shows.
            if (_hasLegsLayer) _layers.SetInputWeight(LegsLayer, LegsWeight());
            if (_hasHoldLayer)
            {
                _layers.SetInputWeight(HoldLayer, _holdWeight);
                _holdPhase += dt;
                _holdClip.SetTime(CharacterAnimRules.LoopTime(_holdPhase, _hold.length));
            }
        }

        private static int GroupOf(CharacterAnimState state)
        {
            switch (state)
            {
                case CharacterAnimState.Walk:
                case CharacterAnimState.Run: return MoveGroup;
                case CharacterAnimState.Crouch: return CrouchGroup;
                case CharacterAnimState.CrouchWalk: return CrouchWalkGroup;
                default: return IdleGroup;
            }
        }

        /// <summary>Moves a group's normalized phase on while it shows (a hidden group restarts where it stopped).</summary>
        private void Advance(int group, float dt, float rate, float loopLength)
        {
            if (_groupWeights[group] <= 0f || loopLength <= 0f) return;
            _groupPhase[group] = (_groupPhase[group] + dt * rate / loopLength) % 1.0;
        }

        private void SetClip(int input, float weight, double phase)
        {
            _base.SetInputWeight(input, weight);
            if (weight <= 0f) return;
            float length = _clips[input].GetAnimationClip().length;
            _clips[input].SetTime(CharacterAnimRules.LoopTime(phase * length, length));
        }

        private void SelectVariant()
        {
            if (_nob == null || !_nob.IsSpawned || _variants.Length == 0) return;
            int id = _variantByOwner ? _nob.OwnerId : _nob.ObjectId;
            if (id == _variantId) return;
            _variantId = id;
            _variant = ArtCatalogRules.CharacterVariant(id, _variants.Length);
            for (int i = 0; i < _variants.Length; i++)
                if (_variants[i] != null) _variants[i].SetActive(i == _variant);
            BuildGraph(_variants[_variant]);
        }

        private void BuildGraph(GameObject variant)
        {
            DestroyGraph();
            Animator animator = variant != null ? variant.GetComponentInChildren<Animator>(true) : null;
            var baseClips = new AnimationClip[InputCount];
            baseClips[IdleInput] = _idle;
            baseClips[WalkInput] = _walk;
            baseClips[JogInput] = _jog;
            baseClips[CrouchInput] = _crouch;
            baseClips[CrouchWalkInput] = _crouchWalk;
            foreach (AnimationClip clip in baseClips)
            {
                if (clip == null || animator == null)
                {
                    Debug.LogWarning("[Character] " + name + ": animator or clip missing, the character stands still");
                    return;
                }
            }
            animator.applyRootMotion = false; // root motion is in the clips, the server moves the body

            _graph = PlayableGraph.Create(name + " character");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(_graph, "Character", animator);
            _base = AnimationMixerPlayable.Create(_graph, InputCount);
            _clips = new AnimationClipPlayable[InputCount];
            int shown = GroupOf(_state);
            for (int g = 0; g < GroupCount; g++) _groupWeights[g] = g == shown ? 1f : 0f;
            for (int i = 0; i < InputCount; i++)
            {
                _clips[i] = CreateClip(baseClips[i], true);
                _graph.Connect(_clips[i], 0, _base, i);
                _base.SetInputWeight(i, 0f);
            }
            _layers = AnimationLayerMixerPlayable.Create(_graph, LayerCount);
            _graph.Connect(_base, 0, _layers, BaseLayer);
            _layers.SetInputWeight(BaseLayer, 1f);
            _hasLegsLayer = _stand != null;
            if (_hasLegsLayer)
            {
                _standClip = CreateClip(_stand, true);
                _graph.Connect(_standClip, 0, _layers, LegsLayer);
                SetMask(LegsLayer, LegsMask());
                _layers.SetInputWeight(LegsLayer, LegsWeight());
            }
            _hasHoldLayer = _hold != null;
            if (_hasHoldLayer)
            {
                _holdClip = CreateClip(_hold, false);
                _graph.Connect(_holdClip, 0, _layers, HoldLayer);
                SetMask(HoldLayer, RightArmMask());
                _layers.SetInputWeight(HoldLayer, _holdWeight);
            }
            output.SetSourcePlayable(_layers);
            _graph.Play();
        }

        /// <summary>Spec 025: foot IK on for the base clips, so the retargeted feet land where the clip puts them.</summary>
        private AnimationClipPlayable CreateClip(AnimationClip clip, bool footIk)
        {
            AnimationClipPlayable playable = AnimationClipPlayable.Create(_graph, clip);
            playable.SetApplyFootIK(footIk);
            playable.SetSpeed(0); // the time is set every frame from the phase (loops, playback rate)
            return playable;
        }

        /// <summary>Spec 027b: the standing-legs weight: the idle over the moving clips (CharacterAnimRules.LegsLayerWeight).</summary>
        private float LegsWeight() => CharacterAnimRules.LegsLayerWeight(_groupWeights[IdleGroup],
            _groupWeights[MoveGroup] + _groupWeights[CrouchWalkGroup]);

        /// <summary>The layer mixer copies the mask, so the mask object is destroyed right away (no leak per graph).</summary>
        private void SetMask(int layer, AvatarMask mask)
        {
            _layers.SetLayerMaskFromAvatarMask((uint)layer, mask);
            Destroy(mask);
        }

        /// <summary>Spec 027b: a Humanoid mask with only the legs and their foot IK active (the standing pose's layer).
        /// A new object: the caller destroys it.</summary>
        public static AvatarMask LegsMask()
        {
            var mask = new AvatarMask();
            for (AvatarMaskBodyPart part = 0; part < AvatarMaskBodyPart.LastBodyPart; part++)
                mask.SetHumanoidBodyPartActive(part, part == AvatarMaskBodyPart.LeftLeg || part == AvatarMaskBodyPart.RightLeg
                    || part == AvatarMaskBodyPart.LeftFootIK || part == AvatarMaskBodyPart.RightFootIK);
            return mask;
        }

        /// <summary>Spec 025: a Humanoid mask with only the right arm and its fingers active.</summary>
        private static AvatarMask RightArmMask()
        {
            var mask = new AvatarMask();
            for (AvatarMaskBodyPart part = 0; part < AvatarMaskBodyPart.LastBodyPart; part++)
                mask.SetHumanoidBodyPartActive(part, part == AvatarMaskBodyPart.RightArm || part == AvatarMaskBodyPart.RightFingers);
            return mask;
        }

        private void DestroyGraph()
        {
            if (_graph.IsValid()) _graph.Destroy();
            _hasHoldLayer = false;
            _hasLegsLayer = false;
        }

        private void OnDestroy()
        {
            DestroyGraph();
        }
    }
}
