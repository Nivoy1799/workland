using UnityEngine;
using Worktown.Game.Audio;
using Worktown.Game.Cooking;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 029, client only: a fryer basket's look. The basket object never moves (a networked root); its "Rig" child (the
    /// wire basket, its collider and the contents) swings between the oil (pose 0) and the hook (pose 1) on a short arc, for
    /// every viewer, from the basket's synced mode byte (<see cref="FryerRules.Mode"/>). The owner predicts the arc at the
    /// click (<see cref="Predict"/>, the 027b pattern: the interactor confirms or denies from the synced state). Each shake in
    /// the counter jolts the basket and (for other players' shakes) rattles; a basket that starts to drain drips and plays the
    /// drip. Display only; no allocations per frame.
    /// </summary>
    public sealed class BasketView : MonoBehaviour
    {
        [SerializeField] private Transform _rig;
        [Tooltip("The rig's offset on the hook from its pose in the oil (basket space).")]
        [SerializeField] private Vector3 _raisedOffset = new Vector3(0f, 0.3f, 0.06f);
        [Tooltip("Tilt on the hook about the basket's x axis (degrees; the handle up, draining towards the back).")]
        [SerializeField] private float _raisedTilt = -12f;
        [SerializeField] private Material _particleMaterial;
        [Tooltip("Where the drops leave the basket (rig space).")]
        [SerializeField] private Vector3 _dripPoint = new Vector3(0f, 0f, 0f);

        private Vessel _vessel;
        private KitchenSounds _sounds;
        private bool _soundsLooked;
        private ParticleSystem _drips;
        private Material _ownMaterial;
        private bool _shownLowered = true;
        private float _pose = 1f;
        private float _from = 1f;
        private float _moveStart = float.NegativeInfinity;
        private bool _started;
        private bool _predicting;
        private bool _predictedLowered;
        private readonly BasketShakeLedger _shakes = new BasketShakeLedger(); // spec 029b
        private float _shakeStart = float.NegativeInfinity;
        private BasketState _seenState;
        private bool _stateSeen;

        /// <summary>The basket is in the oil, as the server last said.</summary>
        public bool SyncedLowered => _vessel != null && FryerRules.StateOf(_vessel.Mode.Value) == BasketState.Lowered;
        /// <summary>
        /// Spec 029b: where this client believes the basket is: its own pending prediction, else the synced state. A button
        /// click asks for the other side (FryerRules.IntentOption).
        /// </summary>
        public bool SeenLowered => _predicting ? _predictedLowered : SyncedLowered;
        /// <summary>The pose drawn now (0 in the oil, 1 on the hook), for tests and the tour log.</summary>
        public float Pose => _pose;
        public bool IsPredicting => _predicting;
        /// <summary>Spec 029b: the frame the predicted arc was first drawn (for the click-to-visual log); -1 before.</summary>
        public int PredictionDrawnFrame { get; private set; } = -1;
        public Transform Rig => _rig;

        private void Awake()
        {
            _vessel = GetComponent<Vessel>();
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                enabled = false;
                return;
            }
            BuildDrips();
        }

        /// <summary>
        /// Spec 029b: a view enabled again (the scene object respawned) starts fresh: the first frame shows the basket where
        /// it is without an arc, and the first shake counter is a baseline (no stale rattles).
        /// </summary>
        private void OnEnable()
        {
            _started = false;
            _stateSeen = false;
            _predicting = false;
            _shakes.Reset();
        }

        /// <summary>Owner: the basket goes into (or out of) the oil now, before the server answers.</summary>
        public void Predict(bool lowered)
        {
            _predicting = true;
            _predictedLowered = lowered;
            PredictionDrawnFrame = -1;
        }

        /// <summary>The prediction is over (confirmed: the synced state agrees; denied: it eases back to it).</summary>
        public void EndPrediction()
        {
            _predicting = false;
        }

        /// <summary>
        /// Owner: a shake at the click (its counter step is then not played again when it arrives). Spec 029b: owed only for
        /// <paramref name="confirmTimeout"/> s, or until the basket's state changes.
        /// </summary>
        public void PredictShake(float confirmTimeout)
        {
            _shakes.NoteOwn(Time.unscaledTime, confirmTimeout);
            _shakeStart = Time.unscaledTime;
        }

        private void LateUpdate()
        {
            if (_vessel == null || _rig == null) return;
            float now = Time.unscaledTime;
            byte mode = _vessel.Mode.Value;
            BasketState state = FryerRules.StateOf(mode);
            bool want = _predicting ? _predictedLowered : state == BasketState.Lowered;
            if (!_started)
            {
                // The first frame (a join) shows the basket where it is, without an arc.
                _started = true;
                _shownLowered = want;
                _pose = _from = want ? 0f : 1f;
            }
            else if (want != _shownLowered)
            {
                _shownLowered = want;
                _from = _pose;
                _moveStart = now;
            }
            float t = now - _moveStart;
            _pose = FryerArtRules.Pose(_from, _shownLowered, t);
            float lift = t < FryerArtRules.ArcSeconds ? FryerArtRules.Lift(t) : 0f;

            WatchShakes(FryerRules.ShakesOf(mode), now);
            WatchDrain(state);

            float shake = FryerArtRules.ShakeOffset(now - _shakeStart);
            _rig.localPosition = _raisedOffset * _pose + new Vector3(0f, lift, shake);
            _rig.localRotation = Quaternion.Euler(_raisedTilt * _pose + shake * 120f, 0f, 0f);
            if (_predicting && PredictionDrawnFrame < 0) PredictionDrawnFrame = Time.frameCount;
        }

        private void WatchShakes(int shakes, float now)
        {
            int others = _shakes.Observe(shakes, now);
            for (int i = 0; i < others; i++)
            {
                _shakeStart = now; // another player's shake
                Play(SoundKey.Rattle);
            }
        }

        private void WatchDrain(BasketState state)
        {
            if (_stateSeen && state == BasketState.Draining && _seenState != BasketState.Draining) Play(SoundKey.Drip);
            if (_stateSeen && state != _seenState) _shakes.StateChanged(); // spec 029b: an owed shake cannot come any more
            if (_drips != null)
            {
                bool drip = state == BasketState.Draining;
                if (drip && !_drips.isEmitting) _drips.Play();
                else if (!drip && _drips.isEmitting) _drips.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            _seenState = state;
            _stateSeen = true;
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

        /// <summary>Small golden drops falling from the basket into the oil while it drains.</summary>
        private void BuildDrips()
        {
            if (_rig == null) return;
            var go = new GameObject("Drips");
            go.transform.SetParent(_rig, false);
            go.transform.localPosition = _dripPoint;
            _drips = go.AddComponent<ParticleSystem>();
            _drips.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = _drips.main;
            main.loop = true;
            main.playOnAwake = false;
            // Spec 029b: bigger, brighter drops that stretch as they fall, so the drain reads from the aisle.
            main.maxParticles = 48;
            main.startLifetime = 0.45f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(FryerArtRules.DripSizeMin, FryerArtRules.DripSizeMax);
            main.startColor = Color.Lerp(Palette.Cream, Palette.Mustard, 0.15f); // pale gold: shows against the golden oil
            main.gravityModifier = 0.8f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule emission = _drips.emission;
            emission.rateOverTime = FryerArtRules.DripRate;
            ParticleSystem.ShapeModule shape = _drips.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.16f, 0.005f, 0.26f);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.06f;
            renderer.lengthScale = 1.6f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _ownMaterial = VesselSteam.DotMaterial(_particleMaterial);
            renderer.sharedMaterial = _ownMaterial;
        }

        private void OnDestroy()
        {
            if (_ownMaterial != null) Destroy(_ownMaterial);
        }
    }
}
