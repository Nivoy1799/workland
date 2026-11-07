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
    /// </summary>
    public sealed class CharacterVisual : MonoBehaviour
    {
        /// <summary>Name of the hand anchor the art builder adds under the right arm bone.</summary>
        public const string HandName = "HandR";
        /// <summary>Bone of the Kenney Mini Characters rig the holding pose is masked to.</summary>
        public const string RightArmBone = "arm-right";
        /// <summary>m: the pack's crouch pose kneels about this far into the floor at 1.8 m; the model is lifted by it.</summary>
        public const float CrouchLift = 0.19f;
        /// <summary>m/s: anything faster is a teleport (spawn, reconcile snap), not walking.</summary>
        private const float TeleportSpeed = 20f;
        private const float LiftSpeed = 1.5f;

        [Tooltip("Prebuilt character wrappers, in ArtCatalogRules variant order. One is active.")]
        [SerializeField] private GameObject[] _variants = new GameObject[0];
        [Tooltip("The HandR anchor of each variant (same order).")]
        [SerializeField] private Transform[] _hands = new Transform[0];
        [Tooltip("Parent of the variants: lifted while crouched (the emote wobble turns it too).")]
        [SerializeField] private Transform _container;
        [Tooltip("Transform whose movement drives the animation (the smoothed Graphic of a player, the customer root).")]
        [SerializeField] private Transform _tracked;
        [Tooltip("Players: the variant follows the owner client id. Customers: the object id.")]
        [SerializeField] private bool _variantByOwner;
        [SerializeField] private PlayerController _player;
        [SerializeField] private FirstPersonBody _firstPerson;
        [SerializeField] private AnimationClip _idle;
        [SerializeField] private AnimationClip _walk;
        [SerializeField] private AnimationClip _run;
        [SerializeField] private AnimationClip _crouch;
        [SerializeField] private AnimationClip _hold;

        private NetworkObject _nob;
        private int _variant = -1;
        private int _variantId = int.MinValue;
        private PlayableGraph _graph;
        private AnimationMixerPlayable _base;
        private AnimationLayerMixerPlayable _layers;
        private AnimationClipPlayable[] _clips = new AnimationClipPlayable[0];
        private bool _hasHoldLayer;
        private readonly float[] _weights = { 1f, 0f, 0f, 0f };
        private readonly double[] _phase = new double[5];
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
            float rate = CharacterAnimRules.PlaybackRate(_state, _speed);
            bool holding = Time.frameCount - _holdFrame <= 2;
            _holdWeight = CharacterAnimRules.FadeWeight(_holdWeight, holding, dt);

            if (_container != null)
            {
                Vector3 local = _container.localPosition;
                float lift = Mathf.MoveTowards(local.y, crouched ? CrouchLift : 0f, LiftSpeed * dt);
                if (!Mathf.Approximately(lift, local.y)) _container.localPosition = new Vector3(local.x, lift, local.z);
            }

            if (!_graph.IsValid()) return;
            for (int i = 0; i < _weights.Length; i++)
            {
                _weights[i] = CharacterAnimRules.FadeWeight(_weights[i], i == (int)_state, dt);
                _base.SetInputWeight(i, _weights[i]);
                if (_weights[i] <= 0f) continue;
                _phase[i] += dt * (i == (int)_state ? rate : 1f);
                _clips[i].SetTime(CharacterAnimRules.LoopTime(_phase[i], _clips[i].GetAnimationClip().length));
            }
            if (_hasHoldLayer)
            {
                _layers.SetInputWeight(1, _holdWeight);
                _phase[4] += dt;
                _clips[4].SetTime(CharacterAnimRules.LoopTime(_phase[4], _clips[4].GetAnimationClip().length));
            }
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
            AnimationClip[] baseClips = { _idle, _walk, _run, _crouch };
            foreach (AnimationClip clip in baseClips)
            {
                if (clip == null || animator == null)
                {
                    Debug.LogWarning("[Character] " + name + ": animator or clip missing, the character stands still");
                    return;
                }
            }
            animator.applyRootMotion = false;

            _graph = PlayableGraph.Create(name + " character");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(_graph, "Character", animator);
            _base = AnimationMixerPlayable.Create(_graph, baseClips.Length);
            _clips = new AnimationClipPlayable[5];
            for (int i = 0; i < baseClips.Length; i++)
            {
                _clips[i] = CreateClip(baseClips[i]);
                _graph.Connect(_clips[i], 0, _base, i);
                _weights[i] = i == (int)_state ? 1f : 0f;
                _base.SetInputWeight(i, _weights[i]);
            }
            _layers = AnimationLayerMixerPlayable.Create(_graph, 2);
            _graph.Connect(_base, 0, _layers, 0);
            _layers.SetInputWeight(0, 1f);
            _hasHoldLayer = _hold != null;
            if (_hasHoldLayer)
            {
                _clips[4] = CreateClip(_hold);
                _graph.Connect(_clips[4], 0, _layers, 1);
                _layers.SetLayerMaskFromAvatarMask(1, RightArmMask(animator.transform));
                _layers.SetInputWeight(1, _holdWeight);
            }
            output.SetSourcePlayable(_layers);
            _graph.Play();
        }

        private AnimationClipPlayable CreateClip(AnimationClip clip)
        {
            AnimationClipPlayable playable = AnimationClipPlayable.Create(_graph, clip);
            playable.SetApplyFootIK(false);
            playable.SetSpeed(0); // the time is set every frame from the phase (loops, playback rate)
            return playable;
        }

        /// <summary>A generic mask with only the right arm (and the hand anchor under it) active.</summary>
        private static AvatarMask RightArmMask(Transform root)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            var mask = new AvatarMask { transformCount = all.Length };
            for (int i = 0; i < all.Length; i++)
            {
                mask.SetTransformPath(i, PathOf(all[i], root));
                mask.SetTransformActive(i, IsUnderArm(all[i], root));
            }
            return mask;
        }

        private static bool IsUnderArm(Transform t, Transform root)
        {
            for (Transform it = t; it != null && it != root; it = it.parent)
                if (it.name == RightArmBone) return true;
            return false;
        }

        private static string PathOf(Transform t, Transform root)
        {
            if (t == root) return string.Empty;
            string path = t.name;
            for (Transform it = t.parent; it != null && it != root; it = it.parent) path = it.name + "/" + path;
            return path;
        }

        private void DestroyGraph()
        {
            if (_graph.IsValid()) _graph.Destroy();
            _hasHoldLayer = false;
        }

        private void OnDestroy()
        {
            DestroyGraph();
        }
    }
}
