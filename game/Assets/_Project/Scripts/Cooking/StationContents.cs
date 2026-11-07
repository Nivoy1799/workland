using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 013: what lies on a plating table or in a bowl, synced to everyone who sees the station,
    /// shown on the station's label. Display only; the real items live in the server's KitchenState.
    /// </summary>
    public sealed class StationContents : NetworkBehaviour
    {
        public readonly SyncList<string> Names = new SyncList<string>();

        [SerializeField] private TextMesh _label;

        private string _baseText;

        private void Awake()
        {
            if (_label != null) _baseText = _label.text;
            Names.OnChange += (_, _, _, _, _) => Refresh();
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

        private void Refresh()
        {
            if (_label == null) return;
            if (Names.Count == 0)
            {
                _label.text = _baseText;
                return;
            }
            var lines = new List<string>(Names.Count + 1) { _baseText };
            foreach (string name in Names) lines.Add("+ " + name);
            _label.text = string.Join("\n", lines);
        }
    }
}
