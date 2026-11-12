using System;
using System.Collections.Generic;

namespace Worktown.Game.Dev
{
    /// <summary>
    /// Spec 054 (B4, the feel guard), pure, no UnityEngine types: how other players are drawn on one client, and how often the own
    /// player is corrected.
    /// Remote players: every frame the drawn position of each (<see cref="AddFrame"/>). Over the frames where it moves (its smoothed
    /// speed above <see cref="MovingSpeed"/>) the jitter of a frame is |drawn speed - smoothed speed| / <see cref="ReferenceSpeed"/>
    /// (horizontal; the smoothing an exponential average over <see cref="SmoothSeconds"/>); <see cref="TakeSecond"/> gives the
    /// window's p95. A teleport is a frame whose drawn jump is over <see cref="TeleportMeters"/> and faster than
    /// <see cref="TeleportSpeed"/> (so a long frame of a busy bot is not one), outside the first <see cref="SpawnGraceSeconds"/> of
    /// a track (a spawn, or a player coming into view).
    /// Owner corrections: the owner's predicted position per tick in a small ring (<see cref="RecordPredicted"/>); a reconcile whose
    /// state is more than <see cref="CorrectionMeters"/> from it is a correction (<see cref="CheckReconcile"/>).
    /// </summary>
    public sealed class RemoteMotionStats
    {
        public const float ReferenceSpeed = 2.5f;
        public const float SmoothSeconds = 0.15f;
        public const float MovingSpeed = 0.2f;
        public const float TeleportMeters = 0.5f;
        /// <summary>m/s: twice the sprint speed (MovementRules.SprintSpeed 5 m/s).</summary>
        public const float TeleportSpeed = 10f;
        public const float SpawnGraceSeconds = 1f;
        /// <summary>s: a frame longer than this (a stall) gives no jitter sample; the track goes on.</summary>
        public const float MaxFrameSeconds = 0.25f;
        public const float CorrectionMeters = 0.05f;
        public const int RingTicks = 128;

        private sealed class Track
        {
            public float X, Y, Z;
            public float Smoothed;
            public float Age;
            public int SeenFrame;
        }

        private readonly Dictionary<int, Track> _tracks = new Dictionary<int, Track>();
        private readonly List<int> _gone = new List<int>();
        private readonly List<float> _jitter = new List<float>();
        private int _frame;
        private int _teleports;

        private readonly uint[] _ringTicks = new uint[RingTicks];
        private readonly float[] _ringX = new float[RingTicks], _ringY = new float[RingTicks], _ringZ = new float[RingTicks];
        private readonly bool[] _ringSet = new bool[RingTicks];
        private int _corrections;
        private int _reconciles;

        /// <summary>
        /// Spec 054 (follow-up): the size of each correction in cm, counted in bins with these upper edges (the last bin is everything
        /// above the last edge). The metrics line writes one field per non-empty bin (<see cref="BinName"/>), so the load-test summary
        /// can give the run's p50 and p95 over all bots; the window's largest correction is exact (<see cref="TakeCorrectionSizes"/>).
        /// </summary>
        public static readonly System.Collections.Generic.IReadOnlyList<float> CorrectionBinEdgesCm =
            System.Array.AsReadOnly(new[] { 6f, 8f, 10f, 13f, 16f, 20f, 25f, 32f, 40f, 50f, 65f, 80f, 100f, 150f }); // 054 review: read-only
        private readonly int[] _sizeBins = new int[CorrectionBinEdgesCm.Count + 1];
        private float _sizeMaxCm;

        /// <summary>The field name of bin <paramref name="bin"/>: owner_corr_cm_le_10 (up to 10 cm), the last owner_corr_cm_gt_150.</summary>
        public static string BinName(int bin) => bin < CorrectionBinEdgesCm.Count
            ? "owner_corr_cm_le_" + CorrectionBinEdgesCm[bin].ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "owner_corr_cm_gt_" + CorrectionBinEdgesCm[CorrectionBinEdgesCm.Count - 1].ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The bin of a correction of <paramref name="cm"/>.</summary>
        public static int BinOf(float cm)
        {
            for (int i = 0; i < CorrectionBinEdgesCm.Count; i++)
                if (cm <= CorrectionBinEdgesCm[i]) return i;
            return CorrectionBinEdgesCm.Count;
        }

        /// <summary>The window's correction sizes (counts per bin into <paramref name="bins"/>, which must have
        /// <see cref="CorrectionBinEdgesCm"/>.Length + 1 entries) and the largest in cm (0 without one), then a new window.</summary>
        public void TakeCorrectionSizes(int[] bins, out float maxCm)
        {
            for (int i = 0; i < _sizeBins.Length; i++)
            {
                if (bins != null && i < bins.Length) bins[i] = _sizeBins[i];
                _sizeBins[i] = 0;
            }
            maxCm = _sizeMaxCm;
            _sizeMaxCm = 0f;
        }

        /// <summary>
        /// Spec 054b: the correction smoothing (PlayerController.DevOwnerSmoothed). A shift is a tick whose reconcile and replay moved the
        /// own current position by more than <see cref="CorrectionMeters"/>; it is binned by its length (before smoothing) and by how far
        /// the drawn body moves in the next tick because of it (after smoothing: CorrectionBlend). Bins with these upper edges (cm), the
        /// last bin is everything above the last edge; field names <c>owner_shift_cm_le_*</c> and <c>owner_vis_cm_le_*</c>.
        /// </summary>
        public static readonly IReadOnlyList<float> SmoothBinEdgesCm =
            Array.AsReadOnly(new[] { 0.5f, 1f, 2f, 3f, 4f, 5f, 6f, 8f, 10f, 13f, 16f, 20f, 25f, 32f, 40f, 50f, 65f, 80f, 100f, 150f });
        private readonly int[] _shiftBins = new int[SmoothBinEdgesCm.Count + 1];
        private readonly int[] _visibleBins = new int[SmoothBinEdgesCm.Count + 1];
        private int _shifts;
        private int _visibleOver;
        private float _shiftMaxCm;
        private float _visibleMaxCm;

        /// <summary>The field name of a smoothing bin: <paramref name="prefix"/> (owner_shift or owner_vis) + _cm_le_EDGE or _cm_gt_LAST.</summary>
        public static string SmoothBinName(string prefix, int bin) => bin < SmoothBinEdgesCm.Count
            ? prefix + "_cm_le_" + SmoothBinEdgesCm[bin].ToString(System.Globalization.CultureInfo.InvariantCulture)
            : prefix + "_cm_gt_" + SmoothBinEdgesCm[SmoothBinEdgesCm.Count - 1].ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The smoothing bin of <paramref name="cm"/>.</summary>
        public static int SmoothBinOf(float cm)
        {
            for (int i = 0; i < SmoothBinEdgesCm.Count; i++)
                if (cm <= SmoothBinEdgesCm[i]) return i;
            return SmoothBinEdgesCm.Count;
        }

        /// <summary>A reconcile moved the own position by <paramref name="shiftMeters"/>; the drawn body moves <paramref name="visibleMeters"/>
        /// in the next tick because of it. Counted as a shift above <see cref="CorrectionMeters"/>; any tick whose visible move is above
        /// <see cref="CorrectionMeters"/> counts as a visible correction.</summary>
        public void RecordSmoothed(float shiftMeters, float visibleMeters)
        {
            if (visibleMeters > CorrectionMeters) _visibleOver++;
            if (shiftMeters <= CorrectionMeters) return;
            _shifts++;
            float shiftCm = shiftMeters * 100f, visibleCm = visibleMeters * 100f;
            _shiftBins[SmoothBinOf(shiftCm)]++;
            _visibleBins[SmoothBinOf(visibleCm)]++;
            if (shiftCm > _shiftMaxCm) _shiftMaxCm = shiftCm;
            if (visibleCm > _visibleMaxCm) _visibleMaxCm = visibleCm;
        }

        /// <summary>The window's smoothing numbers (bins into arrays of <see cref="SmoothBinEdgesCm"/>.Count + 1), then a new window.</summary>
        public void TakeSmoothing(int[] shiftBins, int[] visibleBins, out int shifts, out int visibleOver, out float shiftMaxCm, out float visibleMaxCm)
        {
            for (int i = 0; i < _shiftBins.Length; i++)
            {
                if (shiftBins != null && i < shiftBins.Length) shiftBins[i] = _shiftBins[i];
                if (visibleBins != null && i < visibleBins.Length) visibleBins[i] = _visibleBins[i];
                _shiftBins[i] = 0;
                _visibleBins[i] = 0;
            }
            shifts = _shifts;
            visibleOver = _visibleOver;
            shiftMaxCm = _shiftMaxCm;
            visibleMaxCm = _visibleMaxCm;
            _shifts = 0;
            _visibleOver = 0;
            _shiftMaxCm = 0f;
            _visibleMaxCm = 0f;
        }

        /// <summary>Remote players tracked now.</summary>
        public int Tracked => _tracks.Count;

        /// <summary>Starts a frame: tracks not given a position in the frame before are dropped (despawned or out of view).</summary>
        public void BeginFrame()
        {
            _gone.Clear();
            foreach (KeyValuePair<int, Track> t in _tracks)
                if (t.Value.SeenFrame != _frame) _gone.Add(t.Key);
            foreach (int id in _gone) _tracks.Remove(id);
            _frame++;
        }

        /// <summary>The drawn position of remote player <paramref name="id"/> this frame, <paramref name="dt"/> s after the last one.</summary>
        public void AddFrame(int id, float x, float y, float z, float dt)
        {
            if (!_tracks.TryGetValue(id, out Track t))
            {
                _tracks[id] = new Track { X = x, Y = y, Z = z, SeenFrame = _frame };
                return;
            }
            t.SeenFrame = _frame;
            float dx = x - t.X, dy = y - t.Y, dz = z - t.Z;
            t.X = x;
            t.Y = y;
            t.Z = z;
            if (!(dt > 0f)) return;
            t.Age += dt;
            float jump = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (t.Age > SpawnGraceSeconds && IsTeleport(jump, dt)) _teleports++;
            if (dt > MaxFrameSeconds) return;
            float speed = (float)Math.Sqrt(dx * dx + dz * dz) / dt;
            t.Smoothed += (speed - t.Smoothed) * (1f - (float)Math.Exp(-dt / SmoothSeconds));
            if (t.Smoothed > MovingSpeed) _jitter.Add(Math.Abs(speed - t.Smoothed) / ReferenceSpeed);
        }

        /// <summary>Whether a drawn jump of <paramref name="meters"/> in <paramref name="dt"/> s is a teleport.</summary>
        public static bool IsTeleport(float meters, float dt) => meters > TeleportMeters && (dt <= 0f || meters / dt > TeleportSpeed);

        /// <summary>The owner's predicted position after tick <paramref name="tick"/>.</summary>
        public void RecordPredicted(uint tick, float x, float y, float z)
        {
            int i = (int)(tick % RingTicks);
            _ringTicks[i] = tick;
            _ringX[i] = x;
            _ringY[i] = y;
            _ringZ[i] = z;
            _ringSet[i] = true;
        }

        /// <summary>A reconcile of tick <paramref name="tick"/> to the server's position; true when it corrects the prediction (more than
        /// <see cref="CorrectionMeters"/>). A tick no longer (or never) in the ring is not counted.</summary>
        public bool CheckReconcile(uint tick, float x, float y, float z)
        {
            int i = (int)(tick % RingTicks);
            if (!_ringSet[i] || _ringTicks[i] != tick) return false;
            _reconciles++;
            float dx = x - _ringX[i], dy = y - _ringY[i], dz = z - _ringZ[i];
            bool corrected = dx * dx + dy * dy + dz * dz > CorrectionMeters * CorrectionMeters;
            if (corrected)
            {
                _corrections++;
                float cm = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz) * 100f;
                _sizeBins[BinOf(cm)]++;
                if (cm > _sizeMaxCm) _sizeMaxCm = cm;
            }
            return corrected;
        }

        /// <summary>The window's numbers, then a new window: jitter p95 (0 without moving frames), the moving frames, teleports,
        /// owner corrections and the reconciles compared.</summary>
        public void TakeSecond(out float jitterP95, out int movingFrames, out int teleports, out int corrections, out int reconciles)
        {
            jitterP95 = LoadStats.Percentile(_jitter, 95f);
            movingFrames = _jitter.Count;
            teleports = _teleports;
            corrections = _corrections;
            reconciles = _reconciles;
            _jitter.Clear();
            _teleports = 0;
            _corrections = 0;
            _reconciles = 0;
        }
    }
}
