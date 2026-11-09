using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using Worktown.Game.Art;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 021: marks a pickup as cookware (board, pan, pot, plate, bowl) and shows what lies in it to everyone, also
    /// while someone carries it. Display only: the real items live in the server's KitchenState, keyed by this
    /// object's id. Synced: the content names and looks, the name of what you would get out of it, and the pan's mode
    /// byte (fry / sauté). Never a quality, a time or a temperature (D21). Carried with the spec 003 Pickup on the
    /// same object; no RPCs here (the player's PickupInteractor sends RequestUse).
    /// Spec 027c: two presentation bytes for the pan and pot on the heat, computed by the server from the sim's doneness
    /// window (<see cref="HeatZoneRules"/>): <see cref="HeatProgress"/> and <see cref="HeatZone"/> (zone plus a warning bit).
    /// They reveal no stats or times (D21 amendment pending in docs/human-inbox.md); sent only on change, at most 4 Hz.
    /// </summary>
    public sealed class Vessel : NetworkBehaviour
    {
        public const byte ModeFry = 0;
        public const byte ModeSaute = 1;

        public readonly SyncList<string> Names = new SyncList<string>();
        public readonly SyncList<FoodLook> Looks = new SyncList<FoodLook>();
        /// <summary>What taking, pouring or selling would give ("crispy fried potato sticks"), empty when empty.</summary>
        public readonly SyncVar<string> Title = new SyncVar<string>(string.Empty);
        /// <summary>Pan only: <see cref="ModeFry"/> or <see cref="ModeSaute"/>.</summary>
        public readonly SyncVar<byte> Mode = new SyncVar<byte>(ModeFry);
        /// <summary>Spec 027c: 0-255, the share of the way to the end of the good window (the good window starts at
        /// <see cref="HeatZoneRules.GoodShare"/>).</summary>
        public readonly SyncVar<byte> HeatProgress = new SyncVar<byte>(0);
        /// <summary>Spec 027c: the <see cref="Cooking.HeatZone"/> in the low bits, <see cref="HeatZoneRules.WarnBit"/> from
        /// 3 s before Overdone.</summary>
        public readonly SyncVar<byte> HeatZone = new SyncVar<byte>(0);

        [SerializeField] private VesselKind _kind;
        [SerializeField] private TextMesh _label;
        [Tooltip("Draws the contents inside the vessel.")]
        [SerializeField] private FoodVisual _visual;

        private bool _visualDirty;
        private bool _labelDirty;
        private bool _heatDirty;

        public VesselKind Kind => _kind;

        /// <summary>Spec 027c: the zone clients see (None when nothing is on the heat).</summary>
        public HeatZone Zone => HeatZoneRules.ZoneOf(HeatZone.Value);
        /// <summary>Spec 027c: Overdone comes within 3 s.</summary>
        public bool Warning => HeatZoneRules.WarnOf(HeatZone.Value);
        /// <summary>Spec 027c: the progress byte as 0..1.</summary>
        public float Progress01 => HeatProgress.Value / 255f;

        /// <summary>How many things lie in it, as clients see it.</summary>
        public int ContentCount => Mathf.Max(Names.Count, Looks.Count);

        /// <summary>Name of the child where the contents are drawn.</summary>
        public const string ContentsName = "Contents";
        private Transform _contents;
        private bool _contentsLooked;

        /// <summary>Spec 027b review: the "Contents" anchor (looked up once), or null.</summary>
        public Transform ContentsAnchor
        {
            get
            {
                if (!_contentsLooked)
                {
                    _contentsLooked = true;
                    _contents = transform.Find(ContentsName);
                }
                return _contents;
            }
        }

        /// <summary>Spec 027b: where the contents are drawn, else a little above the pivot (aim, ghost, drop).</summary>
        public Vector3 ContentPoint => ContentsAnchor != null ? ContentsAnchor.position : transform.position + Vector3.up * 0.05f;

        private void Awake()
        {
            Names.OnChange += (_, _, _, _, _) => _labelDirty = true;
            Looks.OnChange += (_, _, _, _, _) => _visualDirty = true;
            Mode.OnChange += (_, _, _) => _labelDirty = true;
            HeatProgress.OnChange += (_, _, _) => _heatDirty = true;
            HeatZone.OnChange += (_, _, _) => _heatDirty = true;
        }

        public override void OnStartClient()
        {
            _labelDirty = true;
            _visualDirty = true;
            _heatDirty = true;
        }

        /// <summary>Server: publish the heat bytes (KitchenService's <see cref="HeatSync"/> limits the rate); only changes go out.</summary>
        internal void ServerSetHeat(byte progress, byte zone)
        {
            if (HeatProgress.Value != progress) HeatProgress.Value = progress;
            if (HeatZone.Value != zone) HeatZone.Value = zone;
        }

        /// <summary>Server: publish the contents; only what changed is sent.</summary>
        internal void ServerSet(IReadOnlyList<string> names, IReadOnlyList<FoodLook> looks, string title, bool saute)
        {
            if (!SameNames(names))
            {
                Names.Clear();
                for (int i = 0; i < names.Count; i++) Names.Add(names[i]);
                _labelDirty = true;
            }
            if (!SameLooks(looks))
            {
                Looks.Clear();
                for (int i = 0; i < looks.Count; i++) Looks.Add(looks[i]);
                _visualDirty = true;
            }
            string t = title ?? string.Empty;
            if (Title.Value != t) Title.Value = t;
            byte mode = saute ? ModeSaute : ModeFry;
            if (Mode.Value != mode) Mode.Value = mode;
        }

        private bool SameNames(IReadOnlyList<string> names)
        {
            if (names.Count != Names.Count) return false;
            for (int i = 0; i < names.Count; i++)
                if (names[i] != Names[i]) return false;
            return true;
        }

        private bool SameLooks(IReadOnlyList<FoodLook> looks)
        {
            if (looks.Count != Looks.Count) return false;
            for (int i = 0; i < looks.Count; i++)
                if (looks[i] != Looks[i]) return false;
            return true;
        }

        private void LateUpdate()
        {
            if (IsServerOnlyStarted) return; // the headless server draws nothing
            if (_visualDirty)
            {
                _visualDirty = false;
                _heatDirty = true;
                if (_visual != null) _visual.ShowParts(Looks);
            }
            if (_heatDirty)
            {
                _heatDirty = false;
                // Spec 027c: the food in the pan or pot shifts colour with the heat, for every viewer.
                if (_visual != null) _visual.SetHeat(Zone, Progress01, _kind == VesselKind.Pot);
            }
            if (_labelDirty)
            {
                _labelDirty = false;
                RefreshLabel();
            }
            Camera cam = Camera.main;
            if (_label == null || cam == null) return;
            _label.transform.rotation = Quaternion.LookRotation(_label.transform.position - cam.transform.position);
        }

        private void RefreshLabel()
        {
            if (_label == null) return;
            if (Names.Count == 0)
            {
                _label.text = string.Empty;
                return;
            }
            var lines = new List<string>(Names.Count + 1);
            if (_kind == VesselKind.Pan) lines.Add(Mode.Value == ModeSaute ? "sauté" : "fry");
            foreach (string name in Names) lines.Add(name);
            _label.text = string.Join("\n", lines);
        }
    }
}
