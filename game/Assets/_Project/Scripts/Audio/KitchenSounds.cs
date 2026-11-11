using System.Collections.Generic;
using UnityEngine;
using Worktown.Game.Art;
using Worktown.Game.Cooking;
using Worktown.Game.Net;

namespace Worktown.Game.Audio
{
    /// <summary>
    /// Spec 023: client-only kitchen sounds. Reacts to what the client already knows and plays nothing over the network:
    /// a chop when a cutting board's synced contents change while it holds something, a soft tick when a stove knob
    /// or mode changes (spec 027b: the local player's pick, place, nope and button sounds come through <see cref="Play"/>), a sizzle loop on a hot pan and
    /// a bubble loop on a hot pot (<see cref="VesselSteam.IsHot"/>). Sounds are synthesised by
    /// <see cref="ProceduralSounds"/>; the headless server removes itself in Awake.
    /// Spec 027c: the loops follow the heat (level and pitch from the vessel's synced zone, <see cref="SoundRules.LoopLevel"/>)
    /// and the Perfect, Warning and Burnt cues play once per zone entry (<see cref="SoundRules.CueFor"/>, with a cooldown).
    /// </summary>
    public sealed class KitchenSounds : MonoBehaviour
    {
        private const int OneShotSources = 6;
        private const float PollInterval = 0.1f;
        private const float RefreshInterval = 1f;
        private const float LoopFadePerSecond = 3f;
        private const float MinDistance = 1.5f;
        private const float MaxDistance = 12f;

        private sealed class LoopState
        {
            public AudioSource Source;
            public float Level;
        }

        private sealed class HeatState
        {
            public HeatZone Zone;
            public bool Warn;
            public float LastPerfect = float.NegativeInfinity;
            public float LastWarning = float.NegativeInfinity;
            public float LastBurnt = float.NegativeInfinity;
        }

        private sealed class BoardState
        {
            public int Count;
            public int Hash;
        }

        private SoundCatalog _catalog;
        private AudioSource[] _oneShots;
        private int _nextOneShot;
        private float _nextPoll;
        private float _nextRefresh;

        private VesselSteam[] _hotCandidates = new VesselSteam[0];
        private Vessel[] _boards = new Vessel[0];
        private StationContents[] _stoves = new StationContents[0];
        private StationContents[] _fryers = new StationContents[0]; // spec 029b: the idle oil hum
        private readonly Dictionary<StationContents, LoopState> _hums = new Dictionary<StationContents, LoopState>();
        private bool _initialised;

        private readonly Dictionary<VesselSteam, LoopState> _loops = new Dictionary<VesselSteam, LoopState>();
        private readonly Dictionary<Vessel, BoardState> _boardStates = new Dictionary<Vessel, BoardState>();
        private readonly Dictionary<StationContents, byte> _stoveFlags = new Dictionary<StationContents, byte>();
        private readonly Dictionary<VesselSteam, HeatState> _heatStates = new Dictionary<VesselSteam, HeatState>();

        // Spec 044: the scene's sounds, for views without a reference (another player's put-down, a flight's landing). Client only.
        private static KitchenSounds _active;

        /// <summary>Spec 044: plays a one-shot on the scene's kitchen sounds, if there are any (none on a headless server).</summary>
        public static void PlayOnce(SoundKey key, Vector3 position)
        {
            if (_active != null) _active.Play(key, position);
        }

        private void OnDestroy()
        {
            if (_active == this) _active = null;
        }

        private void Awake()
        {
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Destroy(this);
                return;
            }
            _active = this;
            _catalog = new SoundCatalog();
            _oneShots = new AudioSource[OneShotSources];
            for (int i = 0; i < _oneShots.Length; i++) _oneShots[i] = CreateSource("OneShot " + i, loop: false);
        }

        private AudioSource CreateSource(string sourceName, bool loop)
        {
            var go = new GameObject(sourceName);
            go.transform.SetParent(transform, false);
            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = MinDistance;
            source.maxDistance = MaxDistance;
            return source;
        }

        private void Update()
        {
            if (_catalog == null) return;
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + RefreshInterval;
                Refresh();
            }
            if (Time.unscaledTime >= _nextPoll)
            {
                _nextPoll = Time.unscaledTime + PollInterval;
                PollEvents();
            }
            UpdateLoops(Time.unscaledDeltaTime);
        }

        /// <summary>Looks up the things that exist (spawned cookware and the stoves); cheap, once a second.</summary>
        private void Refresh()
        {
            _hotCandidates = FindObjectsByType<VesselSteam>(FindObjectsSortMode.None);
            _stoves = FindObjectsByType<StationContents>(FindObjectsSortMode.None);
            var fryers = new List<StationContents>();
            foreach (StationContents contents in _stoves)
                if (contents != null && contents.TryGetComponent(out Station station) && station.Kind == StationKind.Fryer) fryers.Add(contents);
            _fryers = fryers.ToArray();
            var boards = new List<Vessel>();
            foreach (Vessel vessel in FindObjectsByType<Vessel>(FindObjectsSortMode.None))
                if (vessel.Kind == VesselKind.Board) boards.Add(vessel);
            _boards = boards.ToArray();
        }

        private void PollEvents()
        {
            // The first poll after a (re)join only records the state, so joining does not make noise.
            bool play = _initialised;

            for (int i = 0; i < _boards.Length; i++)
            {
                Vessel board = _boards[i];
                if (board == null) continue;
                int count = board.ContentCount;
                int hash = HashOf(board);
                if (!_boardStates.TryGetValue(board, out BoardState state))
                {
                    state = new BoardState { Count = count, Hash = hash };
                    _boardStates[board] = state;
                    continue;
                }
                bool cut = count > 0 && count == state.Count && hash != state.Hash;
                state.Count = count;
                state.Hash = hash;
                if (cut && play) PlayAt(SoundKey.Chop, board.transform.position);
            }

            for (int i = 0; i < _stoves.Length; i++)
            {
                StationContents stove = _stoves[i];
                if (stove == null) continue;
                byte flags = stove.StoveFlags.Value;
                if (_stoveFlags.TryGetValue(stove, out byte previous) && previous != flags && play)
                    PlayAt(SoundKey.Tick, stove.transform.position);
                _stoveFlags[stove] = flags;
            }

            // Spec 027c: the cooking cues, once per zone entry. A vessel seen for the first time only records its state.
            for (int i = 0; i < _hotCandidates.Length; i++)
            {
                VesselSteam steam = _hotCandidates[i];
                if (steam == null || steam.Vessel == null) continue;
                HeatZone zone = steam.Vessel.Zone;
                bool warn = steam.Vessel.Warning;
                if (!_heatStates.TryGetValue(steam, out HeatState heat))
                {
                    _heatStates[steam] = new HeatState { Zone = zone, Warn = warn };
                    continue;
                }
                SoundKey? cue = SoundRules.CueFor(heat.Zone, heat.Warn, zone, warn);
                heat.Zone = zone;
                heat.Warn = warn;
                if (cue.HasValue && play && TakeCue(heat, cue.Value)) PlayAt(cue.Value, steam.transform.position);
            }

            // Spec 027b: the local player's pick and place sounds are played at the click (PickupInteractor), so the old
            // polled tick on a change of hands is gone (it came up to 0.1 s late and would double them).
            _initialised = true;
        }

        /// <summary>True when the cue may play now (not within <see cref="SoundRules.CueCooldown"/> of the last one).</summary>
        private static bool TakeCue(HeatState heat, SoundKey cue)
        {
            float now = Time.unscaledTime;
            switch (cue)
            {
                case SoundKey.Perfect:
                    if (now - heat.LastPerfect < SoundRules.CueCooldown) return false;
                    heat.LastPerfect = now;
                    return true;
                case SoundKey.Warning:
                    if (now - heat.LastWarning < SoundRules.CueCooldown) return false;
                    heat.LastWarning = now;
                    return true;
                case SoundKey.Burnt:
                    if (now - heat.LastBurnt < SoundRules.CueCooldown) return false;
                    heat.LastBurnt = now;
                    return true;
                default:
                    return true;
            }
        }

        private static int HashOf(Vessel vessel)
        {
            int hash = 17;
            for (int i = 0; i < vessel.Looks.Count; i++) hash = hash * 31 + vessel.Looks[i].GetHashCode();
            return hash;
        }

        private void UpdateLoops(float dt)
        {
            for (int i = 0; i < _hotCandidates.Length; i++)
            {
                VesselSteam steam = _hotCandidates[i];
                if (steam == null || steam.Vessel == null) continue;
                SoundKey key = SoundRules.LoopOf(steam.Vessel.Kind); // spec 029: the fryer basket fries
                if (!_loops.TryGetValue(steam, out LoopState loop))
                {
                    if (!steam.IsHot) continue;
                    loop = new LoopState { Source = CreateSource("Loop " + key, loop: true) };
                    loop.Source.transform.SetParent(steam.transform, false);
                    if (_catalog.TryGet(key, out AudioClip clip)) loop.Source.clip = clip;
                    _loops[steam] = loop;
                }
                // Spec 027c: the level follows the heat and the zone; the pitch rises a little when overdone.
                HeatZone zone = steam.Vessel.Zone;
                float target = SoundRules.LoopLevel(steam.IsHot, zone) * steam.HeatScale; // spec 036: the spit's sizzle follows its knob
                loop.Level = Mathf.MoveTowards(loop.Level, target, LoopFadePerSecond * dt);
                loop.Source.volume = loop.Level * SoundCatalog.MasterVolume * SoundCatalog.GainOf(key);
                loop.Source.pitch = Mathf.MoveTowards(loop.Source.pitch, SoundRules.LoopPitchOf(steam.Vessel.Kind, zone), dt);
                if (loop.Level > 0f && !loop.Source.isPlaying && loop.Source.clip != null) loop.Source.Play();
                else if (loop.Level <= 0f && loop.Source.isPlaying) loop.Source.Pause();
            }
            UpdateFryerHums(dt);
        }

        /// <summary>Spec 029b: a quiet oil hum from each fryer that is on with both baskets up (SoundRules.FryerIdleHum).</summary>
        private void UpdateFryerHums(float dt)
        {
            for (int i = 0; i < _fryers.Length; i++)
            {
                StationContents fryer = _fryers[i];
                if (fryer == null) continue;
                float target = SoundRules.FryerIdleHum(fryer.StoveFlags.Value);
                if (!_hums.TryGetValue(fryer, out LoopState hum))
                {
                    if (target <= 0f) continue;
                    hum = new LoopState { Source = CreateSource("Loop fryer hum", loop: true) };
                    hum.Source.transform.SetParent(fryer.transform, false);
                    hum.Source.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                    hum.Source.pitch = SoundRules.FryerIdlePitch;
                    if (_catalog.TryGet(SoundKey.Fryer, out AudioClip clip)) hum.Source.clip = clip;
                    _hums[fryer] = hum;
                }
                hum.Level = Mathf.MoveTowards(hum.Level, target, LoopFadePerSecond * dt);
                hum.Source.volume = hum.Level * SoundCatalog.MasterVolume * SoundCatalog.GainOf(SoundKey.Fryer);
                if (hum.Level > 0f && !hum.Source.isPlaying && hum.Source.clip != null) hum.Source.Play();
                else if (hum.Level <= 0f && hum.Source.isPlaying) hum.Source.Pause();
            }
        }

        /// <summary>Spec 027b: plays a one-shot now (the local player's actions call this in the frame of the click).</summary>
        public void Play(SoundKey key, Vector3 position)
        {
            if (_catalog == null) return;
            PlayAt(key, position);
        }

        private void PlayAt(SoundKey key, Vector3 position)
        {
            if (!_catalog.TryGet(key, out AudioClip clip)) return;
            AudioSource source = _oneShots[_nextOneShot];
            _nextOneShot = (_nextOneShot + 1) % _oneShots.Length;
            source.transform.position = position;
            source.PlayOneShot(clip, SoundCatalog.MasterVolume * SoundCatalog.GainOf(key));
        }
    }
}
