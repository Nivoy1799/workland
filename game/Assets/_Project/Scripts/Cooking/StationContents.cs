using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using Worktown.Game.Art;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 013: what lies on a plating table or in a bowl, synced to everyone who sees the station,
    /// shown on the station's label. Display only; the real items live in the server's KitchenState.
    /// Spec 016: pans and pots also show a status line (knob and mode) and what is cooking.
    /// Spec 017: the contents' coarse looks, drawn as models on the station's content anchor.
    /// Spec 019: the cutting board shows its food the same way; stoves sync a flags byte for the buttons.
    /// </summary>
    public sealed class StationContents : NetworkBehaviour
    {
        public readonly SyncList<string> Names = new SyncList<string>();
        public readonly SyncVar<string> Status = new SyncVar<string>(string.Empty);
        public readonly SyncList<FoodLook> Looks = new SyncList<FoodLook>();
        /// <summary>Spec 019: pan and pot switch and mode for the button lights (bit 0 on, bit 1 saute, see StationRules).</summary>
        public readonly SyncVar<byte> StoveFlags = new SyncVar<byte>(0);

        [SerializeField] private TextMesh _label;
        [Tooltip("Spec 017: models of the contents on the plate, in the bowl, pan or pot. Optional.")]
        [SerializeField] private FoodVisual _visual;

        private string _baseText;
        private bool _visualDirty;

        private void Awake()
        {
            if (_label != null) _baseText = _label.text;
            Names.OnChange += (_, _, _, _, _) => Refresh();
            Status.OnChange += (_, _, _) => Refresh();
            Looks.OnChange += (_, _, _, _, _) => _visualDirty = true;
        }

        public override void OnStartClient()
        {
            Refresh();
            _visualDirty = true;
        }

        internal void ServerSet(IReadOnlyList<string> names, IReadOnlyList<FoodLook> looks)
        {
            Names.Clear();
            foreach (string name in names) Names.Add(name);
            SetLooks(looks);
            Refresh();
        }

        /// <summary>Spec 019: server sets the stove byte; only sends a change.</summary>
        internal void ServerSetStoveFlags(byte flags)
        {
            if (StoveFlags.Value != flags) StoveFlags.Value = flags;
        }

        /// <summary>
        /// Spec 016: server sets the label for a pan or pot; only sends what changed. Spec 019: also used for
        /// the cutting board (status line, the food on it and its look).
        /// </summary>
        internal void ServerSetHeat(string status, string cooking, FoodLook? look)
        {
            if (Status.Value != status) Status.Value = status ?? string.Empty;
            bool sameLook = look.HasValue ? Looks.Count == 1 && Looks[0] == look.Value : Looks.Count == 0;
            if (!sameLook)
            {
                Looks.Clear();
                if (look.HasValue) Looks.Add(look.Value);
                _visualDirty = true;
            }
            bool same = cooking == null ? Names.Count == 0 : Names.Count == 1 && Names[0] == cooking;
            if (same) return;
            Names.Clear();
            if (cooking != null) Names.Add(cooking);
            Refresh();
        }

        private void SetLooks(IReadOnlyList<FoodLook> looks)
        {
            bool same = looks.Count == Looks.Count;
            for (int i = 0; same && i < looks.Count; i++) same = looks[i] == Looks[i];
            if (same) return;
            Looks.Clear();
            for (int i = 0; i < looks.Count; i++) Looks.Add(looks[i]);
            _visualDirty = true;
        }

        private void LateUpdate()
        {
            // Spec 017: rebuild once per change, on clients only (the headless server draws nothing).
            if (!_visualDirty) return;
            _visualDirty = false;
            if (_visual != null && !IsServerOnlyStarted) _visual.ShowParts(Looks);
        }

        private void Refresh()
        {
            if (_label == null) return;
            string head = string.IsNullOrEmpty(Status.Value) ? _baseText : _baseText + "\n" + Status.Value;
            if (Names.Count == 0)
            {
                _label.text = head;
                return;
            }
            var lines = new List<string>(Names.Count + 1) { head };
            foreach (string name in Names) lines.Add("+ " + name);
            _label.text = string.Join("\n", lines);
        }
    }
}
