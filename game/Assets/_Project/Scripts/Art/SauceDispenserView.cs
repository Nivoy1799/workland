using UnityEngine;
using Worktown.Game.Audio;
using Worktown.Game.Cooking;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 031: a sauce dispenser on an assembly board, for every viewer. While it squeezes the bottle lifts over the build
    /// (nozzle down), pulses as it is squeezed, a ribbon of sauce in its colour runs from the nozzle onto the stack in a slow
    /// zigzag, and a squeeze loop plays. Others see it from the board's synced byte (<see cref="BuildRules.IsSqueezing"/>); the
    /// squeezing player sees it from the press to the release at once (<see cref="Predict"/>), not waiting for the server. The
    /// sauce layer itself appears when the server adds it (on the release). No per-frame allocations.
    /// </summary>
    public sealed class SauceDispenserView : MonoBehaviour
    {
        [SerializeField] private Station _board;
        [SerializeField] private int _dispenser;
        [Tooltip("The build spot's stack (the ribbon lands on its top).")]
        [SerializeField] private BuildStackView _stack;
        [Tooltip("The bottle model (moves over the build while squeezing).")]
        [SerializeField] private Transform _bottle;
        [Tooltip("A plain lit material for the ribbon (tinted with the sauce colour).")]
        [SerializeField] private Material _ribbonMaterial;
        [SerializeField] private float _bottleHeight = 0.2f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static SoundCatalog _soundCatalog;
        private StationContents _contents;
        private Transform _ribbon;
        private Renderer _ribbonRenderer;
        private AudioSource _loop;
        private Vector3 _restPosition;
        private Quaternion _restRotation;
        private Vector3 _restScale;
        private bool _predicting;
        private bool _predicted;
        private float _predictedUntil;
        private bool _shownOn;
        private float _changedAt = float.NegativeInfinity;
        private float _squeezeStart;
        private Vector3 _fromPosition;
        private Quaternion _fromRotation;

        /// <summary>True while this client draws the squeeze.</summary>
        public bool IsSqueezing => _shownOn;
        /// <summary>The frame the predicted squeeze was first drawn (click-to-visual log); -1 before.</summary>
        public int PredictionDrawnFrame { get; private set; } = -1;
        public Station Board => _board;
        public int Dispenser => _dispenser;

        private void Awake()
        {
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                enabled = false;
                return;
            }
            if (_board != null) _contents = _board.GetComponent<StationContents>();
            if (_bottle != null)
            {
                _restPosition = _bottle.localPosition;
                _restRotation = _bottle.localRotation;
                _restScale = _bottle.localScale;
            }
            GameObject ribbon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ribbon.name = "Ribbon";
            Destroy(ribbon.GetComponent<Collider>());
            ribbon.transform.SetParent(transform, false);
            _ribbon = ribbon.transform;
            _ribbonRenderer = ribbon.GetComponent<Renderer>();
            if (_ribbonMaterial != null) _ribbonRenderer.sharedMaterial = _ribbonMaterial;
            _ribbonRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, _dispenser == 0 ? Palette.FoodHerbSauce : Palette.FoodChiliSauce);
            _ribbonRenderer.SetPropertyBlock(block);
            _ribbonRenderer.enabled = false;
            _loop = gameObject.AddComponent<AudioSource>();
            _loop.playOnAwake = false;
            _loop.loop = true;
            _loop.spatialBlend = 1f;
            _loop.minDistance = 1f;
            _loop.maxDistance = 14f;
        }

        /// <summary>
        /// The squeezing player: the look follows the button now (on at the press, off at the release). After the release the
        /// synced byte is ignored until it agrees or for a second, so the ribbon never comes back while the server catches up.
        /// </summary>
        public void Predict(bool on)
        {
            _predicting = true;
            _predicted = on;
            _predictedUntil = Time.unscaledTime + (on ? 8f : 1f);
            if (on) PredictionDrawnFrame = -1;
        }

        private bool SyncedOn => _contents != null && BuildRules.IsSqueezing(_contents.StoveFlags.Value, _dispenser);

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
                if (_bottle != null)
                {
                    _fromPosition = _bottle.localPosition;
                    _fromRotation = _bottle.localRotation;
                }
                if (on) _squeezeStart = now;
                Loop(on);
            }
            if (_shownOn && _predicting && PredictionDrawnFrame < 0) PredictionDrawnFrame = Time.frameCount;
            if (_bottle == null) return;

            float share = AssemblyArtRules.BottleShare(now - _changedAt, _shownOn);
            Vector3 overPosition = _restPosition;
            Quaternion overRotation = _restRotation * Quaternion.Euler(0f, 0f, 180f);
            if (_stack != null)
            {
                // Pivot (the bottle's bottom) above the stack, so the nozzle (its top, turned down) is NozzleAbove over it.
                Vector3 top = _stack.StackTop;
                Vector3 world = top + Vector3.up * (AssemblyArtRules.NozzleAbove + _bottleHeight);
                overPosition = _bottle.parent != null ? _bottle.parent.InverseTransformPoint(world) : world;
            }
            Vector3 target = _shownOn ? overPosition : _restPosition;
            Quaternion targetRotation = _shownOn ? overRotation : _restRotation;
            _bottle.localPosition = Vector3.Lerp(_fromPosition, target, share);
            _bottle.localRotation = Quaternion.Slerp(_fromRotation, targetRotation, share);
            float pulse = _shownOn && share >= 1f ? 1f - 0.08f * (0.5f + 0.5f * Mathf.Sin((now - _squeezeStart) * 18f)) : 1f;
            _bottle.localScale = new Vector3(_restScale.x * pulse, _restScale.y, _restScale.z * pulse);

            bool ribbon = _shownOn && share >= 1f && _stack != null;
            _ribbonRenderer.enabled = ribbon;
            if (!ribbon) return;
            Vector3 nozzle = _bottle.position + _bottle.up * _bottleHeight;
            Vector2 sway = AssemblyArtRules.RibbonLanding(now - _squeezeStart);
            Vector3 landing = _stack.StackTop + _stack.transform.right * sway.x + _stack.transform.forward * sway.y;
            Vector3 along = landing - nozzle;
            float length = along.magnitude;
            if (length < 0.001f) return;
            _ribbon.position = (nozzle + landing) * 0.5f;
            _ribbon.rotation = Quaternion.FromToRotation(Vector3.up, along / length);
            float w = AssemblyArtRules.RibbonWidth;
            Vector3 parentScale = transform.lossyScale;
            _ribbon.localScale = new Vector3(w / Mathf.Max(0.0001f, parentScale.x), length * 0.5f / Mathf.Max(0.0001f, parentScale.y),
                w / Mathf.Max(0.0001f, parentScale.z));
        }

        private void Loop(bool on)
        {
            if (_loop == null) return;
            if (on)
            {
                _soundCatalog ??= new SoundCatalog();
                if (_loop.clip == null && _soundCatalog.TryGet(SoundKey.Squeeze, out AudioClip clip)) _loop.clip = clip;
                _loop.volume = SoundCatalog.MasterVolume * SoundCatalog.GainOf(SoundKey.Squeeze);
                if (_loop.clip != null) _loop.Play();
            }
            else _loop.Stop();
        }
    }
}
