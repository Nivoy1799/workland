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
            block.SetColor(BaseColorId, Palette.SoftBlue);
            _puddle.SetPropertyBlock(block);
            _puddle.enabled = false;
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
            bool show = bit || now - _predictedAt <= PredictedSpillSeconds;
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
