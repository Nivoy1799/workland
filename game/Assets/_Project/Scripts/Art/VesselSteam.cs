using System.Collections.Generic;
using UnityEngine;
using Worktown.Game.Cooking;
using Worktown.Game.Net;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 023: on a pan or pot (client only): works out whether it holds something and stands on a burner that is on
    /// (<see cref="IsHot"/>, from the synced stove flags and the synced contents, so every player sees the same) and
    /// shows a small steam particle system above it while it is. One pooled system per vessel, at most
    /// <see cref="MaxParticles"/> particles, built once. The headless server does nothing.
    /// </summary>
    public sealed class VesselSteam : MonoBehaviour
    {
        public const int MaxParticles = 30;
        private const float CheckInterval = 0.25f;

        [Tooltip("Transparent URP particle material (generated); a fallback is looked up when empty.")]
        [SerializeField] private Material _material;
        [Tooltip("Height of the steam source above the vessel pivot.")]
        [SerializeField] private float _steamHeight = 0.2f;

        private static Texture2D _dot;

        private Vessel _vessel;
        private Pickup _pickup;
        private ParticleSystem _steam;
        private PlaceSurface[] _burners = new PlaceSurface[0];
        private StationContents[] _stoves = new StationContents[0];
        private float _nextCheck;

        /// <summary>The vessel holds something and stands on a burner that is on.</summary>
        public bool IsHot { get; private set; }
        public Vessel Vessel => _vessel;

        private void Awake()
        {
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                enabled = false;
                return;
            }
            _vessel = GetComponent<Vessel>();
            _pickup = GetComponent<Pickup>();
        }

        private void Start()
        {
            if (!enabled || _vessel == null) return;
            if (_vessel.Kind != VesselKind.Pan && _vessel.Kind != VesselKind.Pot)
            {
                enabled = false;
                return;
            }
            var burners = new List<PlaceSurface>();
            var stoves = new List<StationContents>();
            foreach (PlaceSurface surface in FindObjectsByType<PlaceSurface>(FindObjectsSortMode.None))
            {
                if (!surface.IsBurner) continue;
                StationContents stove = surface.GetComponentInParent<StationContents>();
                if (stove == null) continue;
                burners.Add(surface);
                stoves.Add(stove);
            }
            _burners = burners.ToArray();
            _stoves = stoves.ToArray();
            BuildParticles();
        }

        private void Update()
        {
            if (_vessel == null || Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + CheckInterval;
            bool hot = ComputeHot();
            if (hot == IsHot) return;
            IsHot = hot;
            if (_steam == null) return;
            if (hot) _steam.Play();
            else _steam.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private bool ComputeHot()
        {
            if (_vessel.ContentCount == 0 || (_pickup != null && _pickup.IsHeld)) return false;
            Vector3 position = transform.position;
            for (int i = 0; i < _burners.Length; i++)
            {
                PlaceSurface burner = _burners[i];
                if (!BurnerRules.IsOver(burner.transform.InverseTransformPoint(position), burner.Size, burner.TopY)) continue;
                return StationRules.IsBurnerOn(_stoves[i].StoveFlags.Value, burner.Burner);
            }
            return false;
        }

        private void BuildParticles()
        {
            var go = new GameObject("Steam");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, _steamHeight, 0f);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // the shape cone points up
            _steam = go.AddComponent<ParticleSystem>();
            _steam.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = _steam.main;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = MaxParticles;
            main.startLifetime = 1.6f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.12f);
            main.startColor = Palette.WithAlpha(Palette.Cream, 0.3f); // spec 026: warm white steam
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.02f;

            ParticleSystem.EmissionModule emission = _steam.emission;
            emission.rateOverTime = 14f;

            ParticleSystem.ShapeModule shape = _steam.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.06f;

            ParticleSystem.ColorOverLifetimeModule color = _steam.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;

            ParticleSystem.SizeOverLifetimeModule size = _steam.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = SteamMaterial();
        }

        private Material SteamMaterial()
        {
            Material material;
            if (_material != null) material = new Material(_material);
            else
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
                material = new Material(shader);
            }
            Texture2D dot = SoftDot();
            material.mainTexture = dot;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", dot);
            return material;
        }

        /// <summary>A soft white round sprite, shared by all vessels (a cache of a read-only texture).</summary>
        private static Texture2D SoftDot()
        {
            if (_dot != null) return _dot;
            const int n = 32;
            _dot = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SteamDot" };
            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f;
                    float dy = (y + 0.5f) / n * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    Color32 pixel = Color.white; // spec 026: white sprite, the particles carry the palette tint
                    pixel.a = (byte)(a * a * 255f);
                    pixels[y * n + x] = pixel;
                }
            }
            _dot.SetPixels32(pixels);
            _dot.Apply();
            return _dot;
        }
    }
}
