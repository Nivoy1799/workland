using System.Collections.Generic;
using UnityEngine;
using Worktown.Game.Art;
using Worktown.Game.Climb;
using Worktown.Game.Cooking;
using Worktown.Game.Npc;
using Worktown.Game.Shop;
using Worktown.Sim.Climb;

namespace Worktown.Game.Office
{
    /// <summary>
    /// Spec 059, client: the office terminal's screen: a tab row (Menu · Services · Results), six rows with their buttons, the page buttons
    /// and a footer with the two newest log lines. The tab and the page are this client's own (<see cref="TryLocal"/>, as the 034
    /// TerminalView's page); a row button's click is the press the interactor sends (<see cref="OptionFor"/>: the app, the app's row and the
    /// action). Texts come from synced state only (ShopStage, the menu board's prices, OfficeTerminal) through the pure rules, and are
    /// rebuilt only when something they show moved.
    /// </summary>
    public sealed class OfficeTerminalView : MonoBehaviour
    {
        /// <summary>The buttons each row has on the screen (the row itself is action Press, no label).</summary>
        public const int RowButtons = TerminalOptions.ActionCount - 1;

        [SerializeField] private OfficeTerminal _terminal;
        [SerializeField] private TextMesh _header;
        [SerializeField] private TextMesh[] _tabs = new TextMesh[0];
        [SerializeField] private TextMesh[] _rows = new TextMesh[0];
        /// <summary>Row r's button for action a (1 to 4) is index r * RowButtons + a - 1.</summary>
        [SerializeField] private TextMesh[] _buttons = new TextMesh[0];
        [SerializeField] private TextMesh _footer;

        private static Material _textMaterial;
        private ShopStage _stage;
        private MenuBoard _board;
        private Net.NetBootstrap _bootstrap;
        private float _nextLookup;
        private (int, int, int, int, byte, byte, byte, byte, int) _shown = (-1, -1, -1, -1, 0, 0, 0, 0, -1);
        private readonly List<int> _prices = new List<int>();
        private readonly List<ServiceRecord> _history = new List<ServiceRecord>();
        private readonly List<string> _log = new List<string>();
        private readonly string[] _rowTexts = new string[TerminalOptions.ScreenRows];
        private readonly string[] _buttonTexts = new string[TerminalOptions.ScreenRows * RowButtons];
        private readonly int[] _rowAppRows = new int[TerminalOptions.ScreenRows];
        private readonly string[] _rowWords = new string[TerminalOptions.ScreenRows * TerminalOptions.ActionCount];

        /// <summary>The app this client shows.</summary>
        public TerminalApp App { get; private set; } = TerminalApp.Menu;
        /// <summary>The page of the app this client shows (0-based).</summary>
        public int Page { get; private set; }
        /// <summary>The pages the shown app has now.</summary>
        public int PageCount { get; private set; } = 1;

        /// <summary>The screen as text (the tours log it): the header, the rows, the footer.</summary>
        public string ScreenText
        {
            get
            {
                var sb = new System.Text.StringBuilder(_header != null ? _header.text : string.Empty);
                foreach (string row in _rowTexts) if (!string.IsNullOrEmpty(row)) sb.Append('\n').Append(row);
                if (_footer != null && _footer.text.Length > 0) sb.Append('\n').Append(_footer.text);
                return sb.ToString();
            }
        }

        private void Awake()
        {
            if (Application.isBatchMode)
            {
                enabled = false;
                return;
            }
            foreach (TextMesh text in GetComponentsInChildren<TextMesh>(true))
            {
                if (text == null || text.GetComponent<StationSign>() != null) continue; // the sign is the label system's
                if (_textMaterial == null) _textMaterial = LabelMaterials.CreateText(text.font);
                if (_textMaterial != null && text.TryGetComponent(out MeshRenderer r)) r.sharedMaterial = _textMaterial;
            }
        }

        /// <summary>A tab or a page button at the click: applied here (nothing goes to the server). False for a row slot.</summary>
        public bool TryLocal(int slot)
        {
            if (TerminalOptions.IsTabSlot(slot, out TerminalApp app))
            {
                ShowApp(app);
                return true;
            }
            if (TerminalOptions.IsPageSlot(slot, out int delta))
            {
                Refresh();
                Page = (Page + delta + PageCount) % System.Math.Max(1, PageCount);
                _shown = (-1, -1, -1, -1, 0, 0, 0, 0, -1);
                return true;
            }
            return false;
        }

        /// <summary>Shows <paramref name="app"/> from its first page (the tours open the app they use).</summary>
        public void ShowApp(TerminalApp app)
        {
            App = app;
            Page = 0;
            _shown = (-1, -1, -1, -1, 0, 0, 0, 0, -1);
        }

        /// <summary>The press a row slot sends now (-1: nothing there, or not a row slot).</summary>
        public int OptionFor(int slot)
        {
            if (!TerminalOptions.TryRowSlot(slot, out int screenRow, out int action)) return -1;
            Refresh();
            if (_rowWords[screenRow * TerminalOptions.ActionCount + action] == null) return -1;
            return TerminalOptions.Pack(App, _rowAppRows[screenRow], action);
        }

        /// <summary>The prompt's words for <paramref name="slot"/> ("open the Results tab", "price up $0.50"), null when it does nothing.</summary>
        public string WordsFor(int slot)
        {
            if (TerminalOptions.IsTabSlot(slot, out TerminalApp app)) return "open the " + AppName(app) + " tab";
            if (TerminalOptions.IsPageSlot(slot, out int delta)) return PageCount > 1 ? (delta < 0 ? "page back" : "next page") : null;
            if (!TerminalOptions.TryRowSlot(slot, out int screenRow, out int action)) return null;
            Refresh();
            return _rowWords[screenRow * TerminalOptions.ActionCount + action];
        }

        public static string AppName(TerminalApp app) => app == TerminalApp.Menu ? "Menu" : app == TerminalApp.Services ? "Services" : "Results";

        private void Update() => Refresh();

        private void Lookup()
        {
            if (Time.unscaledTime >= _nextLookup)
            {
                _nextLookup = Time.unscaledTime + 1f;
                if (_stage == null) _stage = ShopStage.Find();
                if (_board == null)
                {
                    Station s = Worktown.Game.Layout.LayoutRegistry.First(StationKind.MenuBoard);
                    _board = s != null ? s.GetComponent<MenuBoard>() : null;
                }
                if (_bootstrap == null) _bootstrap = FindFirstObjectByType<Net.NetBootstrap>();
            }
        }

        private void Refresh()
        {
            if (_stage == null || _board == null) Lookup();
            byte stage = _stage != null ? _stage.StageNow : ShopStageRules.OpenKitchen;
            byte owned = _stage != null ? _stage.RecipesOwned.Value : (byte)0;
            byte menuNow = _stage != null ? _stage.MenuNow.Value : (byte)0;
            byte menuNext = _stage != null ? _stage.MenuNext.Value : (byte)0;
            int roster = _bootstrap != null && _bootstrap.Join != null && _bootstrap.Join.Roster.Ids != null ? _bootstrap.Join.Roster.Ids.Length : 0;
            var shown = ((int)App, Page, _terminal != null ? _terminal.Version : 0, _board != null ? _board.Version : 0, stage, owned, menuNow, menuNext, roster);
            if (shown == _shown) return;
            _shown = shown;
            _prices.Clear();
            if (_board != null) for (int i = 0; i < _board.Prices.Count; i++) _prices.Add(_board.Prices[i]);
            _history.Clear();
            if (_terminal != null) for (int i = 0; i < _terminal.History.Count; i++) _history.Add(_terminal.History[i]);
            _log.Clear();
            if (_terminal != null)
                for (int i = 0; i < _terminal.Log.Count; i++) _log.Add(ChangeLog.Text(_terminal.Log[i], NameOf));
            int page = Page;
            string header = Compose(App, ref page, stage, owned, menuNow, menuNext, _prices, _terminal != null ? _terminal.SpecialNow : Menu.None,
                _history, _log, FoodCornerRules.DefaultClimb, _rowTexts, _buttonTexts, _rowAppRows, _rowWords, out int pages);
            Page = page;
            PageCount = pages;
            if (_header != null) _header.text = header;
            for (int t = 0; t < _tabs.Length; t++)
                if (_tabs[t] != null) _tabs[t].text = t == (int)App ? "[" + AppName((TerminalApp)t) + "]" : AppName((TerminalApp)t);
            for (int r = 0; r < _rows.Length && r < _rowTexts.Length; r++) if (_rows[r] != null) _rows[r].text = _rowTexts[r] ?? string.Empty;
            for (int b = 0; b < _buttons.Length && b < _buttonTexts.Length; b++) if (_buttons[b] != null) _buttons[b].text = _buttonTexts[b] ?? string.Empty;
            if (_footer != null)
            {
                int n = _log.Count;
                _footer.text = n == 0 ? string.Empty : n == 1 ? _log[0] : _log[n - 2] + "\n" + _log[n - 1];
            }
        }

        private string NameOf(int slot)
        {
            if (_bootstrap == null || _bootstrap.Join == null) return null;
            Net.LobbyRoster roster = _bootstrap.Join.Roster;
            return ChangeLog.NameIn(slot, roster.Ids, roster.Names);
        }

        /// <summary>
        /// Pure: the screen of <paramref name="app"/>'s page <paramref name="page"/> (clamped to its pages): the header (returned), each screen
        /// row's text, each row button's label, the app row each screen row shows (-1 none) and the prompt words of each row slot (null:
        /// that slot does nothing).
        /// </summary>
        public static string Compose(TerminalApp app, ref int page, byte stage, byte owned, byte menuNow, byte menuNext, IReadOnlyList<int> prices,
            byte special, IReadOnlyList<ServiceRecord> history, IReadOnlyList<string> log, ClimbCatalog climb, string[] rows, string[] buttons,
            int[] appRows, string[] words, out int pages)
        {
            for (int i = 0; i < rows.Length; i++) rows[i] = string.Empty;
            for (int i = 0; i < buttons.Length; i++) buttons[i] = string.Empty;
            for (int i = 0; i < appRows.Length; i++) appRows[i] = -1;
            for (int i = 0; i < words.Length; i++) words[i] = null;
            const int perPage = TerminalOptions.ScreenRows;
            switch (app)
            {
                case TerminalApp.Menu:
                {
                    IReadOnlyList<int> lines = MenuAppRules.Lines(stage, climb, owned);
                    pages = TerminalOptions.Pages(lines.Count);
                    page = Clamp(page, pages);
                    for (int r = 0; r < perPage; r++)
                    {
                        int appRow = page * perPage + r;
                        if (appRow >= lines.Count) break;
                        int line = lines[appRow];
                        MenuPrices.Line l = MenuPrices.Lines[line];
                        long price = prices != null && line < prices.Count ? prices[line] : l.Reference;
                        bool isSpecial = l.Drink == null && special != Menu.None && l.Dish == special;
                        rows[r] = MenuAppRules.RowText(line, price, MenuAppRules.IsOn(stage, climb, menuNow, line),
                            MenuAppRules.IsOn(stage, climb, menuNext, line), isSpecial);
                        appRows[r] = appRow;
                        bool canToggle = !MenuAppRules.IsFixed(stage, climb, line);
                        bool canSpecial = l.Drink == null && stage != ShopStageRules.FoodCorner;
                        for (int a = TerminalOptions.Minus; a <= TerminalOptions.Special; a++)
                        {
                            bool shows = a == TerminalOptions.Toggle ? canToggle : a != TerminalOptions.Special || canSpecial;
                            if (!shows) continue;
                            buttons[r * RowButtons + a - 1] = MenuAppRules.ButtonLabel(a);
                            words[r * TerminalOptions.ActionCount + a] = a == TerminalOptions.Special && isSpecial
                                ? "clear the special" : MenuAppRules.ButtonWords(a, line);
                        }
                    }
                    return "MENU" + PageText(page, pages) + " · " + (special == Menu.None ? SpecialRules.NoSpecialText.ToLowerInvariant()
                        : "special: " + Menu.NameOf(special).ToLowerInvariant());
                }
                case TerminalApp.Services:
                {
                    int count = ServicesAppRules.RowCount(climb);
                    pages = TerminalOptions.Pages(count);
                    page = Clamp(page, pages);
                    for (int r = 0; r < perPage; r++)
                    {
                        int appRow = page * perPage + r;
                        if (appRow >= count) break;
                        rows[r] = ServicesAppRules.RowText(appRow, climb);
                        appRows[r] = appRow;
                        words[r * TerminalOptions.ActionCount + TerminalOptions.Press] = ServicesAppRules.PressWords(appRow, climb);
                    }
                    return "SERVICES" + PageText(page, pages);
                }
                default:
                {
                    // Each Results page starts on a screen of its own; a long page goes on over several screens.
                    var screens = new List<(int Page, List<string> Lines)>();
                    for (int p = 0; p < ResultsRules.Pages; p++)
                    {
                        List<string> lines = ResultsRules.PageLines(p, history, log);
                        for (int at = 0; at < lines.Count || at == 0; at += perPage)
                            screens.Add((p, lines.GetRange(at, System.Math.Min(perPage, System.Math.Max(0, lines.Count - at)))));
                    }
                    pages = screens.Count;
                    page = Clamp(page, pages);
                    (int resultsPage, List<string> shown) = screens[page];
                    for (int r = 0; r < perPage && r < shown.Count; r++) rows[r] = shown[r];
                    return "RESULTS" + PageText(page, pages) + " · " + ResultsRules.PageTitle(resultsPage).ToLowerInvariant();
                }
            }
        }

        private static int Clamp(int page, int pages) => page < 0 ? 0 : page >= pages ? pages - 1 : page;

        private static string PageText(int page, int pages) => pages > 1 ? " " + (page + 1) + "/" + pages : string.Empty;
    }
}
