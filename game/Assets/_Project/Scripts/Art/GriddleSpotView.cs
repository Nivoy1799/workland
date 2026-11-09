using System.Collections.Generic;
using UnityEngine;
using Worktown.Game.Audio;
using Worktown.Game.Cooking;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 030, client only: a griddle spot's look. The spot object never moves (a networked root); its "Contents" child (the
    /// food) flips on a <see cref="GriddleArtRules.FlipSeconds"/> arc with a half turn for every viewer when the flip counter in
    /// the synced mode byte moves on (<see cref="GriddleRules.Mode"/>), with the "tssh". The owner predicts the flip at the click
    /// (<see cref="PredictFlip"/>, the 027b pattern: the interactor confirms or denies from the counter); its own flip is not
    /// played again when the counter arrives. Grill marks lie on the food's top for the side that is up and under it for the
    /// side that is down, each shown from the good window on (<see cref="GriddleArtRules.MarksShown"/>), so the half turn brings
    /// the cooked side's marks up. Display only; no allocations per frame.
    /// </summary>
    public sealed class GriddleSpotView : MonoBehaviour
    {
        private const int MarkCount = 3;
        private const float MarkWidth = 0.01f;
        private const float MarkThickness = 0.006f;
        private const float BoundsInterval = 0.25f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] private Material _markMaterial;

        private Vessel _vessel;
        private Transform _contents;
        private KitchenSounds _sounds;
        private bool _soundsLooked;
        private Transform _top;
        private Transform _bottom;
        private readonly Renderer[] _topMarks = new Renderer[MarkCount];
        private readonly Renderer[] _bottomMarks = new Renderer[MarkCount];
        private MaterialPropertyBlock _block;
        private readonly List<Renderer> _scratch = new List<Renderer>(16);
        private float _flipStart = float.NegativeInfinity;
        private int _seenFlips;
        private bool _started;
        private int _owed;
        private float _owedUntil;
        private bool _predicting;
        private float _nextBounds;
        private int _boundsCount = -1;
        private float _foodTop = 0.03f;
        private float _foodLength = 0.12f;
        private Vector3 _restPosition;
        private HeatZone _shownTop = (HeatZone)255;
        private HeatZone _shownBottom = (HeatZone)255;

        /// <summary>The synced flip counter (mod 4).</summary>
        public int SyncedFlips => _vessel != null ? GriddleRules.FlipsOf(_vessel.Mode.Value) : 0;
        /// <summary>True while a flip arc is drawn (for tests and the tour log).</summary>
        public bool IsFlipping => GriddleArtRules.IsFlipping(Time.unscaledTime - _flipStart);
        public bool IsPredicting => _predicting;
        /// <summary>030 review: our own predicted flips the synced counter has not shown yet (expired after the confirm timeout).</summary>
        public int OwedFlips => _owed > 0 && Time.unscaledTime > _owedUntil ? 0 : _owed;
        /// <summary>The frame the predicted flip was first drawn (for the click-to-visual log); -1 before.</summary>
        public int PredictionDrawnFrame { get; private set; } = -1;
        /// <summary>Grill marks are drawn on the food's top now (for tests and the tour log).</summary>
        public bool TopMarksShown => _topMarks[0] != null && _topMarks[0].enabled;

        private void Awake()
        {
            _vessel = GetComponent<Vessel>();
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                enabled = false;
                return;
            }
            _contents = _vessel != null ? _vessel.ContentsAnchor : null;
            if (_contents != null) _restPosition = _contents.localPosition;
            BuildMarks();
        }

        private void OnEnable()
        {
            _started = false;
            _predicting = false;
            _owed = 0;
        }

        /// <summary>Owner: the flip at the click, before the server answers (its counter step is not played again).</summary>
        public void PredictFlip(float confirmTimeout)
        {
            float now = Time.unscaledTime;
            _predicting = true;
            _owed++;
            _owedUntil = now + Mathf.Max(0.1f, confirmTimeout);
            _flipStart = now;
            PredictionDrawnFrame = -1;
        }

        /// <summary>The prediction is over (confirmed by the counter, or denied: the food lies as the server keeps it).</summary>
        public void EndPrediction(bool confirmed)
        {
            _predicting = false;
            if (!confirmed)
            {
                _owed = 0;
                _flipStart = Time.unscaledTime; // denied: it turns back over
            }
        }

        private void LateUpdate()
        {
            if (_vessel == null || _contents == null) return;
            float now = Time.unscaledTime;
            byte mode = _vessel.Mode.Value;
            int flips = GriddleRules.FlipsOf(mode);
            // 030 review: owed flips expire every frame, not only when the counter moves, so a refused flip never leaves the marks swapped.
            if (_owed > 0 && now > _owedUntil) _owed = 0;
            if (!_started)
            {
                _started = true; // a join: the counter is a baseline, nothing flips
                _seenFlips = flips;
            }
            else if (flips != _seenFlips)
            {
                int steps = GriddleRules.FlipsBetween(_seenFlips, flips);
                _seenFlips = flips;
                for (int i = 0; i < steps; i++)
                {
                    if (_owed > 0)
                    {
                        _owed--;
                        continue;
                    }
                    _flipStart = now; // someone else's flip (or ours without a prediction)
                    Play(SoundKey.Flip);
                }
            }

            float t = now - _flipStart;
            bool flipping = GriddleArtRules.IsFlipping(t);
            // The half turn is about the food's middle (not its bottom), so it never dips into the plate.
            float turn = flipping ? GriddleArtRules.Turn(t) : 0f;
            float c = _foodTop * 0.5f, rad = turn * Mathf.Deg2Rad;
            _contents.localPosition = _restPosition + new Vector3(0f, GriddleArtRules.Lift(t) + c * (1f - Mathf.Cos(rad)), -c * Mathf.Sin(rad));
            _contents.localRotation = flipping ? Quaternion.Euler(turn, 0f, 0f) : Quaternion.identity;
            if (_predicting && PredictionDrawnFrame < 0) PredictionDrawnFrame = Time.frameCount;
            UpdateMarks(now, mode);
        }

        private void UpdateMarks(float now, byte mode)
        {
            if (_top == null) return;
            bool hasFood = _vessel.ContentCount > 0;
            // While our own flip waits for the server, the sides are drawn as they will be (the down side comes up).
            HeatZone up = GriddleRules.UpZoneOf(mode);
            HeatZone down = _vessel.Zone;
            if (_owed % 2 == 1) (up, down) = (down, up);
            if (!hasFood) up = down = HeatZone.None;
            if (!IsFlipping && (now >= _nextBounds || _boundsCount != _vessel.ContentCount))
            {
                _nextBounds = now + BoundsInterval;
                _boundsCount = _vessel.ContentCount;
                MeasureFood();
            }
            // Just under the top: on a flat top they stand 1 mm proud, on a round one they cross it as stripes.
            _top.localPosition = new Vector3(0f, _foodTop - MarkThickness * 0.5f + 0.001f, 0f);
            _bottom.localPosition = new Vector3(0f, -MarkThickness * 0.5f, 0f);
            SetMarks(_topMarks, up, ref _shownTop);
            SetMarks(_bottomMarks, down, ref _shownBottom);
        }

        private void SetMarks(Renderer[] marks, HeatZone zone, ref HeatZone shown)
        {
            bool visible = GriddleArtRules.MarksShown(zone);
            for (int i = 0; i < marks.Length; i++)
            {
                if (marks[i] == null) continue;
                marks[i].enabled = visible;
                marks[i].transform.localScale = new Vector3(MarkWidth, MarkThickness, _foodLength * 0.75f);
                marks[i].transform.localPosition = new Vector3((i - (marks.Length - 1) * 0.5f) * _foodLength * 0.25f, 0f, 0f);
            }
            if (!visible || zone == shown) return;
            shown = zone;
            _block ??= new MaterialPropertyBlock();
            Color c = GriddleArtRules.MarkColor(zone);
            _block.SetColor(BaseColorId, c);
            _block.SetColor(ColorId, c);
            for (int i = 0; i < marks.Length; i++)
                if (marks[i] != null) marks[i].SetPropertyBlock(_block);
        }

        /// <summary>How high the food stands on the spot and how long it is (its renderers, without the marks).</summary>
        private void MeasureFood()
        {
            _scratch.Clear();
            _contents.GetComponentsInChildren(false, _scratch);
            bool any = false;
            Bounds b = default;
            foreach (Renderer r in _scratch)
            {
                if (r == null || r.transform.IsChildOf(_top) || r.transform.IsChildOf(_bottom)) continue;
                if (!any) b = r.bounds;
                else b.Encapsulate(r.bounds);
                any = true;
            }
            _scratch.Clear();
            if (!any) return;
            _foodTop = Mathf.Max(0.005f, b.max.y - _contents.position.y);
            _foodLength = Mathf.Clamp(Mathf.Min(b.size.x, b.size.z), 0.03f, 0.2f); // the narrower side: the marks never stick out
        }

        private void BuildMarks()
        {
            if (_contents == null) return;
            _top = MarkSet("GrillMarks (top)", _topMarks);
            _bottom = MarkSet("GrillMarks (bottom)", _bottomMarks);
        }

        private Transform MarkSet(string setName, Renderer[] marks)
        {
            var set = new GameObject(setName).transform;
            set.SetParent(_contents, false);
            for (int i = 0; i < marks.Length; i++)
            {
                GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bar.name = "Mark " + i;
                Collider c = bar.GetComponent<Collider>();
                if (c != null) Destroy(c);
                bar.transform.SetParent(set, false);
                bar.transform.localPosition = new Vector3((i - (marks.Length - 1) * 0.5f) * 0.035f, 0f, 0f);
                bar.transform.localRotation = Quaternion.Euler(0f, 35f, 0f);
                Renderer r = bar.GetComponent<Renderer>();
                if (_markMaterial != null) r.sharedMaterial = _markMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.enabled = false;
                marks[i] = r;
            }
            return set;
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
