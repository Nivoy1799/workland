using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Worktown.Game.Art;
using Worktown.Game.Climb;
using Worktown.Game.Cooking;
using Worktown.Game.Economy;
using Worktown.Game.Net;
using Worktown.Game.Npc;
using Worktown.Game.Player;
using Worktown.Game.Shop;

namespace Worktown.Game.Office
{
    /// <summary>
    /// Spec 059, client only: the pocket clipboard, a paper overlay (a stand-in until the art pass) with the office terminal's facts,
    /// read-only, anywhere: the clipboard key (PlayerInputReader.ClipboardKey) toggles it, Esc closes it, the player keeps walking. Its text
    /// comes from synced state through <see cref="ClipboardRules"/> and is rebuilt only when that state moved. It makes itself at start
    /// (never in batch mode: a server has no screen).
    /// </summary>
    public sealed class ClipboardView : MonoBehaviour
    {
        private const float Width = 380f;
        private const float Margin = 16f;

        private NetBootstrap _bootstrap;
        private PlayerInputReader _input;
        private ShopStage _stage;
        private MenuBoard _board;
        private OfficeTerminal _office;
        private float _nextLookup;
        private string _text = string.Empty;
        private (int, int, byte, byte, byte, long, long, bool, int, long) _shown;
        private readonly List<int> _prices = new List<int>();
        private readonly List<ServiceRecord> _history = new List<ServiceRecord>();
        private GUIStyle _paper;
        private Texture2D _paperTexture;
        // 059 review: the overlay's content and height are built once per change (Rebuild), never per OnGUI call.
        private readonly GUIContent _content = new GUIContent(string.Empty);
        private Rect _rect;

        /// <summary>Whether the clipboard is shown now.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>The clipboard's text now (the tours log it).</summary>
        public string Text => _text;

        public static ClipboardView Find() => FindFirstObjectByType<ClipboardView>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (Application.isBatchMode || FindFirstObjectByType<ClipboardView>() != null) return;
            var go = new GameObject("Clipboard");
            DontDestroyOnLoad(go);
            go.AddComponent<ClipboardView>();
        }

        /// <summary>Opens or closes the clipboard (the key; the tours call it, their input is scripted).</summary>
        public void Toggle()
        {
            IsOpen = !IsOpen;
            if (IsOpen) _shown = default; // rebuild at once
        }

        private PickupInteractor Me => _bootstrap != null ? UI.LocalPlayer.Find(_bootstrap.NetworkManager) : null;

        private void Update()
        {
            if (Time.unscaledTime >= _nextLookup)
            {
                _nextLookup = Time.unscaledTime + 1f;
                if (_bootstrap == null) _bootstrap = FindFirstObjectByType<NetBootstrap>();
                if (_input == null) _input = FindFirstObjectByType<PlayerInputReader>();
                if (_stage == null) _stage = ShopStage.Find();
                if (_board == null)
                {
                    Station s = Worktown.Game.Layout.LayoutRegistry.First(StationKind.MenuBoard);
                    _board = s != null ? s.GetComponent<MenuBoard>() : null;
                }
                if (_office == null) _office = OfficeTerminal.Find();
            }
            if (_input != null && _input.ConsumeClipboard()) Toggle();
            if (IsOpen && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) IsOpen = false;
            if (IsOpen) Rebuild();
        }

        private void Rebuild()
        {
            PickupInteractor me = Me;
            long balance = 0L;
            bool hasWallet = me != null && me.TryGetComponent(out Wallet wallet) && wallet.TryGetBalance(out balance);
            byte stage = _stage != null ? _stage.StageNow : ShopStageRules.OpenKitchen;
            byte owned = _stage != null ? _stage.RecipesOwned.Value : (byte)0;
            byte menuNow = _stage != null ? _stage.MenuNow.Value : (byte)0;
            long goal = _stage != null ? _stage.GoalMoney.Value : 0L;
            var shown = (_office != null ? _office.Version : 0, _board != null ? _board.Version : 0, stage, owned, menuNow, balance, goal,
                _stage != null && _stage.GoalReached.Value, _stage != null ? _stage.WindowCount.Value : 0, _stage != null ? _stage.RentOwed.Value : 0L);
            if (shown.Equals(_shown) && _text.Length > 0) return;
            _shown = shown;
            _prices.Clear();
            if (_board != null) for (int i = 0; i < _board.Prices.Count; i++) _prices.Add(_board.Prices[i]);
            _history.Clear();
            if (_office != null) for (int i = 0; i < _office.History.Count; i++) _history.Add(_office.History[i]);
            string goalLine = hasWallet && _stage != null
                ? UI.HudView.GoalText(stage, balance, goal, _stage.GoalReached.Value, _stage.WindowAverageTenths.Value, _stage.WindowCount.Value,
                    _stage.RentOwed.Value)
                : string.Empty;
            List<string> lines = ClipboardRules.Lines(stage, FoodCornerRules.DefaultClimb, owned, menuNow, _prices,
                _office != null ? _office.SpecialNow : Menu.None, goalLine, _history);
            lines.Add(string.Empty);
            lines.Add(ClipboardRules.CloseHint(PlayerInputReader.ClipboardKey.ToString()));
            _text = ResultsRules.Join(lines);
            EnsurePaper();
            _content.text = ClipboardRules.Title + "\n\n" + _text;
            float height = Mathf.Min(Screen.height - 2f * Margin, _paper.CalcHeight(_content, Width) + 4f);
            _rect = new Rect(Screen.width - Width - Margin, Margin + 60f, Width, height);
        }

        /// <summary>The paper style (its own font and background, so it needs no OnGUI skin).</summary>
        private void EnsurePaper()
        {
            if (_paper != null) return;
            _paperTexture = new Texture2D(1, 1);
            _paperTexture.SetPixel(0, 0, Palette.Cream);
            _paperTexture.Apply();
            _paper = new GUIStyle
            {
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),
                alignment = TextAnchor.UpperLeft,
                fontSize = 15,
                wordWrap = true,
                padding = new RectOffset(14, 14, 12, 12),
            };
            _paper.normal.background = _paperTexture;
            _paper.normal.textColor = Palette.Ink;
        }

        private void OnGUI()
        {
            if (!IsOpen || _text.Length == 0 || _paper == null || Me == null) return;
            GUI.Box(_rect, _content, _paper); // built in Rebuild (059 review: no allocation or layout per OnGUI call)
        }

        private void OnDestroy()
        {
            if (_paperTexture != null) Destroy(_paperTexture);
        }
    }
}
