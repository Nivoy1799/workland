using UnityEngine;
using Worktown.Game.Audio;
using Worktown.Game.Cooking;
using Worktown.Game.Shop;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 035: the sink's basin, for every viewer: the water, the dirty plates in it (the synced basin count), bubbles on the water
    /// (more with more plates, lively while scrubbing), and while someone scrubs the top plate wobbles and the scrub loop plays. A plate
    /// going in splashes. The scrubbing player sees it from its press to its release at once (<see cref="Predict"/>, the IPourView of
    /// the held tool), and its own plate in the water at the click (<see cref="PredictAdd"/>), before the server answers; a scrubbed plate
    /// leaves the water when the byte drops. No per-frame allocations.
    /// </summary>
    public sealed class SinkView : MonoBehaviour, IPourView
    {
        [SerializeField] private Station _sink;
        [Tooltip("The plates in the basin, bottom first (CleaningRules.BasinCapacity).")]
        [SerializeField] private Transform[] _dishes = new Transform[0];
        [Tooltip("The bubbles on the water (CleaningArtRules.MaxBubbles).")]
        [SerializeField] private Transform[] _bubbles = new Transform[0];
        [SerializeField] private Transform _water;
        [SerializeField] private float _step = 0.02f;

        private static SoundCatalog _soundCatalog;
        private static KitchenSounds _sounds;
        private static bool _soundsLooked;
        private StationContents _contents;
        private AudioSource _loop;
        private bool _predicting;
        private bool _predicted;
        private float _predictedUntil;
        private bool _shownScrub;
        private float _scrubStart;
        private int _add;
        private int _addFrom;
        private float _addUntil;
        private int _shownBasin = -1;
        private int _lastSynced = -1;
        private Vector3[] _dishBase = new Vector3[0];
        private Quaternion[] _dishRot = new Quaternion[0];
        private float _bubbleY;

        /// <summary>True while this client draws a scrub.</summary>
        public bool IsScrubbing => _shownScrub;
        /// <summary>The frame the predicted scrub was first drawn (click-to-visual log); -1 before.</summary>
        public int PredictionDrawnFrame { get; private set; } = -1;

        private byte SyncedByte => _contents != null ? _contents.StoveFlags.Value : (byte)0;

        /// <summary>The sink's byte as this client sees it: its own plate put in first (the scrubbing bit as synced).</summary>
        public byte SeenByte
        {
            get
            {
                byte b = SyncedByte;
                int basin = CleaningRules.BasinOf(b);
                if (_add > 0 && basin <= _addFrom) basin = Mathf.Min(CleaningRules.BasinCapacity, _addFrom + _add);
                return CleaningRules.SinkByte(basin, CleaningRules.IsScrubbing(b));
            }
        }

        private void Awake()
        {
            if (_sink == null) _sink = GetComponentInParent<Station>();
            if (_sink != null) _contents = _sink.GetComponent<StationContents>();
            _dishBase = new Vector3[_dishes.Length];
            _dishRot = new Quaternion[_dishes.Length];
            for (int i = 0; i < _dishes.Length; i++)
                if (_dishes[i] != null)
                {
                    _dishBase[i] = _dishes[i].localPosition;
                    _dishRot[i] = _dishes[i].localRotation;
                }
            if (_bubbles.Length > 0 && _bubbles[0] != null) _bubbleY = _bubbles[0].localPosition.y;
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                enabled = false;
                return;
            }
            _loop = gameObject.AddComponent<AudioSource>();
            _loop.playOnAwake = false;
            _loop.loop = true;
            _loop.spatialBlend = 1f;
            _loop.minDistance = 1f;
            _loop.maxDistance = 14f;
        }

        /// <summary>The scrubbing player: the look follows the button (on at the press, off at the release).</summary>
        public void Predict(bool on)
        {
            _predicting = true;
            _predicted = on;
            _predictedUntil = Time.unscaledTime + (on ? 8f : 1f);
            if (on) PredictionDrawnFrame = -1;
        }

        /// <summary>The owner put a dirty plate in (it drops into the water now).</summary>
        public void PredictAdd(float timeout)
        {
            _add = 1;
            _addFrom = CleaningRules.BasinOf(SyncedByte);
            _addUntil = Time.unscaledTime + Mathf.Max(0.5f, timeout);
        }

        private void LateUpdate()
        {
            float now = Time.unscaledTime;
            byte synced = SyncedByte;
            int syncedBasin = CleaningRules.BasinOf(synced);
            if (_add > 0 && (syncedBasin > _addFrom || now > _addUntil)) _add = 0;
            // Another cook's plate went in: the splash (our own splashed at the click).
            if (_lastSynced >= 0 && syncedBasin > _lastSynced && _add == 0 && !(_predicting && _predicted)) Play(SoundRules.IntoTheSinkSound);
            if (_lastSynced >= 0 && syncedBasin < _lastSynced) Play(SoundKey.PlaceCeramic); // a plate came out clean
            _lastSynced = syncedBasin;

            bool syncedScrub = CleaningRules.IsScrubbing(synced);
            if (_predicting && (now > _predictedUntil || (!_predicted && !syncedScrub))) _predicting = false;
            int basin = CleaningRules.BasinOf(SeenByte);
            bool scrub = (_predicting ? _predicted : syncedScrub) && basin > 0;
            if (scrub != _shownScrub)
            {
                _shownScrub = scrub;
                _scrubStart = now;
                Loop(scrub);
            }
            if (_shownScrub && _predicting && PredictionDrawnFrame < 0) PredictionDrawnFrame = Time.frameCount;
            if (_shownScrub && _loop != null)
                _loop.pitch = SoundRules.ScrubPitch(CleaningRules.HoldShare(CleaningRules.HoldScrub, now - _scrubStart));

            if (basin != _shownBasin)
            {
                _shownBasin = basin;
                for (int i = 0; i < _dishes.Length; i++)
                    if (_dishes[i] != null) _dishes[i].gameObject.SetActive(i < basin);
            }
            // The top plate wobbles while it is scrubbed; the others rest.
            for (int i = 0; i < _dishes.Length && i < basin; i++)
            {
                if (_dishes[i] == null) continue;
                bool top = i == basin - 1 && _shownScrub;
                float wobble = top ? CleaningArtRules.ScrubWobble(now - _scrubStart) : 0f;
                _dishes[i].localRotation = _dishRot[i] * Quaternion.Euler(wobble * 0.4f, wobble, 0f);
                _dishes[i].localPosition = _dishBase[i] + (top ? Vector3.up * (0.006f * Mathf.Abs(Mathf.Sin((now - _scrubStart) * 25f))) : Vector3.zero);
            }
            int bubbles = CleaningArtRules.BubbleCount(basin, _shownScrub);
            for (int i = 0; i < _bubbles.Length; i++)
            {
                Transform b = _bubbles[i];
                if (b == null) continue;
                bool on = i < bubbles;
                if (b.gameObject.activeSelf != on) b.gameObject.SetActive(on);
                if (!on) continue;
                Vector2 p = CleaningArtRules.BubblePoint(i, now, _shownScrub);
                float bob = 0.004f * Mathf.Sin(now * 3f + i);
                b.localPosition = new Vector3(p.x, _bubbleY + bob, p.y);
            }
        }

        private void Loop(bool on)
        {
            if (_loop == null) return;
            if (on)
            {
                _soundCatalog ??= new SoundCatalog();
                if (_loop.clip == null && _soundCatalog.TryGet(SoundRules.ScrubLoop, out AudioClip clip)) _loop.clip = clip;
                _loop.volume = SoundCatalog.MasterVolume * SoundCatalog.GainOf(SoundRules.ScrubLoop);
                if (_loop.clip != null) _loop.Play();
            }
            else _loop.Stop();
        }

        private void Play(SoundKey key)
        {
            if (!_soundsLooked)
            {
                _soundsLooked = true;
                _sounds = FindFirstObjectByType<KitchenSounds>();
            }
            if (_sounds != null) _sounds.Play(key, transform.position);
        }
    }
}
