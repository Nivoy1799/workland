using UnityEngine;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// A kitchen station (spec 007), part of a layout piece (spec 062a), the same on server and clients; requests refer to it by
    /// <see cref="Id"/>, its piece's id. Holds no rules: StationRules decides.
    /// Spec 028b: a fridge or dry shelf lists its <see cref="StockSlot"/>s; slot n is station option n.
    /// </summary>
    public sealed class Station : MonoBehaviour
    {
        [SerializeField] private StationKind _kind;
        [SerializeField] private string _displayName = string.Empty;
        [Tooltip("Crates only: index into DemoIngredients.All.")]
        [SerializeField] private int _ingredientIndex;
        [Tooltip("Spec 028b, fridge and shelf: the slots; index in this array = the station option.")]
        [SerializeField] private StockSlot[] _slots = new StockSlot[0];

        private Layout.PieceBehaviour _piece;

        /// <summary>Spec 062a: the station's id is its piece's id (-1 outside a piece).</summary>
        public int Id
        {
            get
            {
                if (_piece == null) _piece = GetComponentInParent<Layout.PieceBehaviour>(true);
                return _piece != null && _piece.PieceId > 0 ? _piece.PieceId : -1;
            }
        }

        /// <summary>Spec 062a: the piece this station is part of (null outside one).</summary>
        public Layout.PieceBehaviour Piece => _piece != null ? _piece : _piece = GetComponentInParent<Layout.PieceBehaviour>(true);
        public StationKind Kind => _kind;
        public string DisplayName => _displayName;
        public int IngredientIndex => _ingredientIndex;
        public int SlotCount => _slots != null ? _slots.Length : 0;

        /// <summary>Spec 048 (review): with the bound Use and view keys; rebuilt only when the settings change.</summary>
        public string Prompt
        {
            get
            {
                if (_prompt == null || _promptVersion != Settings.GameSettings.Version)
                {
                    Settings.ControlBindings keys = Settings.GameSettings.CurrentBindings;
                    _prompt = StationRules.Prompt(_kind, _displayName, Settings.KeyPrompts.KeyName(keys, Settings.ControlAction.Use),
                        Settings.KeyPrompts.KeyName(keys, Settings.ControlAction.ToggleView));
                    _promptVersion = Settings.GameSettings.Version;
                }
                return _prompt;
            }
        }

        private string _prompt;
        private int _promptVersion;

        /// <summary>Spec 062a: a numbered piece's name ("Stove 2"), set by its piece.</summary>
        internal void SetDisplayName(string displayName)
        {
            _displayName = displayName ?? string.Empty;
            _prompt = null;
        }

        /// <summary>Spec 028b: slot <paramref name="option"/> of this station; false for any other number or a missing slot.</summary>
        public bool TryGetSlot(int option, out StockSlot slot)
        {
            slot = null;
            if (_slots == null || option < 0 || option >= _slots.Length) return false;
            slot = _slots[option];
            return slot != null;
        }

        /// <summary>Spec 028b: the slot whose anchor is nearest to <paramref name="point"/> (a ray hit on the body), or null.</summary>
        public StockSlot NearestSlot(Vector3 point)
        {
            StockSlot best = null;
            float bestSqr = float.MaxValue;
            if (_slots == null) return null;
            foreach (StockSlot slot in _slots)
            {
                if (slot == null) continue;
                float sqr = (slot.Anchor - point).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                best = slot;
            }
            return best;
        }
    }
}
