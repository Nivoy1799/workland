using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// A piece of food in the world (spec 007). The server owns the real sim item (KitchenState)
    /// and only syncs what clients show: name, stars and ingredient color. Carried with the
    /// spec 003 Pickup component on the same object.
    /// </summary>
    public sealed class Food : NetworkBehaviour
    {
        private static readonly Color[] IngredientColors =
        {
            new Color(0.85f, 0.72f, 0.45f), // potato
            new Color(0.80f, 0.15f, 0.15f), // apple
            new Color(0.95f, 0.35f, 0.20f), // tomato
            new Color(0.85f, 0.80f, 0.90f)  // onion
        };

        public readonly SyncVar<string> DisplayName = new SyncVar<string>(string.Empty);
        public readonly SyncVar<int> Stars = new SyncVar<int>(1);
        public readonly SyncVar<float> Quality = new SyncVar<float>(0f);
        public readonly SyncVar<byte> Ingredient = new SyncVar<byte>(0);

        [SerializeField] private TextMesh _label;
        [SerializeField] private Renderer _renderer;

        private MaterialPropertyBlock _block;

        private void Awake()
        {
            DisplayName.OnChange += (_, _, _) => Refresh();
            Stars.OnChange += (_, _, _) => Refresh();
            Ingredient.OnChange += (_, _, _) => Refresh();
        }

        public override void OnStartClient()
        {
            Refresh();
        }

        /// <summary>Server: publish the current sim item's name and rating.</summary>
        internal void ServerSet(string displayName, float quality, byte ingredient)
        {
            DisplayName.Value = displayName;
            Quality.Value = quality;
            Stars.Value = StarRating.Stars(quality);
            Ingredient.Value = ingredient;
            Refresh();
        }

        public string Describe()
        {
            return DisplayName.Value + " " + StarRating.Format(Stars.Value);
        }

        private void Refresh()
        {
            if (_label != null)
                _label.text = DisplayName.Value + "\n" + StarRating.Format(Stars.Value);
            if (_renderer != null)
            {
                _block ??= new MaterialPropertyBlock();
                Color color = IngredientColors[Ingredient.Value % IngredientColors.Length];
                _block.SetColor("_BaseColor", color);
                _renderer.SetPropertyBlock(_block);
            }
        }

        private void LateUpdate()
        {
            // Billboard the label towards the local camera (clients only; servers have no view).
            Camera cam = Camera.main;
            if (_label == null || cam == null) return;
            _label.transform.rotation = Quaternion.LookRotation(_label.transform.position - cam.transform.position);
        }
    }
}
