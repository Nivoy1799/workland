using UnityEngine;
using Worktown.Game.Climb;
using Worktown.Game.Cooking;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 053: a graybox cloth and a small sign ("Opens with your restaurant") over a station that is closed at the shop's stage
    /// (ShopStageRules.IsStationOpen). Built by the graybox scene builder (palette colours only, D32), no collider: the station under it
    /// still answers the aim with its closed prompt. Client presentation only: it reads the synced ShopStage twice a second.
    /// </summary>
    public sealed class ClosedCover : MonoBehaviour
    {
        public const float CheckSeconds = 0.5f;

        [SerializeField] private StationKind _kind;
        [Tooltip("The cloth and the sign; shown while the station is closed.")]
        [SerializeField] private GameObject _cover;

        private ShopStage _stage;
        private float _nextCheck;
        private float _nextLookup;

        public StationKind Kind => _kind;
        public bool Showing => _cover != null && _cover.activeSelf;

        private void Awake()
        {
            if (_cover != null) _cover.SetActive(false); // the open kitchen until a stage says otherwise
        }

        private void Update()
        {
            if (_cover == null || Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + CheckSeconds;
            if (_stage == null && Time.unscaledTime >= _nextLookup)
            {
                _nextLookup = Time.unscaledTime + 2f;
                _stage = ShopStage.Find();
            }
            byte stage = _stage != null && (_stage.IsServerInitialized || _stage.IsClientInitialized) ? _stage.StageNow : ShopStageRules.OpenKitchen;
            bool closed = !ShopStageRules.IsStationOpen(stage, _kind);
            if (_cover.activeSelf != closed) _cover.SetActive(closed);
        }
    }
}
