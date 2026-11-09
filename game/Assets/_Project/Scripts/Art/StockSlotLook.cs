using UnityEngine;
using Worktown.Game.Cooking;
using Worktown.Game.Shop;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 034, client: a fridge or shelf slot shows its stock. Its sample models (1 to 3) stand for the count: as many show as there
    /// are units, at most all of them (<see cref="VisibleSamples"/>); an empty slot shows none, and its tag turns Terracotta ("tomato ·
    /// empty"); below a quarter it turns Mustard. The tag reads "tomato 4/12". The owner predicts its own take (one fewer at the click)
    /// and unpack (up to what fits, the new samples pop in); the synced count (StockCounts) takes over when it moves, a refusal or a
    /// timeout puts the prediction back. Text and visibility change only when the shown count changes (no per-frame allocation).
    /// </summary>
    public sealed class StockSlotLook : MonoBehaviour
    {
        [SerializeField] private StockSlot _slot;
        [SerializeField] private TextMesh _tag;

        /// <summary>s: a sample that appears pops in over this long.</summary>
        public const float PopSeconds = 0.18f;
        /// <summary>s: an unanswered prediction gives up after this long.</summary>
        public const float PredictionTimeout = 2f;

        private int _line = -2;
        private StockCounts _counts;
        private float _nextLookup;
        private int _seenVersion = -1;
        private int _synced = -1;
        private int _shown = -1;
        // The prediction: the synced count it was made on, its change, the request it waits for.
        private int _base = -1;
        private int _delta;
        private int _request;
        private float _until;
        private Transform[] _samples;
        private Vector3[] _sampleScales;
        private float[] _popStart;
        private string _name;

        public int Line
        {
            get
            {
                if (_line == -2) _line = _slot != null && _slot.Station != null ? StockCatalog.LineOfSlot(_slot.Station.Kind, _slot.Option) : -1;
                return _line;
            }
        }

        /// <summary>Pure: how many of <paramref name="samples"/> sample models show for <paramref name="count"/> units (0 to all).</summary>
        public static int VisibleSamples(int count, int samples) => Mathf.Clamp(count, 0, Mathf.Max(0, samples));

        /// <summary>Pure: the tag's colour for <paramref name="count"/> of line <paramref name="line"/>: empty Terracotta, low Mustard, else Cream.</summary>
        public static Color TagColour(int line, int count)
        {
            if (count <= 0) return Palette.Terracotta;
            return StockCatalog.IsLow(line, (byte)Mathf.Min(count, 255)) ? Palette.Mustard : Palette.Cream;
        }

        /// <summary>Pure: the tag's text: "tomato 4/12", "tomato · empty".</summary>
        public static string TagText(string name, int line, int count) =>
            count <= 0 ? name + " · empty" : name + " " + count + "/" + StockCatalog.PresentedCapacity(line);

        /// <summary>The count as this client shows it: <paramref name="synced"/> with our own pending take or unpack.</summary>
        public int Shown(int synced)
        {
            if (_base < 0 || synced != _base || Time.unscaledTime > _until) return synced;
            return Mathf.Clamp(synced + _delta, 0, 255);
        }

        /// <summary>Our take went out (request <paramref name="request"/>): one fewer at once.</summary>
        public void PredictTake(int request) => Predict(-1, request);

        /// <summary>Our unpack of <paramref name="units"/> went out: up to what fits, at once.</summary>
        public void PredictUnpack(int units, int request)
        {
            int room = StockCatalog.RoomFor(Line, _synced);
            Predict(room < 0 ? 0 : Mathf.Min(units, room), request);
        }

        private void Predict(int delta, int request)
        {
            if (_synced < 0) return;
            _base = _synced;
            _delta = delta;
            _request = request;
            _until = Time.unscaledTime + PredictionTimeout;
            // Applied in this component's Update (this frame or the next): the take's flying copy is made from the sample first.
        }

        /// <summary>The server answered request <paramref name="request"/> (true when it was ours): a refusal puts the count back.</summary>
        public bool Answer(int request, bool ok)
        {
            if (request != _request || _base < 0) return false;
            if (!ok)
            {
                _base = -1;
                Apply();
            }
            return true;
        }

        private void Awake()
        {
            _samples = new Transform[transform.childCount];
            _sampleScales = new Vector3[_samples.Length];
            _popStart = new float[_samples.Length];
            for (int i = 0; i < _samples.Length; i++)
            {
                _samples[i] = transform.GetChild(i);
                _sampleScales[i] = _samples[i].localScale;
                _popStart[i] = -1f;
            }
            _name = _slot != null && _slot.IngredientId != null ? StockCatalog.NameOf(Line) : "stock";
        }

        private void Update()
        {
            if (_counts == null)
            {
                if (Time.unscaledTime < _nextLookup) return;
                _nextLookup = Time.unscaledTime + 1f;
                _counts = FindFirstObjectByType<StockCounts>();
                if (_counts == null) return;
            }
            if (_counts.Version != _seenVersion)
            {
                _seenVersion = _counts.Version;
                if (_counts.TryGet(Line, out byte count)) _synced = count;
            }
            if (_base >= 0 && (_synced != _base || Time.unscaledTime > _until)) _base = -1; // the server's count took over
            Apply();
            AnimatePops();
        }

        private void Apply()
        {
            if (_synced < 0) return;
            int shown = Shown(_synced);
            if (shown == _shown) return;
            int before = _shown < 0 ? shown : VisibleSamples(_shown, _samples.Length);
            _shown = shown;
            int visible = VisibleSamples(shown, _samples.Length);
            for (int i = 0; i < _samples.Length; i++)
            {
                bool on = i < visible;
                if (on && !_samples[i].gameObject.activeSelf && i >= before) _popStart[i] = Time.unscaledTime;
                _samples[i].gameObject.SetActive(on);
            }
            if (_tag != null)
            {
                _tag.text = TagText(_name, Line, shown);
                _tag.color = TagColour(Line, shown);
            }
        }

        private void AnimatePops()
        {
            for (int i = 0; i < _samples.Length; i++)
            {
                if (_popStart[i] < 0f) continue;
                float t = (Time.unscaledTime - _popStart[i]) / PopSeconds;
                if (t >= 1f)
                {
                    _popStart[i] = -1f;
                    _samples[i].localScale = _sampleScales[i];
                    continue;
                }
                float ease = 1f - (1f - t) * (1f - t);
                float overshoot = 1f + 0.12f * Mathf.Sin(t * Mathf.PI);
                _samples[i].localScale = _sampleScales[i] * Mathf.Lerp(0.3f, 1f, ease) * overshoot;
            }
        }
    }
}
