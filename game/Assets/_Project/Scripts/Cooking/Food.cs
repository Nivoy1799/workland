using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using Worktown.Game.Art;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// A piece of food in the world (spec 007). The server owns the real sim item (KitchenState)
    /// and only syncs what clients show: name, stars and (spec 017) the coarse look, plus the part
    /// looks of a plate or mix. The exact quality stays on the server (D21): clients only see stars. Carried with the spec 003 Pickup component on the same object.
    /// </summary>
    public sealed class Food : NetworkBehaviour
    {
        public readonly SyncVar<string> DisplayName = new SyncVar<string>(string.Empty);
        public readonly SyncVar<int> Stars = new SyncVar<int>(1);
        /// <summary>Spec 017: replaces the colour-only ingredient byte.</summary>
        public readonly SyncVar<FoodLook> Look = new SyncVar<FoodLook>(FoodLook.Unknown);
        /// <summary>Spec 017: looks of a plate's or mix's parts (max 4), empty for a single food.</summary>
        public readonly SyncList<FoodLook> Parts = new SyncList<FoodLook>();

        [SerializeField] private TextMesh _label;
        [Tooltip("Spec 017: client-side model; the fallback cube lives on it.")]
        [SerializeField] private FoodVisual _visual;

        private bool _visualDirty;

        /// <summary>Spec 033: the client-side model (the owner predicts a cup's level and lid on it); null on a headless server.</summary>
        public FoodVisual Visual => _visual;

        private void Awake()
        {
            DisplayName.OnChange += (_, _, _) => Refresh();
            Stars.OnChange += (_, _, _) => Refresh();
            Look.OnChange += (_, _, _) =>
            {
                _visualDirty = true;
                Refresh(); // spec 031b: the label's short build name follows the look
            };
            Parts.OnChange += (_, _, _, _, _) => _visualDirty = true;
        }

        public override void OnStartClient()
        {
            Refresh();
            _visualDirty = true;
        }

        /// <summary>Server: publish the current sim item's name, star rating and look. Quality is not synced.</summary>
        internal void ServerSet(string displayName, float quality, FoodLook look, IReadOnlyList<FoodLook> parts)
        {
            DisplayName.Value = displayName;
            Stars.Value = StarRating.Stars(quality);
            Look.Value = look;
            if (!SameParts(parts))
            {
                Parts.Clear();
                for (int i = 0; i < parts.Count; i++) Parts.Add(parts[i]);
            }
            Refresh();
            _visualDirty = true;
        }

        public string Describe()
        {
            return DisplayName.Value + " " + StarRating.Format(Stars.Value);
        }

        private bool SameParts(IReadOnlyList<FoodLook> parts)
        {
            if (parts.Count != Parts.Count) return false;
            for (int i = 0; i < parts.Count; i++)
                if (parts[i] != Parts[i]) return false;
            return true;
        }

        private void Refresh()
        {
            // Spec 031b: a build without a dish name is shortened in the label (BuildRules.LabelName); Describe keeps the full name.
            if (_label != null)
                _label.text = BuildRules.LabelName(DisplayName.Value, FoodLookRules.IsBuild(Look.Value)) + "\n" + StarRating.Format(Stars.Value);
        }

        private void LateUpdate()
        {
            // Spec 017: rebuild the model once per change, on clients only (the headless server draws nothing).
            if (_visualDirty)
            {
                _visualDirty = false;
                if (_visual != null && !IsServerOnlyStarted) _visual.ShowFood(Look.Value, Parts);
            }

            // Billboard the label towards the local camera (clients only; servers have no view).
            Camera cam = Camera.main;
            if (_label == null || cam == null) return;
            _label.transform.rotation = Quaternion.LookRotation(_label.transform.position - cam.transform.position);
        }
    }
}
