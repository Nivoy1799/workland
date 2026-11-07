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

        [SerializeField] private VesselKind _kind;
        [SerializeField] private TextMesh _label;
        [Tooltip("Draws the contents inside the vessel.")]
        [SerializeField] private FoodVisual _visual;

        private bool _visualDirty;
        private bool _labelDirty;

        public VesselKind Kind => _kind;

        /// <summary>How many things lie in it, as clients see it.</summary>
        public int ContentCount => Mathf.Max(Names.Count, Looks.Count);

        private void Awake()
        {
            Names.OnChange += (_, _, _, _, _) => _labelDirty = true;
            Looks.OnChange += (_, _, _, _, _) => _visualDirty = true;
            Mode.OnChange += (_, _, _) => _labelDirty = true;
        }

        public override void OnStartClient()
        {
            _labelDirty = true;
            _visualDirty = true;
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
                if (_visual != null) _visual.ShowParts(Looks);
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
