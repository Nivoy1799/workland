using System;
using System.Collections.Generic;
using UnityEngine;
using Worktown.Game.Climb;
using Worktown.Game.Economy;
using Worktown.Sim.Climb;
using Worktown.Sim.Economy;

namespace Worktown.Game.UI
{
    /// <summary>
    /// Spec 057, client: draws the goal board's rows (one text line per row, GoalBoardRules.RowText) from the synced ShopStage fields, the
    /// one shop balance (ShopLedger) and the crew size measured at the last service start (D40: the recipe costs follow it). Redraws only
    /// when one of them changed. Holds no rule: the server decides every press.
    /// </summary>
    public sealed class GoalBoardView : MonoBehaviour
    {
        [SerializeField] private TextMesh[] _rows = new TextMesh[0];

        private ShopStage _stage;
        private ShopLedger _ledger;
        private float _nextLookup;
        private (byte Stage, long Balance, long Goal, bool Pending, byte Owned, byte Next, byte Now, long Owed, int Average, int Count, byte Crew,
            bool Reached) _shown = (byte.MaxValue, -1, -1, false, 0, 0, 0, -1, -1, -1, 0, false);
        private readonly EconomyCatalog _economy = EconomyCatalog.CreateDefault();

        /// <summary>Every row's text, top to bottom (the tours log it).</summary>
        public string DisplayText
        {
            get
            {
                var lines = new List<string>();
                foreach (TextMesh row in _rows) if (row != null && row.text.Length > 0) lines.Add(row.text);
                return string.Join("\n", lines);
            }
        }

        private void Update()
        {
            if (_rows == null || _rows.Length == 0) return;
            if ((_stage == null || _ledger == null) && Time.unscaledTime >= _nextLookup)
            {
                _nextLookup = Time.unscaledTime + 1f;
                if (_stage == null) _stage = ShopStage.Find();
                if (_ledger == null) _ledger = FindFirstObjectByType<ShopLedger>();
            }
            if (_stage == null) return;
            long balance = _ledger != null && _ledger.TryGet(out long b) ? b : 0L;
            var shown = (_stage.StageNow, balance, _stage.GoalMoney.Value, _stage.MovePending.Value, _stage.RecipesOwned.Value, _stage.MenuNext.Value,
                _stage.MenuNow.Value, _stage.RentOwed.Value, (int)_stage.WindowAverageTenths.Value, (int)_stage.WindowCount.Value, _stage.CrewPlayers.Value,
                _stage.GoalReached.Value);
            if (shown == _shown) return;
            _shown = shown;
            string[] texts = Texts(shown.Item1, balance, shown.Item3, shown.Item4, shown.Item5, shown.Item6, shown.Item7, shown.Item8, shown.Item9,
                shown.Item10, shown.Item11, shown.Item12, _economy, FoodCornerRules.DefaultClimb);
            for (int i = 0; i < _rows.Length; i++)
                if (_rows[i] != null) _rows[i].text = i < texts.Length ? texts[i] : string.Empty;
        }

        /// <summary>Every row's text for these synced values (pure; tested).</summary>
        public static string[] Texts(byte stage, long balance, long goal, bool movePending, byte owned, byte menuNext, byte menuNow, long owed,
            int averageTenths, int count, int crew, bool reached, EconomyCatalog economy, ClimbCatalog climb)
        {
            IReadOnlyList<PurchaseDef> recipes = RestaurantRules.Recipes(climb);
            CrewFactors factors = RestaurantRules.Crew(climb, Math.Max(1, crew));
            var state = new GoalBoardRules.State(stage, balance, goal, movePending, owned, menuNext, owed, RestaurantRules.RentReserve(economy, climb));
            string gate = reached ? GateRules.ReachedText
                : GateRules.GateLine(averageTenths, count, RestaurantRules.GateWindow(climb), RestaurantRules.GateStarsTenths(climb), balance, goal);
            if (owed > 0) gate += "\n" + RentRules.OwedText(owed);
            var texts = new string[GoalBoardRules.RowCount];
            for (int row = 0; row < texts.Length; row++)
                texts[row] = GoalBoardRules.RowText(state, row, recipes, r => RestaurantRules.RecipeCost(recipes[r].Id, economy, climb, factors), menuNow,
                    gate, count == 0);
            return texts;
        }
    }
}
