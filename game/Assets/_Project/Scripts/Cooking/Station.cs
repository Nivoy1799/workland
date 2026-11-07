using UnityEngine;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// A kitchen station in the scene (spec 007). Static scene object, the same on server and
    /// clients; requests refer to it by <see cref="Index"/>. Holds no rules: StationRules decides.
    /// </summary>
    public sealed class Station : MonoBehaviour
    {
        [SerializeField] private int _index;
        [SerializeField] private StationKind _kind;
        [SerializeField] private string _displayName = string.Empty;
        [Tooltip("Crates only: index into DemoIngredients.All.")]
        [SerializeField] private int _ingredientIndex;

        public int Index => _index;
        public StationKind Kind => _kind;
        public string DisplayName => _displayName;
        public int IngredientIndex => _ingredientIndex;

        public string Prompt => StationRules.Prompt(_kind, _displayName);
    }
}
