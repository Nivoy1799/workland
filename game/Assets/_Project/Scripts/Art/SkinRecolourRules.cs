using UnityEngine;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 027d: recolours the shirt, trousers and hair on the Kenney skin atlas (one 1024 texture for the whole body; the
    /// regions below are read off the two skins). Pixels keep their brightness relative to the region's brightest pixel, so
    /// the shading survives; white, grey, skin and shoes are left alone. Pure, so it is testable without the editor.
    /// </summary>
    public static class SkinRecolourRules
    {
        private const float Atlas = 1024f;
        // Rectangles in atlas pixels with y down from the top (x0, y0, x1, y1).
        private static readonly float[] HairRect = { 0f, 0f, 640f, 490f };
        private static readonly float[] FaceRect = { 255f, 195f, 390f, 300f }; // eyes, brows, mouth: not hair
        private static readonly float[] ShirtRect = { 0f, 490f, 640f, 1024f };
        private static readonly float[] TrouserRect = { 611f, 764f, 1024f, 1024f };

        private enum Part { None, Hair, Shirt, Trousers }

        /// <summary>Returns the recoloured copy of <paramref name="source"/> (a square atlas, rows from the bottom as in a Texture2D).</summary>
        public static Color32[] Apply(Color32[] source, int size, bool female, Color? shirt, Color? trousers, Color? hair)
        {
            var result = (Color32[])source.Clone();
            var parts = new Part[source.Length];
            var peak = new float[4];
            for (int i = 0; i < source.Length; i++)
            {
                int x = i % size, y = size - 1 - i / size; // y down from the top
                Part part = Classify(x * Atlas / size, y * Atlas / size, source[i], female);
                parts[i] = part;
                Color.RGBToHSV(source[i], out _, out _, out float v);
                if (part != Part.None && v > peak[(int)part]) peak[(int)part] = v;
            }
            for (int i = 0; i < source.Length; i++)
            {
                Part part = parts[i];
                Color? target = part == Part.Shirt ? shirt : part == Part.Trousers ? trousers : part == Part.Hair ? hair : null;
                if (part == Part.None || !target.HasValue) continue;
                Color.RGBToHSV(source[i], out _, out _, out float v);
                Color.RGBToHSV(target.Value, out float th, out float ts, out float tv);
                Color painted = Color.HSVToRGB(th, ts, Mathf.Min(1f, tv * v / Mathf.Max(0.0001f, peak[(int)part])));
                Color32 pixel = painted;
                pixel.a = source[i].a;
                result[i] = pixel;
            }
            return result;
        }

        private static Part Classify(float x, float y, Color32 pixel, bool female)
        {
            Color.RGBToHSV(pixel, out float h, out float s, out float v);
            if (Inside(TrouserRect, x, y))
            {
                bool trouser = female ? v < 0.35f : s > 0.4f;
                return trouser ? Part.Trousers : Part.None;
            }
            if (Inside(ShirtRect, x, y))
                return s > 0.5f && v > 0.5f && (h < 0.08f || h > 0.92f) ? Part.Shirt : Part.None; // the red cloth, not the teal heart
            if (Inside(HairRect, x, y) && !Inside(FaceRect, x, y))
                return v < (female ? 0.3f : 0.5f) ? Part.Hair : Part.None;
            return Part.None;
        }

        private static bool Inside(float[] r, float x, float y) => x >= r[0] && x < r[2] && y >= r[1] && y < r[3];
    }
}
