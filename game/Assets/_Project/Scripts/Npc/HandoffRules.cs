using UnityEngine;
using Worktown.Game.Art;

namespace Worktown.Game.Npc
{
    /// <summary>
    /// Spec 032, pure: the guest's hand-off. Which parcel it carries away (a wrap in its paper, the flatbread in a bag, the veggie box
    /// in its box, anything else in a takeaway bag) and the arc from the counter into its hand: <see cref="FlySeconds"/> with an
    /// ease-out and a small lift (feel.md 2: nothing teleports).
    /// </summary>
    public static class HandoffRules
    {
        public const float FlySeconds = 0.35f;
        public const float Lift = 0.12f;

        public static ArtKey ParcelOf(byte dish)
        {
            switch (dish)
            {
                case Menu.SeitanWrap:
                case Menu.FalafelWrap:
                    return ArtKey.FoodWrapPaper;
                case Menu.VeggieBox:
                    return ArtKey.PropTakeawayBox;
                default:
                    return ArtKey.PropPaperBag;
            }
        }

        /// <summary>The point at share <paramref name="t"/> (0..1) of the arc: ease-out along the line, plus a lift that peaks halfway.</summary>
        public static Vector3 Arc(Vector3 from, Vector3 to, float t)
        {
            t = Mathf.Clamp01(t);
            float e = 1f - (1f - t) * (1f - t);
            return Vector3.Lerp(from, to, e) + Vector3.up * (Lift * 4f * t * (1f - t));
        }
    }
}
