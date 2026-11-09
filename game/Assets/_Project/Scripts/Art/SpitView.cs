using UnityEngine;
using Worktown.Game.Audio;
using Worktown.Game.Cooking;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 036: the seitan spit for every viewer (on its cone slot, the hold's IPourView): the cone turns while it has seitan on it (a
    /// motor hum), its stacked discs shrink with the portions left (the cone vessel's mode byte) and brown with the outer layer (the cone
    /// vessel's 027c zone and progress), the heat panel glows with the knob; while someone shaves, curls fly off the cone into the catch
    /// tray, a knife works down the cone for onlookers, and the rasp loops. The shaving player sees it from its press to its release at
    /// once (<see cref="Predict"/>), a loaded cone at its click (<see cref="PredictLoad"/>), before the server answers. Colours by property
    /// block, everything built once: no per-frame allocations.
    /// </summary>
    public sealed class SpitView : MonoBehaviour, IPourView
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private Station _spit;
        [SerializeField] private Vessel _cone;
        [SerializeField] private Vessel _tray;
        [Tooltip("Turns about the skewer (the discs are its children).")]
        [SerializeField] private Transform _turn;
        [Tooltip("The cone's discs, bottom first (SpitArtRules.Discs).")]
        [SerializeField] private Renderer[] _discs = new Renderer[0];
        [SerializeField] private Renderer _glow;
        [SerializeField] private Renderer[] _curls = new Renderer[0];
        [Tooltip("Onlookers' knife on the cone while someone shaves (the shaver's own knife is in its hands).")]
        [SerializeField] private Transform _knife;
        [Tooltip("The heat knob (lit by the level).")]
        [SerializeField] private Renderer _knob;

        private StationContents _contents;
        private MaterialPropertyBlock _block;
        private AudioSource _rasp;
        private AudioSource _hum;
        private static SoundCatalog _soundCatalog;
        private bool _predicting;
        private bool _predicted;
        private float _predictedUntil;
        private float _loadUntil;
        private bool _shownShave;
        private double _shaveStart;
        private float _nextCurl;
        private int _nextCurlIndex;
        private readonly float[] _curlStart = new float[SpitArtRules.MaxCurls];
        private readonly Vector3[] _curlFrom = new Vector3[SpitArtRules.MaxCurls];
        private float _shownShare = -1f;
        private int _shownLevel = -1;
        private HeatZone _shownZone = (HeatZone)255;
        private byte _shownProgress;
        private float _angle;

        /// <summary>True while this client draws a shave.</summary>
        public bool IsShaving => _shownShave;
        /// <summary>The frame the predicted shave was first drawn (click-to-visual log); -1 before.</summary>
        public int PredictionDrawnFrame { get; private set; } = -1;
        /// <summary>The cone vessel and the tray (the interactor reads what is on them).</summary>
        public Vessel Cone => _cone;
        public Vessel Tray => _tray;
        /// <summary>The owner shaves in first person with its own knife (the onlookers' knife is hidden then).</summary>
        public bool OwnKnifeInHand { get; set; }

        private byte SyncedFlags => _contents != null ? _contents.StoveFlags.Value : (byte)0;

        /// <summary>The station's byte as this client sees it: its own loaded cone first (until the server's byte says so, or 1.5 s).</summary>
        public byte SeenFlags
        {
            get
            {
                byte b = SyncedFlags;
                if (Time.unscaledTime < _loadUntil && !SpitRules.HasCone(b)) b = (byte)(b | SpitRules.ConeBit);
                return b;
            }
        }

        private void Awake()
        {
            if (_spit == null) _spit = GetComponentInParent<Station>();
            if (_spit != null) _contents = _spit.GetComponent<StationContents>();
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                enabled = false;
                return;
            }
            _block = new MaterialPropertyBlock();
            _rasp = Loop("Rasp", 10f);
            _hum = Loop("Motor", 6f);
            for (int i = 0; i < _curls.Length; i++)
                if (_curls[i] != null) _curls[i].gameObject.SetActive(false);
            if (_knife != null) _knife.gameObject.SetActive(false);
        }

        private AudioSource Loop(string name, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 1f;
            source.minDistance = 1f;
            source.maxDistance = range;
            return source;
        }

        /// <summary>The shaving player: the look follows the button (on at the press, off at the release).</summary>
        public void Predict(bool on)
        {
            _predicting = true;
            _predicted = on;
            _predictedUntil = Time.unscaledTime + (on ? 6f : 1f);
            if (on) PredictionDrawnFrame = -1;
        }

        /// <summary>The owner loaded a cone: it stands full on the spit from the click.</summary>
        public void PredictLoad(float timeout) => _loadUntil = Time.unscaledTime + Mathf.Max(0.5f, timeout);

        private void LateUpdate()
        {
            float now = Time.unscaledTime;
            byte flags = SeenFlags;
            bool cone = SpitRules.HasCone(flags);
            int level = SpitRules.LevelOf(flags);
            if (now < _loadUntil && SpitRules.HasCone(SyncedFlags)) _loadUntil = 0f;

            // The shave: our own from the press, else the synced bit.
            bool syncedShave = SpitRules.IsShaving(SyncedFlags);
            if (_predicting && (now > _predictedUntil || (!_predicted && !syncedShave))) _predicting = false;
            bool shave = (_predicting ? _predicted : syncedShave) && cone;
            if (shave != _shownShave)
            {
                _shownShave = shave;
                _shaveStart = Time.unscaledTimeAsDouble;
                Play(_rasp, SoundKey.ShaveRasp, shave, SoundCatalog.GainOf(SoundKey.ShaveRasp));
            }
            if (_shownShave && _predicting && PredictionDrawnFrame < 0) PredictionDrawnFrame = Time.frameCount;
            double held = Time.unscaledTimeAsDouble - _shaveStart;
            if (_shownShave && _rasp != null) _rasp.pitch = SoundRules.ShavePitch(SpitArtRules.HoldShare(held));

            // The cone: turning (the motor hum), shrinking, browning.
            bool turning = cone;
            if (_hum != null && turning != _hum.isPlaying) Play(_hum, SoundKey.MotorHum, turning, SoundCatalog.GainOf(SoundKey.MotorHum));
            if (_turn != null)
            {
                _turn.gameObject.SetActive(cone || (_cone != null && SpitRules.PortionsOf(_cone.Mode.Value) > 0));
                if (turning)
                {
                    _angle = Mathf.Repeat(_angle + SpitArtRules.TurnDegreesPerSecond * Time.deltaTime, 360f);
                    _turn.localRotation = Quaternion.Euler(0f, _angle, 0f);
                }
            }
            byte mode = _cone != null ? _cone.Mode.Value : (byte)0;
            float share = now < _loadUntil ? 1f : cone ? SpitRules.ShareLeft(mode) : 0f;
            HeatZone zone = _cone != null && cone ? _cone.Zone : HeatZone.None;
            byte progress = _cone != null ? _cone.HeatProgress.Value : (byte)0;
            if (now < _loadUntil)
            {
                zone = HeatZone.Raw;
                progress = 0;
            }
            if (!Mathf.Approximately(share, _shownShare) || zone != _shownZone || progress != _shownProgress) DrawCone(share, zone, progress);
            if (level != _shownLevel) DrawGlow(level);
            UpdateCurls(now, held);
            UpdateKnife(held);
        }

        private void DrawCone(float share, HeatZone zone, byte progress)
        {
            _shownShare = share;
            _shownZone = zone;
            _shownProgress = progress;
            float h = SpitArtRules.DiscHeight;
            for (int i = 0; i < _discs.Length; i++)
            {
                Renderer disc = _discs[i];
                if (disc == null) continue;
                float d = 2f * SpitArtRules.DiscRadius(i, share);
                disc.transform.localScale = new Vector3(d, h * 0.5f, d); // Unity's cylinder is 2 m high
                _block.SetColor(BaseColorId, SpitArtRules.OuterColour(zone, progress / 255f, i));
                disc.SetPropertyBlock(_block);
            }
        }

        private void DrawGlow(int level)
        {
            _shownLevel = level;
            if (_glow != null)
            {
                _block.SetColor(BaseColorId, SpitArtRules.PanelColour(level));
                _block.SetColor(EmissionColorId, SpitArtRules.GlowColour(level));
                _glow.SetPropertyBlock(_block);
            }
            if (_knob == null) return;
            _block.SetColor(BaseColorId, SpitArtRules.KnobColour(level));
            _block.SetColor(EmissionColorId, level == Worktown.Sim.Cooking.Spit.Off ? Color.black : SpitArtRules.KnobColour(level) * 1.2f);
            _knob.SetPropertyBlock(_block);
        }

        /// <summary>Curls leave the cone at the knife while shaving and fall into the tray; each is hidden when it lands.</summary>
        private void UpdateCurls(float now, double held)
        {
            if (_shownShave && now >= _nextCurl && _curls.Length > 0)
            {
                _nextCurl = now + SpitArtRules.CurlEvery;
                int i = _nextCurlIndex;
                _nextCurlIndex = (_nextCurlIndex + 1) % _curls.Length;
                Vector3 from = KnifePoint(held, _nextCurlIndex * 0.37f);
                _curlFrom[i] = from;
                _curlStart[i] = now;
                if (_curls[i] != null)
                {
                    _curls[i].gameObject.SetActive(true);
                    _block.SetColor(BaseColorId, SpitArtRules.OuterColour(_shownZone, _shownProgress / 255f, 0));
                    _curls[i].SetPropertyBlock(_block);
                }
            }
            Vector3 tray = TrayPoint();
            for (int i = 0; i < _curls.Length; i++)
            {
                Renderer curl = _curls[i];
                if (curl == null || !curl.gameObject.activeSelf) continue;
                float t = now - _curlStart[i];
                if (t >= SpitArtRules.CurlFallSeconds)
                {
                    curl.gameObject.SetActive(false);
                    continue;
                }
                curl.transform.position = SpitArtRules.CurlPoint(_curlFrom[i], tray + new Vector3((i % 3 - 1) * 0.04f, 0f, (i % 2) * 0.03f), t);
                curl.transform.rotation = Quaternion.Euler(t * 900f, i * 47f, t * 600f);
            }
        }

        /// <summary>Onlookers see a knife working down the cone (the shaver itself has its own in its hands).</summary>
        private void UpdateKnife(double held)
        {
            if (_knife == null) return;
            bool show = _shownShave && !OwnKnifeInHand;
            if (_knife.gameObject.activeSelf != show) _knife.gameObject.SetActive(show);
            if (!show) return;
            _knife.position = KnifePoint(held, 0f);
            _knife.rotation = _spit != null ? _spit.transform.rotation * Quaternion.Euler(0f, 0f, -30f) : Quaternion.identity;
        }

        /// <summary>The world point on the cone's cook side where the knife is now.</summary>
        private Vector3 KnifePoint(double held, float spread)
        {
            Transform root = _spit != null ? _spit.transform : transform;
            float y = SpitArtRules.KnifeHeight(held + spread * 0.1);
            float r = SpitArtRules.DiscRadius(Mathf.RoundToInt((y - SpitArtRules.ConeBottom) / SpitArtRules.DiscHeight), _shownShare < 0f ? 1f : _shownShare);
            Vector3 local = new Vector3(SpitArtRules.ConeAxis.x + (spread - 0.5f) * 0.04f, y - SpitArtRules.Height * 0.5f, SpitArtRules.ConeAxis.y - r - 0.01f);
            return root.TransformPoint(local);
        }

        private Vector3 TrayPoint() =>
            _tray != null ? _tray.ContentPoint : transform.position + Vector3.down * 0.3f;

        private void Play(AudioSource source, SoundKey key, bool on, float gain)
        {
            if (source == null) return;
            if (!on)
            {
                source.Stop();
                return;
            }
            _soundCatalog ??= new SoundCatalog();
            if (source.clip == null && _soundCatalog.TryGet(key, out AudioClip clip)) source.clip = clip;
            source.volume = SoundCatalog.MasterVolume * gain;
            if (source.clip != null) source.Play();
        }
    }
}
