using UnityEngine;
using Worktown.Game.Audio;
using Worktown.Game.Cooking;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 033: one tap of a drink dispenser, for every viewer. While it pours, its lever is pressed down (and tips towards the cook), a
    /// thin stream in the drink's colour runs from the spout and a pour loop plays. Others see it from the dispenser's synced byte
    /// (<see cref="DrinkRules.IsPouring"/>); the pouring player from the press to the release at once (<see cref="Predict"/>), with its own
    /// predicted ml (<see cref="PredictFrom"/>): the splash plays the moment its cup runs over. Spec 042: the cup stands on the drip tray
    /// under the tap; this view draws its liquid rising and lands the stream on it, for the pourer (from its press, its own ml) and for
    /// everyone else alike (from the byte's rise, at the same rate from the level they see), so all viewers watch the same pour. No
    /// per-frame allocations.
    /// </summary>
    public sealed class DrinkTapView : MonoBehaviour, IPourView
    {
        [SerializeField] private Station _dispenser;
        [SerializeField] private int _tap;
        [Tooltip("The lever (pressed down while pouring).")]
        [SerializeField] private Transform _lever;
        [Tooltip("A plain lit material for the stream (tinted with the drink's colour).")]
        [SerializeField] private Material _streamMaterial;
        [SerializeField] private DrinkDispenserView _station;

        private const float LeverDrop = 0.018f;
        /// <summary>Spec 042: the lever tips this far towards the cook while pressed (degrees), so the press reads from across the counter.</summary>
        private const float LeverTilt = 22f;
        private const float LeverSeconds = 0.06f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static SoundCatalog _soundCatalog;
        private StationContents _contents;
        private Transform _stream;
        private Renderer _streamRenderer;
        private AudioSource _loop;
        private Vector3 _leverRest;
        private Quaternion _leverRestRotation;
        private static readonly Collider[] CupProbe = new Collider[8];
        // Spec 042: the cup on the tray this view fills (the pourer's from its press; the others' found when the byte goes on).
        private Food _cup;
        private bool _othersPour;
        private float _othersStartMl;
        private double _othersStart;
        private byte _othersStartLevel;
        private bool _predicting;
        private bool _predicted;
        private float _predictedUntil;
        private bool _shownOn;
        private float _changedAt = float.NegativeInfinity;
        private float _startMl;
        private double _pressTime;
        private bool _splashed;

        public Station Dispenser => _dispenser;
        public int Tap => _tap;
        /// <summary>True while this client draws the pour.</summary>
        public bool IsPouring => _shownOn;
        /// <summary>The frame the predicted pour was first drawn (click-to-visual log); -1 before.</summary>
        public int PredictionDrawnFrame { get; private set; } = -1;

        /// <summary>The spout's tip in the world (where the stream starts).</summary>
        public Vector3 SpoutTip => transform.position - transform.up * (DrinkArtRules.SpoutSize.y * 0.5f);

        /// <summary>Where a cup stands under this tap (its bottom on the drip tray), in the world.</summary>
        public Pose CupPose
        {
            get
            {
                Transform station = _dispenser != null ? _dispenser.transform : transform.parent;
                Vector3 local = DrinkArtRules.CupUnderTap(_tap) - DrinkArtRules.SpoutCentre(_tap);
                return new Pose(transform.position + transform.rotation * local, station != null ? station.rotation : transform.rotation);
            }
        }

        private void Awake()
        {
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                enabled = false;
                return;
            }
            if (_dispenser != null) _contents = _dispenser.GetComponent<StationContents>();
            if (_lever != null)
            {
                _leverRest = _lever.localPosition;
                _leverRestRotation = _lever.localRotation;
            }
            GameObject stream = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stream.name = "Stream";
            Destroy(stream.GetComponent<Collider>());
            stream.transform.SetParent(transform, false);
            _stream = stream.transform;
            _streamRenderer = stream.GetComponent<Renderer>();
            if (_streamMaterial != null) _streamRenderer.sharedMaterial = _streamMaterial;
            _streamRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, DrinkArtRules.DrinkColour(_tap));
            _streamRenderer.SetPropertyBlock(block);
            _streamRenderer.enabled = false;
            _loop = gameObject.AddComponent<AudioSource>();
            _loop.playOnAwake = false;
            _loop.loop = true;
            _loop.spatialBlend = 1f;
            _loop.minDistance = 1f;
            _loop.maxDistance = 14f;
        }

        /// <summary>
        /// The pouring player: the look follows the button now (on at the press, off at the release). After the release the synced byte
        /// is ignored until it agrees or for a second, so the stream never comes back while the server catches up.
        /// </summary>
        public void Predict(bool on)
        {
            _predicting = true;
            _predicted = on;
            _predictedUntil = Time.unscaledTime + (on ? 8f : 1f);
            if (on) PredictionDrawnFrame = -1;
        }

        /// <summary>
        /// The pouring player's cup (<paramref name="cup"/>, on this tap's spot; null: none seen) held <paramref name="ml"/> at the press
        /// (<paramref name="pressTime"/>, unscaled s).
        /// </summary>
        public void PredictFrom(float ml, double pressTime, Food cup)
        {
            _startMl = ml;
            _pressTime = pressTime;
            _splashed = ml >= Drinks.SpillFrom;
            EndOthersPour(false);
            _cup = cup;
        }

        /// <summary>The pouring player's own ml now (from its press), as it predicts it.</summary>
        public float PredictedMl(double now) => DrinkRules.PredictedMl(_startMl, now - _pressTime);

        /// <summary>Spec 042: the cup this view fills now (null: none).</summary>
        public Food Cup => _shownOn ? _cup : null;

        private bool SyncedOn => _contents != null && DrinkRules.IsPouring(_contents.StoveFlags.Value, _tap);

        private void LateUpdate()
        {
            float now = Time.unscaledTime;
            bool synced = SyncedOn;
            if (_predicting && (now > _predictedUntil || (!_predicted && !synced))) _predicting = false;
            bool on = _predicting ? _predicted : synced;
            if (on != _shownOn)
            {
                _shownOn = on;
                _changedAt = now;
                Loop(on);
            }
            if (_shownOn && _predicting && PredictionDrawnFrame < 0) PredictionDrawnFrame = Time.frameCount;
            // Spec 042: someone else's pour (the byte, not our own prediction): the cup on the tray fills from the level we see in it.
            bool others = _shownOn && !_predicting;
            if (others && !_othersPour) StartOthersPour();
            else if (!others && _othersPour) EndOthersPour(true);
            float ml = -1f;
            if (_shownOn && _predicting && _predicted) ml = PredictedMl(Time.unscaledTimeAsDouble);
            else if (_othersPour) ml = DrinkRules.PredictedMl(_othersStartMl, Time.unscaledTimeAsDouble - _othersStart);
            bool filling = ml >= 0f && _cup != null && _cup.isActiveAndEnabled;
            if (filling && _cup.Visual != null) _cup.Visual.PredictMl(ml, true);
            if (_shownOn && _predicting && _predicted && !_splashed && PredictedMl(Time.unscaledTimeAsDouble) >= Drinks.SpillFrom)
            {
                // The pouring player's cup runs over now: the splash, and the puddle at once (the server's spill bit confirms it).
                _splashed = true;
                if (_station != null) _station.PredictSpill(transform.position);
            }

            if (_lever != null)
            {
                float t = Mathf.Clamp01((now - _changedAt) / LeverSeconds);
                float down = _shownOn ? t : 1f - t;
                _lever.localPosition = _leverRest - Vector3.up * (LeverDrop * down);
                _lever.localRotation = _leverRestRotation * Quaternion.Euler(-LeverTilt * down, 0f, 0f);
            }

            bool stream = _shownOn && now - _changedAt >= LeverSeconds * 0.5f;
            _streamRenderer.enabled = stream;
            if (!stream) return;
            Vector3 tip = SpoutTip;
            Vector3 end = CupPose.position; // the drip tray, or (spec 042) the liquid surface of the cup on it
            if (filling) end += Vector3.up * (DrinkArtRules.CupHeight * DrinkArtRules.CupLiquidHeight(DrinkRules.DrawnFill(ml)));
            float length = tip.y - end.y;
            if (length < 0.005f) length = 0.005f;
            _stream.position = new Vector3(tip.x, tip.y - length * 0.5f, tip.z);
            _stream.rotation = Quaternion.identity;
            Vector3 parentScale = transform.lossyScale;
            float w = DrinkArtRules.StreamWidth * (0.9f + 0.1f * Mathf.Sin(now * 31f));
            _stream.localScale = new Vector3(w / Mathf.Max(0.0001f, parentScale.x), length * 0.5f / Mathf.Max(0.0001f, parentScale.y),
                w / Mathf.Max(0.0001f, parentScale.z));
        }

        /// <summary>Spec 042: another player's pour started: the cup on this tap's spot and what it held, as this client sees it.</summary>
        private void StartOthersPour()
        {
            _othersPour = true;
            _othersStart = Time.unscaledTimeAsDouble;
            Worktown.Game.Net.Pickup item = Worktown.Game.Net.PickupService.OccupantAt(CupPose.position, CupProbe);
            _cup = item != null && item.TryGetComponent(out Food food) && FoodLookRules.IsCup(food.Look.Value) ? food : null;
            _othersStartMl = 0f;
            if (_cup == null) return;
            byte level = _cup.Look.Value.Shape;
            _othersStartLevel = level;
            _othersStartMl = _cup.Visual != null && _cup.Visual.PredictedMl >= 0f ? _cup.Visual.PredictedMl : DrinkRules.SeenMl(level);
            if (_cup.Visual != null) _cup.Visual.SetPredictedTap(_tap);
        }

        /// <summary>
        /// Spec 042: another player's pour ended (<paramref name="keep"/>: its byte went off): the drawn level stays until the cup's look
        /// brings the server's bucket (FoodVisual drops it then when they differ, or after its keep time).
        /// </summary>
        private void EndOthersPour(bool keep)
        {
            if (!_othersPour) return;
            _othersPour = false;
            if (_cup != null && _cup.Visual != null)
            {
                float ml = DrinkRules.PredictedMl(_othersStartMl, Time.unscaledTimeAsDouble - _othersStart);
                // The server's bucket already came (the look changed since the start) and says otherwise: show it now.
                byte synced = _cup.Look.Value.Shape;
                bool answered = synced != _othersStartLevel && DrinkRules.LevelOfMl(ml) != synced;
                if (keep && !answered) _cup.Visual.PredictMl(ml, false);
                else _cup.Visual.EndPrediction();
            }
            _cup = null;
        }

        private void Loop(bool on)
        {
            if (_loop == null) return;
            if (on)
            {
                _soundCatalog ??= new SoundCatalog();
                if (_loop.clip == null && _soundCatalog.TryGet(SoundKey.Pour, out AudioClip clip)) _loop.clip = clip;
                _loop.volume = SoundCatalog.MasterVolume * SoundCatalog.GainOf(SoundKey.Pour);
                if (_loop.clip != null) _loop.Play();
            }
            else _loop.Stop();
        }
    }
}
