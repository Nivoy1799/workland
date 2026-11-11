using UnityEngine;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 053 follow-up (2026-11-11 evening): the shape of a closed station's cover, pure. One calm fitted cloth (a single box in a
    /// palette colour, D32) over the station and everything resting on it (a pan, a pot, a basket: separate objects, not the station's
    /// children), so nothing pokes through, and a small sign card pinned to its front. The graybox scene builder builds it from these
    /// rules; the scene test checks the result.
    /// </summary>
    public static class ClosedCoverRules
    {
        /// <summary>m: the cloth stands this far off the sides of the station and its contents.</summary>
        public const float Margin = 0.02f;
        /// <summary>m: the cloth's top stands this far above the highest thing it covers.</summary>
        public const float Clearance = 0.02f;
        /// <summary>m: the cloth hangs this far below the station's top (or the lowest thing resting on it).</summary>
        public const float Skirt = 0.12f;
        /// <summary>m: a thing whose bottom is at least this far above the station's bottom rests on it (the floor and the wall do not).</summary>
        public const float RestFloor = 0.05f;
        /// <summary>m: a thing whose bottom is at most this far above the station's top still rests on it (a hood far above does not).</summary>
        public const float RestAbove = 0.6f;

        /// <summary>m: the sign card (width, height); it shrinks to fit a narrow cover.</summary>
        public static readonly Vector2 SignSize = new Vector2(0.44f, 0.075f);
        /// <summary>m: the sign card stands this far proud of the cloth's front, and the text this far proud of the card.</summary>
        public const float SignProud = 0.004f;
        public const float TextProud = 0.002f;
        public const int SignFontSize = 48;
        /// <summary>The sign text's character size at full width (about 0.026 m letters, the text about 0.38 m wide).</summary>
        public const float SignCharacterSize = 0.0055f;

        /// <summary>
        /// True when <paramref name="item"/> rests on <paramref name="station"/>: its centre is over the station's footprint and its bottom
        /// is between just above the station's bottom and a little above its top.
        /// </summary>
        public static bool RestsOn(Bounds station, Bounds item)
        {
            Vector3 c = item.center;
            if (c.x < station.min.x || c.x > station.max.x || c.z < station.min.z || c.z > station.max.z) return false;
            return item.min.y >= station.min.y + RestFloor && item.min.y <= station.max.y + RestAbove;
        }

        /// <summary>
        /// The cloth box over <paramref name="station"/> and <paramref name="contents"/> (the bounds of everything resting on it;
        /// <paramref name="hasContents"/> false when nothing does): the margin around both, the clearance above the highest, down to
        /// <see cref="Skirt"/> below the station's top or the lowest resting thing, whichever is lower.
        /// </summary>
        public static Bounds Cloth(Bounds station, Bounds contents, bool hasContents)
        {
            Bounds all = station;
            float rest = station.max.y;
            if (hasContents)
            {
                all.Encapsulate(contents);
                rest = Mathf.Min(rest, contents.min.y);
            }
            float top = all.max.y + Clearance;
            float bottom = Mathf.Max(station.min.y, rest - Skirt);
            var min = new Vector3(all.min.x - Margin, bottom, all.min.z - Margin);
            var max = new Vector3(all.max.x + Margin, top, all.max.z + Margin);
            var cloth = new Bounds();
            cloth.SetMinMax(min, max);
            return cloth;
        }

        /// <summary>The sign's scale (0..1) on a cloth face <paramref name="faceWidth"/> wide: full size, or smaller to fit with a margin.</summary>
        public static float SignScale(float faceWidth)
        {
            float room = faceWidth - 2f * Margin;
            if (float.IsNaN(room) || room <= 0f) return 0f;
            return Mathf.Min(1f, room / SignSize.x);
        }

        /// <summary>
        /// The sign card's centre on the cloth's front: the face towards <paramref name="front"/> (a horizontal unit direction along x or
        /// z), centred across it, a little below the top.
        /// </summary>
        public static Vector3 SignCentre(Bounds cloth, Vector3 front, float scale)
        {
            bool alongZ = Mathf.Abs(front.z) >= Mathf.Abs(front.x);
            float half = alongZ ? cloth.extents.z : cloth.extents.x;
            Vector3 flat = alongZ ? new Vector3(0f, 0f, Mathf.Sign(front.z)) : new Vector3(Mathf.Sign(front.x), 0f, 0f);
            float y = cloth.max.y - Mathf.Min(cloth.size.y * 0.5f, 0.01f + SignSize.y * scale * 0.5f + 0.02f);
            return new Vector3(cloth.center.x, y, cloth.center.z) + flat * (half + SignProud);
        }
    }
}
