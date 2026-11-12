using UnityEngine;
using Worktown.Game.Audio;
using Worktown.Game.Cooking;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 033: the drink dispenser's spill, for every viewer: while its synced byte has the spill bit (DrinkRules.HasSpill) a flat
    /// puddle lies on the counter in front of the drip tray, until spec 035's cleaning clears it; the bit turning on splashes. The
    /// pouring player sees its own spill at once (<see cref="PredictSpill"/>, from its tap's predicted ml) and hears the splash then, not
    /// again when the bit arrives. No per-frame allocations.
    /// </summary>
    public sealed class DrinkDispenserView : MonoBehaviour
    {
        [SerializeField] private Station _dispenser;
        [Tooltip("A plain lit material for the puddle (tinted SoftBlue: a wet patch).")]
        [SerializeField] private Material _puddleMaterial;

        private const float PredictedSpillSeconds = 2f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static KitchenSounds _sounds;
        private static bool _soundsLooked;
        private StationContents _contents;
        private Renderer _puddle;
        private bool _shown;
        private bool _seenBit;
        private bool _hasSeen;
        private float _predictedAt = float.NegativeInfinity;

        /// <summary>True while the puddle is drawn.</summary>
        public bool PuddleShown => _shown;

        // Spec 035: the wipe. While someone wipes the puddle shrinks (SpillWipeView sets the share left); the wiping player's finished
        // wipe hides it at once until the server's bit agrees (or a few seconds).
        private float _left = 1f;
        private float _wipedUntil = float.NegativeInfinity;
        private Vector3 _fullScale;

        /// <summary>Spec 035: the puddle's centre on the counter (world).</summary>
        public Vector3 PuddleCentre => _puddle != null ? _puddle.transform.position : transform.position;

        /// <summary>Spec 035: the share of the puddle still drawn (1 untouched, 0 gone), set every frame of a wipe.</summary>
        public void SetWipeLeft(float left)
        {
            left = Mathf.Clamp01(left);
            if (Mathf.Approximately(left, _left) || _puddle == null) return;
            _left = left;
            _puddle.transform.localScale = new Vector3(_fullScale.x * Mathf.Max(0.05f, left), _fullScale.y, _fullScale.z * Mathf.Max(0.05f, left));
        }

        /// <summary>Spec 035: our own wipe finished: the puddle goes now (the server's bit follows).</summary>
        public void PredictWiped(float seconds)
        {
            _wipedUntil = Time.unscaledTime + Mathf.Max(0.5f, seconds);
            _predictedAt = float.NegativeInfinity;
        }

        private void Awake()
        {
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                enabled = false;
                return;
            }
            if (_dispenser == null) _dispenser = GetComponent<Station>();
            if (_dispenser != null) _contents = _dispenser.GetComponent<StationContents>();
            GameObject puddle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            puddle.name = "Spill";
            Destroy(puddle.GetComponent<Collider>());
            puddle.transform.SetParent(transform, false);
            float h = DrinkArtRules.Height;
            puddle.transform.localPosition = DrinkArtRules.PuddleCentre + new Vector3(0f, -h * 0.5f, 0f); // the root is the body's middle
            puddle.transform.localScale = new Vector3(DrinkArtRules.PuddleSize, 0.001f, DrinkArtRules.PuddleSize * 0.7f);
            _puddle = puddle.GetComponent<Renderer>();
            if (_puddleMaterial != null) _puddle.sharedMaterial = _puddleMaterial;
            _puddle.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, Palette.Sky);
            _puddle.SetPropertyBlock(block);
            _puddle.enabled = false;
            _fullScale = puddle.transform.localScale;
        }

        /// <summary>The pouring player's cup ran over at <paramref name="where"/>: the splash now, the puddle at once.</summary>
        public void PredictSpill(Vector3 where)
        {
            _predictedAt = Time.unscaledTime;
            Play(SoundKey.Splash, where);
        }

        private void LateUpdate()
        {
            bool bit = _contents != null && DrinkRules.HasSpill(_contents.StoveFlags.Value);
            float now = Time.unscaledTime;
            if (_hasSeen && bit && !_seenBit && now - _predictedAt > PredictedSpillSeconds) Play(SoundKey.Splash, _puddle.transform.position);
            _seenBit = bit;
            _hasSeen = true;
            if (!bit && now < _wipedUntil) _wipedUntil = float.NegativeInfinity; // the server agrees
            bool show = (bit && now >= _wipedUntil) || now - _predictedAt <= PredictedSpillSeconds;
            if (!show && _left < 1f) SetWipeLeft(1f); // the next spill is whole again
            if (show == _shown) return;
            _shown = show;
            _puddle.enabled = show;
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
