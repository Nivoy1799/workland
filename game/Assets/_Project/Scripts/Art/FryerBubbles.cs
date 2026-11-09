using UnityEngine;
using Worktown.Game.Cooking;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 029, client only: oil bubbles in one basket's well, on the oil surface. The rate follows the load and the heat
    /// (<see cref="FryerArtRules.BubbleRate"/>: the fryer's synced on flag, the basket's state, contents and heat zone), read four
    /// times a second. One particle system built once; no allocations per frame. The headless server does nothing.
    /// </summary>
    public sealed class FryerBubbles : MonoBehaviour
    {
        private const float CheckInterval = 0.25f;

        [SerializeField] private Material _material;
        [Tooltip("The oil surface above the basket's pivot (m).")]
        [SerializeField] private float _oilHeight = 0.12f;
        [Tooltip("The bubbling area of the well (x, z in m).")]
        [SerializeField] private Vector2 _area = new Vector2(0.24f, 0.36f);

        private Vessel _vessel;
        private StationContents _fryer;
        private ParticleSystem _bubbles;
        private Material _ownMaterial;
        private float _nextCheck;
        private float _rate = -1f;

        /// <summary>Bubbles per second now (for tests and the tour log).</summary>
        public float Rate => Mathf.Max(0f, _rate);

        private void Awake()
        {
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                enabled = false;
                return;
            }
            _vessel = GetComponent<Vessel>();
            FryerBasket basket = GetComponent<FryerBasket>();
            if (basket != null && basket.Fryer != null) _fryer = basket.Fryer.GetComponent<StationContents>();
            Build();
        }

        private void Update()
        {
            if (_vessel == null || _bubbles == null || Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + CheckInterval;
            bool on = _fryer != null && FryerRules.IsOn(_fryer.StoveFlags.Value);
            float rate = FryerArtRules.BubbleRate(on, FryerRules.StateOf(_vessel.Mode.Value), _vessel.ContentCount, _vessel.Zone);
            if (Mathf.Approximately(rate, _rate)) return;
            _rate = rate;
            ParticleSystem.EmissionModule emission = _bubbles.emission;
            emission.rateOverTime = rate;
            if (rate > 0f && !_bubbles.isPlaying) _bubbles.Play();
            else if (rate <= 0f && _bubbles.isPlaying) _bubbles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private void Build()
        {
            var go = new GameObject("Bubbles");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, _oilHeight + 0.004f, 0f);
            _bubbles = go.AddComponent<ParticleSystem>();
            _bubbles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = _bubbles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = 60;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.01f, 0.04f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.03f);
            main.startColor = Palette.WithAlpha(Color.Lerp(Palette.Cream, Palette.Mustard, 0.35f), 0.85f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule emission = _bubbles.emission;
            emission.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = _bubbles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(_area.x, 0.002f, _area.y);
            ParticleSystem.SizeOverLifetimeModule size = _bubbles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.2f));
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _ownMaterial = VesselSteam.DotMaterial(_material);
            renderer.sharedMaterial = _ownMaterial;
        }

        private void OnDestroy()
        {
            if (_ownMaterial != null) Destroy(_ownMaterial);
        }
    }
}
