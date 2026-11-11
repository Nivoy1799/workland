using UnityEngine;
using Worktown.Game.Art;
using Worktown.Game.Audio;
using Worktown.Sim.Economy;

namespace Worktown.Game.Shop
{
    /// <summary>
    /// Spec 032, client only: the register's look. The display (facing the cook) shows "CHANGE $8 / $18", "CARD" or "PAID"; the
    /// change lies as coins and notes on the change mat in front of it. The clicking player sees its own coin at the click
    /// (<see cref="TrayPrediction"/>: feel.md 1), everyone else when the synced tray (cents) rises (with the clink); a card payment
    /// beeps. Display only: the server builds the change and decides (RegisterDesk). Pieces are a fixed pool, positioned only when
    /// the shown sum changes; no allocations per frame.
    /// </summary>
    public sealed class RegisterView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        public const float PaidSeconds = 1.4f;
        private const int MaxClicked = 16;

        [SerializeField] private Register _register;
        [SerializeField] private TextMesh _display;
        [Tooltip("The change pieces (a pool, drawn on the change mat).")]
        [SerializeField] private Renderer[] _pieces = new Renderer[0];
        [Tooltip("Where the change lies (the middle of the mat, its local x along the counter).")]
        [SerializeField] private Transform _mat;
        [Tooltip("The register's own texts (the display, the tray's and the buttons' labels): drawn depth tested (027d).")]
        [SerializeField] private TextMesh[] _texts = new TextMesh[0];

        private static Material _textMaterial;

        private readonly TrayPrediction _prediction = new TrayPrediction();
        private readonly int[] _clicked = new int[MaxClicked];
        private readonly int[] _layout = new int[MaxClicked];
        private int _clickedCount;
        private MaterialPropertyBlock _block;
        private KitchenSounds _sounds;
        private bool _soundsLooked;
        private int _lastTray = -1;
        private ushort _lastPayment;
        private bool _seen;
        private int _shown = -1;
        private string _shownText;
        private float _paidUntil = float.NegativeInfinity;
        private bool _paidByCard;
        private float _cardPredictedUntil = float.NegativeInfinity;
        private float _takeBackUntil = float.NegativeInfinity;

        /// <summary>The tray's sum as this player sees it (its own clicks first).</summary>
        public int ShownTray => Time.unscaledTime < _takeBackUntil ? 0 : _prediction.Shown;

        /// <summary>Pieces drawn now (tests and the tour log).</summary>
        public int ShownPieces { get; private set; }

        /// <summary>The display's text now (tests and the tour log).</summary>
        public string DisplayText => _display != null ? _display.text : string.Empty;

        private void Awake()
        {
            if (_register == null) _register = GetComponent<Register>();
            if (Application.isBatchMode)
            {
                enabled = false;
                return;
            }
            foreach (Renderer r in _pieces)
                if (r != null) r.enabled = false;
            foreach (TextMesh text in _texts)
            {
                if (text == null) continue;
                if (_textMaterial == null) _textMaterial = LabelMaterials.CreateText(text.font);
                if (_textMaterial != null && text.TryGetComponent(out MeshRenderer r)) r.sharedMaterial = _textMaterial;
            }
        }

        /// <summary>Owner: a coin or note of <paramref name="value"/> lies in the tray at the click (until confirmed or <paramref name="until"/>).</summary>
        public void PredictAdd(int value, float until)
        {
            _takeBackUntil = float.NegativeInfinity;
            _prediction.Add(value, until);
            if (_clickedCount < MaxClicked) _clicked[_clickedCount++] = value;
        }

        /// <summary>Owner: the change is taken back at the click.</summary>
        public void PredictTakeBack(float until)
        {
            _takeBackUntil = until;
            _clickedCount = 0;
        }

        /// <summary>Owner: the card terminal shows "approved" at the click (the server confirms with the payment going).</summary>
        public void PredictCard(float until)
        {
            _cardPredictedUntil = until;
            _paidUntil = Time.unscaledTime + PaidSeconds;
            _paidByCard = true;
        }

        /// <summary>Owner: the server refused our oldest pending click; its coin goes back out.</summary>
        public void Refused()
        {
            _prediction.Refused();
            if (_clickedCount > 0) _clickedCount--;
            _takeBackUntil = float.NegativeInfinity;
            if (Time.unscaledTime < _cardPredictedUntil)
            {
                _cardPredictedUntil = float.NegativeInfinity;
                _paidUntil = float.NegativeInfinity;
            }
        }

        private void LateUpdate()
        {
            if (_register == null || !_register.IsSpawned) return;
            float now = Time.unscaledTime;
            int tray = _register.Tray.Value;
            ushort payment = _register.Payment.Value;
            if (!_seen)
            {
                _seen = true;
                _lastTray = tray;
                _lastPayment = payment;
                _prediction.OnServer(tray);
            }
            if (payment != _lastPayment)
            {
                // The head payer changed: the last one paid (or a new one stepped up).
                bool paid = RegisterRules.HasPayer(_lastPayment);
                bool wasCard = RegisterRules.IsCard(_lastPayment);
                if (paid)
                {
                    if (wasCard && now >= _cardPredictedUntil) Play(SoundKey.Beep); // others hear the terminal; the clicker heard it at the click
                    _paidUntil = now + PaidSeconds;
                    _paidByCard = wasCard;
                }
                _cardPredictedUntil = float.NegativeInfinity;
                _lastPayment = payment;
                _prediction.Reset();
                _prediction.OnServer(tray);
                _lastTray = tray;
                _clickedCount = 0;
            }
            else if (tray != _lastTray)
            {
                bool own = _prediction.OnServer(tray);
                if (tray > _lastTray && !own) Play(SoundKey.Clink);
                if (tray < _lastTray) _clickedCount = 0;
                if (tray == 0) _takeBackUntil = float.NegativeInfinity;
                _lastTray = tray;
            }
            _prediction.Expire(now);

            int shown = ShownTray;
            if (shown != _shown) Lay(shown);
            // The text only when what it shows changed (no string per frame).
            int paidState = now < _paidUntil ? (_paidByCard ? 2 : 1) : 0;
            if (_display != null && (paidState != _textPaid || payment != _textPayment || shown != _textShown || _shownText == null))
            {
                _textPaid = paidState;
                _textPayment = payment;
                _textShown = shown;
                _shownText = paidState == 2 ? "APPROVED" : paidState == 1 ? "PAID" : RegisterRules.DisplayText(payment, shown);
                _display.text = _shownText;
                _display.color = paidState != 0 ? Palette.Sage
                    : RegisterRules.IsCash(payment) && shown > RegisterRules.ChangeDue(payment) ? Palette.Terracotta : Palette.Mustard;
            }
        }

        private int _textPaid = -1;
        private ushort _textPayment;
        private int _textShown = -1;

        /// <summary>The pieces for <paramref name="sum"/>: our own clicks when they add up to it, else the fewest coins and notes.</summary>
        private void Lay(int sum)
        {
            _shown = sum;
            int count = 0;
            int own = 0;
            for (int i = 0; i < _clickedCount; i++) own += _clicked[i];
            if (own == sum && _clickedCount > 0)
            {
                for (int i = 0; i < _clickedCount && count < _layout.Length; i++) _layout[count++] = _clicked[i];
            }
            else
            {
                int left = sum;
                for (int d = CashRules.Tray.Count - 1; d >= 0 && count < _layout.Length; d--)
                    while (left >= CashRules.Tray[d] && count < _layout.Length)
                    {
                        _layout[count++] = CashRules.Tray[d];
                        left -= CashRules.Tray[d];
                    }
            }
            _block ??= new MaterialPropertyBlock();
            int coins = 0, notes = 0;
            int drawn = 0;
            for (int i = 0; i < _pieces.Length; i++)
            {
                Renderer r = _pieces[i];
                if (r == null) continue;
                bool on = i < count;
                r.enabled = on;
                if (!on) continue;
                drawn++;
                int value = _layout[i];
                bool coin = IsCoin(value);
                Transform t = r.transform;
                // Spec 037: up to six coins a row (two rows), the notes fanned behind them.
                Vector3 local = coin ? new Vector3(-0.085f + (coins % 6) * 0.034f, 0.004f, -0.05f + (coins / 6) * 0.034f)
                    : new Vector3(-0.07f + notes * 0.03f, 0.002f + notes * 0.002f, 0.035f);
                t.localPosition = (_mat != null ? _mat.localPosition : Vector3.zero) + local;
                t.localRotation = Quaternion.Euler(0f, coin ? 0f : -8f + notes * 6f, 0f);
                float size = CoinSize(value);
                t.localScale = coin ? new Vector3(size, 0.006f, size) : new Vector3(0.08f, 0.003f, 0.04f);
                if (coin) coins++;
                else notes++;
                r.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, ColorOf(value));
                r.SetPropertyBlock(_block);
            }
            ShownPieces = drawn;
        }

        /// <summary>Spec 037: coins are $0.10 to $2 (below $5, in cents), notes $5 to $20.</summary>
        public static bool IsCoin(int cents) => cents < 500;

        /// <summary>Spec 037: a coin's diameter (m): $0.10 the smallest, then $0.20, $0.50, $1 and $2.</summary>
        public static float CoinSize(int cents)
        {
            switch (cents)
            {
                case 10: return 0.020f;
                case 20: return 0.023f;
                case 50: return 0.026f;
                case 100: return 0.028f;
                default: return 0.031f;
            }
        }

        /// <summary>
        /// The colour of a coin or note (palette only), by its value in cents (spec 037): the small coins ($0.10 to $0.50) Steel, $1 and
        /// $2 Mustard, the notes $5 Sage, $10 SoftBlue, $20 Terracotta.
        /// </summary>
        public static Color ColorOf(int cents)
        {
            switch (cents)
            {
                case 10:
                case 20:
                case 50: return Palette.Steel;
                case 100:
                case 200: return Palette.Mustard;
                case 500: return Palette.Sage;
                case 1000: return Palette.SoftBlue;
                case 2000: return Palette.Terracotta;
                default: return Palette.DeepBlue;
            }
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
