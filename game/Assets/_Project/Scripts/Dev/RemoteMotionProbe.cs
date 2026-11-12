#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using Worktown.Game.Net;

namespace Worktown.Game.Dev
{
    /// <summary>
    /// Spec 054 (B4, the feel guard), development builds, a load-test bot (<c>-bot -metrics</c>): every frame the drawn position of
    /// every other player's graphic (what this client would show: the root moved by the NetworkTransform, or before spec 054 by the
    /// forwarded prediction, then the tick smoother) into <see cref="RemoteMotionStats"/>; and the own player's predicted position per
    /// tick against each reconcile from the server (owner corrections). The player list is looked up once a second, not every frame.
    /// Reads only; the owner's prediction is unchanged (PlayerController raises two dev-only events).
    /// </summary>
    public sealed class RemoteMotionProbe
    {
        private const float LookupSeconds = 1f;

        private readonly RemoteMotionStats _stats = new RemoteMotionStats();
        private readonly List<PlayerController> _remote = new List<PlayerController>();
        private PlayerController _own;
        private float _nextLookup;

        /// <summary>One frame: <paramref name="now"/> and <paramref name="dt"/> in unscaled seconds.</summary>
        public void Frame(float now, float dt)
        {
            if (now >= _nextLookup)
            {
                _nextLookup = now + LookupSeconds;
                Lookup();
            }
            _stats.BeginFrame();
            for (int i = 0; i < _remote.Count; i++)
            {
                PlayerController p = _remote[i];
                if (p == null || !p.IsSpawned || p.IsOwner) continue;
                Vector3 drawn = p.Graphic.position;
                _stats.AddFrame(p.ObjectId, drawn.x, drawn.y, drawn.z, dt);
            }
        }

        /// <summary>The second's fields: remote_jitter_p95, remote_moving_frames, remote_teleports, remote_players, owner_corrections,
        /// owner_reconciles.</summary>
        public void AddFields(List<KeyValuePair<string, double>> fields)
        {
            _stats.TakeSecond(out float jitter, out int moving, out int teleports, out int corrections, out int reconciles);
            fields.Add(new KeyValuePair<string, double>("remote_jitter_p95", jitter));
            fields.Add(new KeyValuePair<string, double>("remote_moving_frames", moving));
            fields.Add(new KeyValuePair<string, double>("remote_teleports", teleports));
            fields.Add(new KeyValuePair<string, double>("remote_players", _stats.Tracked));
            fields.Add(new KeyValuePair<string, double>("owner_corrections", corrections));
            fields.Add(new KeyValuePair<string, double>("owner_reconciles", reconciles));
            // Spec 054 follow-up: the size of the corrections (bins written only when not empty, and the largest).
            _stats.TakeCorrectionSizes(_bins, out float maxCm);
            fields.Add(new KeyValuePair<string, double>("owner_corr_cm_max", maxCm));
            for (int i = 0; i < _bins.Length; i++)
                if (_bins[i] > 0) fields.Add(new KeyValuePair<string, double>(RemoteMotionStats.BinName(i), _bins[i]));
            // Spec 054b: the correction smoothing, before (shift) and after (vis); bins written only when not empty.
            _stats.TakeSmoothing(_shiftBins, _visibleBins, out int shifts, out int visibleOver, out float shiftMax, out float visibleMax);
            fields.Add(new KeyValuePair<string, double>("owner_shifts", shifts));
            fields.Add(new KeyValuePair<string, double>("owner_visible_corrections", visibleOver));
            fields.Add(new KeyValuePair<string, double>("owner_shift_cm_max", shiftMax));
            fields.Add(new KeyValuePair<string, double>("owner_vis_cm_max", visibleMax));
            for (int i = 0; i < _shiftBins.Length; i++)
            {
                if (_shiftBins[i] > 0) fields.Add(new KeyValuePair<string, double>(RemoteMotionStats.SmoothBinName("owner_shift", i), _shiftBins[i]));
                if (_visibleBins[i] > 0) fields.Add(new KeyValuePair<string, double>(RemoteMotionStats.SmoothBinName("owner_vis", i), _visibleBins[i]));
            }
        }

        private readonly int[] _bins = new int[RemoteMotionStats.CorrectionBinEdgesCm.Count + 1];
        private readonly int[] _shiftBins = new int[RemoteMotionStats.SmoothBinEdgesCm.Count + 1];
        private readonly int[] _visibleBins = new int[RemoteMotionStats.SmoothBinEdgesCm.Count + 1];

        public void Dispose() => Watch(null);

        private void Lookup()
        {
            _remote.Clear();
            PlayerController own = null;
            foreach (PlayerController p in Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            {
                if (p == null || !p.IsSpawned) continue;
                if (p.IsOwner) own = p;
                else _remote.Add(p);
            }
            if (own != _own) Watch(own);
        }

        private void Watch(PlayerController own)
        {
            if (_own != null)
            {
                _own.DevOwnerTicked -= OnOwnerTicked;
                _own.DevOwnerReconciled -= OnOwnerReconciled;
                _own.DevOwnerSmoothed -= OnOwnerSmoothed;
            }
            _own = own;
            if (_own != null)
            {
                _own.DevOwnerTicked += OnOwnerTicked;
                _own.DevOwnerReconciled += OnOwnerReconciled;
                _own.DevOwnerSmoothed += OnOwnerSmoothed;
            }
        }

        private void OnOwnerTicked(uint tick, Vector3 position) => _stats.RecordPredicted(tick, position.x, position.y, position.z);

        private void OnOwnerReconciled(uint tick, Vector3 position) => _stats.CheckReconcile(tick, position.x, position.y, position.z);

        private void OnOwnerSmoothed(float shiftMeters, float visibleMeters) => _stats.RecordSmoothed(shiftMeters, visibleMeters);
    }
}
#endif
