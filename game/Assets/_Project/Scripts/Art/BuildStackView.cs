using UnityEngine;
using Worktown.Game.Audio;
using Worktown.Game.Cooking;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 031: draws the build on an assembly board's spot for every viewer, from the spot's synced Vessel state only (its
    /// layer looks in order and its mode byte). Each layer stands on the one before (<see cref="AssemblyArtRules.LayerHeight"/>);
    /// a new layer drops onto the stack on a short arc and lands with a soft "plop" and a squash. The fold (the wrap closes over
    /// the fillings), the paper wrap and a box or bag going on are short animations (<see cref="AssemblyArtRules.StepSeconds"/>).
    /// The owner's fold or packaging click shows at once (<see cref="PredictStep"/>; the interactor judges it, a refusal turns
    /// it back). The spot object never moves; only the "Contents" children are animated. No per-frame allocations.
    /// </summary>
    public sealed class BuildStackView : MonoBehaviour
    {
        [SerializeField] private ArtCatalog _catalog;

        private const int MaxLayers = CookingLimits.MaxBuildLayers;
        private Vessel _vessel;
        private Transform _contents;
        private readonly Transform[] _layerRoots = new Transform[MaxLayers];
        private readonly FoodVisual[] _layers = new FoodVisual[MaxLayers];
        private readonly FoodLook[] _shownLooks = new FoodLook[MaxLayers];
        private readonly float[] _rest = new float[MaxLayers];
        private readonly float[] _dropStart = new float[MaxLayers];
        private readonly bool[] _landed = new bool[MaxLayers];
        private int _shownCount = -1;
        private Transform _closedRoot;
        private FoodVisual _closed;
        private byte _closedKind = 255;
        private byte _shownMode;
        private bool _modeStarted;
        private float _stepStart = float.NegativeInfinity;
        private float _closedSince = float.NegativeInfinity;
        private bool _predicting;
        private byte _predictedMode;
        private float _predictUntil;
        private float _height;
        private KitchenSounds _sounds;
        private bool _soundsLooked;

        /// <summary>The mode byte this client shows: its own pending step first, else the synced one.</summary>
        public byte SeenMode => _predicting ? _predictedMode : (_vessel != null ? _vessel.Mode.Value : (byte)0);
        public bool IsPredicting => _predicting;
        /// <summary>The frame the predicted step was first drawn (for the click-to-visual log); -1 before.</summary>
        public int PredictionDrawnFrame { get; private set; } = -1;
        /// <summary>The top of the stack in world space (where the sauce ribbon lands).</summary>
        public Vector3 StackTop => (_contents != null ? _contents.position : transform.position) + Vector3.up * _height;
        /// <summary>Layers drawn now (for tests and the tour log).</summary>
        public int ShownLayers => _shownCount < 0 ? 0 : _shownCount;

        private void Awake()
        {
            _vessel = GetComponent<Vessel>();
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                enabled = false;
                return;
            }
            _contents = _vessel != null ? _vessel.ContentsAnchor : null;
            if (_contents == null) return;
            for (int i = 0; i < MaxLayers; i++)
            {
                var go = new GameObject("Layer " + i);
                go.transform.SetParent(_contents, false);
                _layerRoots[i] = go.transform;
                _layers[i] = go.AddComponent<FoodVisual>();
                _layers[i].Configure(_catalog, true);
            }
            var closed = new GameObject("Closed");
            closed.transform.SetParent(_contents, false);
            _closedRoot = closed.transform;
            _closed = closed.AddComponent<FoodVisual>();
            _closed.Configure(_catalog, true);
        }

        private void OnEnable()
        {
            _shownCount = -1;
            _modeStarted = false;
            _predicting = false;
        }

        /// <summary>Owner: a fold or packaging click shows now as <paramref name="mode"/> until the server answers.</summary>
        public void PredictStep(byte mode, float confirmTimeout)
        {
            _predicting = true;
            _predictedMode = mode;
            _predictUntil = Time.unscaledTime + Mathf.Max(0.1f, confirmTimeout) + 0.5f;
            PredictionDrawnFrame = -1;
        }

        /// <summary>The prediction is over: confirmed (the synced mode caught up) or denied (it turns back to the synced mode).</summary>
        public void EndPrediction(bool confirmed) => _predicting = false;

        private void LateUpdate()
        {
            if (_vessel == null || _contents == null) return;
            float now = Time.unscaledTime;
            if (_predicting && now > _predictUntil) _predicting = false;
            UpdateLayers(now);
            UpdateMode(now);
            if (_predicting && PredictionDrawnFrame < 0) PredictionDrawnFrame = Time.frameCount;
        }

        private void UpdateLayers(float now)
        {
            int count = Mathf.Min(_vessel.Looks.Count, MaxLayers);
            float height = 0f;
            for (int i = 0; i < MaxLayers; i++)
            {
                if (i >= count)
                {
                    if (i < _shownCount || _shownCount < 0) _layers[i].Clear();
                    continue;
                }
                FoodLook look = _vessel.Looks[i];
                bool fresh = _shownCount >= 0 && i >= _shownCount;
                if (i >= _shownCount || look != _shownLooks[i])
                {
                    _layers[i].ShowLayer(look);
                    _shownLooks[i] = look;
                }
                if (fresh)
                {
                    _dropStart[i] = now;
                    _landed[i] = false;
                }
                else if (i >= _shownCount)
                {
                    _dropStart[i] = float.NegativeInfinity; // a (re)join: it just lies there
                    _landed[i] = true;
                }
                _rest[i] = height;
                height += AssemblyArtRules.LayerHeight(look);
            }
            _shownCount = count;
            _height = height;

            float hide = LayerScale(now);
            for (int i = 0; i < count; i++)
            {
                float t = now - _dropStart[i];
                Vector3 drop = AssemblyArtRules.DropOffset(t);
                Vector2 shift = AssemblyArtRules.LayerShift(i, _shownLooks[i]);
                _layerRoots[i].localPosition = new Vector3(shift.x, _rest[i], shift.y) + drop;
                float squash = t >= AssemblyArtRules.DropSeconds ? AssemblyArtRules.SquashAfter(t - AssemblyArtRules.DropSeconds) : 1f;
                _layerRoots[i].localScale = new Vector3(hide, hide * squash, hide);
                if (!_landed[i] && t >= AssemblyArtRules.DropSeconds)
                {
                    _landed[i] = true;
                    Play(SoundKey.Plop, _layerRoots[i].position);
                }
            }
        }

        /// <summary>The layers' scale under a fold, paper, box or bag: 1 open, shrinking into the fold and gone once closed.</summary>
        private float LayerScale(float now)
        {
            if (BuildRules.IsOpen(_shownMode)) return 1f;
            float share = AssemblyArtRules.StepShare(now - _closedSince); // only the step that closed it shrinks them
            return share >= 1f ? 0f : 1f - share;
        }

        private void UpdateMode(float now)
        {
            byte mode = SeenMode;
            if (!_modeStarted)
            {
                _modeStarted = true;
                _shownMode = mode;
                _stepStart = float.NegativeInfinity; // a join: the build is shown as it is
                _closedSince = float.NegativeInfinity;
            }
            else if (mode != _shownMode)
            {
                bool closer = BuildRules.CloseOf(mode) > BuildRules.CloseOf(_shownMode) || BuildRules.PackOf(mode) > BuildRules.PackOf(_shownMode);
                bool wasOpen = BuildRules.IsOpen(_shownMode);
                _shownMode = mode;
                _stepStart = wasOpen || closer ? now : float.NegativeInfinity;
                if (wasOpen && !BuildRules.IsOpen(mode)) _closedSince = now;
                if (closer) Play(BuildRules.CloseOf(mode) == BuildRules.CloseFolded && BuildRules.PackOf(mode) == BuildRules.PackNone ? SoundKey.Fold : SoundKey.Paper, transform.position);
            }

            byte kind = KindOf(_shownMode);
            if (kind != _closedKind)
            {
                _closedKind = kind;
                if (kind == FoodLook.KindBuild) _closed.Clear();
                else
                {
                    byte bread = _shownCount > 0 ? _shownLooks[0].Ingredient : FoodLook.UnknownIngredient;
                    _closed.ShowFood(new FoodLook(bread, FoodLook.ShapeWhole, 0, 0, kind, 0), System.Array.Empty<FoodLook>());
                }
            }
            if (kind == FoodLook.KindBuild) return;
            float share = AssemblyArtRules.StepShare(now - _stepStart);
            switch (kind)
            {
                case FoodLook.KindFolded:
                    // The wrap closes over the fillings: it rises from flat to its folded height.
                    _closedRoot.localPosition = Vector3.zero;
                    _closedRoot.localScale = new Vector3(1f, Mathf.Lerp(0.15f, 1f, share), Mathf.Lerp(1.8f, 1f, share));
                    break;
                case FoodLook.KindWrapped:
                    // The paper goes round it.
                    _closedRoot.localPosition = Vector3.zero;
                    _closedRoot.localScale = Vector3.one * Mathf.Lerp(0.82f, 1f, share);
                    break;
                default:
                    // A box or a bag comes down over it.
                    _closedRoot.localPosition = Vector3.up * (AssemblyArtRules.DropHeight * (1f - share));
                    _closedRoot.localScale = Vector3.one;
                    break;
            }
        }

        /// <summary>The look kind a mode byte shows: open, folded, in paper, boxed, bagged.</summary>
        public static byte KindOf(byte mode)
        {
            int pack = BuildRules.PackOf(mode);
            if (pack == BuildRules.PackBox) return FoodLook.KindBoxed;
            if (pack == BuildRules.PackBag) return FoodLook.KindBagged;
            int close = BuildRules.CloseOf(mode);
            if (close == BuildRules.CloseWrapped) return FoodLook.KindWrapped;
            return close == BuildRules.CloseFolded ? FoodLook.KindFolded : FoodLook.KindBuild;
        }

        private void Play(SoundKey key, Vector3 position)
        {
            if (!_soundsLooked)
            {
                _soundsLooked = true;
                _sounds = FindFirstObjectByType<KitchenSounds>();
            }
            if (_sounds != null) _sounds.Play(key, position);
        }
    }
}
