using UnityEngine;
using Worktown.Game.Audio;
using Worktown.Game.Cooking;
using Worktown.Game.Shop;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 035: the wipe of a drink dispenser's spill (on the dispenser's spill slot), for every viewer. While someone wipes, a cloth goes
    /// round on the puddle (CleaningArtRules.WipeCircle) and the puddle shrinks over CleaningRules.WipeSeconds; a soft scrub plays. The
    /// wiping player sees it from its press (<see cref="Predict"/>, the IPourView of the held tool): its own held cloth goes to the
    /// puddle (<see cref="ClothPose"/>, the interactor eases it there) and the puddle is gone once the wipe's time is reached
    /// (<see cref="SeenWiped"/>), before the server's spill bit clears. Onlookers see this view's own cloth (the dispenser byte's wiping bit).
    /// No per-frame allocations.
    /// </summary>
    public sealed class SpillWipeView : MonoBehaviour, IPourView
    {
        [SerializeField] private Station _dispenser;
        [SerializeField] private DrinkDispenserView _spill;
        [Tooltip("The onlookers' cloth (no collider), drawn while someone else wipes.")]
        [SerializeField] private Transform _cloth;

        private static SoundCatalog _soundCatalog;
        // Spec 044: the views that can lend their cloth pose to a wiper's held cloth (client only).
        private static readonly System.Collections.Generic.List<SpillWipeView> Enabled = new System.Collections.Generic.List<SpillWipeView>();
        /// <summary>m: a cloth holder this close to the dispenser is the one who wipes.</summary>
        public const float WiperRange = 1.6f;
        private int _claimedFrame = -100;
        private StationContents _contents;
        private AudioSource _loop;
        private bool _predicting;
        private bool _predicted;
        private float _predictedUntil;
        private bool _shown;
        private double _start;
        private float _wipedUntil = float.NegativeInfinity;

        /// <summary>The frame the predicted wipe was first drawn (click-to-visual log); -1 before.</summary>
        public int PredictionDrawnFrame { get; private set; } = -1;

        /// <summary>
        /// The wiping player's own cloth is drawn going round (first person: the held cloth is eased to <see cref="ClothPose"/>); in third
        /// person this view draws the cloth for it too.
        /// </summary>
        public bool OwnClothInHand { get; set; } = true;

        /// <summary>Our own wipe reached its time: the spill is gone for us (until the server agrees or a few seconds).</summary>
        public bool SeenWiped => Time.unscaledTime < _wipedUntil;

        private void Awake()
        {
            if (_dispenser == null) _dispenser = GetComponentInParent<Station>();
            if (_dispenser != null) _contents = _dispenser.GetComponent<StationContents>();
            if (_spill == null && _dispenser != null) _spill = _dispenser.GetComponent<DrinkDispenserView>();
            if (_cloth != null) _cloth.gameObject.SetActive(false);
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                enabled = false;
                return;
            }
            _loop = gameObject.AddComponent<AudioSource>();
            _loop.playOnAwake = false;
            _loop.loop = true;
            _loop.spatialBlend = 1f;
            _loop.minDistance = 1f;
            _loop.maxDistance = 10f;
        }

        private void OnEnable()
        {
            if (!Enabled.Contains(this)) Enabled.Add(this);
        }

        private void OnDisable()
        {
            Enabled.Remove(this);
        }

        /// <summary>
        /// Spec 044 (035 feels off: "the third-person wipe cloth in the hand does not move"): a cloth held in a hand by someone standing at
        /// a dispenser that is being wiped goes round on the puddle (<paramref name="pose"/>), and that view stops drawing its own cloth, so
        /// there is one cloth and it moves; the holder's arm reaches for it. False when nobody wipes near <paramref name="wiper"/>.
        /// </summary>
        public static bool TryClothFor(Vector3 wiper, out Pose pose)
        {
            for (int i = 0; i < Enabled.Count; i++)
            {
                SpillWipeView view = Enabled[i];
                if (view == null || !view._shown) continue;
                Vector3 d = view.transform.position - wiper;
                d.y = 0f;
                if (d.sqrMagnitude > WiperRange * WiperRange) continue;
                view._claimedFrame = Time.frameCount;
                pose = view.ClothPose(Time.unscaledTimeAsDouble);
                return true;
            }
            pose = default;
            return false;
        }

        public void Predict(bool on)
        {
            _predicting = true;
            _predicted = on;
            _predictedUntil = Time.unscaledTime + (on ? 6f : 1f);
            if (on) PredictionDrawnFrame = -1;
        }

        /// <summary>Where the wiping player's cloth is drawn now: on the puddle, going round, flat on the counter.</summary>
        public Pose ClothPose(double now)
        {
            Vector3 centre = _spill != null ? _spill.PuddleCentre : transform.position;
            Vector2 c = CleaningArtRules.WipeCircle(_shown ? now - _start : 0d);
            Transform t = _dispenser != null ? _dispenser.transform : transform;
            Vector3 p = centre + t.right * c.x + t.forward * c.y + Vector3.up * (CleaningArtRules.ClothThickness * 0.5f);
            return new Pose(p, Quaternion.LookRotation(Vector3.ProjectOnPlane(t.forward, Vector3.up), Vector3.up));
        }

        private void LateUpdate()
        {
            float now = Time.unscaledTime;
            double nowD = Time.unscaledTimeAsDouble;
            byte flags = _contents != null ? _contents.StoveFlags.Value : (byte)0;
            bool spill = DrinkRules.HasSpill(flags);
            bool syncedWiping = CleaningRules.IsWiping(flags);
            if (_predicting && (now > _predictedUntil || (!_predicted && !syncedWiping))) _predicting = false;
            if (!spill && SeenWiped) _wipedUntil = float.NegativeInfinity; // the server agrees
            bool on = (_predicting ? _predicted : syncedWiping) && (spill || _predicting) && !SeenWiped;
            if (on != _shown)
            {
                _shown = on;
                _start = nowD;
                Loop(on);
                if (!on && _spill != null) _spill.SetWipeLeft(1f);
            }
            if (_shown && _predicting && PredictionDrawnFrame < 0) PredictionDrawnFrame = Time.frameCount;
            bool own = _predicting && _predicted;
            bool drawCloth = _shown && !(own && OwnClothInHand) && Time.frameCount - _claimedFrame > 2; // spec 044: a held cloth goes round instead
            if (_cloth != null && _cloth.gameObject.activeSelf != drawCloth) _cloth.gameObject.SetActive(drawCloth);
            if (!_shown) return;
            float share = CleaningRules.HoldShare(CleaningRules.HoldWipe, nowD - _start);
            if (_spill != null) _spill.SetWipeLeft(CleaningArtRules.PuddleLeft(share));
            if (_cloth != null && drawCloth)
            {
                Pose pose = ClothPose(nowD);
                _cloth.SetPositionAndRotation(pose.position, pose.rotation);
            }
            if (own && share >= 1f)
            {
                // Our own wipe is done (the server stops it at the same time): the spill is gone for us now.
                _wipedUntil = now + 3f;
                if (_spill != null) _spill.PredictWiped(3f);
            }
        }

        private void Loop(bool on)
        {
            if (_loop == null) return;
            if (on)
            {
                _soundCatalog ??= new SoundCatalog();
                if (_loop.clip == null && _soundCatalog.TryGet(SoundRules.ScrubLoop, out AudioClip clip)) _loop.clip = clip;
                _loop.volume = SoundCatalog.MasterVolume * SoundCatalog.GainOf(SoundRules.ScrubLoop) * 0.7f;
                _loop.pitch = SoundRules.ScrubPitch(1f) * 1.2f; // a cloth on a counter: lighter than a brush in water
                if (_loop.clip != null) _loop.Play();
            }
            else _loop.Stop();
        }
    }
}
