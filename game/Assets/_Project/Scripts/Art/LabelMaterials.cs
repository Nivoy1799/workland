using UnityEngine;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 027d: the two materials of world text, loaded from Resources (the scene builder creates the assets, so the
    /// shader is in every build). <see cref="CreateText"/> gives a copy that draws the built-in font's atlas, depth tested;
    /// <see cref="CreatePanel"/> a flat Panel-coloured copy drawn just before the text. Client only.
    /// </summary>
    public static class LabelMaterials
    {
        public const string TextPath = "Labels/WorldText";
        public const string PanelPath = "Labels/WorldPanel";
        public const int TextQueue = 3000;
        public const int PanelQueue = 2999; // before the text, so the panel never covers it

        /// <summary>A text material for <paramref name="font"/>; null when the asset is missing.</summary>
        public static Material CreateText(Font font)
        {
            var source = Resources.Load<Material>(TextPath);
            if (source == null || font == null) return null;
            var material = new Material(source) { name = "WorldText (runtime)" };
            material.mainTexture = font.material.mainTexture;
            material.renderQueue = TextQueue;
            return material;
        }

        public static Material CreatePanel()
        {
            var source = Resources.Load<Material>(PanelPath);
            if (source == null) return null;
            var material = new Material(source) { name = "WorldPanel (runtime)" };
            material.mainTexture = Texture2D.whiteTexture;
            material.color = Palette.Panel;
            material.renderQueue = PanelQueue;
            return material;
        }
    }
}
