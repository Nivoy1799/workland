using UnityEngine;
using Worktown.Game.Audio;
using Worktown.Game.Cooking;
using Worktown.Game.Shop;
using Worktown.Sim.Economy;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 035: the trash can by the staff gap, for every viewer: the bag inside rises with the synced fill (CleaningArtRules.TrashLevel),
    /// its rim reads Terracotta when full, the lid swings open and falls shut with a thump at every discard; the bag leaving rustles.
    /// The discarding player sees its item go in at the click (<see cref="PredictDiscard"/>) and its own bag come out
    /// (<see cref="PredictTakeBag"/>), until the byte agrees or the request times out. The bag is the take slot's model (the copy that
    /// flies into the hands is made from it). No per-frame allocations.
    /// </summary>
    public sealed class TrashCanView : MonoBehaviour
    {
        [SerializeField] private Station _station;
        [Tooltip("The bag in the can (the take slot's model): its height follows the fill.")]
        [SerializeField] private Transform _bag;
        [Tooltip("The lid, hinged at its back edge.")]
        [SerializeField] private Transform _lid;
        [Tooltip("The rim's bars (tinted Terracotta when the can is full).")]
        [SerializeField] private Renderer[] _rims = new Renderer[0];
        [SerializeField] private Transform _opening;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static KitchenSounds _sounds;
        private static bool _soundsLooked;
        private StationContents _contents;
        private MaterialPropertyBlock _block;
        private int _shown = -1;
        private int _lastSynced = -1;
        private int _pendingAdd;
        private int _pendingFrom;
        private bool _pendingEmpty;
        private float _pendingUntil;
        private float _lidAt = float.NegativeInfinity;
        private bool _thumped = true;
        private Quaternion _lidRest = Quaternion.identity;
        private Vector3 _bagScale = Vector3.one;
        private float _bagBottom;
        private bool _full;

        /// <summary>Where things go in (the can's opening, world).</summary>
        public Vector3 Opening => _opening != null ? _opening.position : transform.position + Vector3.up * 0.5f;

        private int Synced => _contents != null ? _contents.StoveFlags.Value : 0;

        /// <summary>The fill as this client sees it (its own discard or bag first).</summary>
        public byte SeenFill
        {
            get
            {
                int synced = Synced;
                if (_pendingEmpty && synced > 0) return 0;
                int seen = _pendingAdd > 0 && synced <= _pendingFrom ? _pendingFrom + _pendingAdd : synced;
                return (byte)Mathf.Clamp(seen, 0, HygieneRules.TrashCapacity);
            }
        }

        private void Awake()
        {
            if (_station == null) _station = GetComponent<Station>();
            if (_station != null) _contents = _station.GetComponent<StationContents>();
            if (_lid != null) _lidRest = _lid.localRotation;
            if (_bag != null)
            {
                _bagScale = _bag.localScale;
                _bagBottom = _bag.localPosition.y - _bagScale.y * 0.5f;
            }
            _block = new MaterialPropertyBlock();
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) enabled = false;
        }

        /// <summary>The owner threw something in: one more now, the lid swings.</summary>
        public void PredictDiscard(float timeout)
        {
            _pendingAdd = 1;
            _pendingFrom = Synced;
            _pendingEmpty = false;
            _pendingUntil = Time.unscaledTime + Mathf.Max(0.5f, timeout);
            OpenLid(false); // the interactor played the thump at the click
        }

        /// <summary>The owner took the bag out: empty now.</summary>
        public void PredictTakeBag(float timeout)
        {
            _pendingAdd = 0;
            _pendingEmpty = true;
            _pendingUntil = Time.unscaledTime + Mathf.Max(0.5f, timeout);
        }

        private void OpenLid(bool thump)
        {
            _lidAt = Time.unscaledTime;
            _thumped = !thump;
        }

        private void LateUpdate()
        {
            float now = Time.unscaledTime;
            int synced = Synced;
            if (_pendingAdd > 0 && (synced > _pendingFrom || now > _pendingUntil)) _pendingAdd = 0;
            if (_pendingEmpty && (synced == 0 || now > _pendingUntil)) _pendingEmpty = false;
            // Another cook's discard: the lid and its thump; another cook's bag: the rustle.
            if (_lastSynced >= 0 && synced > _lastSynced && _pendingAdd == 0 && now - _lidAt > 0.3f) OpenLid(true);
            if (_lastSynced > 0 && synced == 0 && !_pendingEmpty) Play(SoundRules.BagSound);
            _lastSynced = synced;

            if (_lid != null)
            {
                float since = now - _lidAt;
                // Open at rest (the fill shows); a discard swings it down onto the can and back up.
                _lid.localRotation = _lidRest * Quaternion.Euler(-(CleaningArtRules.LidRestOpen - CleaningArtRules.LidAngle(since)), 0f, 0f);
                if (!_thumped && since >= CleaningArtRules.LidOpenSeconds * 0.3f) // the lid hits the can
                {
                    _thumped = true;
                    Play(SoundRules.DiscardSound(false));
                }
            }

            int fill = SeenFill;
            if (fill == _shown) return;
            _shown = fill;
            if (_bag != null)
            {
                _bag.gameObject.SetActive(fill > 0);
                // The bag stands on the can's floor (its bottom where the scene put it) and rises to the trash level.
                float height = Mathf.Max(0.02f, CleaningArtRules.TrashLevel(CleaningRules.FillShare(fill)) - CleaningArtRules.TrashFloor);
                _bag.localScale = new Vector3(_bagScale.x, height, _bagScale.z);
                _bag.localPosition = new Vector3(_bag.localPosition.x, _bagBottom + height * 0.5f, _bag.localPosition.z);
            }
            bool full = HygieneRules.IsTrashFull(fill);
            if (full != _full)
            {
                _full = full;
                _block.SetColor(BaseColorId, full ? Palette.Terracotta : Palette.Charcoal);
                foreach (Renderer rim in _rims)
                    if (rim != null) rim.SetPropertyBlock(_block);
            }
        }

        private void Play(SoundKey key)
        {
            if (!_soundsLooked)
            {
                _soundsLooked = true;
                _sounds = FindFirstObjectByType<KitchenSounds>();
            }
            if (_sounds != null) _sounds.Play(key, Opening);
        }
    }
}
