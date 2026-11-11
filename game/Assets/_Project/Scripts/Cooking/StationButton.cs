using UnityEngine;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 019: marks a small collider on a station (the stove's switch or the pan's mode button) as a button.
    /// Looking at it in first person and pressing E sends the station's option, the same request the spec 016
    /// digit keys send (spec 021: per burner, option StationRules.StoveOption). Data only; the server decides
    /// (KitchenState.TryUseBurner).
    /// Spec 044: with graphics, a <see cref="Worktown.Game.Art.ButtonPressView"/> makes it sink when pressed.
    /// </summary>
    public sealed class StationButton : MonoBehaviour
    {
        [SerializeField] private Station _station;
        [Tooltip("StationRules.StoveSwitch or StationRules.PanMode.")]
        [SerializeField] private int _option;

        public Station Station => _station;
        public int Option => _option;

        private void Awake()
        {
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            if (!TryGetComponent(out Worktown.Game.Art.ButtonPressView _)) gameObject.AddComponent<Worktown.Game.Art.ButtonPressView>();
        }

        /// <summary>Spec 044: the presser's own click: the button sinks at once (no view without graphics).</summary>
        public void ShowPress()
        {
            if (TryGetComponent(out Worktown.Game.Art.ButtonPressView view)) view.PressNow();
        }
    }
}
