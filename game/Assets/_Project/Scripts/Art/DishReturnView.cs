using UnityEngine;
using Worktown.Game.Audio;
using Worktown.Game.Cooking;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 035: the dish return's stack of dirty plates, for every viewer: as many smudged plates as its synced byte counts (at most
    /// <see cref="CleaningArtRules.ReturnStackMax"/>); a plate coming back clinks onto it. The taking player sees its plate go at the
    /// click (<see cref="PredictTake"/>; the copy flies into the hands, StockTakeLook) until the byte agrees or the request times out.
    /// The stack's top plate is the take slot's model (the copy is made from it). No per-frame allocations.
    /// </summary>
    public sealed class DishReturnView : MonoBehaviour
    {
        [SerializeField] private Station _station;
        [Tooltip("The stack's plates, bottom first (the last one is the take slot's model, moved to the top).")]
        [SerializeField] private Transform[] _plates = new Transform[0];
        [Tooltip("The take slot's model: always the top plate drawn.")]
        [SerializeField] private Transform _top;
        [SerializeField] private float _step = 0.02f;

        private static KitchenSounds _sounds;
        private static bool _soundsLooked;
        private StationContents _contents;
        private int _shown = -1;
        private int _lastSynced = -1;
        private int _pending;
        private int _pendingFrom;
        private float _pendingUntil;
        private Vector3 _topBase;

        /// <summary>The dirty plates as this client sees them (its own take first).</summary>
        public byte SeenCount
        {
            get
            {
                int synced = Synced;
                int seen = _pending > 0 && synced >= _pendingFrom ? synced - _pending : synced;
                return (byte)(seen < 0 ? 0 : seen);
            }
        }

        private int Synced => _contents != null ? _contents.StoveFlags.Value : 0;

        private void Awake()
        {
            if (_station == null) _station = GetComponent<Station>();
            if (_station != null) _contents = _station.GetComponent<StationContents>();
            if (_top != null) _topBase = _top.localPosition;
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) enabled = false;
        }

        /// <summary>The owner took the top plate (its copy is on the way to the hands): one fewer now.</summary>
        public void PredictTake(float timeout)
        {
            _pending = 1;
            _pendingFrom = Synced;
            _pendingUntil = Time.unscaledTime + Mathf.Max(0.5f, timeout);
        }

        private void LateUpdate()
        {
            int synced = Synced;
            if (_pending > 0 && (synced < _pendingFrom || Time.unscaledTime > _pendingUntil)) _pending = 0;
            if (_lastSynced >= 0 && synced > _lastSynced) Play(SoundKey.PlaceCeramic, transform.position); // a plate came back
            _lastSynced = synced;
            int count = SeenCount;
            if (count == _shown) return;
            _shown = count;
            int drawn = Mathf.Min(count, CleaningArtRules.ReturnStackMax);
            // The stack's lower plates, then the top (the slot's model) on them.
            int below = Mathf.Max(0, drawn - 1);
            for (int i = 0; i < _plates.Length; i++)
                if (_plates[i] != null) _plates[i].gameObject.SetActive(i < below);
            if (_top != null)
            {
                _top.gameObject.SetActive(drawn > 0);
                _top.localPosition = _topBase + Vector3.up * (_step * below);
            }
        }

        private static void Play(SoundKey key, Vector3 at)
        {
            if (!_soundsLooked)
            {
                _soundsLooked = true;
                _sounds = FindFirstObjectByType<KitchenSounds>();
            }
            if (_sounds != null) _sounds.Play(key, at);
        }
    }
}
