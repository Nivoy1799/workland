using System.Collections.Generic;
using UnityEngine;
using Worktown.Game.Climb;

namespace Worktown.Game.Shop
{
    /// <summary>
    /// Spec 037, client: draws the menu board's text from the synced <see cref="MenuBoard"/> (prices in cents, the selected line) and the
    /// shop's stage (which lines are on sale), through <see cref="MenuBoardRules.DisplayText(byte, IReadOnlyList{int}, int, byte)"/>. Spec 059: the
    /// special of the day (the office terminal's synced Special) is tagged on its line. Redraws only when something moved.
    /// </summary>
    public sealed class MenuBoardView : MonoBehaviour
    {
        [SerializeField] private MenuBoard _board;
        [SerializeField] private TextMesh _text;

        private readonly List<int> _prices = new List<int>();
        private int _version = -1;
        private int _stage = -1;
        private int _special = -1;
        private ShopStage _shopStage;
        private bool _stageLooked;
        private Worktown.Game.Office.OfficeTerminal _office;
        private float _nextOfficeLookup;

        /// <summary>The text the board shows now (the tours log it).</summary>
        public string DisplayText => _text != null ? _text.text : string.Empty;

        private void Update()
        {
            if (_board == null || _text == null) return;
            if (!_stageLooked)
            {
                _stageLooked = true;
                _shopStage = ShopStage.Find();
            }
            if (_office == null && Time.unscaledTime >= _nextOfficeLookup)
            {
                _nextOfficeLookup = Time.unscaledTime + 1f;
                _office = Worktown.Game.Office.OfficeTerminal.Find();
            }
            byte stage = _shopStage != null ? _shopStage.StageNow : ShopStageRules.OpenKitchen;
            byte special = _office != null ? _office.SpecialNow : Worktown.Game.Npc.Menu.None;
            if (_board.Version == _version && stage == _stage && special == _special) return;
            _version = _board.Version;
            _stage = stage;
            _special = special;
            _prices.Clear();
            for (int i = 0; i < _board.Prices.Count; i++) _prices.Add(_board.Prices[i]);
            _text.text = MenuBoardRules.DisplayText(stage, _prices.Count > 0 ? _prices : null, _board.Selected.Value, special);
        }
    }
}
