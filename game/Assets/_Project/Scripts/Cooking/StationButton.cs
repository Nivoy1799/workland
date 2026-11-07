using UnityEngine;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 019: marks a small collider on a station (the stove's switch or the pan's mode button) as a button.
    /// Looking at it in first person and pressing E sends the station's option, the same request the spec 016
    /// digit keys send (spec 021: per burner, option StationRules.StoveOption). Data only; the server decides
    /// (KitchenState.TryUseBurner).
    /// </summary>
    public sealed class StationButton : MonoBehaviour
    {
        [SerializeField] private Station _station;
        [Tooltip("StationRules.StoveSwitch or StationRules.PanMode.")]
        [SerializeField] private int _option;

        public Station Station => _station;
        public int Option => _option;
    }
}
