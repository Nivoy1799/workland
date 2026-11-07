using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 013: what lies on a plating table or in a bowl, synced to everyone who sees the station,
    /// shown on the station's label. Display only; the real items live in the server's KitchenState.
    /// Spec 016: pans and pots also show a status line (knob and mode) and what is cooking.
    /// </summary>
    public sealed class StationContents : NetworkBehaviour
    {
        public readonly SyncList<string> Names = new SyncList<string>();
        public readonly SyncVar<string> Status = new SyncVar<string>(string.Empty);

        [SerializeField] private TextMesh _label;

        private string _baseText;

        private void Awake()
        {
            if (_label != null) _baseText = _label.text;
            Names.OnChange += (_, _, _, _, _) => Refresh();
            Status.OnChange += (_, _, _) => Refresh();
        }

        public override void OnStartClient()
        {
            Refresh();
        }

        internal void ServerSet(IReadOnlyList<string> names)
        {
            Names.Clear();
            foreach (string name in names) Names.Add(name);
            Refresh();
        }

        /// <summary>Spec 016: server sets the label for a pan or pot; only sends what changed.</summary>
        internal void ServerSetHeat(string status, string cooking)
        {
            if (Status.Value != status) Status.Value = status ?? string.Empty;
            bool same = cooking == null ? Names.Count == 0 : Names.Count == 1 && Names[0] == cooking;
            if (same) return;
            Names.Clear();
            if (cooking != null) Names.Add(cooking);
            Refresh();
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
