using UnityEngine;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 027d: one customer look: a Kenney skin plus optional palette recolours of the shirt, trousers and hair
    /// (null keeps the skin's own colour). The art builder paints the recoloured texture (<see cref="SkinRecolourRules"/>).
    /// </summary>
    public sealed class CustomerLook
    {
        public ArtKey Key { get; }
        /// <summary>Texture name of the Kenney skin this look starts from.</summary>
        public string BaseSkin { get; }
        public bool Female { get; }
        public Color? Shirt { get; }
        public Color? Trousers { get; }
        public Color? Hair { get; }

        public CustomerLook(ArtKey key, string baseSkin, bool female, Color? shirt, Color? trousers, Color? hair)
        {
            Key = key;
            BaseSkin = baseSkin;
            Female = female;
            Shirt = shirt;
            Trousers = trousers;
            Hair = hair;
        }

        /// <summary>Name of the texture and material of this look.</summary>
        public string SkinName => Shirt == null && Trousers == null && Hair == null ? BaseSkin : "customer_" + Key;
    }
}
