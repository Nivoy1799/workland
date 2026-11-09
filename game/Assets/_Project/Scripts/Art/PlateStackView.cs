using UnityEngine;
using Worktown.Game.Cooking;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 035: the plate shelf's stack shows its clean plates (the shelf's synced byte, CleaningService): fewer plates as they run
    /// out, none when every plate is in use or dirty. Without the byte (no cleaning service) the whole stack shows, as before.
    /// </summary>
    public sealed class PlateStackView : MonoBehaviour
    {
        [SerializeField] private Station _shelf;
        [SerializeField] private Transform[] _plates = new Transform[0];

        private StationContents _contents;
        private int _shown = -1;

        private void Awake()
        {
            if (_shelf == null) _shelf = GetComponentInParent<Station>();
            if (_shelf != null) _contents = _shelf.GetComponent<StationContents>();
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) enabled = false;
        }

        private void LateUpdate()
        {
            int count = _contents != null && _contents.IsSpawned ? _contents.StoveFlags.Value : _plates.Length;
            if (count == _shown) return;
            _shown = count;
            for (int i = 0; i < _plates.Length; i++)
                if (_plates[i] != null) _plates[i].gameObject.SetActive(i < count);
        }
    }
}
