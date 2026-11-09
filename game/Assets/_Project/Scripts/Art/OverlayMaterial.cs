using UnityEngine;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 027c: a transparent, unlit, double-sided material that shows a mesh's vertex colours, for the heat ring and the
    /// serve reward. Copies the steam material (URP Particles/Unlit, spec 023) when one is given, so the shader variant is
    /// in the build; else looks the same shader up. Client only.
    /// </summary>
    public static class OverlayMaterial
    {
        public static Material Create(Material source, string name)
        {
            Material material;
            if (source != null) material = new Material(source);
            else
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
                material = new Material(shader);
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.SetOverrideTag("RenderType", "Transparent");
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
            material.name = name;
            material.mainTexture = Texture2D.whiteTexture;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", Texture2D.whiteTexture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f); // both sides
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 50; // over steam and smoke
            return material;
        }
    }
}
