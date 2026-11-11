using System.Collections.Generic;
using UnityEngine;
using Worktown.Game.Climb;
using Worktown.Game.Cooking;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 053: a cloth and a small sign card ("Opens with your restaurant"; follow-up 2026-11-11 evening: one fitted Sage cloth over
    /// the station and what rests on it, ClosedCoverRules) over a station that is closed at the shop's stage
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

        /// <summary>Spec 053: the bounds of the station's own renderers (not its labels, particles or cover).</summary>
        public static bool TryStationBounds(Station station, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (Renderer r in station.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r.GetComponent<TextMesh>() != null || r.GetComponentInParent<ClosedCover>() != null) continue;
                if (!any) bounds = r.bounds;
                else bounds.Encapsulate(r.bounds);
                any = true;
            }
            return any;
        }

        /// <summary>
        /// Follow-up 2026-11-11 evening (spec 053): what rests on a station (a pan on the stove, a pot, a basket): scene renderers outside
        /// it, not a label, a particle, a cover or another station's part, whose bounds rest on it (ClosedCoverRules.RestsOn).
        /// </summary>
        public static bool TryRestingBounds(Station station, Bounds stationBounds, IEnumerable<Renderer> renderers, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (Renderer r in renderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || r.transform.IsChildOf(station.transform)) continue;
                if (r is ParticleSystemRenderer || r.GetComponent<TextMesh>() != null) continue;
                if (r.GetComponentInParent<ClosedCover>() != null || r.GetComponentInParent<Station>() != null) continue;
                if (!ClosedCoverRules.RestsOn(stationBounds, r.bounds)) continue;
                if (!any) bounds = r.bounds;
                else bounds.Encapsulate(r.bounds);
                any = true;
            }
            return any;
        }
    }
}
