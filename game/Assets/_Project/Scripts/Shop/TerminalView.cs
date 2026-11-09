using UnityEngine;
using Worktown.Game.Art;
using Worktown.Sim.Economy;

namespace Worktown.Game.Shop
{
    /// <summary>
    /// Spec 034, client: the ordering terminal's screen on the back wall: a header with the page and the pack count, <see cref="TerminalRules.Rows"/>
    /// rows ("tomato  0/12 !  · $336": the stock as clients see it, the price of the chosen pack count), and the buttons (page back, next
    /// page, packs). The page and the pack count are this client's own (<see cref="Press"/>); a row's click is the order the interactor
    /// sends. The clicked row lights Mustard at the click (predicted), Sage when the server took the order, Terracotta when it refused.
    /// Texts change only when the stock, the page or the pack count change (no per-frame allocation).
    /// </summary>
    public sealed class TerminalView : MonoBehaviour
    {
        [SerializeField] private TextMesh _header;
        [SerializeField] private TextMesh[] _rows = new TextMesh[0];
        [SerializeField] private Renderer[] _rowBacks = new Renderer[0];
        [SerializeField] private TextMesh _footer;

        /// <summary>s: how long a clicked row stays lit.</summary>
        public const float FlashSeconds = 0.9f;

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly Color RowIdle = Palette.Charcoal;

        private EconomyCatalog _catalog;
        private StockCounts _counts;
        private float _nextLookup;
        private int _seenVersion = -1;
        private bool _dirty = true;
        private MaterialPropertyBlock _block;
        private int _flashRow = -1;
        private float _flashUntil;
        private Color _flashColour;
        private int _request;
        private bool _flashShown;

        /// <summary>The page this client shows (0-based).</summary>
        public int Page { get; private set; }

        /// <summary>The packs a row's click orders (1 to Supplier.MaxPacksPerOrder).</summary>
        public int Packs { get; private set; } = 1;

        // The words and price of each slot's click for the current page and pack count (built when they change, not per frame).
        private readonly string[] _words = new string[TerminalRules.SlotCount];
        private readonly long[] _prices = new long[TerminalRules.SlotCount];
        private int _wordsPage = -1, _wordsPacks = -1;

        /// <summary>
        /// The words of slot <paramref name="slot"/>'s click ("order 1 pack of tomato ($336, 1 h)", "next page (2/4)"), null when it does
        /// nothing (a row past the list's end); <paramref name="price"/> a row's order price (-1 else). Cached per page and pack count.
        /// </summary>
        public string WordsFor(int slot, out long price)
        {
            price = -1;
            if (slot < 0 || slot >= TerminalRules.SlotCount) return null;
            if (_wordsPage != Page || _wordsPacks != Packs)
            {
                _wordsPage = Page;
                _wordsPacks = Packs;
                _catalog ??= EconomyCatalog.CreateDefault();
                for (int s = 0; s < TerminalRules.SlotCount; s++)
                {
                    _prices[s] = -1;
                    if (!TerminalRules.IsRow(s))
                    {
                        _words[s] = TerminalRules.ButtonWords(s, Page, Packs);
                        continue;
                    }
                    int line = TerminalRules.LineAt(Page, s);
                    if (line < 0)
                    {
                        _words[s] = null;
                        continue;
                    }
                    _prices[s] = TerminalRules.PriceOf(_catalog, line, Packs);
                    _words[s] = TerminalRules.OrderWords(line, Packs, _prices[s], Supplier.DelayMinutes);
                }
            }
            price = _prices[slot];
            return _words[slot];
        }

        /// <summary>Pure: what a button press does to the page and the pack count.</summary>
        public static void Apply(int slot, ref int page, ref int packs)
        {
            if (slot == TerminalRules.PrevSlot) page = TerminalRules.PrevPage(page);
            else if (slot == TerminalRules.NextSlot) page = TerminalRules.NextPage(page);
            else if (slot == TerminalRules.PacksSlot) packs = TerminalRules.NextPacks(packs);
        }

        /// <summary>A button of the screen (page back, next page, packs) at the click.</summary>
        public void Press(int slot)
        {
            int page = Page, packs = Packs;
            Apply(slot, ref page, ref packs);
            Page = page;
            Packs = packs;
            _dirty = true;
        }

        /// <summary>Shows line <paramref name="line"/>'s page (the tours open the page they order from).</summary>
        public void ShowLine(int line)
        {
            Page = TerminalRules.PageOf(line);
            _dirty = true;
        }

        /// <summary>Our order from row <paramref name="row"/> went out as request <paramref name="request"/>: the row lights at once.</summary>
        public void PredictOrder(int row, int request)
        {
            _request = request;
            Flash(row, Palette.Mustard);
        }

        /// <summary>The server answered <paramref name="request"/> (true when it was our order): Sage for taken, Terracotta for refused.</summary>
        public bool Answer(int request, bool ok)
        {
            if (request != _request || _flashRow < 0) return false;
            Flash(_flashRow, ok ? Palette.Sage : Palette.Terracotta);
            return true;
        }

        private void Flash(int row, Color colour)
        {
            _flashRow = row;
            _flashColour = colour;
            _flashUntil = Time.unscaledTime + FlashSeconds;
            _flashShown = false;
        }

        private static Material _textMaterial;

        private void Awake()
        {
            if (Application.isBatchMode)
            {
                enabled = false;
                return;
            }
            // Spec 027d: the screen's texts are depth tested (they never show through the fridge door or a wall).
            foreach (TextMesh text in GetComponentsInChildren<TextMesh>(true))
            {
                if (text == null || text.GetComponent<StationSign>() != null) continue; // the sign is the label system's
                if (_textMaterial == null) _textMaterial = LabelMaterials.CreateText(text.font);
                if (_textMaterial != null && text.TryGetComponent(out MeshRenderer r)) r.sharedMaterial = _textMaterial;
            }
        }

        private void Update()
        {
            if (_counts == null && Time.unscaledTime >= _nextLookup)
            {
                _nextLookup = Time.unscaledTime + 1f;
                _counts = FindFirstObjectByType<StockCounts>();
            }
            if (_counts != null && _counts.Version != _seenVersion)
            {
                _seenVersion = _counts.Version;
                _dirty = true;
            }
            if (_dirty)
            {
                _dirty = false;
                Refresh();
            }
            UpdateFlash();
        }

        private void Refresh()
        {
            _catalog ??= EconomyCatalog.CreateDefault();
            if (_header != null)
                _header.text = "ORDER STOCK · page " + (Page + 1) + "/" + TerminalRules.Pages + " · " + TerminalRules.PacksText(Packs)
                    + " · " + TerminalRules.DelayText(Supplier.DelayMinutes);
            for (int r = 0; r < _rows.Length; r++)
            {
                if (_rows[r] == null) continue;
                int line = TerminalRules.LineAt(Page, r);
                if (line < 0)
                {
                    _rows[r].text = string.Empty;
                    continue;
                }
                byte count = 0;
                if (_counts != null) _counts.TryGet(line, out count);
                _rows[r].text = TerminalRules.RowText(line, count, Packs, TerminalRules.PriceOf(_catalog, line, Packs));
                _rows[r].color = StockSlotLook.TagColour(line, count);
            }
            if (_footer != null) _footer.text = "< page          page >          packs: " + Packs;
        }

        private void UpdateFlash()
        {
            if (_flashRow < 0) return;
            bool over = Time.unscaledTime > _flashUntil;
            if (_flashShown && !over) return;
            _block ??= new MaterialPropertyBlock();
            for (int r = 0; r < _rowBacks.Length; r++)
            {
                if (_rowBacks[r] == null) continue;
                _rowBacks[r].GetPropertyBlock(_block);
                _block.SetColor(BaseColor, !over && r == _flashRow ? _flashColour : RowIdle);
                _rowBacks[r].SetPropertyBlock(_block);
            }
            _flashShown = true;
            if (over) _flashRow = -1;
        }
    }
}
